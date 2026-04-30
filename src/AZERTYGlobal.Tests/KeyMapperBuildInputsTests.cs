using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

/// <summary>
/// Tests niveau 1 + 2 sur les méthodes de construction d'INPUT du KeyMapper.
/// Valide la séquence générée — pas le comportement runtime de Windows
/// (cf. plan v0.9.7 § Limitations du mock).
/// </summary>
public class KeyMapperBuildInputsTests
{
    private static KeyMapper NewMapper(MockWin32Api mock) => new(new Layout(), mock);

    // ────────────────────────────────────────────────────────────────
    // BuildUnicodeInputs (niveau 1)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildUnicodeInputs_BmpChar_Yields2Events()
    {
        var mock = new MockWin32Api();
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        km.BuildUnicodeInputs('@', null, list);

        Assert.Equal(2, list.Count);
        Assert.Equal('@', list[0].u.ki.wScan);
        Assert.Equal('@', list[1].u.ki.wScan);
    }

    [Fact]
    public void BuildUnicodeInputs_SurrogatePair_Yields4Events()
    {
        var mock = new MockWin32Api();
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        // Codepoint U+1F600 (😀) en surrogate pair
        char high = '\uD83D';
        char low = '\uDE00';
        km.BuildUnicodeInputs(high, low, list);

        Assert.Equal(4, list.Count); // down high, down low, up high, up low
    }

    // ────────────────────────────────────────────────────────────────
    // BuildAltCodeInputs (niveau 1)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildAltCodeInputs_E_Acute_GeneratesAlt0201Sequence_NumLockOn()
    {
        var mock = new MockWin32Api();
        mock.KeyStateScript[(int)Win32.VK_NUMLOCK] = 0x0001; // NumLock ON
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        km.BuildAltCodeInputs('É', list); // codepoint 201 → 0/2/0/1

        // 1 LAlt down + 4*2 numpad + 1 LAlt up = 10 events (pas de NumLock toggle)
        Assert.Equal(10, list.Count);
        // Vérifier les VK successifs : LAlt(0xA4), Numpad0(0x60), Numpad0(0x60), Numpad2, ...
        Assert.Equal(0xA4, list[0].u.ki.wVk); // VK_LMENU down
        Assert.Equal(0x60, list[1].u.ki.wVk); // VK_NUMPAD0 down
        Assert.Equal(0x60, list[2].u.ki.wVk); // VK_NUMPAD0 up
        Assert.Equal(0x62, list[3].u.ki.wVk); // VK_NUMPAD2 down
        Assert.Equal(0x60, list[5].u.ki.wVk); // VK_NUMPAD0 down
        Assert.Equal(0x61, list[7].u.ki.wVk); // VK_NUMPAD1 down
        Assert.Equal(0xA4, list[9].u.ki.wVk); // VK_LMENU up
    }

    [Fact]
    public void BuildAltCodeInputs_NumLockOff_TogglesAround()
    {
        var mock = new MockWin32Api();
        // NumLock OFF par défaut
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        km.BuildAltCodeInputs('@', list); // 0x40 = 64 → 0/0/6/4

        // 2 NumLock toggle + 1 LAlt down + 4*2 numpad + 1 LAlt up + 2 NumLock toggle = 14
        Assert.Equal(14, list.Count);
        Assert.Equal(0x90, list[0].u.ki.wVk); // VK_NUMLOCK down
        Assert.Equal(0x90, list[1].u.ki.wVk); // VK_NUMLOCK up
        Assert.Equal(0x90, list[12].u.ki.wVk); // restore NumLock down
        Assert.Equal(0x90, list[13].u.ki.wVk); // restore NumLock up
    }

    // ────────────────────────────────────────────────────────────────
    // BuildVkComboInputs — matrice 4x4 modifs (niveau 2)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildVkComboInputs_NoMods_YieldsOnlyVkPressRelease()
    {
        var mock = new MockWin32Api();
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        // Combo simple : VK = 0x41 (A), aucun modif requis, aucun modif tenu
        km.BuildVkComboInputs(0x41, 0x1E, false, false, false, false, list);

        Assert.Equal(2, list.Count); // VK down + up
        Assert.Equal(0x41, list[0].u.ki.wVk);
    }

    [Fact]
    public void BuildVkComboInputs_NeedAltGr_NotHeld_PressesAndReleasesRMenu()
    {
        var mock = new MockWin32Api();
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        // Combo AltGr+0 → @ : on n'a pas AltGr tenu, on doit l'ajouter en synthétique
        km.BuildVkComboInputs(0x30, 0x0B, false, true /*needsAltGr*/, false, false, list);

        Assert.Equal(4, list.Count); // RMenu down + VK down + VK up + RMenu up
        Assert.Equal(0xA5, list[0].u.ki.wVk); // VK_RMENU down
        Assert.Equal(0x30, list[1].u.ki.wVk); // VK down
        Assert.Equal(0xA5, list[3].u.ki.wVk); // VK_RMENU up
    }

    [Fact]
    public void BuildVkComboInputs_NeedShift_AlreadyHeld_NoExtraShift()
    {
        var mock = new MockWin32Api();
        var km = NewMapper(mock);
        // Simuler Shift physique tenu via TrackModifiers
        km.TrackModifiers(0xA0 /*VK_LSHIFT*/, 0x2A, 0, true);
        var list = new List<Win32.INPUT>();

        km.BuildVkComboInputs(0x41, 0x1E, true /*needsShift*/, false, false, false, list);

        // Shift déjà tenu physiquement → pas de keydown synthétique additionnel
        Assert.Equal(2, list.Count); // juste VK down + up
    }

    [Fact]
    public void BuildVkComboInputs_NeedNoShift_ShiftHeld_TogglesShiftOff()
    {
        var mock = new MockWin32Api();
        var km = NewMapper(mock);
        km.TrackModifiers(0xA0, 0x2A, 0, true); // Shift physique tenu
        var list = new List<Win32.INPUT>();

        km.BuildVkComboInputs(0x30, 0x0B, false /*needsNoShift*/, false, false, false, list);

        // 4 events : Shift up + VK down + VK up + Shift down (restore)
        Assert.Equal(4, list.Count);
        Assert.Equal(0xA0, list[0].u.ki.wVk); // Shift up
        Assert.True((list[0].u.ki.dwFlags & 0x0002) != 0); // KEYEVENTF_KEYUP
        Assert.Equal(0xA0, list[3].u.ki.wVk); // Shift down (restore)
    }

    [Fact]
    public void BuildVkComboInputs_CapsLockActive_AndShiftCombo_TogglesCapsAround()
    {
        var mock = new MockWin32Api();
        // KeyState pour Caps Lock initial dans le ctor
        mock.KeyStateScript[0x14] = 0x0001;
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        // Combo Shift+lettre avec Caps actif → toggle Caps autour (conditionnel)
        km.BuildVkComboInputs(0x41, 0x1E, true, false, false, false, list);

        // Au minimum : Caps off + Shift down + VK + Shift up + Caps on (toggle = 2 events chacun)
        // Caps off (down+up) + Shift down + VK down + VK up + Shift up + Caps on (down+up)
        Assert.True(list.Count >= 8, $"Expected at least 8 inputs, got {list.Count}");
        Assert.Equal(0x14, list[0].u.ki.wVk); // VK_CAPITAL down
        Assert.Equal(0x14, list[1].u.ki.wVk); // VK_CAPITAL up
    }

    [Fact]
    public void BuildVkComboInputs_CapsLockActive_AltGrCombo_DoesNotToggleCaps()
    {
        var mock = new MockWin32Api();
        mock.KeyStateScript[0x14] = 0x0001; // Caps actif
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        // Combo AltGr+0 (pas affectée par Caps) → pas de toggle Caps
        km.BuildVkComboInputs(0x30, 0x0B, false, true, false, false, list);

        // Aucune entrée ne doit toucher VK_CAPITAL (0x14)
        Assert.DoesNotContain(list, ev => ev.u.ki.wVk == 0x14);
    }

    // ────────────────────────────────────────────────────────────────
    // BuildNativeComboInputs (niveau 2)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildNativeComboInputs_VkKeyScanReturns_Minus1_ReturnsFalse()
    {
        var mock = new MockWin32Api();
        // pas de script → retourne -1 par défaut
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        bool ok = km.BuildNativeComboInputs('É', mock.CurrentHkl, list);
        Assert.False(ok);
        Assert.Empty(list);
    }

    [Fact]
    public void BuildNativeComboInputs_AtSign_AzertyFr_BuildsAltGrPlus0()
    {
        var mock = new MockWin32Api();
        mock.CurrentHkl = (IntPtr)0x040C040C;
        // Script : sur AZERTY FR, '@' → VK_0 (0x30) + mod Ctrl|Alt = 6 (AltGr)
        mock.VkKeyScanScript[('@', mock.CurrentHkl)] = 0x0630; // mod=6, vk=0x30
        mock.MapVirtualKeyScript[(0x30, 0, mock.CurrentHkl)] = 0x0B; // scancode SC00B
        var km = NewMapper(mock);
        var list = new List<Win32.INPUT>();

        bool ok = km.BuildNativeComboInputs('@', mock.CurrentHkl, list);
        Assert.True(ok);
        // Au minimum : RMenu down + VK_0 down + VK_0 up + RMenu up = 4 inputs
        Assert.True(list.Count >= 4);
        Assert.Equal(0xA5, list[0].u.ki.wVk); // RMenu (AltGr) down
        Assert.Equal(0x30, list[1].u.ki.wVk); // VK_0 down
        Assert.Equal((ushort)0x0B, list[1].u.ki.wScan); // scancode SC00B
        Assert.Equal(0xA5, list[3].u.ki.wVk); // RMenu up
    }
}

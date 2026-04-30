using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

/// <summary>
/// Tests niveau 3 — EmitText() avec ForegroundMonitor mocké.
/// Vérifie le dispatch entre Default (Unicode), NativeCombo (combo native ou Alt+code).
/// </summary>
public class KeyMapperEmitTextIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public KeyMapperEmitTextIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AZGKM_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        ConfigManager.OverrideConfigPathForTests(_configPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void EmitText_DefaultMode_UsesKeyEventfUnicode()
    {
        var mock = new MockWin32Api { ScriptedProcessName = "notepad.exe" };
        var km = new KeyMapper(new Layout(), mock);
        // ForegroundMonitor null → fallback Unicode (mode Default implicite)

        km.EmitText("@");

        Assert.Single(mock.SendInputCalls);
        var inputs = mock.SendInputCalls[0];
        Assert.Equal(2, inputs.Length); // @ down + up
        // KEYEVENTF_UNICODE = 0x0004, wVk = 0
        Assert.Equal(0, inputs[0].u.ki.wVk);
        Assert.Equal('@', inputs[0].u.ki.wScan);
        Assert.True((inputs[0].u.ki.dwFlags & 0x0004) != 0); // KEYEVENTF_UNICODE
    }

    [Fact]
    public void EmitText_NativeComboMode_UsesAltGrCombo_For_AtSign()
    {
        var mock = new MockWin32Api
        {
            ScriptedProcessName = "javaw.exe",
            ScriptedFullPath = @"C:\Java\javaw.exe",
            ScriptedModules = new[] { "lwjgl_glfw.dll" },
            CurrentHkl = (IntPtr)0x040C040C
        };
        // Script : '@' = AltGr+0 sur AZERTY FR
        mock.VkKeyScanScript[('@', mock.CurrentHkl)] = 0x0630; // mod=6 (Ctrl|Alt = AltGr), vk=0x30
        mock.MapVirtualKeyScript[(0x30, 0, mock.CurrentHkl)] = 0x0B;

        var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.Equal(CompatibilityMode.NativeCombo, fm.CurrentMode);

        var km = new KeyMapper(new Layout(), mock);
        km.SetForegroundMonitor(fm);

        // Reset les recordings après l'init
        mock.SendInputCalls.Clear();

        km.EmitText("@");

        Assert.Single(mock.SendInputCalls);
        var inputs = mock.SendInputCalls[0];
        // RMenu down + VK_0 down + VK_0 up + RMenu up (au minimum)
        Assert.True(inputs.Length >= 4);
        Assert.Equal(0xA5, inputs[0].u.ki.wVk); // VK_RMENU
        Assert.Equal(0x30, inputs[1].u.ki.wVk); // VK_0
        Assert.Equal((ushort)0x0B, inputs[1].u.ki.wScan); // scancode SC00B (pas 0)
    }

    [Fact]
    public void EmitText_NativeCombo_UnknownChar_FallsBackToAltCode()
    {
        var mock = new MockWin32Api
        {
            ScriptedProcessName = "javaw.exe",
            ScriptedFullPath = @"C:\Java\javaw.exe",
            ScriptedModules = new[] { "lwjgl_glfw.dll" },
            CurrentHkl = (IntPtr)0x040C040C
        };
        // 'É' n'est pas scripté → VkKeyScanExW retourne -1 → fallback Alt+code

        var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        var km = new KeyMapper(new Layout(), mock);
        km.SetForegroundMonitor(fm);
        mock.SendInputCalls.Clear();

        km.EmitText("É"); // codepoint 0xC9 = 201

        Assert.Single(mock.SendInputCalls);
        var inputs = mock.SendInputCalls[0];
        // Doit contenir un LAlt down quelque part (Alt+0201 séquence)
        Assert.Contains(inputs, ev => ev.u.ki.wVk == 0xA4); // VK_LMENU
        // Doit contenir un VK_NUMPAD2 (0x62) pour le chiffre 2 du 0201
        Assert.Contains(inputs, ev => ev.u.ki.wVk == 0x62); // VK_NUMPAD2
    }

    [Fact]
    public void EmitText_NativeCombo_BeyondLatin1_FallsBackToUnicode()
    {
        var mock = new MockWin32Api
        {
            ScriptedProcessName = "javaw.exe",
            ScriptedFullPath = @"C:\Java\javaw.exe",
            ScriptedModules = new[] { "SDL2.dll" },
            CurrentHkl = (IntPtr)0x040C040C
        };
        // '≠' (U+2260) hors Win-1252 et non scripté → fallback Unicode

        var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        var km = new KeyMapper(new Layout(), mock);
        km.SetForegroundMonitor(fm);
        mock.SendInputCalls.Clear();

        km.EmitText("≠");

        Assert.Single(mock.SendInputCalls);
        var inputs = mock.SendInputCalls[0];
        // KEYEVENTF_UNICODE → wVk=0, wScan='≠'
        Assert.Equal(2, inputs.Length);
        Assert.Equal(0, inputs[0].u.ki.wVk);
        Assert.Equal('≠', inputs[0].u.ki.wScan);
        Assert.True((inputs[0].u.ki.dwFlags & 0x0004) != 0); // KEYEVENTF_UNICODE
    }

    [Fact]
    public void EmitText_MultipleChars_BatchedInSingleSendInputCall()
    {
        var mock = new MockWin32Api { ScriptedProcessName = "notepad.exe" };
        var km = new KeyMapper(new Layout(), mock);

        km.EmitText("abc");

        // Un seul appel SendInput pour les 3 caractères (batching)
        Assert.Single(mock.SendInputCalls);
        Assert.Equal(6, mock.SendInputCalls[0].Length); // 3 chars × 2 events
    }

    [Fact]
    public void EmitText_SurrogatePair_UsesUnicodeFallback_EvenInNativeCombo()
    {
        var mock = new MockWin32Api
        {
            ScriptedProcessName = "javaw.exe",
            ScriptedFullPath = @"C:\Java\javaw.exe",
            ScriptedModules = new[] { "lwjgl_glfw.dll" },
            CurrentHkl = (IntPtr)0x040C040C
        };

        var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        var km = new KeyMapper(new Layout(), mock);
        km.SetForegroundMonitor(fm);
        mock.SendInputCalls.Clear();

        // 😀 = U+1F600 (surrogate pair) → toujours Unicode
        km.EmitText("😀");

        Assert.Single(mock.SendInputCalls);
        Assert.Equal(4, mock.SendInputCalls[0].Length); // 4 events (down high+low, up high+low)
    }
}

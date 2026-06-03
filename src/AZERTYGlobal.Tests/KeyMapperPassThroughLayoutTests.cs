using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

public class KeyMapperPassThroughLayoutTests : IDisposable
{
    private const uint SC_E01 = 0x02;
    private const uint SC_D01 = 0x10;
    private const uint SC_LCONTROL = 0x1D;

    private const ushort VK_1 = 0x31;
    private const ushort VK_7 = 0x37;
    private const ushort VK_A = 0x41;
    private const ushort VK_Q = 0x51;
    private const ushort VK_LCONTROL = 0xA2;

    private static readonly IntPtr HklFrAzerty = (IntPtr)0x040C040C;
    private static readonly IntPtr HklUsQwerty = (IntPtr)0x04090409;
    private static readonly short KeyDown = unchecked((short)0x8000);

    private readonly string _tempDir;

    public KeyMapperPassThroughLayoutTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AZGPT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        ConfigManager.OverrideConfigPathForTests(Path.Combine(_tempDir, "config.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ProcessKey_QwertyForeground_DoesNotPassThroughAzertyPositions()
    {
        var mock = new MockWin32Api();
        ScriptFrenchAzerty(mock);
        ScriptUsQwerty(mock);

        using var foreground = CreateForegroundMonitor(mock, HklUsQwerty);
        mock.CurrentHkl = HklFrAzerty; // app thread layout: the old code read this by mistake

        var mapper = new KeyMapper(LayoutWithD01AndE01(), mock);
        mapper.SetForegroundMonitor(foreground);
        mock.SendInputCalls.Clear();

        bool d01Handled = mapper.ProcessKey(VK_Q, SC_D01, 0, true);
        Assert.True(d01Handled);
        AssertUnicodeEmission(mock, 'a');

        mock.SendInputCalls.Clear();

        bool e01Handled = mapper.ProcessKey(VK_1, SC_E01, 0, true);
        Assert.True(e01Handled);
        AssertUnicodeEmission(mock, '&');
    }

    [Fact]
    public void ProcessKey_AzertyForeground_KeepsPassThroughWhenNativeOutputMatches()
    {
        var mock = new MockWin32Api();
        ScriptFrenchAzerty(mock);
        ScriptUsQwerty(mock);

        using var foreground = CreateForegroundMonitor(mock, HklFrAzerty);
        mock.CurrentHkl = HklUsQwerty; // proves pass-through uses the foreground HKL

        var mapper = new KeyMapper(LayoutWithD01AndE01(), mock);
        mapper.SetForegroundMonitor(foreground);
        mock.SendInputCalls.Clear();

        bool handled = mapper.ProcessKey(VK_A, SC_D01, 0, true);

        Assert.False(handled);
        Assert.Empty(mock.SendInputCalls);
    }

    [Fact]
    public void ProcessKey_CtrlD01_QwertyForeground_EmitsCtrlAInsteadOfPassingCtrlQ()
    {
        var mock = new MockWin32Api();
        ScriptFrenchAzerty(mock);
        ScriptUsQwerty(mock);
        mock.AsyncKeyStateScript[VK_LCONTROL] = KeyDown;

        using var foreground = CreateForegroundMonitor(mock, HklUsQwerty);
        mock.CurrentHkl = HklFrAzerty;

        var mapper = new KeyMapper(LayoutWithD01AndE01(), mock);
        mapper.SetForegroundMonitor(foreground);
        mapper.TrackModifiers(VK_LCONTROL, SC_LCONTROL, 0, true);
        mock.SendInputCalls.Clear();

        bool handled = mapper.ProcessKey(VK_Q, SC_D01, 0, true);

        Assert.True(handled);
        Assert.Single(mock.SendInputCalls);
        Assert.Single(mock.SendInputCalls[0]);
        Assert.Equal(VK_A, mock.SendInputCalls[0][0].u.ki.wVk);
    }

    private static Layout LayoutWithD01AndE01()
    {
        var layout = new Layout();
        layout.Keys[SC_D01] = new KeyDefinition
        {
            Position = "D01",
            Scancode = SC_D01,
            Base = "a",
            Shift = "A",
            Caps = "A",
            CapsShift = "a"
        };
        layout.Keys[SC_E01] = new KeyDefinition
        {
            Position = "E01",
            Scancode = SC_E01,
            Base = "&",
            Shift = "1"
        };
        return layout;
    }

    private static ForegroundMonitor CreateForegroundMonitor(MockWin32Api mock, IntPtr hkl)
    {
        mock.CurrentHkl = hkl;
        mock.ScriptedProcessName = "notepad.exe";
        mock.ScriptedFullPath = @"C:\Windows\notepad.exe";
        mock.ScriptedModules = new[] { "kernel32.dll", "user32.dll" };
        mock.ForegroundWindow = (IntPtr)0x1234;
        return new ForegroundMonitor(mock, IntPtr.Zero);
    }

    private static void ScriptFrenchAzerty(MockWin32Api mock)
    {
        mock.MapVirtualKeyScript[(SC_D01, 1, HklFrAzerty)] = VK_A;
        mock.VkKeyScanScript[('a', HklFrAzerty)] = (short)VK_A;

        mock.MapVirtualKeyScript[(SC_E01, 1, HklFrAzerty)] = VK_1;
        mock.VkKeyScanScript[('&', HklFrAzerty)] = (short)VK_1;
    }

    private static void ScriptUsQwerty(MockWin32Api mock)
    {
        mock.MapVirtualKeyScript[(SC_D01, 1, HklUsQwerty)] = VK_Q;
        mock.VkKeyScanScript[('a', HklUsQwerty)] = (short)VK_A;

        mock.MapVirtualKeyScript[(SC_E01, 1, HklUsQwerty)] = VK_1;
        mock.VkKeyScanScript[('&', HklUsQwerty)] = (short)((1 << 8) | VK_7);
    }

    private static void AssertUnicodeEmission(MockWin32Api mock, char expected)
    {
        Assert.Single(mock.SendInputCalls);
        var inputs = mock.SendInputCalls[0];
        Assert.Equal(2, inputs.Length);
        Assert.Equal(0, inputs[0].u.ki.wVk);
        Assert.Equal(expected, inputs[0].u.ki.wScan);
        Assert.True((inputs[0].u.ki.dwFlags & 0x0004) != 0);
    }
}

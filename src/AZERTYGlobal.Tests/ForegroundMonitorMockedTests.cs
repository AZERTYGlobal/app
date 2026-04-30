using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

/// <summary>
/// Tests d'intégration ForegroundMonitor via MockWin32Api.
/// Notes : tests de fumée (pas de timing réel). Le debounce 100 ms n'est pas testé
/// car bypassé via trayHwnd = IntPtr.Zero (Recompute synchrone à la place).
/// </summary>
public class ForegroundMonitorMockedTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public ForegroundMonitorMockedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AZGFG_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        ConfigManager.OverrideConfigPathForTests(_configPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static MockWin32Api SetupMock(string? processName, string? fullPath = null, string[]? modules = null)
    {
        var mock = new MockWin32Api
        {
            ScriptedProcessName = processName,
            ScriptedFullPath = fullPath,
            ScriptedModules = modules,
            ForegroundWindow = processName == null ? IntPtr.Zero : (IntPtr)0x1234,
        };
        return mock;
    }

    [Fact]
    public void Recompute_NoForeground_ReturnsDefault()
    {
        var mock = SetupMock(null);
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.Equal(CompatibilityMode.Default, fm.CurrentMode);
        Assert.Null(fm.CurrentProcessName);
    }

    [Fact]
    public void Recompute_AntiCheatProcess_ReturnsDisabledAntiCheat()
    {
        var mock = SetupMock("VALORANT-Win64-Shipping.exe", @"C:\Riot\Valorant\VALORANT-Win64-Shipping.exe");
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.Equal(CompatibilityMode.DisabledAntiCheat, fm.CurrentMode);
    }

    [Fact]
    public void Recompute_GameFrameworkLoaded_ReturnsNativeCombo()
    {
        var mock = SetupMock("javaw.exe", @"C:\Java\javaw.exe",
            modules: new[] { "kernel32.dll", "lwjgl_glfw.dll" });
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.Equal(CompatibilityMode.NativeCombo, fm.CurrentMode);
    }

    [Fact]
    public void Recompute_NormalApp_ReturnsDefault()
    {
        var mock = SetupMock("notepad.exe", @"C:\Windows\notepad.exe",
            modules: new[] { "kernel32.dll", "user32.dll" });
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.Equal(CompatibilityMode.Default, fm.CurrentMode);
    }

    [Fact]
    public void OverrideForceOn_OnNonAntiCheat_AppliesNativeCombo()
    {
        ConfigManager.SetCompatibilityOverride("MyApp.exe", "forceOn");
        var mock = SetupMock("MyApp.exe", @"C:\MyApp\MyApp.exe");
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.Equal(CompatibilityMode.NativeCombo, fm.CurrentMode);
    }

    [Fact]
    public void OverrideForceOn_OnAntiCheat_IsOverridden_ToDisabled()
    {
        // Sécurité utilisateur : un override forceOn sur un process anti-cheat
        // doit être ignoré et le process désactivé totalement.
        ConfigManager.SetCompatibilityOverride("VALORANT.exe", "forceOn");
        var mock = SetupMock("VALORANT.exe", @"C:\Riot\Valorant\VALORANT.exe");
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.Equal(CompatibilityMode.DisabledAntiCheat, fm.CurrentMode);
    }

    [Fact]
    public void OverrideForceOff_AppliesDisabled()
    {
        ConfigManager.SetCompatibilityOverride("Notepad.exe", "forceOff");
        var mock = SetupMock("Notepad.exe", @"C:\Windows\notepad.exe");
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.Equal(CompatibilityMode.DisabledAntiCheat, fm.CurrentMode);
    }

    [Fact]
    public void DegradedMode_WhenSetWinEventHookFails()
    {
        var mock = new MockWin32Api { ShouldFailSetWinEventHook = true };
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        Assert.False(fm.IsHookInstalled);
        // Recompute fonctionne quand même (mode dégradé : juste pas d'updates auto)
        Assert.Equal(CompatibilityMode.Default, fm.CurrentMode);
    }

    [Fact]
    public void Dispose_CallsUnhookWinEvent()
    {
        var mock = SetupMock("notepad.exe");
        var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        fm.Dispose();
        Assert.True(mock.UnhookWinEventCalled);
    }

    [Fact]
    public void ForegroundChangedEvent_FiresOnSimulatedChange()
    {
        var mock = SetupMock("notepad.exe");
        using var fm = new ForegroundMonitor(mock, IntPtr.Zero);
        int eventCount = 0;
        fm.ForegroundChanged += () => eventCount++;

        // Simuler un changement de foreground via le mock
        mock.SimulateForegroundChange("javaw.exe", @"C:\javaw.exe", (IntPtr)0x040C040C,
            modules: new[] { "lwjgl_glfw.dll" });

        // Le callback du WinEventDelegate appelle Recompute() (puisque _trayHwnd = IntPtr.Zero)
        Assert.True(eventCount >= 1, "ForegroundChanged should fire at least once");
        Assert.Equal(CompatibilityMode.NativeCombo, fm.CurrentMode);
    }
}

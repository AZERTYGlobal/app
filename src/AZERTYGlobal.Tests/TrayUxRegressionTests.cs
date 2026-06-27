using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

public class TrayUxRegressionTests
{
    [Fact]
    public void ShortcutMatching_UsesLayoutScancodeForLetterShortcuts()
    {
        var layout = new Layout();
        layout.Keys[0x10] = new KeyDefinition { Scancode = 0x10, Position = "C01", Base = "q" };
        layout.Keys[0x11] = new KeyDefinition { Scancode = 0x11, Position = "C02", Base = "w" };

        var mapper = new KeyMapper(layout, new MockWin32Api());

        Assert.True(mapper.MatchesShortcutKey(0x51, eventVk: 0x44, scanCode: 0x10));
        Assert.False(mapper.MatchesShortcutKey(0x51, eventVk: 0x51, scanCode: 0x1E));
    }

    [Fact]
    public void VirtualKeyboard_PrefersTrayReferenceWindowOverForegroundTrayWindow()
    {
        var appWindow = (IntPtr)0x1234;
        var trayWindow = (IntPtr)0x5678;

        Assert.Equal(appWindow, VirtualKeyboard.ChooseMonitorReferenceWindow(
            preferredWindow: appWindow,
            foregroundWindow: trayWindow,
            ignoredWindow: trayWindow));
    }

    [Fact]
    public void VirtualKeyboard_IgnoresHiddenTrayWindowAsMonitorReference()
    {
        var trayWindow = (IntPtr)0x5678;

        Assert.Equal(IntPtr.Zero, VirtualKeyboard.ChooseMonitorReferenceWindow(
            preferredWindow: trayWindow,
            foregroundWindow: trayWindow,
            ignoredWindow: trayWindow));
    }
}

using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

public class KeyMapperCtrlRegressionTests
{
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_LCONTROL = 0xA2;
    private const uint SC_SPACE = 0x39;
    private const uint SC_LCONTROL = 0x1D;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly short KeyDown = unchecked((short)0x8000);

    private static Layout LayoutWithSpace()
    {
        var layout = new Layout();
        layout.Keys[SC_SPACE] = new KeyDefinition
        {
            Position = "A03",
            Scancode = SC_SPACE,
            Base = " ",
            Shift = " "
        };
        return layout;
    }

    [Fact]
    public void ProcessKey_CtrlSpace_ReleasesSyntheticSpace_WhenCtrlReleasedBeforeSpace()
    {
        var mock = new MockWin32Api();
        mock.AsyncKeyStateScript[VK_LCONTROL] = KeyDown;
        mock.VkKeyScanScript[(' ', mock.CurrentHkl)] = (short)VK_SPACE;

        var mapper = new KeyMapper(LayoutWithSpace(), mock);
        mapper.TrackModifiers(VK_LCONTROL, SC_LCONTROL, 0, true);

        bool downHandled = mapper.ProcessKey(VK_SPACE, SC_SPACE, 0, true);

        Assert.True(downHandled);
        Assert.Single(mock.SendInputCalls);
        Assert.Single(mock.SendInputCalls[0]);
        Assert.Equal(VK_SPACE, mock.SendInputCalls[0][0].u.ki.wVk);
        Assert.Equal(0u, mock.SendInputCalls[0][0].u.ki.dwFlags & KEYEVENTF_KEYUP);

        mock.AsyncKeyStateScript[VK_LCONTROL] = 0;
        mapper.TrackModifiers(VK_LCONTROL, SC_LCONTROL, 0, false);

        bool upHandled = mapper.ProcessKey(VK_SPACE, SC_SPACE, 0, false);

        Assert.True(upHandled);
        Assert.Equal(2, mock.SendInputCalls.Count);
        Assert.Single(mock.SendInputCalls[1]);
        Assert.Equal(VK_SPACE, mock.SendInputCalls[1][0].u.ki.wVk);
        Assert.True((mock.SendInputCalls[1][0].u.ki.dwFlags & KEYEVENTF_KEYUP) != 0);
    }

    [Fact]
    public void ClearPassedThroughKeys_ReleasesSyntheticCtrlSpaceKeyDown()
    {
        var mock = new MockWin32Api();
        mock.AsyncKeyStateScript[VK_LCONTROL] = KeyDown;
        mock.VkKeyScanScript[(' ', mock.CurrentHkl)] = (short)VK_SPACE;

        var mapper = new KeyMapper(LayoutWithSpace(), mock);
        mapper.TrackModifiers(VK_LCONTROL, SC_LCONTROL, 0, true);

        mapper.ProcessKey(VK_SPACE, SC_SPACE, 0, true);
        mapper.ClearPassedThroughKeys();

        Assert.Equal(2, mock.SendInputCalls.Count);
        Assert.Single(mock.SendInputCalls[1]);
        Assert.Equal(VK_SPACE, mock.SendInputCalls[1][0].u.ki.wVk);
        Assert.True((mock.SendInputCalls[1][0].u.ki.dwFlags & KEYEVENTF_KEYUP) != 0);
    }
}

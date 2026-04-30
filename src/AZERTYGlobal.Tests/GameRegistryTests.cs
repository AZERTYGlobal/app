using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

public class GameRegistryTests
{
    // ────────────────────────────────────────────────────────────────
    // IsAntiCheatProcess — niveau 1 sous-chaîne
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("valorant.exe")]
    [InlineData("VALORANT-Win64-Shipping.exe")]   // sous-chaîne dans nom long
    [InlineData("VALORANT.exe")]                   // casse différente
    [InlineData("FortniteClient-Win64-Shipping.exe")]
    [InlineData("r5apex.exe")]
    [InlineData("r5apex_dx12.exe")]
    [InlineData("BlackOps6.exe")]
    [InlineData("RobloxPlayerBeta.exe")]
    [InlineData("GenshinImpact.exe")]
    [InlineData("EscapeFromTarkov.exe")]
    public void IsAntiCheatProcess_Level1_Substring_Returns_True(string processName)
    {
        Assert.True(GameRegistry.IsAntiCheatProcess(processName, null));
    }

    // ────────────────────────────────────────────────────────────────
    // IsAntiCheatProcess — niveau 1 match exact (cod.exe)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsAntiCheatProcess_CodExe_Returns_True()
    {
        Assert.True(GameRegistry.IsAntiCheatProcess("cod.exe", @"C:\Games\CallOfDuty\cod.exe"));
        Assert.True(GameRegistry.IsAntiCheatProcess("COD.EXE", null)); // casse insensible
    }

    [Fact]
    public void IsAntiCheatProcess_CodVariant_Returns_False()
    {
        // « cod » seul ne match pas en sous-chaîne. Seul `cod.exe` exact match.
        Assert.False(GameRegistry.IsAntiCheatProcess("hot-cod-recipe.exe", null));
    }

    // ────────────────────────────────────────────────────────────────
    // IsAntiCheatProcess — ace.exe avec/sans \Tencent\ dans le chemin
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsAntiCheatProcess_AceExe_TencentPath_Returns_True()
    {
        Assert.True(GameRegistry.IsAntiCheatProcess(
            "ace.exe", @"C:\Program Files\Tencent\AntiCheat\ace.exe"));
    }

    [Fact]
    public void IsAntiCheatProcess_AceExe_OtherPath_Returns_False()
    {
        // Faux positif évité : ace.exe d'un autre éditeur n'est pas détecté
        Assert.False(GameRegistry.IsAntiCheatProcess(
            "ace.exe", @"C:\Program Files\SomeUtility\ace.exe"));
    }

    [Fact]
    public void IsAntiCheatProcess_AceExe_NoPath_Returns_False()
    {
        // Sans chemin, on ne peut pas confirmer que c'est Tencent ACE
        Assert.False(GameRegistry.IsAntiCheatProcess("ace.exe", null));
    }

    // ────────────────────────────────────────────────────────────────
    // IsAntiCheatProcess — niveau 2
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("destiny2.exe")]
    [InlineData("RustClient.exe")]
    [InlineData("FC26.exe")]
    [InlineData("HaloInfinite.exe")]
    public void IsAntiCheatProcess_Level2_Returns_True(string processName)
    {
        Assert.True(GameRegistry.IsAntiCheatProcess(processName, null));
    }

    // ────────────────────────────────────────────────────────────────
    // IsAntiCheatProcess — faux positifs à éviter
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("notepad.exe")]
    [InlineData("chrome.exe")]
    [InlineData("Code.exe")]
    [InlineData("explorer.exe")]
    [InlineData("Discord.exe")]
    [InlineData("")]
    public void IsAntiCheatProcess_Common_Apps_Returns_False(string processName)
    {
        Assert.False(GameRegistry.IsAntiCheatProcess(processName, @"C:\Path\" + processName));
    }

    [Fact]
    public void IsAntiCheatProcess_Null_Returns_False()
    {
        Assert.False(GameRegistry.IsAntiCheatProcess(null, null));
    }

    // ────────────────────────────────────────────────────────────────
    // HasGameFrameworkLoaded
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void HasGameFrameworkLoaded_GlfwPresent_Returns_True()
    {
        var modules = new[] { "kernel32.dll", "user32.dll", "glfw3.dll" };
        Assert.True(GameRegistry.HasGameFrameworkLoaded(modules));
    }

    [Fact]
    public void HasGameFrameworkLoaded_LwjglMinecraft_Returns_True()
    {
        var modules = new[] { "javaw.exe", "lwjgl_glfw.dll", "opengl32.dll" };
        Assert.True(GameRegistry.HasGameFrameworkLoaded(modules));
    }

    [Fact]
    public void HasGameFrameworkLoaded_UnityPlayer_Returns_True()
    {
        var modules = new[] { "kernel32.dll", "UnityPlayer.dll" }; // casse différente
        Assert.True(GameRegistry.HasGameFrameworkLoaded(modules));
    }

    [Fact]
    public void HasGameFrameworkLoaded_OnlyStandardDlls_Returns_False()
    {
        // D3D11/OpenGL ne suffisent pas (Notepad, Chrome les chargent aussi)
        var modules = new[] { "kernel32.dll", "user32.dll", "d3d11.dll", "opengl32.dll" };
        Assert.False(GameRegistry.HasGameFrameworkLoaded(modules));
    }

    [Fact]
    public void HasGameFrameworkLoaded_Empty_Returns_False()
    {
        Assert.False(GameRegistry.HasGameFrameworkLoaded(Array.Empty<string>()));
        Assert.False(GameRegistry.HasGameFrameworkLoaded(null));
    }
}

using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

/// <summary>
/// Tests du bloc compatibility de ConfigManager : CRUD overrides + persistance JSON
/// + tolérance des champs inconnus (downgrade v0.9.7 → v0.9.6 ne plante pas).
/// </summary>
public class ConfigManagerCompatTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public ConfigManagerCompatTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AZGTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        ConfigManager.OverrideConfigPathForTests(_configPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void GetCompatibilityOverride_NoFile_ReturnsNull()
    {
        Assert.Null(ConfigManager.GetCompatibilityOverride("anything.exe"));
    }

    [Fact]
    public void SetCompatibilityOverride_ForceOn_PersistsAndReadsBack()
    {
        ConfigManager.SetCompatibilityOverride("Minecraft.exe", "forceOn");
        Assert.Equal("forceOn", ConfigManager.GetCompatibilityOverride("Minecraft.exe"));
        // Persistance disque
        Assert.True(File.Exists(_configPath));
        var json = File.ReadAllText(_configPath);
        Assert.Contains("compatibility", json);
        Assert.Contains("Minecraft.exe", json);
        Assert.Contains("forceOn", json);
    }

    [Fact]
    public void SetCompatibilityOverride_CaseInsensitive_Read()
    {
        ConfigManager.SetCompatibilityOverride("Minecraft.exe", "forceOn");
        Assert.Equal("forceOn", ConfigManager.GetCompatibilityOverride("minecraft.exe"));
        Assert.Equal("forceOn", ConfigManager.GetCompatibilityOverride("MINECRAFT.EXE"));
    }

    [Fact]
    public void SetCompatibilityOverride_Null_Removes()
    {
        ConfigManager.SetCompatibilityOverride("VLC.exe", "forceOff");
        Assert.Equal("forceOff", ConfigManager.GetCompatibilityOverride("VLC.exe"));

        ConfigManager.SetCompatibilityOverride("VLC.exe", null);
        Assert.Null(ConfigManager.GetCompatibilityOverride("VLC.exe"));
    }

    [Fact]
    public void SetCompatibilityOverride_InvalidMode_Ignored()
    {
        ConfigManager.SetCompatibilityOverride("App.exe", "garbage");
        Assert.Null(ConfigManager.GetCompatibilityOverride("App.exe"));
    }

    [Fact]
    public void GetAllCompatibilityOverrides_ReturnsCopy()
    {
        ConfigManager.SetCompatibilityOverride("A.exe", "forceOn");
        ConfigManager.SetCompatibilityOverride("B.exe", "forceOff");

        var all = ConfigManager.GetAllCompatibilityOverrides();
        Assert.Equal(2, all.Count);
        Assert.Equal("forceOn", all["A.exe"]);
        Assert.Equal("forceOff", all["B.exe"]);
    }

    [Fact]
    public void Downgrade_V0_9_6_Tolerates_CompatibilityBlock()
    {
        // Simuler un config.json de v0.9.7 avec le bloc compatibility
        File.WriteAllText(_configPath, @"{
            ""showOnboardingAtStartup"": true,
            ""compatibility"": { ""Foo.exe"": ""forceOn"" },
            ""compatibilityDebugLog"": false
        }");

        // v0.9.6 ne connaît pas ces clés mais ne doit pas planter à la lecture.
        // On simule en relisant via un OverrideConfigPathForTests qui force le rechargement.
        ConfigManager.OverrideConfigPathForTests(_configPath);

        // Les champs connus restent lisibles
        Assert.True(ConfigManager.ShowOnboardingAtStartup);
        // Le bloc compatibility est correctement chargé
        Assert.Equal("forceOn", ConfigManager.GetCompatibilityOverride("Foo.exe"));
    }

    [Fact]
    public void CompatibilityDebugLog_DefaultsFalse_Toggleable()
    {
        Assert.False(ConfigManager.CompatibilityDebugLog);
        ConfigManager.SetCompatibilityDebugLog(true);
        Assert.True(ConfigManager.CompatibilityDebugLog);
        ConfigManager.SetCompatibilityDebugLog(false);
        Assert.False(ConfigManager.CompatibilityDebugLog);
    }

    [Fact]
    public void EmptyProcessName_NoOp()
    {
        ConfigManager.SetCompatibilityOverride("", "forceOn");
        Assert.Empty(ConfigManager.GetAllCompatibilityOverrides());
        Assert.Null(ConfigManager.GetCompatibilityOverride(""));
    }
}

using System.Text.Json;
using AZERTYGlobal;
using Xunit;

namespace AZERTYGlobal.Tests;

public class ResourceAlignmentTests
{
    [Fact]
    public void EmbeddedLayout_MatchesCurrentPublicShortcuts()
    {
        var layout = LayoutLoader.LoadFromResource();

        Assert.Equal(29, layout.DeadKeys.Count);
        Assert.Equal("#", Key(layout, "E00").Shift);
        Assert.Equal("#", Key(layout, "B09").AltGr);
        Assert.Equal("^", Key(layout, "D08").AltGr);
        Assert.Equal("`", Key(layout, "C09").AltGr);
        Assert.Equal("~", Key(layout, "B06").AltGr);
        Assert.Equal("\u202F", Key(layout, "A03").AltGr);
        Assert.Equal("\u00A0", Key(layout, "A03").ShiftAltGr);
        Assert.Equal("dk_extended_latin", Key(layout, "E06").AltGr);
        Assert.Equal("\u2011", Key(layout, "E06").ShiftAltGr);
        Assert.Equal("dk_hook", Key(layout, "E01").AltGr);
        Assert.Equal("dk_horn", Key(layout, "E01").ShiftAltGr);
        Assert.Equal("dk_dot_below", Key(layout, "E03").AltGr);
        Assert.Equal("dk_dot_above", Key(layout, "E03").ShiftAltGr);
    }

    [Fact]
    public void EmbeddedCharacterIndex_MatchesCurrentCharacterCount()
    {
        using var doc = LoadCharacterIndex();
        var root = doc.RootElement;
        var characters = root.GetProperty("characters");

        Assert.Equal(1032, root.GetProperty("totalCharacters").GetInt32());
        Assert.Equal(1032, characters.EnumerateObject().Count());
        Assert.True(characters.TryGetProperty("\u02BC", out _));
        Assert.True(characters.TryGetProperty("\u02BB", out _));
    }

    [Fact]
    public void EmbeddedCharacterIndex_LocksCriticalFinalMetadata()
    {
        using var doc = LoadCharacterIndex();
        var characters = doc.RootElement.GetProperty("characters");

        AssertDirectMethod(Character(characters, "#"), "Backquote", "Shift", recommended: true);
        AssertDirectMethod(Character(characters, "#"), "Period", "AltGr", recommended: false);
        AssertHasAlias(Character(characters, "#"), "hashtag");

        AssertDirectMethod(Character(characters, "^"), "KeyI", "AltGr", recommended: true);
        AssertDirectMethod(Character(characters, "`"), "KeyL", "AltGr", recommended: true);
        AssertHasAlias(Character(characters, "`"), "backtick");
        AssertDirectMethod(Character(characters, "~"), "KeyN", "AltGr", recommended: true);

        Assert.Equal("SIGNE INFÉRIEUR À", Character(characters, "<").GetProperty("unicodeNameFr").GetString());
        AssertHasAlias(Character(characters, "<"), "chevron ouvrant");
        Assert.Equal("SIGNE SUPÉRIEUR À", Character(characters, ">").GetProperty("unicodeNameFr").GetString());
        AssertHasAlias(Character(characters, ">"), "chevron fermant");

        AssertDirectMethod(Character(characters, "\u202F"), "Space", "AltGr", recommended: true);
        AssertDirectMethod(Character(characters, "\u00A0"), "Space", "Shift+AltGr", recommended: true);
        AssertDirectMethod(Character(characters, "\u2011"), "Digit6", "Shift+AltGr", recommended: true);

        AssertDeadKeyActivation(Character(characters, "dk:hook"), "dk_hook", "Digit1", "AltGr");
        AssertDeadKeyActivation(Character(characters, "dk:horn"), "dk_horn", "Digit1", "Shift+AltGr");
        AssertDeadKeyActivation(Character(characters, "dk:dot_below"), "dk_dot_below", "Digit3", "AltGr");
        AssertDeadKeyActivation(Character(characters, "dk:dot_above"), "dk_dot_above", "Digit3", "Shift+AltGr");
        AssertDeadKeyActivation(Character(characters, "dk:extended_latin"), "dk_extended_latin", "Digit6", "AltGr");

        AssertDeadKeyMethod(Character(characters, "\u0253"), "dk_acute", "KeyB", "Base", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u0181"), "dk_acute", "KeyB", "Shift", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u0199"), "dk_circumflex", "KeyK", "Base", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u0198"), "dk_circumflex", "KeyK", "Shift", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u0272"), "dk_extended_latin", "KeyJ", "Base", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u019D"), "dk_extended_latin", "KeyJ", "Shift", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u0269"), "dk_extended_latin", "KeyI", "Base", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u0196"), "dk_extended_latin", "KeyI", "Shift", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u0188"), "dk_hook", "KeyC", "Base", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u0187"), "dk_hook", "KeyC", "Shift", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u01A5"), "dk_hook", "KeyP", "Base", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u01A4"), "dk_hook", "KeyP", "Shift", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u02BC"), "dk_acute", "Digit4", "Base", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u02BC"), "dk_extended_latin", "Digit4", "Base", recommended: false);
        AssertDeadKeyMethod(Character(characters, "\u2116"), "dk_misc_symbols", "Backquote", "Base", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u2116"), "dk_misc_symbols", "Backquote", "Shift", recommended: false);
        AssertDeadKeyMethod(Character(characters, "\u2209"), "dk_scientific", "KeyE", "AltGr", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u2286"), "dk_scientific", "KeyJ", "AltGr", recommended: true);
        AssertDeadKeyMethod(Character(characters, "\u2287"), "dk_scientific", "KeyK", "AltGr", recommended: true);
    }

    private static KeyDefinition Key(Layout layout, string position)
    {
        return layout.Keys.Values.Single(key => key.Position == position);
    }

    private static JsonDocument LoadCharacterIndex()
    {
        using var stream = typeof(LayoutLoader).Assembly.GetManifestResourceStream("character-index.json")
            ?? throw new InvalidOperationException("character-index.json resource missing");
        return JsonDocument.Parse(stream);
    }

    private static JsonElement Character(JsonElement characters, string value)
    {
        Assert.True(characters.TryGetProperty(value, out var entry), $"Entry missing: {value}");
        return entry;
    }

    private static void AssertHasAlias(JsonElement entry, string alias)
    {
        Assert.True(entry.TryGetProperty("frenchAliases", out var aliases), $"Aliases missing for {entry}");
        Assert.Contains(aliases.EnumerateArray(), item => item.GetString() == alias);
    }

    private static void AssertDirectMethod(JsonElement entry, string key, string layer, bool recommended)
    {
        var method = entry.GetProperty("methods").EnumerateArray().SingleOrDefault(m =>
            m.GetProperty("type").GetString() == "direct" &&
            m.GetProperty("key").GetString() == key &&
            m.GetProperty("layer").GetString() == layer);

        Assert.NotEqual(JsonValueKind.Undefined, method.ValueKind);
        bool isRecommended = method.TryGetProperty("recommended", out var rec) && rec.GetBoolean();
        Assert.Equal(recommended, isRecommended);
    }

    private static void AssertDeadKeyActivation(JsonElement entry, string deadKey, string key, string layer)
    {
        var method = entry.GetProperty("methods").EnumerateArray().SingleOrDefault(m =>
            m.GetProperty("type").GetString() == "deadkey_activation" &&
            m.GetProperty("deadkey").GetString() == deadKey &&
            m.GetProperty("key").GetString() == key &&
            m.GetProperty("layer").GetString() == layer &&
            m.TryGetProperty("recommended", out var rec) &&
            rec.GetBoolean());

        Assert.NotEqual(JsonValueKind.Undefined, method.ValueKind);
    }

    private static void AssertDeadKeyMethod(JsonElement entry, string deadKey, string key, string layer, bool recommended)
    {
        var method = entry.GetProperty("methods").EnumerateArray().SingleOrDefault(m =>
            m.GetProperty("type").GetString() == "deadkey" &&
            m.GetProperty("deadkey").GetString() == deadKey &&
            m.GetProperty("key").GetString() == key &&
            m.GetProperty("layer").GetString() == layer);

        Assert.NotEqual(JsonValueKind.Undefined, method.ValueKind);
        bool isRecommended = method.TryGetProperty("recommended", out var rec) && rec.GetBoolean();
        Assert.Equal(recommended, isRecommended);
    }
}

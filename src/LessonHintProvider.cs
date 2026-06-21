using System.Text.Json;

namespace AZERTYGlobal;

internal sealed record LessonHintMethod(
    string Type,
    string? Key,
    string? Layer,
    string? DeadKey,
    string? DkActivationKey,
    string? DkActivationLayer)
{
    public bool IsDeadKey => string.Equals(Type, "deadkey", StringComparison.OrdinalIgnoreCase);

    public string? DeadKeyToken => !string.IsNullOrEmpty(DeadKey) && DeadKey.StartsWith("dk_", StringComparison.Ordinal)
        ? "dk:" + DeadKey[3..]
        : null;
}

internal readonly record struct LessonHintKeyStep(string? Key, string? Layer);

internal sealed class LessonHintProvider
{
    private readonly Dictionary<string, LessonHintMethod> _methods = new(StringComparer.Ordinal);

    public LessonHintProvider()
    {
        LoadCharacterIndex();
    }

    public LessonHintMethod? GetRecommendedMethod(char ch)
    {
        return _methods.TryGetValue(ch.ToString(), out var method) ? method : null;
    }

    public void AddRequiredCharacters(IEnumerable<string> characters, ISet<string> visibleCharacters)
    {
        foreach (var value in characters)
            AddRequiredCharacters(value, visibleCharacters);
    }

    public void AddRequiredCharacters(string text, ISet<string> visibleCharacters)
    {
        foreach (char ch in text)
        {
            if (ch == '\r' || ch == '\n')
                continue;

            visibleCharacters.Add(ch.ToString());
            AddRequiredDeadKey(ch, visibleCharacters);
        }
    }

    public void AddRequiredDeadKey(char ch, ISet<string> visibleCharacters)
    {
        var method = GetRecommendedMethod(ch);
        if (!string.IsNullOrEmpty(method?.DeadKeyToken))
            visibleCharacters.Add(method.DeadKeyToken);
    }

    public static LessonHintKeyStep GetCurrentStep(LessonHintMethod method, string? activeDeadKey)
    {
        if (method.IsDeadKey &&
            !string.IsNullOrEmpty(method.DeadKey) &&
            !string.Equals(activeDeadKey, method.DeadKey, StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(method.DkActivationKey))
            return new LessonHintKeyStep(method.DkActivationKey, method.DkActivationLayer);

        return new LessonHintKeyStep(method.Key, method.Layer);
    }

    private void LoadCharacterIndex()
    {
        try
        {
            using var stream = typeof(LessonHintProvider).Assembly.GetManifestResourceStream("character-index.json");
            if (stream == null) return;
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("characters", out var characters)) return;

            var dkActivations = new Dictionary<string, (string Key, string Layer)>(StringComparer.Ordinal);
            foreach (var entry in characters.EnumerateObject())
            {
                if (!entry.Name.StartsWith("dk:", StringComparison.Ordinal)) continue;
                if (!entry.Value.TryGetProperty("methods", out var methods) || methods.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var method in methods.EnumerateArray())
                {
                    if (!string.Equals(ReadString(method, "type"), "deadkey_activation", StringComparison.Ordinal))
                        continue;
                    string? deadKey = ReadString(method, "deadkey");
                    string? key = ReadString(method, "key");
                    string? layer = ReadString(method, "layer");
                    if (!string.IsNullOrEmpty(deadKey) && !string.IsNullOrEmpty(key))
                        dkActivations[deadKey] = (key, layer ?? "Base");
                    break;
                }
            }

            foreach (var entry in characters.EnumerateObject())
            {
                if (entry.Name.StartsWith("dk:", StringComparison.Ordinal)) continue;
                if (!entry.Value.TryGetProperty("methods", out var methods) || methods.ValueKind != JsonValueKind.Array)
                    continue;

                JsonElement? selected = null;
                foreach (var method in methods.EnumerateArray())
                {
                    if (method.TryGetProperty("recommended", out var rec) && rec.ValueKind == JsonValueKind.True)
                    {
                        selected = method;
                        break;
                    }
                    selected ??= method;
                }

                if (!selected.HasValue) continue;
                var el = selected.Value;
                string type = ReadString(el, "type") ?? "";
                string? key = ReadString(el, "key");
                string? layer = ReadString(el, "layer");
                string? deadKey = ReadString(el, "deadkey");
                dkActivations.TryGetValue(deadKey ?? "", out var activation);
                _methods[entry.Name] = new LessonHintMethod(
                    type,
                    key,
                    layer,
                    deadKey,
                    activation.Key,
                    activation.Layer);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            ConfigManager.Log("LessonHintProvider.LoadCharacterIndex", ex);
        }
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

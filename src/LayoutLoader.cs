// Charge le layout AZERTY Global depuis le JSON
using System.Text.Json;

namespace AZERTYGlobal;

/// <summary>
/// Charge et parse le fichier AZERTY Global Beta.json.
/// Construit les tables de mapping en mémoire.
/// </summary>
static class LayoutLoader
{
    /// <summary>Charge le layout depuis une ressource embarquée.</summary>
    public static Layout LoadFromResource(string resourceName = "layout.json")
    {
        using var stream = typeof(LayoutLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Ressource '{resourceName}' introuvable dans l'assemblage.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static Layout Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var layout = new Layout();

        // Charger les touches depuis "rows"
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            foreach (var key in row.GetProperty("keys").EnumerateArray())
            {
                var scancodeStr = key.GetProperty("scancode").GetString() ?? "0";
                // Format attendu : "SC029" (préfixe SC + hex) ou "0x29" ou numérique
                uint scancode;
                if (scancodeStr.StartsWith("SC", StringComparison.OrdinalIgnoreCase))
                    scancode = Convert.ToUInt32(scancodeStr[2..], 16);
                else if (scancodeStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    scancode = Convert.ToUInt32(scancodeStr, 16);
                else
                    scancode = uint.Parse(scancodeStr);
                var keyDef = new KeyDefinition
                {
                    Position = key.GetProperty("position").GetString() ?? "",
                    Scancode = scancode,
                    Base = GetStringOrNull(key, "base"),
                    Shift = GetStringOrNull(key, "shift"),
                    AltGr = GetStringOrNull(key, "alt_gr"),
                    ShiftAltGr = GetStringOrNull(key, "shift_alt_gr"),
                    Caps = GetStringOrNull(key, "caps"),
                    CapsShift = GetStringOrNull(key, "caps_shift"),
                    CapsAltGr = GetStringOrNull(key, "caps_alt_gr"),
                    CapsShiftAltGr = GetStringOrNull(key, "caps_shift_alt_gr"),
                };
                layout.Keys[scancode] = keyDef;
            }
        }

        // Charger les touches mortes depuis "dead_keys"
        if (root.TryGetProperty("dead_keys", out var deadKeys))
        {
            foreach (var dk in deadKeys.EnumerateObject())
            {
                var deadKey = new DeadKeyDefinition
                {
                    Name = dk.Name,
                    Description = GetStringOrNull(dk.Value, "description") ?? dk.Name,
                };

                if (dk.Value.TryGetProperty("table", out var table))
                {
                    foreach (var entry in table.EnumerateObject())
                    {
                        deadKey.Table[entry.Name] = entry.Value.GetString() ?? "";
                    }
                }

                layout.DeadKeys[dk.Name] = deadKey;
            }
        }

        return layout;
    }

    private static string? GetStringOrNull(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }
}

/// <summary>
/// Représente la disposition clavier complète en mémoire.
/// </summary>
class Layout
{
    /// <summary>Touches indexées par scancode.</summary>
    public Dictionary<uint, KeyDefinition> Keys { get; } = new();

    /// <summary>Touches mortes indexées par identifiant (ex: "dk_circumflex").</summary>
    public Dictionary<string, DeadKeyDefinition> DeadKeys { get; } = new();
}

/// <summary>
/// Définition d'une touche avec ses 8 couches possibles.
/// </summary>
class KeyDefinition
{
    public string Position { get; set; } = "";
    public uint Scancode { get; set; }
    public string? Base { get; set; }
    public string? Shift { get; set; }
    public string? AltGr { get; set; }
    public string? ShiftAltGr { get; set; }
    public string? Caps { get; set; }
    public string? CapsShift { get; set; }
    public string? CapsAltGr { get; set; }
    public string? CapsShiftAltGr { get; set; }

    /// <summary>
    /// Retourne le caractère/action pour la couche demandée.
    /// Implémente le Smart Caps Lock : si la couche caps n'est pas définie,
    /// retombe sur la couche base (le Caps Lock n'affecte pas cette touche).
    /// </summary>
    public string? GetOutput(bool shift, bool altGr, bool capsLock)
    {
        if (capsLock)
        {
            if (shift && altGr) return CapsShiftAltGr ?? ShiftAltGr;
            if (altGr) return CapsAltGr ?? AltGr;
            if (shift) return CapsShift ?? Shift;
            return Caps ?? Base;
        }
        else
        {
            if (shift && altGr) return ShiftAltGr;
            if (altGr) return AltGr;
            if (shift) return Shift;
            return Base;
        }
    }
}

/// <summary>
/// Définition d'une touche morte avec sa table de transformation.
/// </summary>
class DeadKeyDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Table : caractère d'entrée → caractère de sortie.</summary>
    public Dictionary<string, string> Table { get; } = new();

    /// <summary>
    /// Applique la touche morte au caractère donné.
    /// Retourne le caractère transformé, ou null si pas de correspondance.
    /// </summary>
    public string? Apply(string input)
    {
        return Table.TryGetValue(input, out var result) ? result : null;
    }

    /// <summary>
    /// Retourne le caractère isolé de la touche morte (espace → diacritique).
    /// </summary>
    public string? GetIsolated()
    {
        return Table.TryGetValue(" ", out var result) ? result : null;
    }
}

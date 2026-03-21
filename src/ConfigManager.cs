// Gestion de la configuration persistante (config.json)
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AZERTYGlobalPortable;

/// <summary>
/// Lit et écrit les préférences utilisateur dans config.json.
/// Mode portable (unpackaged) : à côté de l'exécutable.
/// Mode MSIX (packaged) : dans %LocalAppData%\AZERTY Global Portable\.
/// </summary>
static class ConfigManager
{
    private static readonly string _configPath = GetConfigPath();

    // APPMODEL_ERROR_NO_PACKAGE (15700) = l'app n'est pas dans un package MSIX
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder? packageFullName);

    /// <summary>Indique si l'app tourne dans un package MSIX.</summary>
    public static bool IsPackaged { get; } = DetectPackaged();

    private static bool DetectPackaged()
    {
        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != APPMODEL_ERROR_NO_PACKAGE;
        }
        catch
        {
            return false;
        }
    }

    private static string GetConfigPath()
    {
        if (DetectPackaged())
        {
            // Mode MSIX : dossier du package en lecture seule → utiliser LocalAppData
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AZERTY Global Portable");
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "config.json");
        }

        // Mode portable (unpackaged) : à côté de l'exe
        return Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    private static Dictionary<string, JsonElement>? _cache;

    /// <summary>Vérifie si l'onboarding a déjà été affiché.</summary>
    public static bool OnboardingDone => GetBool("onboardingDone");

    /// <summary>Marque l'onboarding comme terminé.</summary>
    public static void SetOnboardingDone() => SetBool("onboardingDone", true);

    /// <summary>Vérifie si le lancement automatique au démarrage est activé.</summary>
    public static bool AutoStartEnabled => GetBool("autoStartEnabled");

    /// <summary>Active ou désactive le lancement automatique au démarrage.</summary>
    public static void SetAutoStart(bool enabled) => SetBool("autoStartEnabled", enabled);

    /// <summary>
    /// Raccourcis Ctrl+Shift+Lettre interdits car trop courants dans les applications.
    /// Voir aussi : Ctrl+Shift+Verr.Maj (toggle on/off), bloqué séparément via VK_CAPITAL.
    /// </summary>
    private static readonly HashSet<char> BlockedLetters = new()
    {
        'T', // Rouvrir onglet fermé (tous les navigateurs)
        'N', // Navigation privée (Chrome, Edge, Firefox)
        'V', // Coller sans formatage (quasi universel)
        'P', // Palette de commandes (VS Code, Sublime, JetBrains)
        'I', // DevTools (Chrome, Edge, Firefox)
        'S', // Enregistrer sous (Word, Excel, Photoshop, VS Code)
        'Z', // Refaire (Photoshop, Figma, Google Docs)
        'F', // Rechercher dans les fichiers (tous les IDE)
        'C', // Copier (terminal), Inspecter élément (Chrome)
        'B', // Barre favoris (Chrome, Edge), Build (VS Code)
        'R', // Rechargement forcé (navigateurs)
    };

    /// <summary>
    /// Vérifie si une touche est autorisée comme raccourci Ctrl+Shift+touche.
    /// Formats acceptés : lettre (A-Z sauf bloquées), chiffre (0-9), "F1" à "F12".
    /// </summary>
    public static bool IsShortcutAllowed(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        // Touches de fonction F1-F12
        if (ParseFunctionKey(key) > 0) return true;
        // Chiffres 0-9
        if (key.Length == 1 && key[0] >= '0' && key[0] <= '9') return true;
        // Lettres A-Z (sauf bloquées)
        char c = char.ToUpper(key[0]);
        if (key.Length == 1 && c >= 'A' && c <= 'Z') return !BlockedLetters.Contains(c);
        return false;
    }

    /// <summary>Lettre du raccourci clavier virtuel. Défaut : "Q" → Ctrl+Maj+Q.</summary>
    public static string ShortcutVirtualKeyboard
    {
        get => GetString("shortcutVirtualKeyboard") ?? "Q";
        set { if (IsShortcutAllowed(value)) SetString("shortcutVirtualKeyboard", value.ToUpperInvariant()); }
    }

    /// <summary>Lettre du raccourci recherche de caractère. Défaut : "W" → Ctrl+Maj+W.</summary>
    public static string ShortcutCharacterSearch
    {
        get => GetString("shortcutCharacterSearch") ?? "W";
        set { if (IsShortcutAllowed(value)) SetString("shortcutCharacterSearch", value.ToUpperInvariant()); }
    }

    /// <summary>
    /// Parse une touche de fonction "F1"-"F12". Retourne le numéro (1-12) ou 0 si invalide.
    /// </summary>
    private static int ParseFunctionKey(string key)
    {
        if (key.Length >= 2 && key.Length <= 3 &&
            (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key.AsSpan(1), out int n) &&
            n >= 1 && n <= 12)
            return n;
        return 0;
    }

    /// <summary>
    /// Retourne le VK code pour un raccourci configurable.
    /// Accepte : lettre A-Z (sauf bloquées), chiffre 0-9, "F1"-"F12".
    /// Retourne 0 si la touche est invalide ou bloquée.
    /// </summary>
    public static uint GetVkCode(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;

        // Touches de fonction : VK_F1 (0x70) à VK_F12 (0x7B)
        int fn = ParseFunctionKey(key);
        if (fn > 0) return (uint)(0x6F + fn); // 0x6F + 1 = 0x70 = VK_F1

        char c = char.ToUpper(key[0]);

        // Lettres A-Z (bloquer les raccourcis courants)
        if (c >= 'A' && c <= 'Z')
            return BlockedLetters.Contains(c) ? 0 : (uint)c;

        // Chiffres 0-9
        if (c >= '0' && c <= '9') return (uint)c;

        return 0;
    }

    private static bool GetBool(string key)
    {
        EnsureLoaded();
        if (_cache!.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.True)
            return true;
        return false;
    }

    private static void SetBool(string key, bool value)
    {
        EnsureLoaded();
        _cache![key] = JsonDocument.Parse(value ? "true" : "false").RootElement.Clone();
        Save();
    }

    private static string? GetString(string key)
    {
        EnsureLoaded();
        if (_cache!.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }

    private static void SetString(string key, string value)
    {
        EnsureLoaded();
        // Sérialisation sûre via JsonEncodedText (échappe guillemets, backslash, etc.)
        var encoded = JsonEncodedText.Encode(value);
        _cache![key] = JsonDocument.Parse($"\"{encoded}\"").RootElement.Clone();
        Save();
    }

    private static void EnsureLoaded()
    {
        if (_cache != null) return;
        _cache = new Dictionary<string, JsonElement>();
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    _cache[prop.Name] = prop.Value.Clone();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Config corrompue ou inaccessible — on repart de zéro
        }
    }

    private static void Save()
    {
        try
        {
            using var stream = File.Create(_configPath);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            foreach (var (key, val) in _cache!)
            {
                writer.WritePropertyName(key);
                switch (val.ValueKind)
                {
                    case JsonValueKind.True: writer.WriteBooleanValue(true); break;
                    case JsonValueKind.False: writer.WriteBooleanValue(false); break;
                    case JsonValueKind.Number: writer.WriteNumberValue(val.GetDouble()); break;
                    case JsonValueKind.String: writer.WriteStringValue(val.GetString()); break;
                    default: writer.WriteStringValue(val.ToString()); break;
                }
            }
            writer.WriteEndObject();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pas critique — on continue sans sauvegarder
        }
    }
}

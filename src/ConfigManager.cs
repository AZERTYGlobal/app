// Gestion de la configuration persistante (config.json)
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AZERTYGlobal;

/// <summary>
/// Lit et écrit les préférences utilisateur dans config.json.
/// Mode portable (unpackaged) : à côté de l'exécutable.
/// Mode MSIX (packaged) : dans %LocalAppData%\AZERTY Global\.
/// </summary>
static class ConfigManager
{
    private static string _configPath = GetConfigPath();

    /// <summary>
    /// Hook de test : redirige config.json vers un fichier temporaire.
    /// À n'utiliser que dans le projet de tests via InternalsVisibleTo.
    /// </summary>
    internal static void OverrideConfigPathForTests(string path)
    {
        lock (_lock)
        {
            _configPath = path;
            _cache = null;
            _compatibilityCache = null;
        }
    }

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
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDataDir = Path.Combine(localAppData, "AZERTY Global");
            Directory.CreateDirectory(appDataDir);

            // Migration : copier les fichiers depuis l'ancien dossier "AZERTY Global Portable"
            var oldDir = Path.Combine(localAppData, "AZERTY Global Portable");
            if (Directory.Exists(oldDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(oldDir))
                    {
                        var dest = Path.Combine(appDataDir, Path.GetFileName(file));
                        if (!File.Exists(dest))
                            File.Copy(file, dest);
                    }
                    Directory.Delete(oldDir, true);
                }
                catch { /* Migration best-effort — ne pas bloquer le démarrage */ }
            }

            return Path.Combine(appDataDir, "config.json");
        }

        // Mode portable (unpackaged) : à côté de l'exe
        return Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    private static Dictionary<string, JsonElement>? _cache;
    private static Dictionary<string, string>? _compatibilityCache; // sous-objet compatibility (process → "forceOn"/"forceOff")
    private static readonly object _lock = new();

    /// <summary>Vérifie si l'onboarding a déjà été affiché.</summary>
    public static bool ShowOnboardingAtStartup
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();

                if (_cache!.TryGetValue("showOnboardingAtStartup", out var showValue))
                    return showValue.ValueKind != JsonValueKind.False;

                bool show = true;
                if (_cache.TryGetValue("onboardingDone", out var legacyDone) &&
                    legacyDone.ValueKind == JsonValueKind.True)
                {
                    show = false;
                }

                using var doc = JsonDocument.Parse(show ? "true" : "false");
                _cache["showOnboardingAtStartup"] = doc.RootElement.Clone();
                Save();
                return show;
            }
        }
    }

    /// <summary>Marque l'onboarding comme terminé.</summary>
    public static void SetShowOnboardingAtStartup(bool show) => SetBool("showOnboardingAtStartup", show);

    /// <summary>Cache de compatibilité du lancement automatique. L'UI doit utiliser AutoStart.IsRegistered.</summary>
    public static bool AutoStartEnabled => GetBool("autoStartEnabled");

    /// <summary>Met à jour le cache de compatibilité du lancement automatique.</summary>
    public static void SetAutoStart(bool enabled) => SetBool("autoStartEnabled", enabled);

    /// <summary>Vérifie si les notifications (balloons) sont activées. Défaut : true.</summary>
    public static bool NotificationsEnabled
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                // Défaut true : si la clé n'existe pas, les notifications sont activées
                return !_cache!.TryGetValue("notificationsEnabled", out var val) || val.ValueKind != JsonValueKind.False;
            }
        }
    }

    /// <summary>Active ou désactive les notifications.</summary>
    public static void SetNotifications(bool enabled) => SetBool("notificationsEnabled", enabled);

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

    private const uint DefaultShortcutVirtualKeyboardVk = 0x51; // Q
    private const uint DefaultShortcutCharacterSearchVk = 0x57; // W

    /// <summary>
    /// Vérifie si une touche est autorisée comme raccourci Ctrl+Shift+touche.
    /// Formats acceptés : lettre (A-Z sauf bloquées), chiffre (0-9), "F1" à "F12".
    /// </summary>
    public static bool IsShortcutAllowedVk(uint vk)
    {
        if (vk >= 0x70 && vk <= 0x7B) return true;
        // Touches de fonction F1-F12
        if (vk >= '0' && vk <= '9') return true;
        // Chiffres 0-9
        if (vk >= 'A' && vk <= 'Z') return !BlockedLetters.Contains((char)vk);
        // Lettres A-Z (sauf bloquées)
        return false;
    }

    /// <summary>Lettre du raccourci clavier virtuel. Défaut : "Q" → Ctrl+Maj+Q.</summary>
    public static uint ShortcutVirtualKeyboardVk
    {
        get
        {
            uint vk = GetUInt("shortcutVirtualKeyboardVk");
            return IsShortcutAllowedVk(vk) ? vk : DefaultShortcutVirtualKeyboardVk;
        }
        set
        {
            if (IsShortcutAllowedVk(value))
                SetUInt("shortcutVirtualKeyboardVk", value);
        }
    }

    /// <summary>Lettre du raccourci recherche de caractère. Défaut : "W" → Ctrl+Maj+W.</summary>
    public static uint ShortcutCharacterSearchVk
    {
        get
        {
            uint vk = GetUInt("shortcutCharacterSearchVk");
            return IsShortcutAllowedVk(vk) ? vk : DefaultShortcutCharacterSearchVk;
        }
        set
        {
            if (IsShortcutAllowedVk(value))
                SetUInt("shortcutCharacterSearchVk", value);
        }
    }

    /// <summary>
    /// Parse une touche de fonction "F1"-"F12". Retourne le numéro (1-12) ou 0 si invalide.
    /// </summary>
    public static string GetShortcutDisplayName(uint vk)
    {
        if (vk >= 0x70 && vk <= 0x7B)
            return $"F{vk - 0x6F}";

        if ((vk >= '0' && vk <= '9') || (vk >= 'A' && vk <= 'Z'))
            return ((char)vk).ToString();

        return string.Empty;
    }

    /// <summary>
    /// Retourne le VK code pour un raccourci configurable.
    /// Accepte : lettre A-Z (sauf bloquées), chiffre 0-9, "F1"-"F12".
    /// Retourne 0 si la touche est invalide ou bloquée.
    /// </summary>

    // ═══════════════════════════════════════════════════════════════
    // Compatibilité jeux — overrides par process et flag debug log
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Mode override utilisateur pour un process : "forceOn" (combo native), "forceOff"
    /// (désactivation totale), ou null (mode auto). Lecture case-insensitive.
    /// </summary>
    public static string? GetCompatibilityOverride(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return null;
        lock (_lock)
        {
            EnsureLoaded();
            if (_compatibilityCache == null) return null;
            foreach (var (k, v) in _compatibilityCache)
                if (string.Equals(k, processName, StringComparison.OrdinalIgnoreCase))
                    return v;
            return null;
        }
    }

    /// <summary>
    /// Définit ou supprime un override pour un process. mode = "forceOn" / "forceOff"
    /// ou null pour retirer l'entrée (retour au mode auto).
    /// </summary>
    public static void SetCompatibilityOverride(string processName, string? mode)
    {
        if (string.IsNullOrEmpty(processName)) return;
        if (mode != null && mode != "forceOn" && mode != "forceOff") return;
        lock (_lock)
        {
            EnsureLoaded();
            _compatibilityCache ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (mode == null)
                _compatibilityCache.Remove(processName);
            else
                _compatibilityCache[processName] = mode;
            Save();
        }
    }

    /// <summary>Retourne une copie de la map des overrides (pour audit au démarrage).</summary>
    public static Dictionary<string, string> GetAllCompatibilityOverrides()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _compatibilityCache == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(_compatibilityCache, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Active le journal de debug compat niveau 2 (statistiques agrégées d'émission).
    /// Désactivé par défaut. Aucune frappe n'est jamais loguée, quel que soit le flag.
    /// </summary>
    public static bool CompatibilityDebugLog => GetBool("compatibilityDebugLog");

    public static void SetCompatibilityDebugLog(bool enabled) => SetBool("compatibilityDebugLog", enabled);

    // ═══════════════════════════════════════════════════════════════
    // Logging centralisé
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Dossier de logs (LocalAppData en MSIX, à côté de l'exe sinon).</summary>
    public static string LogDirectory => IsPackaged
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AZERTY Global")
        : AppContext.BaseDirectory;

    /// <summary>Écrit une entrée dans error.log de façon asynchrone et non-bloquante.</summary>
    public static void Log(string context, Exception ex)
    {
        var logDir = LogDirectory;
        var logEntry = $"[{DateTime.Now:s}] {context}: {ex}\n";
        var logFile = Path.Combine(logDir, "error.log");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Directory.CreateDirectory(logDir); File.AppendAllText(logFile, logEntry); } catch { }
        });
    }

    /// <summary>
    /// Loggue un event de la couche compatibilité (changement de mode foreground,
    /// désactivation auto anti-cheat, override cleanup, etc.) dans error.log.
    /// **Aucune frappe utilisateur ne doit jamais être loguée**, conformément à la
    /// politique de confidentialité onboarding. Ne loguer QUE des événements agrégés
    /// sans contenu (process name, mode, action) — pas de codepoint, pas de char.
    /// Async non-bloquant via ThreadPool, comme <see cref="Log"/>.
    /// </summary>
    public static void LogCompatEvent(string eventName, string details)
    {
        var logDir = LogDirectory;
        var logEntry = $"[{DateTime.Now:s}] {eventName}: {details}\n";
        var logFile = Path.Combine(logDir, "error.log");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { Directory.CreateDirectory(logDir); File.AppendAllText(logFile, logEntry); } catch { }
        });
    }

    /// <summary>
    /// Variante synchrone de <see cref="LogCompatEvent"/> pour les events critiques
    /// (AntiCheatDetected, OverrideInvalidCleanup) qui doivent être persistés même si
    /// l'app crash juste après. Acquiert un lock sur _logFlushLock.
    /// </summary>
    private static readonly object _logFlushLock = new();
    public static void LogCompatCriticalEvent(string eventName, string details)
    {
        var logDir = LogDirectory;
        var logEntry = $"[{DateTime.Now:s}] {eventName}: {details}\n";
        var logFile = Path.Combine(logDir, "error.log");
        lock (_logFlushLock)
        {
            try { Directory.CreateDirectory(logDir); File.AppendAllText(logFile, logEntry); } catch { }
        }
    }

    private const long LogRotationSizeBytes = 5 * 1024 * 1024; // 5 Mo

    /// <summary>
    /// Vérifie la taille de error.log au démarrage. Si > 5 Mo, renomme en error.log.old
    /// (écrase l'éventuel précédent .old). Une seule rotation, pas d'archivage cumulatif.
    /// À appeler une fois au démarrage de l'app, avant toute écriture de log.
    /// </summary>
    public static void RotateLogIfNeeded()
    {
        try
        {
            var logFile = Path.Combine(LogDirectory, "error.log");
            if (!File.Exists(logFile)) return;
            var info = new FileInfo(logFile);
            if (info.Length < LogRotationSizeBytes) return;

            var oldFile = Path.Combine(LogDirectory, "error.log.old");
            if (File.Exists(oldFile))
                File.Delete(oldFile);
            File.Move(logFile, oldFile);
        }
        catch { /* best-effort, ne pas bloquer le démarrage */ }
    }

    private static bool GetBool(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (_cache!.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.True)
                return true;
            return false;
        }
    }

    private static void SetBool(string key, bool value)
    {
        lock (_lock)
        {
            EnsureLoaded();
            using var doc = JsonDocument.Parse(value ? "true" : "false");
            _cache![key] = doc.RootElement.Clone();
            Save();
        }
    }

    private static string? GetString(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (_cache!.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String)
                return val.GetString();
            return null;
        }
    }

    private static uint GetUInt(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (_cache!.TryGetValue(key, out var val) &&
                val.ValueKind == JsonValueKind.Number &&
                val.TryGetUInt32(out uint number))
                return number;
            return 0;
        }
    }

    private static void SetString(string key, string value)
    {
        lock (_lock)
        {
            EnsureLoaded();
            // Sérialisation sûre via JsonEncodedText (échappe guillemets, backslash, etc.)
            var encoded = JsonEncodedText.Encode(value);
            using var doc = JsonDocument.Parse($"\"{encoded}\"");
            _cache![key] = doc.RootElement.Clone();
            Save();
        }
    }

    private static void SetUInt(string key, uint value)
    {
        lock (_lock)
        {
            EnsureLoaded();
            using var doc = JsonDocument.Parse(value.ToString());
            _cache![key] = doc.RootElement.Clone();
            Save();
        }
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
                {
                    if (prop.Name == "compatibility" && prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        // Sous-objet compatibility : process → "forceOn"/"forceOff"
                        _compatibilityCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var entry in prop.Value.EnumerateObject())
                        {
                            if (entry.Value.ValueKind == JsonValueKind.String)
                            {
                                var v = entry.Value.GetString();
                                if (v == "forceOn" || v == "forceOff")
                                    _compatibilityCache[entry.Name] = v;
                            }
                        }
                        continue;
                    }
                    _cache[prop.Name] = prop.Value.Clone();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Config corrompue ou inaccessible — on repart de zéro
        }
    }

    private static void Save()
    {
        string tempPath = _configPath + ".tmp";
        try
        {
            var configDir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(configDir))
                Directory.CreateDirectory(configDir);

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
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
                // Sous-objet compatibility (overrides utilisateur par process)
                if (_compatibilityCache != null && _compatibilityCache.Count > 0)
                {
                    writer.WritePropertyName("compatibility");
                    writer.WriteStartObject();
                    foreach (var (proc, mode) in _compatibilityCache)
                    {
                        writer.WritePropertyName(proc);
                        writer.WriteStringValue(mode);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(_configPath))
                File.Replace(tempPath, _configPath, null, true);
            else
                File.Move(tempPath, _configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pas critique — on continue sans sauvegarder
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { }
        }
    }
}

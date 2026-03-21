// Recherche de caractère — fenêtre permettant de trouver comment taper un caractère
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AZERTYGlobalPortable;

/// <summary>
/// Fenêtre de recherche de caractère positionnée en bas à droite (au-dessus de la barre des tâches).
/// 3 colonnes : caractère | nom | méthode de saisie.
/// Hauteur de ligne dynamique selon le contenu.
/// </summary>
sealed class CharacterSearch : IDisposable
{
    private const uint ES_AUTOHSCROLL = 0x0080;

    private const uint EN_CHANGE = 0x0300;

    private const int VK_ESCAPE = 0x1B;
    private const int VK_RETURN = 0x0D;
    private const int VK_UP = 0x26;
    private const int VK_DOWN = 0x28;

    // ── Edit control ──────────────────────────────────────────────
    private const int IDC_SEARCH = 3001;
    private const int IDC_TIMER_COPYFEEDBACK = 3002;
    private const uint EM_SETCUEBANNER = 0x1501;

    // ── Colors (COLORREF = 0x00BBGGRR) ──────────────────────────
    private const uint CLR_BG = 0x00282828;           // Fond sombre
    private const uint CLR_SEARCH_BG = 0x00323232;    // Fond champ recherche
    private const uint CLR_SEPARATOR = 0x00404040;    // Séparateur
    private const uint CLR_CHAR = 0x00FF9040;          // Caractère (bleu vif)
    private const uint CLR_NAME = 0x00DDDDDD;          // Nom (gris clair)
    private const uint CLR_METHOD = 0x00F0A050;        // Méthode de saisie — fallback
    private const uint CLR_METHOD_ALTGR = 0x00E8A848;  // AltGr (jaune doré)
    private const uint CLR_METHOD_MAJ = 0x006EAAF0;    // Maj (bleu ciel)
    private const uint CLR_METHOD_SEP = 0x00808080;    // + et "puis" (gris)
    private const uint CLR_METHOD_KEY = 0x00FFFFFF;    // Noms de touches (blanc)
    private const uint CLR_FOOTER = 0x00AAAAAA;        // Footer (gris clair)
    private const uint CLR_SELECTED = 0x00483828;      // Fond sélectionné (brun chaud)
    private const uint CLR_COPIED = 0x0060D060;        // Vert "Copié !"
    private const uint CLR_HINT = 0x00777777;          // Texte d'aide (gris moyen)
    private const uint CLR_HINT_LIGHT = 0x00606060;    // Exemples (gris discret)

    // ── Dimensions (base 96 DPI) ─────────────────────────────────
    private const int BASE_WIN_W = 360;
    private const int BASE_WIN_H_MIN = 60;      // Hauteur minimale (juste le champ de recherche)
    private const int BASE_WIN_H_MAX = 420;
    private const int BASE_SEARCH_H = 20;
    private const int BASE_CHAR_COL_W = 40;
    private const int BASE_NAME_COL_W = 180;
    private const int BASE_ROW_PAD = 8;
    private const int BASE_FOOTER_H = 42;
    private const int MAX_RESULTS = 20;
    private const int VISIBLE_RESULTS = 4;

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    // ═══════════════════════════════════════════════════════════════
    // Données de recherche
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Données brutes de méthode de saisie (pour le highlight clavier virtuel).</summary>
    public sealed class MethodData
    {
        public string Type { get; set; } = "";       // "direct", "deadkey", "deadkey_activation"
        public string Key { get; set; } = "";        // Web API key code (ex: "KeyQ", "Digit2")
        public string Layer { get; set; } = "";      // "Base", "Shift", "AltGr", etc.
        public string DeadKey { get; set; } = "";    // dk name (pour type="deadkey")
        // Pour type="deadkey" : comment activer la touche morte
        public string DkActivationKey { get; set; } = "";
        public string DkActivationLayer { get; set; } = "";
    }

    /// <summary>Entrée dans l'index des caractères.</summary>
    private sealed class CharEntry
    {
        public string Character { get; set; } = "";
        public string CodePoint { get; set; } = "";
        public string NameFr { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string[] Aliases { get; set; } = Array.Empty<string>();
        public string MethodDisplay { get; set; } = "";
        public bool IsDirectAccess { get; set; }
        public MethodData? Method { get; set; }

        // Champs pré-normalisés (calculés au chargement, évite NormalizeForSearch à chaque frappe)
        public string NormalizedNameFr { get; set; } = "";
        public string NormalizedNameEn { get; set; } = "";
        public string[][] NormalizedAliasWords { get; set; } = Array.Empty<string[]>();
        public string[] NormalizedNameFrWords { get; set; } = Array.Empty<string>();
        public string[] NormalizedNameEnWords { get; set; } = Array.Empty<string>();
        public string NormalizedChar { get; set; } = "";
    }

    // ═══════════════════════════════════════════════════════════════
    // Champs d'instance
    // ═══════════════════════════════════════════════════════════════
    private IntPtr _hWnd;
    private IntPtr _hEdit;
    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly Win32.SUBCLASSPROC _editSubclassProc;
    private List<CharEntry> _allEntries = new();
    private List<CharEntry> _filteredResults = new();
    private int _selectedIndex;
    private int _scrollOffset;
    private float _dpiScale = 1.0f;
    private bool _showCopiedFeedback;
    private string _copiedChar = "";
    private IntPtr _hEditBgBrush;

    // Polices cachées (créées une fois, détruites au Dispose)
    private IntPtr _hFontChar;
    private IntPtr _hFontName;
    private IntPtr _hFontMethod;
    private IntPtr _hFontFooter;
    private IntPtr _hFontPlaceholder;
    private IntPtr _hFontEdit;

    // Mapping des touches mortes : dk_name → méthode d'activation lisible
    private Dictionary<string, string> _deadKeyActivations = new();
    // Mapping des touches mortes : dk_name → (key, layer) brut pour le highlight
    private Dictionary<string, (string key, string layer)> _deadKeyActivationRaw = new();

    // Mapping key code (Web API style) → label AZERTY
    private static readonly Dictionary<string, string> KeyLabels = new()
    {
        ["Backquote"] = "²",
        ["Digit1"] = "&",
        ["Digit2"] = "é",
        ["Digit3"] = "\"",
        ["Digit4"] = "'",
        ["Digit5"] = "(",
        ["Digit6"] = "-",
        ["Digit7"] = "è",
        ["Digit8"] = "_",
        ["Digit9"] = "ç",
        ["Digit0"] = "à",
        ["Minus"] = ")",
        ["Equal"] = "=",
        ["KeyQ"] = "A",
        ["KeyW"] = "Z",
        ["KeyE"] = "E",
        ["KeyR"] = "R",
        ["KeyT"] = "T",
        ["KeyY"] = "Y",
        ["KeyU"] = "U",
        ["KeyI"] = "I",
        ["KeyO"] = "O",
        ["KeyP"] = "P",
        ["BracketLeft"] = "^",
        ["BracketRight"] = "$",
        ["KeyA"] = "Q",
        ["KeyS"] = "S",
        ["KeyD"] = "D",
        ["KeyF"] = "F",
        ["KeyG"] = "G",
        ["KeyH"] = "H",
        ["KeyJ"] = "J",
        ["KeyK"] = "K",
        ["KeyL"] = "L",
        ["Semicolon"] = "M",
        ["Quote"] = "ù",
        ["Backslash"] = "*",
        ["IntlBackslash"] = "<",
        ["KeyZ"] = "W",
        ["KeyX"] = "X",
        ["KeyC"] = "C",
        ["KeyV"] = "V",
        ["KeyB"] = "B",
        ["KeyN"] = "N",
        ["KeyM"] = ",",
        ["Comma"] = ".",
        ["Period"] = "/",
        ["Slash"] = "§",
        ["Space"] = "Espace",
    };

    /// <summary>Événement déclenché quand la sélection dans les résultats change.</summary>
    public event Action<MethodData?>? SelectionChanged;

    public bool IsVisible => _hWnd != IntPtr.Zero && Win32.IsWindowVisible(_hWnd);

    /// <summary>Retourne un dictionnaire caractère → nom français (pour le clavier virtuel).</summary>
    public Dictionary<string, string> GetCharacterNames()
    {
        var names = new Dictionary<string, string>();
        foreach (var entry in _allEntries)
        {
            if (!string.IsNullOrEmpty(entry.NameFr))
                names[entry.Character] = entry.NameFr;
        }
        return names;
    }

    public CharacterSearch()
    {
        _wndProcDelegate = WndProcCallback;
        _editSubclassProc = EditSubclassProc;
        LoadCharacterIndex();
        CreateWindow();
    }

    // ═══════════════════════════════════════════════════════════════
    // Chargement des données
    // ═══════════════════════════════════════════════════════════════

    private void LoadCharacterIndex()
    {
        string json;
        // Essayer la ressource embarquée d'abord, puis le fichier à côté de l'exe
        using (var stream = typeof(CharacterSearch).Assembly.GetManifestResourceStream("character-index.json"))
        {
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                json = reader.ReadToEnd();
            }
            else
            {
                var path = Path.Combine(AppContext.BaseDirectory, "character-index.json");
                json = File.ReadAllText(path);
            }
        }

        using var doc = JsonDocument.Parse(json);
        var characters = doc.RootElement.GetProperty("characters");

        // Première passe : collecter les activations de touches mortes
        foreach (var entry in characters.EnumerateObject())
        {
            if (!entry.Name.StartsWith("dk:")) continue;
            if (!entry.Value.TryGetProperty("methods", out var methods)) continue;

            foreach (var method in methods.EnumerateArray())
            {
                if (method.GetProperty("type").GetString() != "deadkey_activation") continue;
                var dkName = method.GetProperty("deadkey").GetString() ?? "";
                var key = method.GetProperty("key").GetString() ?? "";
                var layer = method.GetProperty("layer").GetString() ?? "";
                _deadKeyActivations[dkName] = FormatDirectMethod(key, layer);
                _deadKeyActivationRaw[dkName] = (key, layer);
                break;
            }
        }

        // Deuxième passe : construire les entrées
        foreach (var entry in characters.EnumerateObject())
        {
            // Ignorer les entrées de touche morte elles-mêmes (dk:xxx)
            if (entry.Name.StartsWith("dk:")) continue;

            var charStr = entry.Name;
            var codePoint = entry.Value.TryGetProperty("codePoint", out var cp) ? cp.GetString() ?? "" : "";
            var nameFr = entry.Value.TryGetProperty("unicodeNameFr", out var nf) ? nf.GetString() ?? "" : "";
            var nameEn = entry.Value.TryGetProperty("unicodeName", out var ne) ? ne.GetString() ?? "" : "";

            var aliases = new List<string>();
            if (entry.Value.TryGetProperty("frenchAliases", out var fa) && fa.ValueKind == JsonValueKind.Array)
            {
                foreach (var alias in fa.EnumerateArray())
                    if (alias.GetString() is string a)
                        aliases.Add(a);
            }

            // Trouver la méthode recommandée
            string methodDisplay = "";
            bool isDirectAccess = false;
            MethodData? methodData = null;
            if (entry.Value.TryGetProperty("methods", out var methods))
            {
                // Chercher la méthode recommandée
                JsonElement? recommended = null;
                JsonElement? fallback = null;
                foreach (var method in methods.EnumerateArray())
                {
                    if (method.TryGetProperty("recommended", out var rec) && rec.GetBoolean())
                    {
                        recommended = method;
                        break;
                    }
                    fallback ??= method;
                }
                var chosen = recommended ?? fallback;
                if (chosen.HasValue)
                {
                    methodDisplay = FormatMethod(chosen.Value);
                    isDirectAccess = chosen.Value.TryGetProperty("recommended", out var r) && r.GetBoolean()
                        && chosen.Value.GetProperty("type").GetString() == "direct";

                    // Stocker les données brutes pour le highlight
                    var mType = chosen.Value.GetProperty("type").GetString() ?? "";
                    var mKey = chosen.Value.TryGetProperty("key", out var mk) ? mk.GetString() ?? "" : "";
                    var mLayer = chosen.Value.TryGetProperty("layer", out var ml) ? ml.GetString() ?? "" : "";
                    methodData = new MethodData { Type = mType, Key = mKey, Layer = mLayer };

                    if (mType == "deadkey")
                    {
                        var dkName = chosen.Value.GetProperty("deadkey").GetString() ?? "";
                        methodData.DeadKey = dkName;
                        if (_deadKeyActivationRaw.TryGetValue(dkName, out var dkAct))
                        {
                            methodData.DkActivationKey = dkAct.key;
                            methodData.DkActivationLayer = dkAct.layer;
                        }
                    }
                }
            }

            var entry2 = new CharEntry
            {
                Character = charStr,
                CodePoint = codePoint,
                NameFr = nameFr,
                NameEn = nameEn,
                Aliases = aliases.ToArray(),
                MethodDisplay = methodDisplay,
                IsDirectAccess = isDirectAccess,
                Method = methodData,
            };

            // Pré-normaliser pour la recherche (évite NormalizeForSearch à chaque frappe)
            entry2.NormalizedNameFr = NormalizeForSearch(nameFr);
            entry2.NormalizedNameEn = NormalizeForSearch(nameEn);
            entry2.NormalizedNameFrWords = entry2.NormalizedNameFr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            entry2.NormalizedNameEnWords = entry2.NormalizedNameEn.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            entry2.NormalizedChar = NormalizeForSearch(charStr);
            entry2.NormalizedAliasWords = aliases.Select(a =>
                NormalizeForSearch(a).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            ).ToArray();

            _allEntries.Add(entry2);
        }
    }

    /// <summary>Formate une méthode de saisie en texte lisible.</summary>
    private string FormatMethod(JsonElement method)
    {
        var type = method.GetProperty("type").GetString() ?? "";
        var key = method.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
        var layer = method.TryGetProperty("layer", out var l) ? l.GetString() ?? "" : "";

        if (type == "direct")
        {
            return FormatDirectMethod(key, layer);
        }

        if (type == "deadkey")
        {
            var dkName = method.GetProperty("deadkey").GetString() ?? "";
            var activation = _deadKeyActivations.GetValueOrDefault(dkName, dkName);
            var keyLabel = KeyLabels.GetValueOrDefault(key, key);
            // Le layer de la touche après la DK : si c'est "Shift", il faut Maj + touche
            var afterDk = layer switch
            {
                "Shift" => $"puis Maj + {keyLabel}",
                _ => $"puis {keyLabel}",
            };
            // 2 lignes : activation en haut, association en bas
            return $"{activation}\n{afterDk}";
        }

        return "";
    }

    /// <summary>Formate une méthode directe (couche + touche).</summary>
    private static string FormatDirectMethod(string key, string layer)
    {
        var keyLabel = KeyLabels.GetValueOrDefault(key, key);
        return layer switch
        {
            "Base" => keyLabel,
            "Shift" => $"Maj + {keyLabel}",
            "AltGr" => $"AltGr + {keyLabel}",
            "AltGr+Shift" or "Shift+AltGr" => $"AltGr + Maj + {keyLabel}",
            "Caps" => $"Verr.Maj + {keyLabel}",
            "Caps+Shift" => $"Verr.Maj + Maj + {keyLabel}",
            "Caps+AltGr" => $"Verr.Maj + AltGr + {keyLabel}",
            "Caps+Shift+AltGr" or "Caps+AltGr+Shift" => $"Verr.Maj + AltGr + Maj + {keyLabel}",
            _ => keyLabel,
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Recherche
    // ═══════════════════════════════════════════════════════════════

    private void Search(string query)
    {
        _filteredResults.Clear();
        _selectedIndex = 0;
        _scrollOffset = 0;

        if (string.IsNullOrWhiteSpace(query))
        {
            ResizeToFitResults();
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
            return;
        }

        query = query.Trim();
        var lowerQuery = query.ToLowerInvariant();

        // Score et tri
        var scored = new List<(CharEntry entry, int score)>();
        foreach (var entry in _allEntries)
        {
            int score = MatchScore(entry, query, lowerQuery);
            if (score > 0)
                scored.Add((entry, score));
        }

        scored.Sort((a, b) => b.score.CompareTo(a.score));
        foreach (var (entry, _) in scored.Take(MAX_RESULTS))
            _filteredResults.Add(entry);

        ResizeToFitResults();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
        // Ne PAS appeler NotifySelectionChanged ici : le highlight ne doit
        // se déclencher que sur Entrée, clic ou navigation flèches.
    }

    /// <summary>Normalise un texte pour la recherche (supprime les diacritiques, met en minuscule).</summary>
    private static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = char.GetUnicodeCategory(c);
            if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Vérifie si tous les mots de la requête matchent des mots du texte (chaque mot de requête
    /// est le début d'un mot du texte). L'ordre des mots n'a pas d'importance.
    /// Si un mot de la requête fait 1 caractère, il doit correspondre exactement (pas de StartsWith)
    /// pour éviter que "a" matche "avec", "aigu", etc.</summary>
    private static bool AllWordsMatch(string[] queryWords, string[] textWords)
    {
        foreach (var qw in queryWords)
        {
            bool found = false;
            foreach (var tw in textWords)
            {
                if (qw.Length == 1 ? tw == qw : (tw == qw || tw.StartsWith(qw)))
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>Score de correspondance — identique au site web (tester-modal.js searchCharacters).</summary>
    private static int MatchScore(CharEntry entry, string query, string lowerQuery)
    {
        int score = 0;

        var normalizedQuery = NormalizeForSearch(query);
        var queryWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var originalQueryWords = lowerQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Correspondance exacte du caractère (priorité maximale)
        if (entry.Character == query)
        {
            score = 100;
        }
        // Correspondance caractère insensible à la casse
        else if (entry.Character.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score = 90;
        }
        // Alias français : tous les mots matchent (pré-normalisés)
        else if (entry.NormalizedAliasWords.Length > 0 && queryWords.Length > 0 &&
            entry.NormalizedAliasWords.Any(aliasWords => AllWordsMatch(queryWords, aliasWords)))
        {
            score = 80;
        }
        // Nom français : tous les mots matchent (pré-normalisé)
        else if (entry.NormalizedNameFrWords.Length > 0 && queryWords.Length > 0)
        {
            if (AllWordsMatch(queryWords, entry.NormalizedNameFrWords))
                score = 70;
        }

        // Nom anglais : tous les mots matchent (pré-normalisé)
        if (score == 0 && entry.NormalizedNameEnWords.Length > 0 && queryWords.Length > 0)
        {
            if (AllWordsMatch(queryWords, entry.NormalizedNameEnWords))
                score = 50;
        }

        // Code Unicode (U+xxxx)
        if (score == 0 && lowerQuery.StartsWith("u+") &&
            entry.CodePoint.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            score = 90;
        }

        if (score == 0) return 0;

        // ── Bonus (identiques au site web) ──

        // Bonus Latin de base (U+0020 à U+007F)
        if (int.TryParse(entry.CodePoint.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out int cpNum)
            && cpNum >= 0x0020 && cpNum <= 0x007F)
            score += 15;

        // Bonus si le caractère normalisé correspond à un mot de la requête
        if (queryWords.Any(w => entry.NormalizedChar == w))
            score += 10;

        // Bonus correspondance exacte accent (le caractère tapé est le résultat)
        var lowerChar = entry.Character.ToLowerInvariant();
        if (originalQueryWords.Contains(lowerChar) || originalQueryWords.Contains(entry.Character))
            score += 50;

        // Bonus massif pour recherche d'un seul caractère exact
        if (originalQueryWords.Length == 1 && originalQueryWords[0].Length == 1 &&
            (entry.Character == originalQueryWords[0] ||
             entry.Character.Equals(originalQueryWords[0], StringComparison.OrdinalIgnoreCase)))
            score += 100;

        // Bonus pour les touches mortes (dk:name) — identique au site web
        if (entry.Character.StartsWith("dk:"))
            score += 30;

        // Bonus minuscule (plus fréquemment recherché que majuscule)
        if (entry.Character.Length == 1 && char.IsLetter(entry.Character[0])
            && char.IsLower(entry.Character[0]))
            score += 5;

        // Bonus accès direct (méthode recommandée est directe, pas touche morte)
        if (entry.IsDirectAccess)
            score += 10;

        // Bonus si le nom français commence par la requête (match plus spécifique)
        if (entry.NormalizedNameFr.Length > 0 && entry.NormalizedNameFr.StartsWith(normalizedQuery))
            score += 15;

        return score;
    }

    // ═══════════════════════════════════════════════════════════════
    // Fenêtre Win32
    // ═══════════════════════════════════════════════════════════════

    private void CreateWindow()
    {
        var hInstance = Win32.GetModuleHandleW(null);
        var className = "AZERTYGlobal_CharSearch";

        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            style = 0x0003, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            hbrBackground = Win32.CreateSolidBrush(CLR_BG),
            lpszClassName = className,
        };
        Win32.RegisterClassExW(ref wc);

        // Calculer la position en bas à droite — démarrer avec la hauteur minimale
        var (x, y) = GetBottomRightPosition(BASE_WIN_W, BASE_WIN_H_MIN);

        _hWnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW,
            className,
            "Rechercher un caractère",
            Win32.WS_POPUP | Win32.WS_BORDER | Win32.WS_CLIPCHILDREN,
            x, y, BASE_WIN_W, BASE_WIN_H_MIN,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        // Récupérer le DPI
        try { _dpiScale = Win32.GetDpiForWindow(_hWnd) / 96.0f; } catch { }

        // Redimensionner selon le DPI
        {
            int w = Scale(BASE_WIN_W);
            int h = Scale(BASE_WIN_H_MIN);
            var (dx, dy) = GetBottomRightPosition(w, h);
            Win32.MoveWindow(_hWnd, dx, dy, w, h, false);
        }

        // Créer les polices cachées (réutilisées à chaque repaint)
        _hFontChar = Win32.CreateFontW(Scale(24), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hFontName = Win32.CreateFontW(Scale(20), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hFontMethod = Win32.CreateFontW(Scale(18), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI Semibold");
        _hFontFooter = Win32.CreateFontW(Scale(16), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hFontPlaceholder = Win32.CreateFontW(-Scale(16), 0, 0, 0, 400, 1, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");

        // Créer le champ de recherche (Edit control)
        int searchH = Scale(BASE_SEARCH_H);
        int editPad = Scale(5);
        _hEdit = Win32.CreateWindowExW(0, "EDIT", "",
            Win32.WS_CHILD | Win32.WS_VISIBLE | ES_AUTOHSCROLL,
            editPad, editPad, Scale(BASE_WIN_W) - editPad * 2, searchH,
            _hWnd, (IntPtr)IDC_SEARCH, hInstance, IntPtr.Zero);

        // Police du champ de recherche (négatif = hauteur caractère exacte)
        _hFontEdit = Win32.CreateFontW(-Scale(16), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        Win32.SendMessageW(_hEdit, Win32.WM_SETFONT, _hFontEdit, (IntPtr)1);

        // Pas de placeholder natif — on dessine le nôtre en italique dans OnPaint

        // Brush pour le fond sombre de l'Edit
        _hEditBgBrush = Win32.CreateSolidBrush(CLR_SEARCH_BG);

        // Subclasser l'Edit pour intercepter les touches (Entrée, flèches, Échap)
        Win32.SetWindowSubclass(_hEdit, _editSubclassProc, (UIntPtr)1, IntPtr.Zero);
    }

    private (int x, int y) GetBottomRightPosition(int winW, int winH)
    {
        // Trouver l'écran principal (ou celui où se trouve la souris)
        Win32.GetCursorPos(out var pt);
        var hMonitor = Win32.MonitorFromPoint(pt, 0x00000001); // MONITOR_DEFAULTTOPRIMARY
        var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(hMonitor, ref mi);

        // rcWork = zone de travail (exclut la barre des tâches)
        int x = mi.rcWork.right - winW - 10;
        int y = mi.rcWork.bottom - winH - 10;
        return (x, y);
    }

    private int Scale(int value) => (int)(value * _dpiScale);

    /// <summary>Redimensionne la fenêtre selon le nombre de résultats.</summary>
    private void ResizeToFitResults()
    {
        int searchAreaH = Scale(BASE_SEARCH_H) + Scale(8) * 2 + 1; // +1 pour le séparateur
        int footerH = Scale(BASE_FOOTER_H);
        int w = Scale(BASE_WIN_W);

        if (_filteredResults.Count == 0)
        {
            int h = searchAreaH + Scale(40);
            var (x, y) = GetBottomRightPosition(w, h);
            Win32.MoveWindow(_hWnd, x, y, w, h, true);
            return;
        }

        // Calculer la hauteur totale des lignes visibles
        int totalRowH = 0;
        int rowCount = Math.Min(_filteredResults.Count, VISIBLE_RESULTS);
        for (int i = 0; i < rowCount; i++)
        {
            totalRowH += GetRowHeight(i) + 1; // +1 séparateur
        }

        int targetH = searchAreaH + totalRowH + footerH;
        int maxH = Scale(BASE_WIN_H_MAX);
        targetH = Math.Min(targetH, maxH);

        var (px, py) = GetBottomRightPosition(w, targetH);
        Win32.MoveWindow(_hWnd, px, py, w, targetH, true);
    }

    // ═══════════════════════════════════════════════════════════════
    // Affichage / Masquage
    // ═══════════════════════════════════════════════════════════════

    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else
            Show();
    }

    public void Show()
    {
        // Adapter la taille selon les résultats actuels, puis repositionner
        ResizeToFitResults();

        // Forcer le focus — AttachThreadInput pour voler le foreground
        var foreWnd = Win32.GetForegroundWindow();
        uint foreThread = Win32.GetWindowThreadProcessId(foreWnd, IntPtr.Zero);
        uint curThread = Win32.GetCurrentThreadId();
        if (foreThread != curThread)
            Win32.AttachThreadInput(curThread, foreThread, true);

        Win32.ShowWindow(_hWnd, 5); // SW_SHOW
        Win32.SetForegroundWindow(_hWnd);
        Win32.SetFocus(_hEdit);

        if (foreThread != curThread)
            Win32.AttachThreadInput(curThread, foreThread, false);

        // Sélectionner tout le texte existant
        Win32.SendMessageW(_hEdit, 0x00B1, IntPtr.Zero, (IntPtr)(-1)); // EM_SETSEL 0, -1
    }

    public void Hide()
    {
        Win32.ShowWindow(_hWnd, 0); // SW_HIDE
        SelectionChanged?.Invoke(null); // Effacer le highlight
    }

    // ═══════════════════════════════════════════════════════════════
    // WndProc
    // ═══════════════════════════════════════════════════════════════

    private IntPtr WndProcCallback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case Win32.WM_PAINT:
                    OnPaint(hWnd);
                    return IntPtr.Zero;

                case Win32.WM_ERASEBKGND:
                    return (IntPtr)1; // On gère le fond dans Win32.WM_PAINT

                case Win32.WM_COMMAND:
                    int controlId = (int)(wParam.ToInt64() & 0xFFFF);
                    int notifCode = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
                    if (controlId == IDC_SEARCH && notifCode == EN_CHANGE)
                    {
                        var text = GetEditText();
                        Search(text);
                    }
                    return IntPtr.Zero;

                case Win32.WM_ACTIVATE:
                    int activateState = (int)(wParam.ToInt64() & 0xFFFF);
                    if (activateState == 0) // WA_INACTIVE
                        Hide();
                    return IntPtr.Zero;

                case Win32.WM_MOUSEWHEEL:
                    int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    if (delta > 0 && _scrollOffset > 0)
                        _scrollOffset--;
                    else if (delta < 0 && _scrollOffset < _filteredResults.Count - VISIBLE_RESULTS)
                        _scrollOffset++;
                    Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                    return IntPtr.Zero;

                case Win32.WM_LBUTTONDOWN:
                    {
                        int mouseY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        HandleClick(mouseY);
                    }
                    return IntPtr.Zero;

                case Win32.WM_TIMER:
                    if (wParam == (IntPtr)IDC_TIMER_COPYFEEDBACK)
                    {
                        Win32.KillTimer(_hWnd, (UIntPtr)IDC_TIMER_COPYFEEDBACK);
                        _showCopiedFeedback = false;
                        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                    }
                    return IntPtr.Zero;

                case Win32.WM_CTLCOLOREDIT:
                    Win32.SetTextColor(wParam, CLR_NAME);      // texte clair
                    Win32.SetBkColor(wParam, CLR_SEARCH_BG);   // fond édit sombre
                    return _hEditBgBrush;

                case Win32.WM_DESTROY:
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            var logDir = ConfigManager.IsPackaged
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AZERTY Global Portable")
                : AppContext.BaseDirectory;
            try { Directory.CreateDirectory(logDir); } catch { }
            File.AppendAllText(Path.Combine(logDir, "error.log"), $"[{DateTime.Now:s}] CharSearch WndProc: {ex}\n");
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>Subclass de l'Edit pour intercepter les touches spéciales.</summary>
    private IntPtr EditSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == Win32.WM_KEYDOWN)
        {
            int vk = wParam.ToInt32();
            switch (vk)
            {
                case VK_ESCAPE:
                    Hide();
                    return IntPtr.Zero;

                case VK_RETURN:
                    NotifySelectionChanged();
                    CopySelectedCharacter();
                    return IntPtr.Zero;

                case VK_DOWN:
                    if (_selectedIndex < _filteredResults.Count - 1)
                    {
                        _selectedIndex++;
                        EnsureVisible(_selectedIndex);
                        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                        NotifySelectionChanged();
                    }
                    return IntPtr.Zero;

                case VK_UP:
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                        EnsureVisible(_selectedIndex);
                        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                        NotifySelectionChanged();
                    }
                    return IntPtr.Zero;
            }
        }

        // Bloquer Win32.WM_CHAR pour Escape et Entrée (supprime le beep Windows)
        if (msg == Win32.WM_CHAR)
        {
            int ch = wParam.ToInt32();
            if (ch == VK_ESCAPE || ch == '\r' || ch == '\n')
                return IntPtr.Zero;
        }

        // Placeholder en italique blanc quand le champ est vide
        if (msg == Win32.WM_PAINT)
        {
            var result = Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
            if (GetEditText().Length == 0)
            {
                var hdc = Win32.GetDC(hWnd);
                Win32.SetBkMode(hdc, Win32.TRANSPARENT);
                Win32.SetTextColor(hdc, 0x00999999); // gris clair
                Win32.SelectObject(hdc, _hFontPlaceholder);
                Win32.GetClientRect(hWnd, out var editRect);
                editRect.left += Scale(4);
                var phText = "Rechercher un caractère…";
                Win32.DrawTextW(hdc, phText, phText.Length, ref editRect,
                    Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
                Win32.ReleaseDC(hWnd, hdc);
            }
            return result;
        }

        return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void NotifySelectionChanged()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _filteredResults.Count)
            SelectionChanged?.Invoke(_filteredResults[_selectedIndex].Method);
        else
            SelectionChanged?.Invoke(null);
    }

    private void EnsureVisible(int index)
    {
        if (index < _scrollOffset)
            _scrollOffset = index;
        else if (index >= _scrollOffset + VISIBLE_RESULTS)
            _scrollOffset = index - VISIBLE_RESULTS + 1;
    }

    private string GetEditText()
    {
        int len = (int)Win32.SendMessageW(_hEdit, Win32.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        if (len <= 0) return "";
        var buffer = new char[len + 1];
        var hBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            Win32.SendMessageW(_hEdit, Win32.WM_GETTEXT, (IntPtr)(len + 1), hBuffer.AddrOfPinnedObject());
            return new string(buffer, 0, len);
        }
        finally
        {
            hBuffer.Free();
        }
    }

    private void HandleClick(int mouseY)
    {
        int searchH = Scale(BASE_SEARCH_H) + Scale(8) * 2;
        if (mouseY < searchH) return; // Clic dans la zone de recherche

        // Trouver quel résultat a été cliqué — on calcule les hauteurs de lignes
        int y = searchH;
        for (int i = _scrollOffset; i < _filteredResults.Count && i < _scrollOffset + VISIBLE_RESULTS; i++)
        {
            int rowH = GetRowHeight(i);
            if (mouseY >= y && mouseY < y + rowH)
            {
                _selectedIndex = i;
                NotifySelectionChanged();
                CopySelectedCharacter();
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                return;
            }
            y += rowH;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Copie dans le presse-papier
    // ═══════════════════════════════════════════════════════════════

    private void CopySelectedCharacter()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _filteredResults.Count) return;

        var entry = _filteredResults[_selectedIndex];
        CopyToClipboard(entry.Character);

        // Feedback visuel "Copié !"
        _showCopiedFeedback = true;
        _copiedChar = entry.Character;
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
        Win32.SetTimer(_hWnd, (UIntPtr)IDC_TIMER_COPYFEEDBACK, 1500, IntPtr.Zero);
    }

    private void CopyToClipboard(string text)
    {
        if (!Win32.OpenClipboard(_hWnd)) return;
        try
        {
            Win32.EmptyClipboard();
            int byteCount = (text.Length + 1) * 2; // UTF-16 + null terminator
            var hMem = Win32.GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hMem == IntPtr.Zero) return;

            var ptr = Win32.GlobalLock(hMem);
            if (ptr != IntPtr.Zero)
            {
                Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                Marshal.WriteInt16(ptr, text.Length * 2, 0); // null terminator
                Win32.GlobalUnlock(hMem);
                Win32.SetClipboardData(CF_UNICODETEXT, hMem);
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Rendu
    // ═══════════════════════════════════════════════════════════════

    private void OnPaint(IntPtr hWnd)
    {
        var hdc = Win32.BeginPaint(hWnd, out var ps);
        Win32.GetClientRect(hWnd, out var clientRect);

        int cw = clientRect.right - clientRect.left;
        int ch = clientRect.bottom - clientRect.top;

        // Double buffering
        var hdcMem = Win32.CreateCompatibleDC(hdc);
        var hBitmap = Win32.CreateCompatibleBitmap(hdc, cw, ch);
        var hOldBitmap = Win32.SelectObject(hdcMem, hBitmap);

        // Fond blanc
        var hBgBrush = Win32.CreateSolidBrush(CLR_BG);
        Win32.FillRect(hdcMem, ref clientRect, hBgBrush);
        Win32.DeleteObject(hBgBrush);

        // Zone du champ de recherche : fond gris clair
        int pad = Scale(8);
        int searchAreaH = Scale(BASE_SEARCH_H) + pad * 2;
        var searchBgRect = new Win32.RECT { left = 0, top = 0, right = cw, bottom = searchAreaH };
        var hSearchBrush = Win32.CreateSolidBrush(CLR_SEARCH_BG);
        Win32.FillRect(hdcMem, ref searchBgRect, hSearchBrush);
        Win32.DeleteObject(hSearchBrush);

        // Ligne séparatrice sous la recherche
        var hSepBrush = Win32.CreateSolidBrush(CLR_SEPARATOR);
        var sepRect = new Win32.RECT { left = 0, top = searchAreaH, right = cw, bottom = searchAreaH + 1 };
        Win32.FillRect(hdcMem, ref sepRect, hSepBrush);
        Win32.DeleteObject(hSepBrush);

        // Polices (cachées en champs d'instance)
        var hFontChar = _hFontChar;
        var hFontName = _hFontName;
        var hFontMethod = _hFontMethod;
        var hFontFooter = _hFontFooter;

        Win32.SetBkMode(hdcMem, Win32.TRANSPARENT);

        // Dessiner les résultats
        int y = searchAreaH + 1;
        int charColW = Scale(BASE_CHAR_COL_W);
        int nameColW = Scale(BASE_NAME_COL_W);
        int methodColW = cw - charColW - nameColW - pad;
        int rowPad = Scale(BASE_ROW_PAD);
        int footerH = Scale(BASE_FOOTER_H);
        int availableH = ch - y - footerH;

        if (_filteredResults.Count > 0)
        {
            int drawnRows = 0;
            for (int i = _scrollOffset; i < _filteredResults.Count && drawnRows < VISIBLE_RESULTS; i++)
            {
                var entry = _filteredResults[i];
                bool isSelected = (i == _selectedIndex);

                // Mesurer la hauteur de cette ligne
                int rowH = MeasureRowHeight(hdcMem, entry, nameColW, methodColW, hFontName, hFontMethod, rowPad);

                // Fond sélectionné
                if (isSelected)
                {
                    var selRect = new Win32.RECT { left = 0, top = y, right = cw, bottom = y + rowH };
                    var hSelBrush = Win32.CreateSolidBrush(CLR_SELECTED);
                    Win32.FillRect(hdcMem, ref selRect, hSelBrush);
                    Win32.DeleteObject(hSelBrush);
                }

                // Colonne 1 : caractère
                Win32.SetTextColor(hdcMem, CLR_CHAR);
                Win32.SelectObject(hdcMem, hFontChar);
                var charRect = new Win32.RECT { left = 0, top = y, right = charColW, bottom = y + rowH };
                Win32.DrawTextW(hdcMem, entry.Character, entry.Character.Length, ref charRect,
                    Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

                // Colonne 2 : nom
                Win32.SetTextColor(hdcMem, CLR_NAME);
                Win32.SelectObject(hdcMem, hFontName);
                var nameRect = new Win32.RECT
                {
                    left = charColW,
                    top = y + rowPad,
                    right = charColW + nameColW - pad,
                    bottom = y + rowH - rowPad,
                };
                Win32.DrawTextW(hdcMem, entry.NameFr, entry.NameFr.Length, ref nameRect,
                    Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX);

                // Colonne 3 : méthode (colorée)
                Win32.SelectObject(hdcMem, hFontMethod);
                DrawMethodColorized(hdcMem, entry.MethodDisplay,
                    charColW + nameColW, y + rowPad, cw - pad, y + rowH - rowPad);

                y += rowH;

                // Séparateur
                var hRowSep = Win32.CreateSolidBrush(CLR_SEPARATOR);
                var rowSepRect = new Win32.RECT { left = pad, top = y, right = cw - pad, bottom = y + 1 };
                Win32.FillRect(hdcMem, ref rowSepRect, hRowSep);
                Win32.DeleteObject(hRowSep);
                y += 1;

                drawnRows++;
            }

            // Scrollbar si plus de résultats que visible
            if (_filteredResults.Count > VISIBLE_RESULTS)
            {
                int sbW = Scale(4);
                int sbX = cw - sbW - Scale(2);
                int sbTop = searchAreaH + 1;
                int sbH = y - sbTop;
                if (sbH > 0)
                {
                    float ratio = (float)VISIBLE_RESULTS / _filteredResults.Count;
                    int thumbH = Math.Max(Scale(16), (int)(sbH * ratio));
                    float scrollRatio = _filteredResults.Count - VISIBLE_RESULTS > 0
                        ? (float)_scrollOffset / (_filteredResults.Count - VISIBLE_RESULTS) : 0;
                    int thumbY = sbTop + (int)((sbH - thumbH) * scrollRatio);

                    // Track
                    var hTrackBrush = Win32.CreateSolidBrush(CLR_SEPARATOR);
                    var trackRect = new Win32.RECT { left = sbX, top = sbTop, right = sbX + sbW, bottom = sbTop + sbH };
                    Win32.FillRect(hdcMem, ref trackRect, hTrackBrush);
                    Win32.DeleteObject(hTrackBrush);

                    // Thumb
                    var hThumbBrush = Win32.CreateSolidBrush(CLR_HINT);
                    var thumbRect = new Win32.RECT { left = sbX, top = thumbY, right = sbX + sbW, bottom = thumbY + thumbH };
                    Win32.FillRect(hdcMem, ref thumbRect, hThumbBrush);
                    Win32.DeleteObject(hThumbBrush);
                }
            }
        }
        else
        {
            string editText = GetEditText();
            if (editText.Length > 0)
            {
                // Aucun résultat
                Win32.SetTextColor(hdcMem, CLR_FOOTER);
                Win32.SelectObject(hdcMem, hFontName);
                var noResultRect = new Win32.RECT { left = 0, top = y + Scale(20), right = cw, bottom = y + Scale(60) };
                var noResultText = "Aucun résultat";
                Win32.DrawTextW(hdcMem, noResultText, noResultText.Length, ref noResultRect,
                    Win32.DT_CENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
            }
            else
            {
                // État d'accueil — raccourcis visibles (même style que footer)
                Win32.SelectObject(hdcMem, hFontFooter);
                Win32.SetTextColor(hdcMem, CLR_FOOTER);

                int ty = y + Scale(8);
                var tipText = "Entrée copier · Échap fermer";
                var tipRect = new Win32.RECT { left = 0, top = ty, right = cw, bottom = ty + Scale(24) };
                Win32.DrawTextW(hdcMem, tipText, tipText.Length, ref tipRect,
                    Win32.DT_CENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
            }
        }

        // Footer
        Win32.SelectObject(hdcMem, hFontFooter);
        var footerRect = new Win32.RECT { left = 0, top = ch - footerH, right = cw, bottom = ch };

        if (_showCopiedFeedback)
        {
            Win32.SetTextColor(hdcMem, CLR_COPIED);
            var copiedText = $"« {_copiedChar} » copié !";
            Win32.DrawTextW(hdcMem, copiedText, copiedText.Length, ref footerRect,
                Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        }
        else if (_filteredResults.Count > 0)
        {
            Win32.SetTextColor(hdcMem, CLR_FOOTER);
            var countText = _filteredResults.Count == 1
                ? "1 résultat — Entrée pour copier"
                : $"{_filteredResults.Count} résultats — Entrée pour copier";
            Win32.DrawTextW(hdcMem, countText, countText.Length, ref footerRect,
                Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        }

        // Blit
        Win32.BitBlt(hdc, 0, 0, cw, ch, hdcMem, 0, 0, Win32.SRCCOPY);

        // Cleanup (polices cachées → pas de DeleteObject ici)
        Win32.SelectObject(hdcMem, hOldBitmap);
        Win32.DeleteObject(hBitmap);
        Win32.DeleteDC(hdcMem);

        Win32.EndPaint(hWnd, ref ps);
    }

    /// <summary>Dessine la méthode de saisie avec des couleurs par token.</summary>
    private void DrawMethodColorized(IntPtr hdc, string method, int left, int top, int right, int bottom)
    {
        int x = left;
        int lineY = top;
        int lineH = Scale(18);

        var displayLines = method.Split('\n');

        foreach (var line in displayLines)
        {
            x = left;
            // Tokeniser : séparer par espaces mais garder les tokens
            var parts = line.Split(' ');
            for (int p = 0; p < parts.Length; p++)
            {
                var token = parts[p];
                if (p > 0)
                {
                    // Espace
                    var spaceSize = new Win32.RECT { left = 0, top = 0, right = 999, bottom = 999 };
                    Win32.DrawTextW(hdc, " ", 1, ref spaceSize, Win32.DT_CALCRECT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
                    x += spaceSize.right;
                }

                // Couleur selon le token
                uint color;
                if (token == "AltGr")
                    color = CLR_METHOD_ALTGR;
                else if (token == "Maj")
                    color = CLR_METHOD_MAJ;
                else if (token == "+" || token == "puis")
                    color = CLR_METHOD_SEP;
                else
                    color = CLR_METHOD_KEY;

                Win32.SetTextColor(hdc, color);
                var tokenRect = new Win32.RECT { left = x, top = lineY, right = right, bottom = bottom };
                Win32.DrawTextW(hdc, token, token.Length, ref tokenRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

                // Avancer x
                var measure = new Win32.RECT { left = 0, top = 0, right = 999, bottom = 999 };
                Win32.DrawTextW(hdc, token, token.Length, ref measure, Win32.DT_CALCRECT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
                x += measure.right;
            }
            lineY += lineH;
        }
    }

    /// <summary>Mesure la hauteur d'une ligne de résultat.</summary>
    private int MeasureRowHeight(IntPtr hdc, CharEntry entry, int nameColW, int methodColW,
        IntPtr hFontName, IntPtr hFontMethod, int rowPad)
    {
        int minH = Scale(44); // Hauteur minimale (1 ligne)
        int pad = Scale(8);

        // Mesurer le nom
        Win32.SelectObject(hdc, hFontName);
        var nameRect = new Win32.RECT { left = 0, top = 0, right = nameColW - pad, bottom = 9999 };
        int nameH = Win32.DrawTextW(hdc, entry.NameFr, entry.NameFr.Length, ref nameRect,
            Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_CALCRECT | Win32.DT_NOPREFIX);

        // Mesurer la méthode
        Win32.SelectObject(hdc, hFontMethod);
        var methodRect = new Win32.RECT { left = 0, top = 0, right = methodColW - pad, bottom = 9999 };
        int methodH = Win32.DrawTextW(hdc, entry.MethodDisplay, entry.MethodDisplay.Length, ref methodRect,
            Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_CALCRECT | Win32.DT_NOPREFIX);

        int contentH = Math.Max(nameRect.bottom, methodRect.bottom) + rowPad * 2;
        return Math.Max(minH, contentH);
    }

    /// <summary>Calcule la hauteur d'une ligne pour le hit testing (clic).</summary>
    private int GetRowHeight(int index)
    {
        if (index < 0 || index >= _filteredResults.Count) return Scale(44);

        // On a besoin d'un DC pour mesurer — utiliser le DC de la fenêtre
        var hdc = Win32.GetDC(_hWnd);
        int pad = Scale(8);
        int charColW = Scale(BASE_CHAR_COL_W);
        int nameColW = Scale(BASE_NAME_COL_W);
        Win32.GetClientRect(_hWnd, out var cr);
        int methodColW = (cr.right - cr.left) - charColW - nameColW - pad;
        int rowPad = Scale(BASE_ROW_PAD);

        int h = MeasureRowHeight(hdc, _filteredResults[index], nameColW, methodColW, _hFontName, _hFontMethod, rowPad);

        Win32.ReleaseDC(_hWnd, hdc);
        return h;
    }

    // ═══════════════════════════════════════════════════════════════
    // Cleanup
    // ═══════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_hEdit != IntPtr.Zero)
        {
            Win32.RemoveWindowSubclass(_hEdit, _editSubclassProc, (UIntPtr)1);
            _hEdit = IntPtr.Zero;
        }
        if (_hEditBgBrush != IntPtr.Zero)
        {
            Win32.DeleteObject(_hEditBgBrush);
            _hEditBgBrush = IntPtr.Zero;
        }
        // Polices cachées
        if (_hFontChar != IntPtr.Zero) { Win32.DeleteObject(_hFontChar); _hFontChar = IntPtr.Zero; }
        if (_hFontName != IntPtr.Zero) { Win32.DeleteObject(_hFontName); _hFontName = IntPtr.Zero; }
        if (_hFontMethod != IntPtr.Zero) { Win32.DeleteObject(_hFontMethod); _hFontMethod = IntPtr.Zero; }
        if (_hFontFooter != IntPtr.Zero) { Win32.DeleteObject(_hFontFooter); _hFontFooter = IntPtr.Zero; }
        if (_hFontPlaceholder != IntPtr.Zero) { Win32.DeleteObject(_hFontPlaceholder); _hFontPlaceholder = IntPtr.Zero; }
        if (_hFontEdit != IntPtr.Zero) { Win32.DeleteObject(_hFontEdit); _hFontEdit = IntPtr.Zero; }
        if (_hWnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
    }
}

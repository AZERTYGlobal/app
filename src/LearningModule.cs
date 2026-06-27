// Mini-module d'apprentissage — exercices guidés avec clavier virtuel intégré
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AZERTYGlobal;

/// <summary>
/// Fenêtre d'exercices de frappe lancée depuis l'onboarding (étape 1 → "Essayer maintenant").
/// Affiche un texte cible, compare caractère par caractère, et guide l'utilisateur
/// via un clavier virtuel avec highlight du prochain caractère à taper.
/// </summary>
sealed class LearningModule : IDisposable
{
    // ── Étapes ──────────────────────────────────────────────────────
    // KeepCapsHighlight : si true, Verr.Maj reste systematiquement dans le highlight
    // (contour vert si pas activee, fond vert plein si activee) tant que l'exercice tourne.
    private record struct LearningStep(string Title, string Instruction, string Target,
        bool Skippable, bool KeepCapsHighlight);

    private static readonly LearningStep[] _steps =
    {
        new("Votre premier É",
            "Activez Verr. Maj. puis tapez sur la lettre é",
            "É", false, true),
        new("Majuscules et ponctuation",
            "Gardez le Verrouillage Majuscule activé pour taper cette phrase",
            "GRÂCE À AZERTY GLOBAL, ÉCRIRE EN FRANÇAIS EST TRÈS FACILE !", false, true),
        new("Adresse e-mail",
            "Tapez cette adresse e-mail \u2014 le @ est sur la touche \u00b2 et le point est en acc\u00e8s direct",
            "jean.dupont@education.gouv.fr", false, false),
        new("Typographie fran\u00e7aise",
            "Tapez cette phrase avec les caract\u00e8res typographiques \u2014 suivez les indications du clavier",
            "L\u00e6titia demande \u00ab d'o\u00f9 vient ce chef-d'\u0153uvre\u2026 \u00bb \u2014 elle l'approuve \u00e0 100 %.", false, false),
        new("Ligne de code",
            "Tapez cette ligne de code \u2014 les symboles sont accessibles via AltGr",
            "type Config = { items: string[]; sep: \"~\" | \"\\\\\" };", true, false),
        new("Mots \u00e9trangers",
            "Tapez ces mots \u00e9trangers \u2014 utilisez les touches mortes indiqu\u00e9es sur le clavier",
            "S\u00e3o Paulo, C\u00f3rdoba, Troms\u00f8, \u0141\u00f3d\u017a, luned\u00ec, Gr\u00f6\u00dfe", true, false),
    };

    // ── Window constants ────────────────────────────────────────────
    // Nom de classe Win32. Defini en const partagee entre CreateMainWindow et Dispose
    // (UnregisterClassW au Dispose pour eviter que la classe survive l'instance et garde
    // un delegate WndProc collecte par GC).
    private const string WND_CLASS_NAME = "AZERTYGlobal_Learning";

    // Dimensions de référence à 96 DPI. Bouton « Quitter » placé en haut à droite,
    // pas de légende footer, donc on peut réduire la hauteur.
    private const int BASE_WIN_W = 920;
    private const int BASE_WIN_H = 470;  // -25 (suppression BASE_STATUS_H) -15 (BASE_FOOTER_H reduit)

    // Layout vertical (base 96 DPI)
    private const int BASE_HEADER_H = 54;       // dots progression (~22) + titre (~32) sans overlap
    private const int BASE_INSTRUCTION_H = 32;
    private const int BASE_TARGET_H = 60;
    // BASE_STATUS_H supprime — l'ancienne ligne "Verr.Maj : ... — Touche morte : ..." est
    // remplacee par un bloc droit (PaintRightStatusBlock) dessine au-dessus du clavier.
    private const int BASE_FOOTER_H = 3;        // Marge basse minimale sous le clavier (avant 18, gain de 15px)
    private const int BASE_MARGIN = 20;

    // ── Control IDs ─────────────────────────────────────────────────
    private const int IDC_BTN_QUIT = 4001;
    private const int IDC_BTN_SKIP = 4002;
    private const int IDC_BTN_FINISH = 4003;
    // Boutons affiches a la fin de chaque exercice (page de choix Reessayer / Suivant)
    private const int IDC_BTN_RETRY = 4004;
    private const int IDC_BTN_CONTINUE = 4005;

    // ── Timer IDs ───────────────────────────────────────────────────
    private const uint TIMER_KEYPRESS = 8001;
    private const uint TIMER_TRANSITION = 8002;
    private const uint TIMER_REFOCUS = 8003;
    private const uint TIMER_FOCUS_LOST_CONFIRM = 8004;
    private const uint TIMER_CAPS_RESYNC = 8005;
    private const uint FOCUS_LOSS_DEBOUNCE_MS = 250;
    private const uint KEYPRESS_DURATION_MS = 120;
    private const uint TRANSITION_DURATION_MS = 800;
    private const int REFOCUS_MAX_ATTEMPTS = 6;     // ~6 × 80ms = 480ms total

    // ── Colors (COLORREF = 0x00BBGGRR) ──────────────────────────────
    // Fond unifié sombre (cohérent avec le testeur web). Toutes les zones (titre, instruction,
    // target, status, footer, clavier) partagent le même fond. Couleurs de texte adaptées
    // au contraste sur fond sombre (~#1A1A1A → texte clair #E0E0E0 = ratio 14:1).
    private const uint CLR_BG = 0x001A1A1A;                  // Fond fenêtre — sombre quasi-noir
    private const uint CLR_HEADER_TITLE = 0x00E0E0E0;        // Titre — blanc cassé
    private const uint CLR_INSTRUCTION = 0x00CCCCCC;         // Instruction — gris clair sur fond sombre
    private const uint CLR_TARGET_PENDING = 0x00808080;      // Caractères cible non encore tapés — gris moyen
    private const uint CLR_TARGET_CURRENT = 0x00FFFFFF;      // Caractère courant — blanc pur
    private const uint CLR_TARGET_CORRECT = 0x005EC522;      // Caractère validé — vert
    private const uint CLR_TARGET_ERROR = 0x004444EF;        // Erreur de frappe — rouge
    private const uint CLR_STATUS = 0x00AAAAAA;              // Barre de statut — gris clair
    private const uint CLR_PROGRESS_DONE = 0x00D47800;       // Dots progression terminés — orange brand
    private const uint CLR_PROGRESS_TODO = 0x00606060;       // Dots progression à faire — gris foncé (sur fond sombre)
    private const uint CLR_PROGRESS_CURRENT = 0x005EC522;    // Dot exercice en cours — vert (BGR, = #22C55E)
    private const uint CLR_TRANSITION = 0x005EC522;          // Animation transition entre exercices — vert
    private const uint CLR_BTN_QUIT_TEXT = 0x00AAAAAA;       // Bouton « Quitter les exercices » — gris clair

    // Suffixe « (Bonus) » a la suite du titre pour les exos optionnels — dore-orange.
    private const uint CLR_BONUS_TEXT = 0x000094E2;          // BGR ≈ #E29400 (orange ambré, lisible sur fond sombre)

    // Clavier virtuel — couleurs alignées sur le testeur web (mode dark) pour cohérence
    // visuelle. Cf. tester/keyboard.css : --key-bg #3a3a3a, --key-border #555, --text-base
    // #e0e0e0, --text-dimmed #999, --text-active #66b3ff.
    private const uint CLR_KB_BG = 0x001A1A1A;               // Fond zone clavier — identique à CLR_BG (unifié)
    private const uint CLR_KEY = 0x003A3A3A;                 // Fond touche normale
    private const uint CLR_KEY_BORDER = 0x00555555;          // Bordure touche
    private const uint CLR_KEY_PRESSED = 0x006A4A2A;         // Touche enfoncée — bleu sombre BGR (#2A4A6A)
    private const uint CLR_KEY_CTX = 0x002D2D2D;             // Fond touche contextuelle/modif — légèrement plus sombre
    private const uint CLR_KEY_DISABLED = 0x002A2A2A;        // Fond touche désactivée pendant les exercices (Backspace) — plus terne
    private const uint CLR_CHAR_BASE = 0x00E0E0E0;           // Caractères couches affichées non-actives — blanc cassé
    private const uint CLR_CHAR_ACTIVE = 0x00E0E0E0;         // (legacy) caractère principal des touches lettres — blanc cassé. Phase B: à scinder en CLR_CHAR_ACTIVE_BLUE pour le caractère réellement actif.
    private const uint CLR_CHAR_ACTIVE_BLUE = 0x00FFB366;    // Caractère actif (qui sera tapé selon modificateurs courants) — bleu vif BGR (#66B3FF)
    private const uint CLR_CHAR_DIM = 0x00999999;            // Caractères inactifs (couches non sélectionnées) — gris moyen
    private const uint CLR_CHAR_ALTGR_ACCENT = 0x00FFB366;   // Identique à CLR_CHAR_ACTIVE_BLUE (placeholder, à revoir plus tard)
    private const uint CLR_DK_CHAR = 0x006666FF;             // Touche morte active — rouge clair BGR (#FF6666)
    private const uint CLR_CTX_TEXT = 0x00E0E0E0;            // Texte touches contextuelles — blanc cassé
    private const uint CLR_MOD_ACTIVE = 0x009A5A1A;          // Fond modificateur activé — bleu BGR (#1A5A9A) — utilisé en cas (b) Q5
    private const uint CLR_CAPS_BAR = 0x0000A5FF;            // Barre orange Verr.Maj actif — orange (kept)
    private const uint CLR_DK_RESULT = 0x0066CC66;           // Résultat touche morte — vert clair BGR (#66CC66)

    // Highlight
    private const uint CLR_HL_DIRECT = 0x0064C800;
    private const uint CLR_HL_DIRECT_BG = 0x00284018;
    private const uint CLR_HL_STEP1 = 0x0000A5FF;
    private const uint CLR_HL_STEP1_BG = 0x00283020;
    private const uint CLR_HL_STEP2 = 0x004CB050;
    private const uint CLR_HL_STEP2_BG = 0x00203818;

    // ── Classification des touches (alignée sur tester/keyboard.js LETTER_KEYS / ACCENTED_LETTER_KEYS) ──
    // Lettres ordinaires : caractère principal centré + AltGr discret bottom-right.
    // Layout AZERTY ISO scancodes :
    //   Rangée 2 (azerty)  : KeyA-KeyP = 0x10..0x19
    //   Rangée 3 (qsdfg)   : KeyQ-KeyM = 0x1E..0x27 (Semicolon=KeyM=0x27 sur AZERTY)
    //   Rangée 4 (wxcv)    : KeyZ-KeyN = 0x2C..0x31
    private static readonly HashSet<uint> LetterKeyScancodes = new()
    {
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,  // a z e r t y u i o p
        0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,  // q s d f g h j k l m
        0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31                            // w x c v b n
    };

    // Lettres accentuées sur la rangée numérique (Digit2=é, Digit7=è, Digit9=ç, Digit0=à) :
    // affichage 4 quadrants avec lettre en bottom-left, chiffre en top-left, AltGr/Shift+AltGr à droite.
    private static readonly HashSet<uint> AccentedNumericScancodes = new()
    {
        0x03,  // Digit2 → é/2
        0x08,  // Digit7 → è/7
        0x0A,  // Digit9 → ç/9
        0x0B   // Digit0 → à/0
    };

    // ═══════════════════════════════════════════════════════════════
    // Live tweaks (learning-tweaks.json)
    // ═══════════════════════════════════════════════════════════════
    /// <summary>
    /// Override visuel par caractere : taille de fonte specifique et/ou offset XY et/ou
    /// famille de fonte. Tous les champs sont optionnels (null = pas d'override sur ce champ).
    /// </summary>
    private sealed class CharOverride
    {
        public int? FontSize { get; set; }    // taille en pt (logique 96 DPI)
        public int? OffsetX { get; set; }     // decalage horizontal en px (logique 96 DPI)
        public int? OffsetY { get; set; }     // decalage vertical en px
        public string? Font { get; set; }     // famille de fonte (null = "Segoe UI" par defaut)
        // Champs specifiques aux caracteres prefixes par ◌ (touches mortes) ET dont le suffixe
        // est NON-combinant (˙ ˝ ˘ / − ˇ ˛) : permet de positionner le ◌ independamment du
        // suffixe. Ignores pour les combinants purs (̣ ̏ ̛ ̉ ̑) qui ne peuvent pas etre rendus
        // sans caractere de base.
        public int? CircleFontSize { get; set; }
        public int? CircleOffsetX { get; set; }
        public int? CircleOffsetY { get; set; }
    }

    /// <summary>
    /// Parametres ajustables sans recompilation. Charges depuis learning-tweaks.json
    /// dans <see cref="ConfigManager.LogDirectory"/> (LocalAppData en MSIX, a cote de l'exe
    /// en unpackaged). Re-lus a chaque construction de LearningModule, donc le bouton tray
    /// "Reinitialiser onboarding" suffit a appliquer les changements.
    /// </summary>
    private sealed class LearningTweaks
    {
        public int FontSizeMain  { get; set; } = 28;   // _hFontCharMain
        public int FontSizeSmall { get; set; } = 25;   // _hFontCharSmall
        public int FontSizeCtx   { get; set; } = 22;   // _hFontCtx (labels Tab/Shift/etc.)
        public float PadRatio    { get; set; } = 0.14f; // padding interieur des touches (ratio de l'echelle)
        public Dictionary<string, CharOverride> CharOverrides { get; set; } = new();

        public static string DefaultPath =>
            System.IO.Path.Combine(ConfigManager.LogDirectory, "learning-tweaks.json");

        public static LearningTweaks Load()
        {
            var path = DefaultPath;
            try
            {
                if (!System.IO.File.Exists(path))
                    return new LearningTweaks();
                var json = System.IO.File.ReadAllText(path);
                var tweaks = new LearningTweaks();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("fontSizeMain", out var fm) && fm.TryGetInt32(out int fmv))   tweaks.FontSizeMain = fmv;
                if (root.TryGetProperty("fontSizeSmall", out var fs) && fs.TryGetInt32(out int fsv))  tweaks.FontSizeSmall = fsv;
                if (root.TryGetProperty("fontSizeCtx", out var fc) && fc.TryGetInt32(out int fcv))    tweaks.FontSizeCtx = fcv;
                if (root.TryGetProperty("padRatio", out var pr) && pr.TryGetSingle(out float prv))    tweaks.PadRatio = prv;
                if (root.TryGetProperty("charOverrides", out var co) && co.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in co.EnumerateObject())
                    {
                        var ovr = new CharOverride();
                        if (entry.Value.TryGetProperty("fontSize", out var ofs) && ofs.TryGetInt32(out int ofsv)) ovr.FontSize = ofsv;
                        if (entry.Value.TryGetProperty("offsetX", out var oox) && oox.TryGetInt32(out int ooxv))  ovr.OffsetX = ooxv;
                        if (entry.Value.TryGetProperty("offsetY", out var ooy) && ooy.TryGetInt32(out int ooyv))  ovr.OffsetY = ooyv;
                        if (entry.Value.TryGetProperty("font", out var off) && off.ValueKind == JsonValueKind.String)
                        {
                            var fname = off.GetString();
                            if (!string.IsNullOrWhiteSpace(fname)) ovr.Font = fname;
                        }
                        if (entry.Value.TryGetProperty("circleFontSize", out var ocfs) && ocfs.TryGetInt32(out int ocfsv)) ovr.CircleFontSize = ocfsv;
                        if (entry.Value.TryGetProperty("circleOffsetX", out var ocox) && ocox.TryGetInt32(out int ocoxv)) ovr.CircleOffsetX = ocoxv;
                        if (entry.Value.TryGetProperty("circleOffsetY", out var ocoy) && ocoy.TryGetInt32(out int ocoyv)) ovr.CircleOffsetY = ocoyv;
                        tweaks.CharOverrides[entry.Name] = ovr;
                    }
                }
                ConfigManager.LogCompatEvent("LearningTweaks",
                    $"loaded : main={tweaks.FontSizeMain}, small={tweaks.FontSizeSmall}, ctx={tweaks.FontSizeCtx}, pad={tweaks.PadRatio}, overrides={tweaks.CharOverrides.Count}");
                return tweaks;
            }
            catch (Exception ex)
            {
                ConfigManager.Log("LearningTweaks.Load", ex);
                return new LearningTweaks();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Champs d'instance
    // ═══════════════════════════════════════════════════════════════
    private IntPtr _hWnd;
    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly IntPtr _hWndOnboarding;
    private readonly LearningTweaks _tweaks;
    // Cache des fontes per-(size, fontFamily) pour les CharOverrides (evite recreation a chaque WM_PAINT).
    // Cle = "fontName_sizePt" pour pouvoir mixer plusieurs familles.
    private readonly Dictionary<string, IntPtr> _hFontCharCache = new();

    // Références app
    private readonly KeyMapper _mapper;
    private readonly KeyboardHook _hook;
    private readonly Layout _layout;

    // Données de highlight (character-index.json)
    private readonly Dictionary<string, CharacterSearch.MethodData> _charMethods = new();        // recommended (Shift pour majuscules)
    private readonly Dictionary<string, CharacterSearch.MethodData> _charMethodsCaps = new();    // alternative Caps (utilisée si l'exercice a KeepCapsHighlight=true)
    private readonly Dictionary<string, (string key, string layer)> _dkActivations = new();
    private readonly Dictionary<string, string> _charNames = new(); // char → unicodeNameFr

    /// <summary>
    /// Overrides explicites des noms de caractères pour les tooltips du clavier mini-onboarding,
    /// quand le nom Unicode officiel (unicodeNameFr) est trop technique. Prioritaire sur _charNames.
    /// </summary>
    private static readonly Dictionary<string, string> CharNamesOverride = new()
    {
        ["’"] = "APOSTROPHE TYPOGRAPHIQUE",
    };

    // Layout clavier
    private readonly VirtualKeyboard.VisualKey[] _visualKeys;

    // Hit-testing pour tooltips : reseté à chaque PaintKeyboard. Usage seul-thread (UI).
    private readonly List<(Win32.RECT Rect, uint Scancode, string? ContextLabel)> _keyHitAreas = new();
    private int _hoveredKeyIndex = -1;
    private IntPtr _hTooltip;
    private bool _trackingMouse;

    // Pause visuelle quand la fenêtre n'a plus le focus clavier (option A user 2026-05-01) :
    // dim overlay sur le clavier + message « Cliquez pour reprendre ». Le clic restaure
    // automatiquement le focus via Windows et renvoie un WM_SETFOCUS qui repasse à true.
    // _hasFocus reflete l'etat reel ; _focusLostConfirmed est mis a true seulement apres
    // un debounce de 250ms (FOCUS_LOSS_DEBOUNCE_MS) pour eviter d'afficher l'overlay sur
    // des "blinks" focus rapides causes par MoveWindow / ShowWindow / repaint forces.
    private bool _hasFocus = true;
    private bool _focusLostConfirmed;
    private bool _inputPaused;

    // État
    private int _currentStep;
    private int _cursorPosition;
    private bool _currentCharError;
    private bool _completed; // écran final
    private bool _inTransition;
    // Page de choix affichee a la fin de chaque exercice : Reessayer / Suivant.
    // Remplace l'ancienne transition automatique apres TIMER_TRANSITION.
    private bool _awaitingChoice;
    // Compteur de succes pour l'exercice courant. Reset a chaque AdvanceToNextStep.
    // Le titre « ✓ Bravo ! » et le sous-titre ne s'affichent qu'au 1er succes (=1).
    // Aux reussites suivantes (apres Recommencer), on n'affiche que les boutons.
    private int _currentStepSuccessCount;


    // Animation de frappe
    private uint _pressedScancode;

    // Le module d'initiation doit rester positionnel, meme si le layout Windows sous-jacent
    // laisse passer ponctuellement un caractere natif (ex: QWERTY US D01 -> q). RawKeyDown
    // arrive avant l'emission de caractere : on y capture le texte AZERTY Global attendu.
    private string? _pendingPhysicalText;
    private int _pendingPhysicalTextIndex;
    private long _pendingPhysicalTextTick;
    private const int PENDING_PHYSICAL_TEXT_TIMEOUT_MS = 1000;

    // Highlight du prochain caractère
    private readonly HashSet<uint> _highlightedScancodes = new();
    private readonly HashSet<string> _highlightedLabels = new();
    private readonly HashSet<string> _highlightedContextIds = new();
    private string _highlightType = ""; // "direct", "step1", "step2"
    private CharacterSearch.MethodData? _pendingStep2;

    // Contrôles
    private IntPtr _hWndBtnQuit;
    private IntPtr _hWndBtnSkip;
    private IntPtr _hWndBtnFinish;
    private IntPtr _hWndBtnRetry;     // page de choix fin d'exercice
    private IntPtr _hWndBtnContinue;  // page de choix fin d'exercice (« Suivant » ou « Terminer »)

    // États hover pour boutons owner-drawn (Quit + Skip → fond rouge clair au survol)
    private bool _quitHovered;
    private bool _skipHovered;
    private Win32.SUBCLASSPROC? _quitSubclassProc;
    private Win32.SUBCLASSPROC? _skipSubclassProc;

    // DPI
    private float _dpiScale = 1f;
    private int S(int val) => (int)(val * _dpiScale);

    // Polices
    private IntPtr _hFontTitle;
    private IntPtr _hFontInstruction;
    private IntPtr _hFontTarget;
    private IntPtr _hFontStatus;
    private IntPtr _hFontButton;
    private IntPtr _hFontCharMain;
    private IntPtr _hFontCharSmall;
    private IntPtr _hFontCtx;
    private IntPtr _hFontProgress;
    private IntPtr _hFontTransition;
    private IntPtr _hFontBadge;

    // Brushes
    private IntPtr _hBgBrush;
    private IntPtr _hKbBgBrush;

    // Callback de fermeture
    /// <summary>
    /// Callback invoqué à la fermeture du module. Le bool indique si l'utilisateur a
    /// vraiment complété les 6 exercices (true → page « Bravo ! » + Terminer) ou s'il
    /// a fermé prématurément (false → croix, bouton « Quitter », Esc).
    /// </summary>
    public Action<bool>? OnClosed;

    // replayMode = true : lancement depuis le menu tray (« Exercices »). On ne persiste
    // pas la progression dans config (ConfigManager.SetLearningMaxStepCompleted), car
    // la state sauvegardee doit refleter UNIQUEMENT le 1er passage onboarding. Replay
    // depuis le tray = entrainement, pas de side-effect sur la progression officielle.
    private readonly bool _replayMode;

    public LearningModule(IntPtr hWndOnboarding, KeyMapper? mapper, KeyboardHook? hook, Layout? layout, bool replayMode = false)
    {
        ConfigManager.LogCrashTraceDebug("LM.ctor: enter");
        ConfigManager.LogCrashTraceDebug($"LM.ctor: params hWndOnb={hWndOnboarding}, mapper={mapper != null}, hook={hook != null}, layout={layout != null}, replay={replayMode}");
        // Validation explicite après le log diagnostic : si l'un est null, on aura logué
        // l'état exact des params avant de lever (utile pour le bug crash post-Reset).
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(hook);
        ArgumentNullException.ThrowIfNull(layout);
        _replayMode = replayMode;
        _tweaks = LearningTweaks.Load(); // re-lu a chaque ctor → bouton "Reinitialiser onboarding" applique les changements
        _hWndOnboarding = hWndOnboarding;
        ConfigManager.LogCrashTraceDebug("LM.ctor: A1 _hWndOnboarding assigned");
        _mapper = mapper;
        ConfigManager.LogCrashTraceDebug("LM.ctor: A2 _mapper assigned");
        _hook = hook;
        ConfigManager.LogCrashTraceDebug("LM.ctor: A3 _hook assigned");
        _layout = layout;
        ConfigManager.LogCrashTraceDebug("LM.ctor: A4 _layout assigned");
        _wndProcDelegate = WndProc;
        ConfigManager.LogCrashTraceDebug("LM.ctor: A5 _wndProcDelegate created");
        _visualKeys = VirtualKeyboard.BuildKeyLayout();
        ConfigManager.LogCrashTraceDebug($"LM.ctor: BuildKeyLayout done ({_visualKeys.Length} keys)");

        _hBgBrush = Win32.CreateSolidBrush(CLR_BG);
        _hKbBgBrush = Win32.CreateSolidBrush(CLR_KB_BG);

        // DPI initial
        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        int dpi = Win32.GetDeviceCaps(hdcScreen, 88);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        _dpiScale = dpi / 96f;
        ConfigManager.LogCrashTraceDebug($"LM.ctor: dpi={dpi}, scale={_dpiScale}");

        LoadCharacterMethods();
        ConfigManager.LogCrashTraceDebug("LM.ctor: LoadCharacterMethods done");
        CreateFonts();
        ConfigManager.LogCrashTraceDebug("LM.ctor: CreateFonts done");
        CreateMainWindow();
        ConfigManager.LogCrashTraceDebug($"LM.ctor: CreateMainWindow done, _hWnd={_hWnd}");
        CreateControls();
        ConfigManager.LogCrashTraceDebug("LM.ctor: CreateControls done");
        UpdateControlVisibility();
        ConfigManager.LogCrashTraceDebug("LM.ctor: UpdateControlVisibility done");

        // Corriger le DPI avec le vrai DPI du moniteur
        try
        {
            int realDpi = Win32.GetDpiForWindow(_hWnd);
            if (realDpi > 0 && Math.Abs(realDpi / 96f - _dpiScale) > 0.01f)
            {
                _dpiScale = realDpi / 96f;
                RecreateFonts();
                ResizeWindow();
            }
        }
        catch { }

        // S'abonner aux événements
        _mapper.StateChanged += OnStateChanged;
        _hook.RawKeyDown += OnRawKeyDown;
        ConfigManager.LogCrashTraceDebug("LM.ctor: events subscribed");

        // Highlight initial
        UpdateHighlight();

        // Tooltip pour les touches du clavier (au survol). Comportement aligné sur le
        // testeur web : affiche le caractère + son nom Unicode FR pour les 4 couches.
        CreateTooltip();
        ConfigManager.LogCrashTraceDebug("LM.ctor: UpdateHighlight done — exit");
    }

    // ═══════════════════════════════════════════════════════════════
    // Tooltip
    // ═══════════════════════════════════════════════════════════════
    private const uint TTS_ALWAYSTIP = 0x01;
    private const uint TTS_NOPREFIX = 0x02;
    private const uint TTF_SUBCLASS = 0x0010;
    private const uint TTF_TRANSPARENT = 0x0100;
    private const uint TTM_ADDTOOLW = 0x0432;
    private const uint TTM_UPDATETIPTEXTW = 0x0439;
    private const uint TTM_NEWTOOLRECTW = 0x0434;
    private const uint TTM_SETMAXTIPWIDTH = 0x0418;
    private const uint TTM_SETDELAYTIME = 0x0403;
    private const uint TTDT_INITIAL = 3;
    private const uint TTDT_RESHOW = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TOOLINFOW
    {
        public uint cbSize;
        public uint uFlags;
        public IntPtr hwnd;
        public UIntPtr uId;
        public Win32.RECT rect;
        public IntPtr hinst;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszText;
        public IntPtr lParam;
    }

    private void CreateTooltip()
    {
        _hTooltip = Win32.CreateWindowExW(
            Win32.WS_EX_TOPMOST,
            "tooltips_class32", "",
            Win32.WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX,
            0, 0, 0, 0,
            _hWnd, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);

        if (_hTooltip == IntPtr.Zero) return;

        Win32.SendMessageW(_hTooltip, TTM_SETMAXTIPWIDTH, IntPtr.Zero, (IntPtr)420);
        Win32.SendMessageW(_hTooltip, TTM_SETDELAYTIME, (IntPtr)TTDT_INITIAL, (IntPtr)200);
        Win32.SendMessageW(_hTooltip, TTM_SETDELAYTIME, (IntPtr)TTDT_RESHOW, (IntPtr)50);

        // Outil unique couvrant la fenêtre — on actualise sa zone à chaque hover.
        var ti = new TOOLINFOW
        {
            cbSize = (uint)Marshal.SizeOf<TOOLINFOW>(),
            uFlags = TTF_SUBCLASS | TTF_TRANSPARENT,
            hwnd = _hWnd,
            uId = (UIntPtr)1,
            rect = new Win32.RECT(),
            lpszText = ""
        };
        // Audit sécu 2026-05 SEV-A2-01 : try/finally pour éviter memory leak si exception.
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TOOLINFOW>());
        try
        {
            Marshal.StructureToPtr(ti, ptr, false);
            Win32.SendMessageW(_hTooltip, TTM_ADDTOOLW, IntPtr.Zero, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private void SetTooltip(string text, Win32.RECT rect)
    {
        if (_hTooltip == IntPtr.Zero) return;
        var ti = new TOOLINFOW
        {
            cbSize = (uint)Marshal.SizeOf<TOOLINFOW>(),
            hwnd = _hWnd,
            uId = (UIntPtr)1,
            rect = rect,
            lpszText = text
        };
        // Audit sécu 2026-05 SEV-A2-01 : try/finally pour éviter memory leak si exception.
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TOOLINFOW>());
        try
        {
            Marshal.StructureToPtr(ti, ptr, false);
            Win32.SendMessageW(_hTooltip, TTM_UPDATETIPTEXTW, IntPtr.Zero, ptr);
            Win32.SendMessageW(_hTooltip, TTM_NEWTOOLRECTW, IntPtr.Zero, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Texte multi-ligne décrivant les couches d'une touche. Aligné sur deadkeys.js / keyboard.js.</summary>
    private string BuildTooltipText(uint scancode, string? contextLabel)
    {
        if (contextLabel != null)
        {
            return contextLabel switch
            {
                "Tab" => "Tabulation",
                "⌫" => "Désactivé pendant les exercices — continue de taper,\nl'erreur se corrige toute seule",
                "Verr. Maj." => "Verrouillage Majuscule (Caps Lock)",
                "Maj ⇧" => "Majuscule (Shift)",
                "Entrée" => "Entrée",
                "Ctrl" => "Contrôle (Ctrl)",
                "Win" => "Touche Windows",
                "Alt" => "Alt",
                "AltGr" => "Alt droite (AltGr)",
                "Menu" => "Menu contextuel",
                _ => contextLabel
            };
        }
        if (scancode == 0 || !_layout.Keys.TryGetValue(scancode, out var keyDef)) return "";

        // Si une touche morte est active, le tooltip affiche le RESULTAT de la combinaison
        // pour chaque couche (cf. testeur web). Sinon, comportement standard.
        DeadKeyDefinition? activeDk = null;
        var activeDkName = _mapper.ActiveDeadKey;
        if (activeDkName != null) _layout.DeadKeys.TryGetValue(activeDkName, out activeDk);

        var sb = new System.Text.StringBuilder();
        AppendTooltipLayer(sb, "Base", keyDef.Base, activeDk);
        AppendTooltipLayer(sb, "Maj", keyDef.Shift, activeDk);
        AppendTooltipLayer(sb, "AltGr", keyDef.AltGr, activeDk);
        AppendTooltipLayer(sb, "Maj+AltGr", keyDef.ShiftAltGr, activeDk);
        return sb.ToString().TrimEnd('\n');
    }

    private void AppendTooltipLayer(System.Text.StringBuilder sb, string label, string? value, DeadKeyDefinition? activeDk = null)
    {
        if (string.IsNullOrEmpty(value)) return;

        // Touche morte active : montrer le resultat de la combinaison si la touche morte
        // s'applique au caractere de cette couche. Sinon (pas de combo, ou couche elle-meme
        // une autre touche morte), on n'affiche rien pour cette couche.
        if (activeDk != null)
        {
            if (value.StartsWith("dk_"))
                return; // pas de combinaison dk→dk a afficher
            var combined = activeDk.Apply(value);
            if (combined != null)
            {
                sb.Append(label).Append(" : ").Append(combined);
                if (_charNames.TryGetValue(combined, out var combinedName) && !string.IsNullOrEmpty(combinedName))
                    sb.Append(" — ").Append(combinedName.ToUpperInvariant());
                sb.Append('\n');
            }
            return;
        }

        // Comportement standard (pas de touche morte active OU couche elle-meme une touche morte).
        string disp = GetDisplayChar(value) ?? value;
        sb.Append(label).Append(" : ").Append(disp);
        if (value.StartsWith("dk_"))
        {
            // Touche morte : toujours afficher « Touche morte {nom} », jamais le nom du symbole isole
            // (sinon dk_misc_symbols afficherait « FLÈCHE VERS LA DROITE » au lieu de « Symboles divers »).
            // Source de verite : VirtualKeyboard._deadKeyNamesFr (aligne sur tester/deadkeys.js).
            // Fallback sur _layout.DeadKeys[].Description du JSON (peut etre en anglais).
            if (VirtualKeyboard._deadKeyNamesFr.TryGetValue(value, out var dkName))
                sb.Append(" — TOUCHE MORTE ").Append(dkName.ToUpperInvariant());
            else if (_layout.DeadKeys.TryGetValue(value, out var dk))
                sb.Append(" — TOUCHE MORTE ").Append(dk.Description.ToUpperInvariant());
        }
        else if (CharNamesOverride.TryGetValue(disp, out var overrideName))
            sb.Append(" — ").Append(overrideName.ToUpperInvariant());
        else if (_charNames.TryGetValue(disp, out var name) && !string.IsNullOrEmpty(name))
            sb.Append(" — ").Append(name.ToUpperInvariant());
        sb.Append('\n');
    }

    // ═══════════════════════════════════════════════════════════════
    // Chargement character-index.json
    // ═══════════════════════════════════════════════════════════════
    private void LoadCharacterMethods()
    {
        string json;
        using (var stream = typeof(LearningModule).Assembly.GetManifestResourceStream("character-index.json"))
        {
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }

        using var doc = JsonDocument.Parse(json);
        var characters = doc.RootElement.GetProperty("characters");

        // Première passe : activations de touches mortes
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
                _dkActivations[dkName] = (key, layer);
                break;
            }
        }

        // Deuxième passe : méthodes par caractère
        foreach (var entry in characters.EnumerateObject())
        {
            if (entry.Name.StartsWith("dk:")) continue;
            if (!entry.Value.TryGetProperty("methods", out var methods)) continue;

            JsonElement? recommended = null;
            JsonElement? capsMethod = null;
            JsonElement? fallback = null;
            foreach (var method in methods.EnumerateArray())
            {
                var layer = method.TryGetProperty("layer", out var l) ? l.GetString() ?? "" : "";
                if (capsMethod == null && layer.StartsWith("Caps")) capsMethod = method;
                if (recommended == null
                    && method.TryGetProperty("recommended", out var rec) && rec.GetBoolean())
                    recommended = method;
                fallback ??= method;
            }
            // Méthode par défaut = recommended (généralement Shift pour les majuscules).
            // L'exercice peut décider d'utiliser la variante Caps si KeepCapsHighlight=true,
            // via le dict _charMethodsCaps.
            JsonElement? chosen = recommended ?? fallback;
            if (!chosen.HasValue) continue;

            var mType = chosen.Value.GetProperty("type").GetString() ?? "";
            var mKey = chosen.Value.TryGetProperty("key", out var mk) ? mk.GetString() ?? "" : "";
            var mLayer = chosen.Value.TryGetProperty("layer", out var ml) ? ml.GetString() ?? "" : "";
            var md = new CharacterSearch.MethodData { Type = mType, Key = mKey, Layer = mLayer };

            if (mType == "deadkey")
            {
                var dkName = chosen.Value.GetProperty("deadkey").GetString() ?? "";
                md.DeadKey = dkName;
                if (_dkActivations.TryGetValue(dkName, out var dkAct))
                {
                    md.DkActivationKey = dkAct.key;
                    md.DkActivationLayer = dkAct.layer;
                }
            }

            _charMethods[entry.Name] = md;

            // Méthode alternative Caps (utilisée pour les exercices KeepCapsHighlight=true)
            if (capsMethod.HasValue)
            {
                var capsType = capsMethod.Value.GetProperty("type").GetString() ?? "";
                var capsKey = capsMethod.Value.TryGetProperty("key", out var ck) ? ck.GetString() ?? "" : "";
                var capsLayer = capsMethod.Value.TryGetProperty("layer", out var cl) ? cl.GetString() ?? "" : "";
                var mdCaps = new CharacterSearch.MethodData { Type = capsType, Key = capsKey, Layer = capsLayer };
                if (capsType == "deadkey")
                {
                    var dkName = capsMethod.Value.GetProperty("deadkey").GetString() ?? "";
                    mdCaps.DeadKey = dkName;
                    if (_dkActivations.TryGetValue(dkName, out var dkAct))
                    {
                        mdCaps.DkActivationKey = dkAct.key;
                        mdCaps.DkActivationLayer = dkAct.layer;
                    }
                }
                _charMethodsCaps[entry.Name] = mdCaps;
            }

            // Nom français pour les tooltips (champ "unicodeNameFr" du character-index).
            if (entry.Value.TryGetProperty("unicodeNameFr", out var nameFr))
            {
                var name = nameFr.GetString();
                if (!string.IsNullOrEmpty(name))
                    _charNames[entry.Name] = name;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Polices
    // ═══════════════════════════════════════════════════════════════
    private void CreateFonts()
    {
        _hFontTitle = Win32.CreateFontW(-S(22), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontInstruction = Win32.CreateFontW(-S(16), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontTarget = Win32.CreateFontW(-S(20), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontStatus = Win32.CreateFontW(-S(15), 0, 0, 0, 500, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontButton = Win32.CreateFontW(-S(14), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        // Caractères dans les touches — tailles ajustables via learning-tweaks.json (FontSizeMain/Small/Ctx).
        _hFontCharMain = Win32.CreateFontW(S(_tweaks.FontSizeMain), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 4, 0, "Consolas");
        _hFontCharSmall = Win32.CreateFontW(S(_tweaks.FontSizeSmall), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Consolas");
        _hFontCtx = Win32.CreateFontW(S(_tweaks.FontSizeCtx), 0, 0, 0, 500, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hFontProgress = Win32.CreateFontW(-S(16), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontTransition = Win32.CreateFontW(-S(28), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontBadge = Win32.CreateFontW(S(9), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
    }

    private void DestroyFonts()
    {
        Win32.DeleteObject(_hFontTitle);
        Win32.DeleteObject(_hFontInstruction);
        Win32.DeleteObject(_hFontTarget);
        Win32.DeleteObject(_hFontStatus);
        Win32.DeleteObject(_hFontButton);
        Win32.DeleteObject(_hFontCharMain);
        Win32.DeleteObject(_hFontCharSmall);
        Win32.DeleteObject(_hFontCtx);
        Win32.DeleteObject(_hFontProgress);
        Win32.DeleteObject(_hFontTransition);
        Win32.DeleteObject(_hFontBadge);
        // Cache des fontes per-char (CharOverrides) : libere et vide
        foreach (var hFont in _hFontCharCache.Values)
            Win32.DeleteObject(hFont);
        _hFontCharCache.Clear();
    }

    /// <summary>
    /// Retourne (ou cree et cache) une fonte de la taille et famille demandees, weight 600.
    /// Utilise par DrawCharAt quand un CharOverride.FontSize ou .Font est defini. Liberees
    /// dans DestroyFonts (et donc aussi a chaque RecreateFonts sur WM_DPICHANGED).
    /// </summary>
    private IntPtr GetOrCreateCharFont(int sizePt, string fontFamily)
    {
        var key = $"{fontFamily}_{sizePt}";
        if (_hFontCharCache.TryGetValue(key, out var existing))
            return existing;
        var hFont = Win32.CreateFontW(S(sizePt), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 4, 0, fontFamily);
        _hFontCharCache[key] = hFont;
        return hFont;
    }

    private void RecreateFonts()
    {
        DestroyFonts();
        CreateFonts();
        Win32.SendMessageW(_hWndBtnQuit, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
        Win32.SendMessageW(_hWndBtnSkip, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
        Win32.SendMessageW(_hWndBtnFinish, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Fenêtre
    // ═══════════════════════════════════════════════════════════════
    private void CreateMainWindow()
    {
        var hInstance = Win32.GetModuleHandleW(null);

        // hbrBackground = IntPtr.Zero : ne PAS référencer _hBgBrush dans la WNDCLASSEXW.
        // La classe Win32 reste enregistrée au-delà de la durée de vie de l'instance
        // (RegisterClassExW est idempotent par className). Si on libère _hBgBrush au Dispose
        // (DeleteObject), la classe garde un pointeur invalide → CreateWindowExW crash à la
        // 2e instance (bug Reset → Essayer après 1ère complétion, traces error.log 2026-05-01).
        // L'effacement du fond est géré côté instance via WM_ERASEBKGND (return 1) + FillRect
        // dans OnPaint avec _hBgBrush, donc la classe n'a pas besoin d'un brush.
        // Couplé avec UnregisterClassW au Dispose pour permettre la 2e instance avec un
        // delegate WndProc frais (sans cela la classe garde un delegate potentiellement collecté).
        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            hCursor = Win32.LoadCursorW(IntPtr.Zero, (IntPtr)32512),
            hbrBackground = IntPtr.Zero,
            lpszClassName = WND_CLASS_NAME
        };
        Win32.RegisterClassExW(ref wc);

        int winW = S(BASE_WIN_W);
        int winH = S(BASE_WIN_H);
        // WS_CLIPCHILDREN : empeche la fenetre parent de peindre dans les zones des
        // boutons enfants (Quitter, Passer, Recommencer, etc.). Sans ce flag, OnPaint
        // peut briévement peindre par-dessus les boutons lors d'un repaint frequent
        // (ex. frappe rapide en exo), causant un flicker visible.
        uint dwStyle = Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU | Win32.WS_CLIPCHILDREN;
        uint dwExStyle = Win32.WS_EX_TOPMOST;

        // Adapter aux petits écrans
        Win32.GetCursorPos(out var cursorPt);
        var hMonitor = Win32.MonitorFromPoint(cursorPt, 0x00000001);
        var monInfo = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(hMonitor, ref monInfo);
        int screenW = monInfo.rcWork.right - monInfo.rcWork.left;
        int screenH = monInfo.rcWork.bottom - monInfo.rcWork.top;
        int maxW = (int)(screenW * 0.9f);
        int maxH = (int)(screenH * 0.9f);
        if (winW > maxW || winH > maxH)
        {
            float ratio = 1100f / 600f;
            if ((float)maxW / maxH > ratio)
            { winH = maxH; winW = (int)(winH * ratio); }
            else
            { winW = maxW; winH = (int)(winW / ratio); }
        }

        var adjustRect = new Win32.RECT { left = 0, top = 0, right = winW, bottom = winH };
        Win32.AdjustWindowRectEx(ref adjustRect, dwStyle, false, dwExStyle);
        int windowW = adjustRect.right - adjustRect.left;
        int windowH = adjustRect.bottom - adjustRect.top;

        int screenX = monInfo.rcWork.left;
        int screenY = monInfo.rcWork.top;

        _hWnd = Win32.CreateWindowExW(dwExStyle, WND_CLASS_NAME, "AZERTY Global \u2014 Exercices",
            dwStyle,
            screenX + (screenW - windowW) / 2, screenY + (screenH - windowH) / 2,
            windowW, windowH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        Win32.EnableDarkTitleBar(_hWnd);
    }

    private void ResizeWindow()
    {
        int winW = S(BASE_WIN_W);
        int winH = S(BASE_WIN_H);
        uint dwStyle = Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU;
        uint dwExStyle = Win32.WS_EX_TOPMOST;
        var adjustRect = new Win32.RECT { left = 0, top = 0, right = winW, bottom = winH };
        Win32.AdjustWindowRectEx(ref adjustRect, dwStyle, false, dwExStyle);
        int windowW = adjustRect.right - adjustRect.left;
        int windowH = adjustRect.bottom - adjustRect.top;
        Win32.GetWindowRect(_hWnd, out var currentRect);
        int cx = (currentRect.left + currentRect.right) / 2;
        int cy = (currentRect.top + currentRect.bottom) / 2;
        Win32.MoveWindow(_hWnd, cx - windowW / 2, cy - windowH / 2, windowW, windowH, true);
    }

    private void CreateControls()
    {
        var hInstance = Win32.GetModuleHandleW(null);
        int margin = S(BASE_MARGIN);

        // Bouton Quit en owner-draw pour gérer le hover (fond rouge clair).
        _hWndBtnQuit = Win32.CreateWindowExW(0, "BUTTON", "Quitter les exercices",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP | Win32.BS_OWNERDRAW,
            margin, 0, S(180), S(30),
            _hWnd, (IntPtr)IDC_BTN_QUIT, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnQuit, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
        _quitSubclassProc = QuitButtonSubclassProc;
        Win32.SetWindowSubclass(_hWndBtnQuit, _quitSubclassProc, (UIntPtr)1, IntPtr.Zero);

        // Bouton Skip en owner-draw avec hover rouge clair (même style que Quit).
        _hWndBtnSkip = Win32.CreateWindowExW(0, "BUTTON", "Passer cet exercice",
            Win32.WS_CHILD | Win32.WS_TABSTOP | Win32.BS_OWNERDRAW,
            0, 0, S(160), S(30),
            _hWnd, (IntPtr)IDC_BTN_SKIP, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnSkip, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
        _skipSubclassProc = SkipButtonSubclassProc;
        Win32.SetWindowSubclass(_hWndBtnSkip, _skipSubclassProc, (UIntPtr)2, IntPtr.Zero);

        _hWndBtnFinish = Win32.CreateWindowExW(0, "BUTTON", "Terminer",
            Win32.WS_CHILD | Win32.WS_TABSTOP,
            0, 0, S(140), S(30),
            _hWnd, (IntPtr)IDC_BTN_FINISH, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnFinish, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);

        // Page de choix fin d'exercice : Reessayer + Suivant (cote a cote, centres sous le titre)
        _hWndBtnRetry = Win32.CreateWindowExW(0, "BUTTON", "Recommencer l'exercice",
            Win32.WS_CHILD | Win32.WS_TABSTOP,
            0, 0, S(200), S(36),
            _hWnd, (IntPtr)IDC_BTN_RETRY, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnRetry, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);

        _hWndBtnContinue = Win32.CreateWindowExW(0, "BUTTON", "Exercice suivant",
            Win32.WS_CHILD | Win32.WS_TABSTOP,
            0, 0, S(180), S(36),
            _hWnd, (IntPtr)IDC_BTN_CONTINUE, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnContinue, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);

        RepositionControls();
    }

    private void RepositionControls()
    {
        Win32.GetClientRect(_hWnd, out var cr);
        int cw = cr.right;
        int ch = cr.bottom;
        int margin = S(BASE_MARGIN);

        // Quitter et Skip restent en haut à droite. Terminer (page finale Bravo !) est
        // descendu juste au-dessus du clavier, centré horizontalement, pour rester aligne
        // avec la position du bouton « Suivant » de la page de choix precedente.
        int btnW = S(180);
        int btnH = S(30);
        int btnX = cw - margin - btnW;
        Win32.MoveWindow(_hWndBtnQuit, btnX, S(10), btnW, btnH, true);
        Win32.MoveWindow(_hWndBtnSkip, btnX, S(10) + btnH + S(6), btnW, btnH, true);

        // Page de choix fin d'exercice : Reessayer + Suivant centres horizontalement.
        // Position verticale :
        //   1er succes (avec « ✓ Bravo ! ») → block Bravo + boutons centre verticalement
        //                                      dans la zone superieure (boutons descendus
        //                                      pour laisser place au titre au-dessus)
        //   n-ieme succes (sans Bravo)     → boutons seuls centres verticalement
        int retryW = S(200);
        int continueW = S(180);
        int choiceBtnH = S(36);
        int choiceGap = S(16);
        int totalW = retryW + choiceGap + continueW;
        int choiceX = (cw - totalW) / 2;
        int kbTop = S(BASE_HEADER_H + BASE_INSTRUCTION_H + BASE_TARGET_H);

        int choiceY;
        if (_awaitingChoice && _currentStepSuccessCount <= 1)
        {
            // Block Bravo + gap + boutons, centre verticalement
            int titleH = S(40);
            int gapTitleButtons = S(14);
            int blockH = titleH + gapTitleButtons + choiceBtnH;
            int blockTop = (kbTop - blockH) / 2;
            choiceY = blockTop + titleH + gapTitleButtons;
        }
        else
        {
            choiceY = (kbTop - choiceBtnH) / 2;
        }
        Win32.MoveWindow(_hWndBtnRetry, choiceX, choiceY, retryW, choiceBtnH, true);
        Win32.MoveWindow(_hWndBtnContinue, choiceX + retryW + choiceGap, choiceY, continueW, choiceBtnH, true);

        // Bouton Terminer (page finale « Bravo ! ») : aligné à droite, juste au-dessus du
        // clavier — laisse de la place au sous-titre « Vous maîtrisez... » qui est centré.
        int finishW = S(140);
        int finishH = S(36);
        int finishY = kbTop - finishH - S(16);
        int finishX = cw - margin - finishW;
        Win32.MoveWindow(_hWndBtnFinish, finishX, finishY, finishW, finishH, true);
    }

    private void UpdateControlVisibility()
    {
        if (_completed)
        {
            // Page « Bravo ! » finale (apres les 6 exercices) : seul Terminer
            Win32.ShowWindow(_hWndBtnQuit, 0);
            Win32.ShowWindow(_hWndBtnSkip, 0);
            Win32.ShowWindow(_hWndBtnFinish, 1);
            Win32.ShowWindow(_hWndBtnRetry, 0);
            Win32.ShowWindow(_hWndBtnContinue, 0);
        }
        else if (_awaitingChoice)
        {
            // Page de choix fin d'exercice : Reessayer + Suivant. On masque tout le reste.
            Win32.ShowWindow(_hWndBtnQuit, 0);
            Win32.ShowWindow(_hWndBtnSkip, 0);
            Win32.ShowWindow(_hWndBtnFinish, 0);
            Win32.ShowWindow(_hWndBtnRetry, 1);
            Win32.ShowWindow(_hWndBtnContinue, 1);
            // « Suivant » → « Terminer » au dernier exercice (on basculera ensuite sur la page Bravo finale)
            bool isLast = _currentStep >= _steps.Length - 1;
            Win32.SetWindowTextW(_hWndBtnContinue, isLast ? "Terminer les exercices" : "Exercice suivant");
        }
        else
        {
            Win32.ShowWindow(_hWndBtnQuit, 1);
            bool skippable = _currentStep < _steps.Length && _steps[_currentStep].Skippable;
            Win32.ShowWindow(_hWndBtnSkip, skippable ? 1 : 0);
            Win32.ShowWindow(_hWndBtnFinish, 0);
            Win32.ShowWindow(_hWndBtnRetry, 0);
            Win32.ShowWindow(_hWndBtnContinue, 0);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Show / Close
    // ═══════════════════════════════════════════════════════════════
    public void Show()
    {
        ConfigManager.LogCrashTraceDebug("LM.Show: enter");
        // Tous les exos demarrent avec Caps Lock OFF — l'utilisateur l'active explicitement
        // si l'exo le requiert (ex1, ex2). Sans ca, un Verr.Maj. herite du contexte
        // exterieur (avant le clic Essayer maintenant) brise la pedagogie de l'exo 1
        // (« Activez Verr. Maj. puis tapez sur la lettre é »).
        if (!_inputPaused)
            _mapper.RequestCapsLockOff();
        Win32.EnableWindow(_hWndOnboarding, false);
        Win32.ShowWindow(_hWnd, 1);
        ConfigManager.LogCrashTraceDebug("LM.Show: ShowWindow done");
        // Le synthetic VK_CAPITAL inject par RequestCapsLockOff() est traite par Windows
        // de maniere asynchrone : entre ShowWindow et le 1er WM_PAINT, _mapper._capsLockState
        // peut etre desaligne avec Windows reel. Sans cette resync timeree, la touche
        // Verr. Maj. peut etre rendue comme « activee » jusqu'a la 1ere frappe utilisateur.
        // 50ms suffisent largement pour que Windows ait traite le toggle.
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_CAPS_RESYNC, 50, IntPtr.Zero);
        TakeFocus();
        // Windows bloque souvent SetForegroundWindow au 1er appel (anti-vol de focus).
        // On retry plusieurs fois via timer jusqu'à ce que GetForegroundWindow() == _hWnd.
        _refocusAttempts = 0;
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_REFOCUS, 80, IntPtr.Zero);
        ConfigManager.LogCrashTraceDebug("LM.Show: focus done — exit");
    }

    public void SetInputPaused(bool paused)
    {
        if (_inputPaused == paused) return;
        _inputPaused = paused;

        if (paused)
        {
            _pressedScancode = 0;
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_REFOCUS);
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_FOCUS_LOST_CONFIRM);
            if (_hWnd != IntPtr.Zero)
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        }
    }

    private static bool IsPausedInputMessage(uint msg)
    {
        return msg is Win32.WM_KEYDOWN or Win32.WM_KEYUP or Win32.WM_SYSKEYDOWN or Win32.WM_SYSKEYUP
            or Win32.WM_CHAR or Win32.WM_SYSCHAR or Win32.WM_SYSDEADCHAR
            or Win32.WM_COMMAND or Win32.WM_PASTE or Win32.WM_CUT or Win32.WM_CLEAR or Win32.WM_UNDO;
    }

    private int _refocusAttempts;

    /// <summary>
    /// Force le focus clavier sur la fenêtre LearningModule. Utilisé au lancement
    /// (Show) et après chaque transition entre exercices (AdvanceToNextStep) pour
    /// que les frappes utilisateur arrivent sans qu'il doive recliquer la fenêtre.
    /// </summary>
    private void TakeFocus()
    {
        var foreWnd = Win32.GetForegroundWindow();
        uint foreThread = Win32.GetWindowThreadProcessId(foreWnd, IntPtr.Zero);
        uint curThread = Win32.GetCurrentThreadId();
        if (foreThread != curThread)
            Win32.AttachThreadInput(curThread, foreThread, true);
        Win32.SetForegroundWindow(_hWnd);
        Win32.SetFocus(_hWnd);
        if (foreThread != curThread)
            Win32.AttachThreadInput(curThread, foreThread, false);
    }

    /// <summary>
    /// Ferme le LearningModule : unsubscribe les events, masque la fenetre, reactive
    /// l'OnboardingWindow, declenche OnClosed (qui Dispose). Internal pour permettre
    /// a OnboardingWindow.ResetState() de fermer une session en cours.
    /// </summary>
    internal void Close()
    {
        _mapper.StateChanged -= OnStateChanged;
        _hook.RawKeyDown -= OnRawKeyDown;
        Win32.ShowWindow(_hWnd, 0);
        Win32.EnableWindow(_hWndOnboarding, true);
        Win32.SetForegroundWindow(_hWndOnboarding);
        OnClosed?.Invoke(_completed);
    }

    // ═══════════════════════════════════════════════════════════════
    // Événements KeyMapper / KeyboardHook
    // ═══════════════════════════════════════════════════════════════
    private void OnStateChanged()
    {
        // Mettre à jour le highlight (l'état des modificateurs a changé)
        UpdateHighlight();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);

        // Le rafraichissement du tooltip etait fait ici lors d'un changement d'etat
        // (touche morte, Caps Lock). Probleme : entre deux exercices, RequestCapsLockOff
        // fire StateChanged → si la souris est immobile sur une touche, le tooltip
        // re-popait pour la meme touche, ce qui surprenait l'utilisateur. Le tooltip
        // est desormais rafraichi uniquement sur mouvement souris (OnMouseMove). Le seul
        // cas qui n'est plus couvert : l'utilisateur survole une touche immobile pendant
        // une transition de touche morte → texte legerement obsolete jusqu'au prochain
        // mouvement de souris. Acceptable.
    }

    private void OnRawKeyDown(uint scancode)
    {
        _pressedScancode = scancode;
        CaptureExpectedTextForPhysicalKey(scancode);
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_KEYPRESS, KEYPRESS_DURATION_MS, IntPtr.Zero);
    }

    private void CaptureExpectedTextForPhysicalKey(uint scancode)
    {
        ClearPendingPhysicalText();

        if (!_hasFocus || _completed || _inTransition || _awaitingChoice) return;
        if (!_layout.Keys.TryGetValue(scancode, out var keyDef)) return;

        string? output = keyDef.GetOutput(_mapper.ShiftDown, _mapper.AltGrDown, _mapper.CapsLockActive);
        if (string.IsNullOrEmpty(output)) return;

        string? expectedText = null;
        if (output.StartsWith("dk_", StringComparison.Ordinal))
        {
            // Une activation de touche morte seule ne produit pas de WM_CHAR.
            if (_mapper.ActiveDeadKey != null
                && _layout.DeadKeys.TryGetValue(_mapper.ActiveDeadKey, out var activeDk))
            {
                var isolated = activeDk.GetIsolated();
                if (isolated != null)
                {
                    var newDk = _layout.DeadKeys.GetValueOrDefault(output);
                    expectedText = newDk?.Apply(isolated) ?? isolated;
                }
            }
        }
        else if (_mapper.ActiveDeadKey != null
            && _layout.DeadKeys.TryGetValue(_mapper.ActiveDeadKey, out var dk))
        {
            var transformed = dk.Apply(output);
            if (transformed != null)
                expectedText = transformed;
            else if (dk.GetIsolated() is { } isolated)
                expectedText = isolated + output;
            else
                expectedText = output;
        }
        else
        {
            expectedText = output;
        }

        if (string.IsNullOrEmpty(expectedText)) return;

        _pendingPhysicalText = expectedText;
        _pendingPhysicalTextTick = Environment.TickCount64;
    }

    private void ClearPendingPhysicalText()
    {
        _pendingPhysicalText = null;
        _pendingPhysicalTextIndex = 0;
        _pendingPhysicalTextTick = 0;
    }

    private void OnMouseMove(IntPtr lParam)
    {
        if (!_trackingMouse)
        {
            var tme = new Win32.TRACKMOUSEEVENT
            {
                cbSize = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(),
                dwFlags = Win32.TME_LEAVE,
                hwndTrack = _hWnd,
                dwHoverTime = 0
            };
            Win32.TrackMouseEvent(ref tme);
            _trackingMouse = true;
        }

        int mx = (short)(lParam.ToInt64() & 0xFFFF);
        int my = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        int hitIndex = -1;
        for (int i = 0; i < _keyHitAreas.Count; i++)
        {
            var rc = _keyHitAreas[i].Rect;
            if (mx >= rc.left && mx < rc.right && my >= rc.top && my < rc.bottom)
            {
                hitIndex = i;
                break;
            }
        }

        if (hitIndex != _hoveredKeyIndex)
        {
            _hoveredKeyIndex = hitIndex;
            if (hitIndex >= 0)
            {
                var hit = _keyHitAreas[hitIndex];
                string text = BuildTooltipText(hit.Scancode, hit.ContextLabel);
                if (string.IsNullOrEmpty(text))
                    SetTooltip("", new Win32.RECT()); // pas de correspondance avec la dk active → pas de tooltip
                else
                    SetTooltip(text, hit.Rect);
            }
            else
            {
                SetTooltip("", new Win32.RECT());
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // WndProc
    // ═══════════════════════════════════════════════════════════════
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (_inputPaused && IsPausedInputMessage(msg))
                return IntPtr.Zero;

            switch (msg)
            {
                case Win32.WM_PAINT:
                    OnPaint(hWnd);
                    return IntPtr.Zero;

                case Win32.WM_ERASEBKGND:
                    return (IntPtr)1;

                case Win32.WM_MOUSEMOVE:
                    OnMouseMove(lParam);
                    return IntPtr.Zero;

                case Win32.WM_MOUSELEAVE:
                    _trackingMouse = false;
                    if (_hoveredKeyIndex != -1)
                    {
                        _hoveredKeyIndex = -1;
                        SetTooltip("", new Win32.RECT());
                    }
                    return IntPtr.Zero;

                case Win32.WM_KILLFOCUS:
                {
                    // wParam contient le HWND qui prend le focus. Si c'est un de nos child
                    // windows (boutons en-tete, tooltip), on n'affiche PAS l'overlay
                    // « Cliquez pour reprendre » : le focus reste de fait dans notre arborescence
                    // et les boutons d'avancement (Skip/Retry/Continue) reprennent le focus
                    // tout seuls via TakeFocus() apres leur action.
                    IntPtr nextFocus = wParam;
                    bool focusGoingToOurChild = nextFocus != IntPtr.Zero
                        && (nextFocus == _hWndBtnQuit
                         || nextFocus == _hWndBtnSkip
                         || nextFocus == _hWndBtnFinish
                         || nextFocus == _hWndBtnRetry
                         || nextFocus == _hWndBtnContinue
                         || nextFocus == _hTooltip);
                    if (!focusGoingToOurChild)
                    {
                        _hasFocus = false;
                        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_REFOCUS);
                        // Debounce : ne pas afficher l'overlay immediatement. Attendre 250ms
                        // pour ignorer les "blinks" causes par MoveWindow/ShowWindow/repaint.
                        // Si le focus revient pendant ce delai, on annule. Sinon on confirme
                        // la perte et on affiche l'overlay.
                        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_FOCUS_LOST_CONFIRM, FOCUS_LOSS_DEBOUNCE_MS, IntPtr.Zero);
                    }
                    return IntPtr.Zero;
                }

                case Win32.WM_SETFOCUS:
                    _hasFocus = true;
                    _focusLostConfirmed = false;
                    Win32.KillTimer(_hWnd, (UIntPtr)TIMER_FOCUS_LOST_CONFIRM);
                    // Resync des modificateurs et de Caps Lock au retour de focus — corrige
                    // les desynchros heritees d'un contexte exterieur (jeu en arriere-plan,
                    // touches modifs tenues lors de l'install du hook). Cf. bug 2026-05-03 :
                    // virgule bloquee dans l'exo 2 quand l'app demarrait pendant un jeu.
                    _mapper?.SyncState();
                    Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                    return IntPtr.Zero;

                case Win32.WM_DRAWITEM:
                {
                    var dis = Marshal.PtrToStructure<Win32.DRAWITEMSTRUCT>(lParam);
                    if (dis.CtlID == IDC_BTN_QUIT)
                    {
                        DrawHoverButton(in dis, _quitHovered, "Quitter les exercices");
                        return (IntPtr)1;
                    }
                    if (dis.CtlID == IDC_BTN_SKIP)
                    {
                        DrawHoverButton(in dis, _skipHovered, "Passer cet exercice");
                        return (IntPtr)1;
                    }
                    break;
                }

                case 0x0102: // WM_CHAR
                    OnChar((char)wParam.ToInt32());
                    return IntPtr.Zero;

                case Win32.WM_SYSCHAR:
                    // Sous un layout sous-jacent sans AltGr natif (ex. QWERTY US), Right Alt
                    // reste un Alt systeme pour Windows. Les caracteres injectes par le hook
                    // arrivent alors parfois en WM_SYSCHAR au lieu de WM_CHAR : les traiter
                    // comme une saisie normale evite le bip DefWindowProc et valide l'exercice.
                    OnChar((char)wParam.ToInt32());
                    return IntPtr.Zero;

                case Win32.WM_SYSDEADCHAR:
                    return IntPtr.Zero;

                case Win32.WM_SYSKEYDOWN:
                {
                    int vk = wParam.ToInt32();
                    if (vk == 0x73) // VK_F4, preserve Alt+F4.
                    {
                        Close();
                        return IntPtr.Zero;
                    }
                    if (vk == 0x08) // VK_BACK
                        OnBackspace();
                    else if (vk == 0x1B) // VK_ESCAPE
                        Close();
                    return IntPtr.Zero;
                }

                case Win32.WM_SYSKEYUP:
                    return IntPtr.Zero;

                case Win32.WM_KEYDOWN:
                {
                    int vk = wParam.ToInt32();
                    if (_completed)
                    {
                        // Page finale « Bravo ! » : flèches droite/bas ou Esc → Terminer (Close).
                        if (vk == 0x27 || vk == 0x28 || vk == 0x1B) // VK_RIGHT, VK_DOWN, VK_ESCAPE
                            Close();
                        return IntPtr.Zero;
                    }
                    if (_awaitingChoice)
                    {
                        // Page de choix fin d'exercice : flèches gauche/haut → Recommencer,
                        // flèches droite/bas → Suivant (ou Terminer au dernier exercice).
                        if (vk == 0x25 || vk == 0x26) // VK_LEFT, VK_UP
                            RetryCurrentStep();
                        else if (vk == 0x27 || vk == 0x28) // VK_RIGHT, VK_DOWN
                            ContinueAfterChoice();
                        else if (vk == 0x1B) // VK_ESCAPE
                            Close();
                        return IntPtr.Zero;
                    }
                    if (vk == 0x08) // VK_BACK
                        OnBackspace();
                    else if (vk == 0x1B) // VK_ESCAPE
                        Close();
                    return IntPtr.Zero;
                }

                case Win32.WM_COMMAND:
                    switch (wParam.ToInt32() & 0xFFFF)
                    {
                        case IDC_BTN_QUIT: Close(); break;
                        case IDC_BTN_SKIP: SkipStep(); break;
                        case IDC_BTN_FINISH: Close(); break;
                        case IDC_BTN_RETRY: RetryCurrentStep(); break;
                        case IDC_BTN_CONTINUE: ContinueAfterChoice(); break;
                    }
                    return IntPtr.Zero;

                case Win32.WM_TIMER:
                {
                    var timerId = (uint)wParam.ToInt64();
                    if (timerId == TIMER_KEYPRESS)
                    {
                        Win32.KillTimer(hWnd, (UIntPtr)TIMER_KEYPRESS);
                        _pressedScancode = 0;
                        Win32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    }
                    else if (timerId == TIMER_TRANSITION)
                    {
                        Win32.KillTimer(hWnd, (UIntPtr)TIMER_TRANSITION);
                        _inTransition = false;
                        AdvanceToNextStep();
                    }
                    else if (timerId == TIMER_REFOCUS)
                    {
                        Win32.KillTimer(hWnd, (UIntPtr)TIMER_REFOCUS);
                        _refocusAttempts++;
                        var fg = Win32.GetForegroundWindow();
                        if (fg != _hWnd && _refocusAttempts < REFOCUS_MAX_ATTEMPTS)
                        {
                            TakeFocus();
                            Win32.SetTimer(_hWnd, (UIntPtr)TIMER_REFOCUS, 80, IntPtr.Zero);
                        }
                    }
                    else if (timerId == TIMER_CAPS_RESYNC)
                    {
                        // Resync Caps Lock apres que Windows ait traite le synthetic VK_CAPITAL
                        // inject par Show(). Sans ca, le 1er paint peut afficher Verr. Maj.
                        // comme activee alors que Windows est OFF.
                        Win32.KillTimer(hWnd, (UIntPtr)TIMER_CAPS_RESYNC);
                        _mapper.SyncState();
                        UpdateHighlight();
                        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                    }
                    else if (timerId == TIMER_FOCUS_LOST_CONFIRM)
                    {
                        Win32.KillTimer(hWnd, (UIntPtr)TIMER_FOCUS_LOST_CONFIRM);
                        // Si le focus n'est toujours pas revenu apres le debounce, on confirme
                        // la perte et on affiche l'overlay.
                        if (!_hasFocus)
                        {
                            _focusLostConfirmed = true;
                            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
                }

                case Win32.WM_DPICHANGED:
                {
                    int newDpi = (wParam.ToInt32() >> 16) & 0xFFFF;
                    if (newDpi > 0) _dpiScale = newDpi / 96f;
                    RecreateFonts();
                    var suggested = Marshal.PtrToStructure<Win32.RECT>(lParam);
                    Win32.MoveWindow(_hWnd, suggested.left, suggested.top,
                        suggested.right - suggested.left, suggested.bottom - suggested.top, true);
                    RepositionControls();
                    Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                    return IntPtr.Zero;
                }

                case Win32.WM_CLOSE:
                    Close();
                    return IntPtr.Zero;

                case Win32.WM_DESTROY:
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            ConfigManager.Log("LearningModule WndProc", ex);
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ═══════════════════════════════════════════════════════════════
    // Saisie
    // ═══════════════════════════════════════════════════════════════
    private void OnChar(char c)
    {
        if (_completed || _inTransition || _awaitingChoice) return;
        if (_currentStep >= _steps.Length) return;

        var target = _steps[_currentStep].Target;
        if (_cursorPosition >= target.Length) return;

        char typed = ResolveTypedCharacter(c);
        if (typed == target[_cursorPosition])
        {
            _cursorPosition++;
            _currentCharError = false;

            if (_cursorPosition >= target.Length)
            {
                // Exercice termine : afficher la page de choix Reessayer / Suivant
                // (au lieu d'enchainer automatiquement comme avant). L'utilisateur decide
                // s'il refait l'exercice ou passe au suivant.
                _awaitingChoice = true;
                _currentStepSuccessCount++;
                // Persister la progression : exercice (_currentStep + 1) valide. Setter monotone,
                // donc safe meme en cas de Recommencer puis nouveau succes (no-op si deja persiste).
                // En replayMode (lancement depuis menu tray « Exercices »), on ne persiste pas :
                // la progression sauvegardee reflete uniquement le 1er passage onboarding.
                if (!_replayMode)
                    ConfigManager.SetLearningMaxStepCompleted(_currentStep + 1);
                _mapper.RequestCapsLockOff(); // reinciter a appuyer sur Verr.Maj. au prochain exercice
                ClearHighlight();
                UpdateControlVisibility();
                RepositionControls();
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
            }
            else
            {
                UpdateHighlight();
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
            }
        }
        else
        {
            _currentCharError = true;
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        }
    }

    private char ResolveTypedCharacter(char received)
    {
        if (string.IsNullOrEmpty(_pendingPhysicalText))
            return received;

        if (Environment.TickCount64 - _pendingPhysicalTextTick > PENDING_PHYSICAL_TEXT_TIMEOUT_MS)
        {
            ClearPendingPhysicalText();
            return received;
        }

        if (_pendingPhysicalTextIndex >= _pendingPhysicalText.Length)
        {
            ClearPendingPhysicalText();
            return received;
        }

        char expected = _pendingPhysicalText[_pendingPhysicalTextIndex++];
        if (_pendingPhysicalTextIndex >= _pendingPhysicalText.Length)
        {
            ClearPendingPhysicalText();
        }
        return expected;
    }

    private void OnBackspace()
    {
        // Backspace volontairement desactive pendant les exercices : le reflexe d'effacer
        // une frappe correcte cassait la progression. L'erreur courante (_currentCharError)
        // est de toute facon effacee automatiquement au prochain bon caractere tape.
        return;
    }

    private void AdvanceToNextStep()
    {
        _currentStep++;
        _cursorPosition = 0;
        _currentCharError = false;
        _awaitingChoice = false;
        _currentStepSuccessCount = 0; // reset pour le nouvel exercice (1er succes => « Bravo ! » s'affiche)
        ClearPendingPhysicalText();

        if (_currentStep >= _steps.Length)
        {
            _completed = true;
            ClearHighlight();
        }
        else
        {
            UpdateHighlight();
        }

        UpdateControlVisibility();
        RepositionControls();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
        // Le focus est souvent perdu pendant la transition (boutons « Quitter / Passer »
        // recevant la souris, etc.) — on le reprend pour que l'utilisateur n'ait pas
        // besoin de recliquer la fenêtre avant de taper l'exercice suivant.
        TakeFocus();
    }

    private void SkipStep()
    {
        if (_currentStep < _steps.Length && _steps[_currentStep].Skippable)
            AdvanceToNextStep();
    }

    /// <summary>
    /// Page de choix fin d'exercice → bouton « Recommencer l'exercice ». Reset le cursor
    /// au debut, ramene les controles standards (Quitter/Passer) et le highlight du 1er
    /// caractere a taper.
    /// </summary>
    private void RetryCurrentStep()
    {
        _awaitingChoice = false;
        _cursorPosition = 0;
        _currentCharError = false;
        ClearPendingPhysicalText();
        _mapper.RequestCapsLockOff(); // forcer Verr.Maj. off au reset (re-incite a l'activation si KeepCapsHighlight)
        UpdateHighlight();
        UpdateControlVisibility();
        RepositionControls();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
        TakeFocus();
    }

    /// <summary>
    /// Page de choix fin d'exercice → bouton « Exercice suivant » / « Terminer les exercices ».
    /// Avance au prochain exercice ; au dernier, AdvanceToNextStep mettra _completed = true et
    /// affichera la page « Bravo ! » finale.
    /// </summary>
    private void ContinueAfterChoice()
    {
        AdvanceToNextStep();
    }

    // ═══════════════════════════════════════════════════════════════
    // Highlight du prochain caractère
    // ═══════════════════════════════════════════════════════════════
    private void ClearHighlight()
    {
        _highlightedScancodes.Clear();
        _highlightedLabels.Clear();
        _highlightedContextIds.Clear();
        _highlightType = "";
        _pendingStep2 = null;
    }

    private void AddContextHighlight(string contextId)
    {
        _highlightedContextIds.Add(contextId);
    }

    private void AddShiftHighlight()
    {
        AddContextHighlight(VirtualKeyboard.ContextShiftLeft);
    }

    private CharacterSearch.MethodData? CreateDeadKeyMethod(string deadKey, string key, string layer)
    {
        if (!_dkActivations.TryGetValue(deadKey, out var dkAct)) return null;
        return new CharacterSearch.MethodData
        {
            Type = "deadkey",
            DeadKey = deadKey,
            Key = key,
            Layer = layer,
            DkActivationKey = dkAct.key,
            DkActivationLayer = dkAct.layer,
        };
    }

    private CharacterSearch.MethodData? GetLanguageExerciseMethod(string character)
    {
        return character switch
        {
            "\u00e3" => CreateDeadKeyMethod("dk_tilde", "KeyQ", "Base"),  // ã
            "\u00c3" => CreateDeadKeyMethod("dk_tilde", "KeyQ", "Shift"), // Ã
            "\u00f8" => CreateDeadKeyMethod("dk_stroke", "KeyO", "Base"), // ø
            "\u00d8" => CreateDeadKeyMethod("dk_stroke", "KeyO", "Shift"), // Ø
            "\u0142" => CreateDeadKeyMethod("dk_stroke", "KeyL", "Base"), // ł
            "\u0141" => CreateDeadKeyMethod("dk_stroke", "KeyL", "Shift"), // Ł
            _ => null,
        };
    }

    private void UpdateHighlight()
    {
        ClearHighlight();
        if (_completed || _inTransition) return;
        if (_currentStep >= _steps.Length) return;

        var target = _steps[_currentStep].Target;
        if (_cursorPosition >= target.Length) return;

        var nextChar = target[_cursorPosition].ToString();
        if (!_charMethods.TryGetValue(nextChar, out var method)) return;

        // Si l'exercice demande de garder Verr.Maj activée et qu'une variante Caps existe
        // pour ce caractère, l'utiliser (= ne pas demander Maj redondant à l'utilisateur).
        if (_steps[_currentStep].KeepCapsHighlight
            && _charMethodsCaps.TryGetValue(nextChar, out var capsAlt))
        {
            method = capsAlt;
        }

        if (_currentStep == _steps.Length - 1
            && GetLanguageExerciseMethod(nextChar) is { } languageMethod)
        {
            method = languageMethod;
        }

        // Cas spécial : layer "Caps" — gestion dynamique
        if (method.Layer.StartsWith("Caps") && method.Type == "direct")
        {
            // Verr.Maj reste TOUJOURS highlighted tant qu'elle est requise pour ce caractere
            // (contour vert si pas activee, fond vert plein si activee).
            _highlightType = "direct";
            _highlightedLabels.Add("Verr. Maj.");
            if (_mapper.CapsLockActive)
            {
                if (VirtualKeyboard.KeyCodeToScancode.TryGetValue(method.Key, out var sc))
                    _highlightedScancodes.Add(sc);
                if (method.Layer == "Caps+Shift" || method.Layer == "CapsShift")
                    AddShiftHighlight();
            }
            return;
        }

        if (method.Type == "direct" || method.Type == "deadkey_activation")
        {
            _highlightType = "direct";
            AddKeyHighlight(method.Key, method.Layer);
        }
        else if (method.Type == "deadkey" && !string.IsNullOrEmpty(method.DkActivationKey))
        {
            // Si la touche morte est déjà active, montrer seulement l'étape 2
            if (_mapper.ActiveDeadKey != null)
            {
                _highlightType = "step2";
                AddKeyHighlight(method.Key, method.Layer);
            }
            else
            {
                // Montrer seulement l'étape 1. L'étape 2 apparaîtra après activation de la touche morte.
                _highlightType = "step1";
                AddKeyHighlight(method.DkActivationKey, method.DkActivationLayer);
            }
        }

        // KeepCapsHighlight : exercices qui demandent de garder Verr.Maj activée pendant
        // toute la durée de l'exercice (1 et 2). On force Verr.Maj dans le highlight même
        // pour les caractères dont la méthode ne nécessite pas Caps (espace, virgule, !).
        if (_steps[_currentStep].KeepCapsHighlight)
        {
            _highlightedLabels.Add("Verr. Maj.");
            if (string.IsNullOrEmpty(_highlightType)) _highlightType = "direct";
        }
    }

    private void AddKeyHighlight(string keyCode, string layer)
    {
        bool isLetter = false;
        if (VirtualKeyboard.KeyCodeToScancode.TryGetValue(keyCode, out var scancode))
        {
            _highlightedScancodes.Add(scancode);
            isLetter = LetterKeyScancodes.Contains(scancode);
        }

        bool needsShift = layer == "Shift" || layer == "Shift+AltGr" || layer == "AltGr+Shift";
        bool needsAltGr = layer == "AltGr" || layer == "Shift+AltGr" || layer == "AltGr+Shift";

        // Smart Caps Lock : si Verr.Maj est d\u00e9j\u00e0 active ET la touche cible est une lettre,
        // Shift devient redondant (la majuscule sortira via Caps). On highlight Verr.Maj
        // au lieu de Maj pour rester coh\u00e9rent avec ce que l'utilisateur doit faire.
        if (needsShift)
        {
            if (_mapper.CapsLockActive && isLetter)
                _highlightedLabels.Add("Verr. Maj.");
            else
                AddShiftHighlight();
        }
        if (needsAltGr)
            _highlightedLabels.Add("AltGr");
    }

    private bool IsKeyHighlighted(in VirtualKeyboard.VisualKey vk)
    {
        if (_highlightedScancodes.Count == 0 && _highlightedLabels.Count == 0 && _highlightedContextIds.Count == 0) return false;
        if (vk.Scancode != 0 && _highlightedScancodes.Contains(vk.Scancode)) return true;
        if (vk.IsContextual && vk.ContextId != null && _highlightedContextIds.Contains(vk.ContextId)) return true;
        if (vk.IsContextual && _highlightedLabels.Contains(vk.Label)) return true;
        return false;
    }

    private bool IsStep2Key(in VirtualKeyboard.VisualKey vk)
    {
        if (_pendingStep2 == null || _highlightType != "step1") return false;
        bool targetIsLetter = false;
        if (VirtualKeyboard.KeyCodeToScancode.TryGetValue(_pendingStep2.Key, out var sc))
        {
            if (vk.Scancode != 0 && vk.Scancode == sc) return true;
            targetIsLetter = LetterKeyScancodes.Contains(sc);
        }
        // Modificateurs de l'etape 2 — Smart Caps Lock : si Caps est active ET la cible est
        // une lettre, le « Shift » du JSON devient redondant. On highlight Verr.Maj a la place.
        var layer2 = _pendingStep2.Layer;
        bool needsShift = layer2 == "Shift" || layer2 == "Shift+AltGr" || layer2 == "AltGr+Shift";
        bool needsAltGr = layer2 == "AltGr" || layer2 == "Shift+AltGr" || layer2 == "AltGr+Shift";
        bool capsCoversShift = needsShift && _mapper.CapsLockActive && targetIsLetter;
        if (vk.IsContextual)
        {
            if (needsShift && !capsCoversShift && vk.ContextId == VirtualKeyboard.ContextShiftLeft) return true;
            if (capsCoversShift && vk.Label == "Verr. Maj.") return true;
            if (needsAltGr && vk.Label == "AltGr") return true;
        }
        return false;
    }

    private (uint border, uint bg) GetHighlightColors(bool isStep2, in VirtualKeyboard.VisualKey vk)
    {
        if (!isStep2
            && vk.Label == "Verr. Maj."
            && _currentStep < _steps.Length
            && _steps[_currentStep].KeepCapsHighlight)
        {
            return (CLR_HL_DIRECT, CLR_HL_DIRECT_BG);
        }

        if (isStep2) return (CLR_HL_STEP2, CLR_HL_STEP2_BG);
        return _highlightType switch
        {
            "direct" => (CLR_HL_DIRECT, CLR_HL_DIRECT_BG),
            "step1" => (CLR_HL_STEP1, CLR_HL_STEP1_BG),
            "step2" => (CLR_HL_STEP2, CLR_HL_STEP2_BG),
            _ => (CLR_HL_DIRECT, CLR_HL_DIRECT_BG),
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Rendu — WM_PAINT
    // ═══════════════════════════════════════════════════════════════
    private void OnPaint(IntPtr hWnd)
    {
        var hdcPaint = Win32.BeginPaint(hWnd, out var ps);
        Win32.GetClientRect(hWnd, out var clientRect);
        int cw = clientRect.right;
        int ch = clientRect.bottom;
        if (cw <= 0 || ch <= 0) { Win32.EndPaint(hWnd, ref ps); return; }

        // Double buffering
        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        var hdc = Win32.CreateCompatibleDC(hdcScreen);
        var hBmp = Win32.CreateCompatibleBitmap(hdcScreen, cw, ch);
        var hBmpOld = Win32.SelectObject(hdc, hBmp);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);

        try
        {
            // Fond zones supérieures (clair)
            int kbTop = S(BASE_HEADER_H + BASE_INSTRUCTION_H + BASE_TARGET_H);
            var topRect = new Win32.RECT { left = 0, top = 0, right = cw, bottom = kbTop };
            Win32.FillRect(hdc, ref topRect, _hBgBrush);

            // Fond zone clavier (sombre)
            int footerTop = ch - S(BASE_FOOTER_H);
            var kbRect = new Win32.RECT { left = 0, top = kbTop, right = cw, bottom = footerTop };
            Win32.FillRect(hdc, ref kbRect, _hKbBgBrush);

            // Fond footer (clair)
            var footerRect = new Win32.RECT { left = 0, top = footerTop, right = cw, bottom = ch };
            Win32.FillRect(hdc, ref footerRect, _hBgBrush);

            Win32.SetBkMode(hdc, Win32.TRANSPARENT);

            if (_inTransition)
                PaintTransition(hdc, cw, kbTop);
            else if (_completed)
                PaintFinalScreen(hdc, cw, kbTop);
            else if (_awaitingChoice)
                PaintChoiceScreen(hdc, cw, kbTop);
            else
                PaintExercise(hdc, cw, kbTop);

            // Clavier virtuel
            PaintKeyboard(hdc, cw, kbTop, footerTop);

            // (Légende clavier retirée — peu utile à un seul item ; les tooltips sur survol
            // remplacent désormais l'information par caractère.)

            // Blit
            Win32.BitBlt(hdcPaint, 0, 0, cw, ch, hdc, 0, 0, 0x00CC0020);
        }
        finally
        {
            Win32.SelectObject(hdc, hBmpOld);
            Win32.DeleteObject(hBmp);
            Win32.DeleteDC(hdc);
        }

        Win32.EndPaint(hWnd, ref ps);
    }

    private void PaintExercise(IntPtr hdc, int cw, int kbTop)
    {
        int margin = S(BASE_MARGIN);
        int y = S(8);

        // Header — dots de progression dessines via GDI Ellipse (taille uniforme, couleurs
        // distinctes orange/vert/gris). Avant on utilisait des chars Unicode ● / ○
        // via DrawTextW mais les glyphes ne rendaient pas a la meme taille dans Segoe UI.
        int dotDiam = S(12);
        int dotSpacing = S(8);
        int dotsTop = y + S(4);
        for (int i = 0; i < _steps.Length; i++)
        {
            uint dotColor = i < _currentStep ? CLR_PROGRESS_DONE
                          : i == _currentStep ? CLR_PROGRESS_CURRENT
                          : CLR_PROGRESS_TODO;
            int dotX = margin + i * (dotDiam + dotSpacing);
            var hBrush = Win32.CreateSolidBrush(dotColor);
            var hPen = Win32.CreatePen(0, 1, dotColor);
            var hOldBrush = Win32.SelectObject(hdc, hBrush);
            var hOldPen = Win32.SelectObject(hdc, hPen);
            Win32.Ellipse(hdc, dotX, dotsTop, dotX + dotDiam, dotsTop + dotDiam);
            Win32.SelectObject(hdc, hOldBrush);
            Win32.SelectObject(hdc, hOldPen);
            Win32.DeleteObject(hBrush);
            Win32.DeleteObject(hPen);
        }

        var hOldFont = Win32.SelectObject(hdc, _hFontTitle);
        y += S(22);
        Win32.SelectObject(hdc, _hFontTitle);
        Win32.SetTextColor(hdc, CLR_HEADER_TITLE);
        string title = $"Exercice {_currentStep + 1}/{_steps.Length} \u2014 {_steps[_currentStep].Title}";
        var titleRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + S(32) };
        Win32.DrawTextW(hdc, title, title.Length, ref titleRect, 0);

        // Suffixe \u00ab (Bonus) \u00bb \u00e0 la suite du titre, en dor\u00e9, si l'exercice est facultatif.
        if (_steps[_currentStep].Skippable)
        {
            int titleW = GdiHelpers.MeasureSingleLineWidth(hdc, _hFontTitle, title);
            const string bonusSuffix = " (Bonus)";
            var bonusRect = new Win32.RECT { left = margin + titleW, top = y, right = cw - margin, bottom = y + S(32) };
            Win32.SetTextColor(hdc, CLR_BONUS_TEXT);
            Win32.DrawTextW(hdc, bonusSuffix, bonusSuffix.Length, ref bonusRect, 0);
        }

        // Instruction
        y = S(BASE_HEADER_H) + S(4);
        Win32.SelectObject(hdc, _hFontInstruction);
        Win32.SetTextColor(hdc, CLR_INSTRUCTION);
        string instr = _steps[_currentStep].Instruction;
        var instrRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + S(BASE_INSTRUCTION_H) };
        Win32.DrawTextW(hdc, instr, instr.Length, ref instrRect, 0);

        // Texte cible — caractère par caractère
        y = S(BASE_HEADER_H + BASE_INSTRUCTION_H) + S(4);
        var target = _steps[_currentStep].Target;
        Win32.SelectObject(hdc, _hFontTarget);

        int textX = margin;
        for (int i = 0; i < target.Length; i++)
        {
            string ch = target[i].ToString();
            // Mesurer la largeur du caractère
            var measureRect = new Win32.RECT { left = 0, top = 0, right = 10000, bottom = 1000 };
            Win32.DrawTextW(hdc, ch, 1, ref measureRect, Win32.DT_CALCRECT);
            int charW = measureRect.right;

            if (i < _cursorPosition)
            {
                // Tapé correctement → vert
                Win32.SetTextColor(hdc, CLR_TARGET_CORRECT);
            }
            else if (i == _cursorPosition)
            {
                // Caractère en cours
                Win32.SetTextColor(hdc, _currentCharError ? CLR_TARGET_ERROR : CLR_TARGET_CURRENT);
                // Souligné — décalé sous la base des lettres sans trop s'éloigner.
                int underY = y + S(28);
                var hPen = Win32.CreatePen(0, S(2), _currentCharError ? CLR_TARGET_ERROR : CLR_TARGET_CURRENT);
                var hOldPen = Win32.SelectObject(hdc, hPen);
                Win32.MoveToEx(hdc, textX, underY, IntPtr.Zero);
                Win32.LineTo(hdc, textX + charW, underY);
                Win32.SelectObject(hdc, hOldPen);
                Win32.DeleteObject(hPen);
            }
            else
            {
                // Pas encore tapé → gris
                Win32.SetTextColor(hdc, CLR_TARGET_PENDING);
            }

            var charRect = new Win32.RECT { left = textX, top = y, right = textX + charW + S(2), bottom = y + S(30) };
            Win32.DrawTextW(hdc, ch, 1, ref charRect, 0);
            textX += charW + S(1);
        }

        Win32.SelectObject(hdc, hOldFont);

        // Bloc droit : Verrouillage Majuscule + (eventuellement) Touche morte, juste au-dessus
        // du clavier, aligne a droite. Remplace l'ancienne barre d'état sous le target text.
        int kbTopForStatus = S(BASE_HEADER_H + BASE_INSTRUCTION_H + BASE_TARGET_H);
        PaintRightStatusBlock(hdc, cw, kbTopForStatus);
    }

    /// <summary>
    /// Dessine le bloc droit avec le statut Verrouillage Majuscule (toujours affiche) et
    /// la touche morte active (uniquement si une est active). Les 2 lignes sont collees au
    /// clavier avec une petite marge ; la ligne Verrouillage Majuscule est au-dessus de la
    /// ligne Touche morte. ACTIVE en vert quand le Caps Lock est actif.
    /// </summary>
    private void PaintRightStatusBlock(IntPtr hdc, int cw, int kbTop)
    {
        int margin = S(BASE_MARGIN);
        int marginAboveKb = S(8);
        int statusH = S(20);
        int gap = S(4);
        int rightX = cw - margin;

        var hOldFont = Win32.SelectObject(hdc, _hFontStatus);

        var activeDk = _mapper.ActiveDeadKey;
        bool dkLineShown = !string.IsNullOrEmpty(activeDk);
        int bottomLineY = kbTop - marginAboveKb - statusH;
        int topLineY = dkLineShown ? bottomLineY - gap - statusH : bottomLineY;

        // Ligne Verrouillage Majuscule (toujours)
        bool capsActive = _mapper.CapsLockActive;
        string capsSuffix = capsActive ? "ACTIVÉ" : "désactivé";
        uint capsSuffixColor = capsActive ? CLR_TARGET_CORRECT : CLR_STATUS;
        DrawTwoColorRightAligned(hdc, _hFontStatus,
            "Verrouillage Majuscule : ", CLR_STATUS,
            capsSuffix, capsSuffixColor,
            rightX, topLineY);

        // Ligne Touche morte (si active)
        if (dkLineShown)
        {
            VirtualKeyboard._deadKeyNamesFr.TryGetValue(activeDk!, out var dkName);
            dkName ??= activeDk!;
            string symbol = GetDeadKeySymbolNonCombining(activeDk!);
            string dkText = string.IsNullOrEmpty(symbol)
                ? $"Touche morte : {dkName}"
                : $"Touche morte : {dkName} {symbol}";
            DrawSingleColorRightAligned(hdc, _hFontStatus, dkText, CLR_STATUS, rightX, bottomLineY);
        }

        Win32.SelectObject(hdc, hOldFont);
    }

    /// <summary>Symbole isole d'une touche morte uniquement s'il est non-combinant (sinon vide).</summary>
    private string GetDeadKeySymbolNonCombining(string dkName)
    {
        if (_layout.DeadKeys.TryGetValue(dkName, out var dk))
        {
            var iso = dk.GetIsolated();
            if (!string.IsNullOrWhiteSpace(iso) && iso.Length == 1 && !IsCombiningMark(iso[0]))
                return iso;
        }
        string fallback = TrayApplication.GetDeadKeySymbol(dkName);
        if (!string.IsNullOrEmpty(fallback) && fallback.Length == 1 && !IsCombiningMark(fallback[0]))
            return fallback;
        return "";
    }

    private void DrawSingleColorRightAligned(IntPtr hdc, IntPtr hFont, string text, uint color, int rightX, int y)
    {
        int width = GdiHelpers.MeasureSingleLineWidth(hdc, hFont, text);
        int left = rightX - width;
        Win32.SetTextColor(hdc, color);
        var rc = new Win32.RECT { left = left, top = y, right = rightX, bottom = y + 100 };
        Win32.DrawTextW(hdc, text, text.Length, ref rc,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
    }

    private void DrawTwoColorRightAligned(IntPtr hdc, IntPtr hFont,
        string prefix, uint prefixColor,
        string suffix, uint suffixColor,
        int rightX, int y)
    {
        int prefixWidth = GdiHelpers.MeasureSingleLineWidth(hdc, hFont, prefix);
        int suffixWidth = GdiHelpers.MeasureSingleLineWidth(hdc, hFont, suffix);
        int leftX = rightX - prefixWidth - suffixWidth;
        Win32.SetTextColor(hdc, prefixColor);
        var rcP = new Win32.RECT { left = leftX, top = y, right = leftX + prefixWidth, bottom = y + 100 };
        Win32.DrawTextW(hdc, prefix, prefix.Length, ref rcP,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        Win32.SetTextColor(hdc, suffixColor);
        var rcS = new Win32.RECT { left = leftX + prefixWidth, top = y, right = rightX, bottom = y + 100 };
        Win32.DrawTextW(hdc, suffix, suffix.Length, ref rcS,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
    }

    private void PaintTransition(IntPtr hdc, int cw, int kbTop)
    {
        int centerY = kbTop / 2;
        var hOldFont = Win32.SelectObject(hdc, _hFontTransition);
        Win32.SetTextColor(hdc, CLR_TRANSITION);
        string text = "\u2713 Bravo !";
        var rect = new Win32.RECT { left = 0, top = centerY - S(20), right = cw, bottom = centerY + S(20) };
        Win32.DrawTextW(hdc, text, text.Length, ref rect, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
        Win32.SelectObject(hdc, hOldFont);
    }

    /// <summary>
    /// Page de choix affichee apres chaque exercice reussi.
    /// 1er succes de l'exercice : \u00ab \u2713 Bravo ! \u00bb (gros vert) + sous-titre + 2 boutons.
    /// 2e+ succes (apres Recommencer) : seulement les 2 boutons, pas de titre/sous-titre.
    /// Les boutons eux-memes sont dessines par Windows via leurs HWND.
    /// </summary>
    private void PaintChoiceScreen(IntPtr hdc, int cw, int kbTop)
    {
        if (_currentStepSuccessCount <= 1)
        {
            // Block \u00ab \u2713 Bravo ! \u00bb + gap + boutons : centre verticalement dans la zone superieure.
            // Cette geometrie doit matcher RepositionControls (branche _awaitingChoice + 1er succes).
            int margin = S(BASE_MARGIN);
            int titleH = S(40);
            int gapTitleButtons = S(14);
            int choiceBtnH = S(36);
            int blockH = titleH + gapTitleButtons + choiceBtnH;
            int blockTop = (kbTop - blockH) / 2;
            int titleTop = blockTop;
            int titleBottom = titleTop + titleH;

            // Titre \u00ab \u2713 Bravo ! \u00bb centre, grande police, vert valide
            var hOldFont = Win32.SelectObject(hdc, _hFontTransition);
            Win32.SetTextColor(hdc, CLR_TRANSITION);
            const string title = "\u2713 Bravo !";
            var titleRect = new Win32.RECT { left = margin, top = titleTop, right = cw - margin, bottom = titleBottom };
            Win32.DrawTextW(hdc, title, title.Length, ref titleRect,
                Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
            Win32.SelectObject(hdc, hOldFont);
        }
        // n-ieme succes : pas de titre, juste les boutons (positionnes par RepositionControls
        // au centre vertical de la zone superieure).
    }

    /// <summary>
    /// Affiche une légende centrée. Maj./Verr.Maj./AltGr ne sont plus utiles depuis qu'on
    /// met en bleu le caractère actif (selon modificateurs courants) et non un layer fixe.
    /// On garde uniquement « Touche morte » (en rouge) pour expliquer la couleur des dk_*
    /// affichés sur le clavier.
    /// </summary>
    private void PaintLegend(IntPtr hdc, int cw, int footerTop)
    {
        var hOldFont = Win32.SelectObject(hdc, _hFontStatus);
        try
        {
            var items = new (string Text, uint Color)[]
            {
                ("Touche morte", CLR_DK_CHAR),
            };
            const string sep = "  —  ";
            int sepW = GdiHelpers.MeasureSingleLineWidth(hdc, _hFontStatus, sep);
            int totalW = 0;
            int[] widths = new int[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                widths[i] = GdiHelpers.MeasureSingleLineWidth(hdc, _hFontStatus, items[i].Text);
                totalW += widths[i];
                if (i < items.Length - 1) totalW += sepW;
            }
            int x = (cw - totalW) / 2;
            int y = footerTop + S(8);
            int h = S(20);
            for (int i = 0; i < items.Length; i++)
            {
                Win32.SetTextColor(hdc, items[i].Color);
                var r = new Win32.RECT { left = x, top = y, right = x + widths[i], bottom = y + h };
                Win32.DrawTextW(hdc, items[i].Text, items[i].Text.Length, ref r,
                    Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
                x += widths[i];
                if (i < items.Length - 1)
                {
                    Win32.SetTextColor(hdc, CLR_BTN_QUIT_TEXT);
                    var rs = new Win32.RECT { left = x, top = y, right = x + sepW, bottom = y + h };
                    Win32.DrawTextW(hdc, sep, sep.Length, ref rs,
                        Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
                    x += sepW;
                }
            }
        }
        finally
        {
            Win32.SelectObject(hdc, hOldFont);
        }
    }

    private void PaintFinalScreen(IntPtr hdc, int cw, int kbTop)
    {
        int margin = S(BASE_MARGIN);
        int titleH = S(48);
        int subtitleH = S(28);
        int gap = S(8);
        int blockH = titleH + gap + subtitleH;
        int blockTop = (kbTop - blockH) / 2;

        // Titre \u00ab Bravo ! \u00bb centr\u00e9, grande police, orange brand
        var hOldFont = Win32.SelectObject(hdc, _hFontTransition);
        Win32.SetTextColor(hdc, CLR_PROGRESS_DONE);
        const string title = "Bravo !";
        var titleRect = new Win32.RECT { left = margin, top = blockTop, right = cw - margin, bottom = blockTop + titleH };
        Win32.DrawTextW(hdc, title, title.Length, ref titleRect,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);

        // Sous-titre \u2014 police plus petite, fonc\u00e9e
        Win32.SelectObject(hdc, _hFontTitle);
        Win32.SetTextColor(hdc, CLR_HEADER_TITLE);
        const string subtitle = "Vous ma\u00eetrisez les bases d'AZERTY Global.";
        var subtitleRect = new Win32.RECT { left = margin, top = blockTop + titleH + gap, right = cw - margin, bottom = blockTop + titleH + gap + subtitleH };
        Win32.DrawTextW(hdc, subtitle, subtitle.Length, ref subtitleRect,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);

        Win32.SelectObject(hdc, hOldFont);
    }

    // ═══════════════════════════════════════════════════════════════
    // Rendu du clavier virtuel
    // ═══════════════════════════════════════════════════════════════
    private void PaintKeyboard(IntPtr hdc, int cw, int kbTop, int kbBottom)
    {
        int kbW = cw;
        int kbH = kbBottom - kbTop;
        if (kbW <= 0 || kbH <= 0) return;

        var geo = VirtualKeyboard.GetKeyboardGeometry(kbW, kbH);

        // On reset les hit areas à chaque paint pour qu'elles reflètent la géométrie courante.
        _keyHitAreas.Clear();

        foreach (var vk in _visualKeys)
        {
            int kx = (int)(geo.OffsetX + vk.X * geo.Scale) + 0; // offset ajusté dans la zone clavier
            int ky = kbTop + (int)(geo.OffsetY + vk.Y * geo.Scale);
            int kw = (int)(vk.W * geo.Scale) - 1;
            int kh = (int)(vk.H * geo.Scale) - 1;

            if (kw <= 0 || kh <= 0) continue;

            // Mémoriser la zone pour le hit-test du tooltip.
            _keyHitAreas.Add((
                new Win32.RECT { left = kx, top = ky, right = kx + kw, bottom = ky + kh },
                vk.Scancode,
                vk.IsContextual ? vk.Label : null));

            // Déterminer couleur de fond
            bool isPressed = _pressedScancode != 0 && vk.Scancode == _pressedScancode;
            bool isHighlighted = IsKeyHighlighted(vk);
            bool isStep2 = IsStep2Key(vk);
            bool isModActive = IsModifierActive(vk);

            // Sémantique highlight pédagogique :
            //  • Touche à appuyer pour le caractère courant → contour vert + fond normal (KEY/KEY_CTX)
            //  • Touche modificateur à appuyer ET déjà activée par l'utilisateur → fond vert plein + contour vert épaissi
            //  • Modificateur activé hors contexte exercice (rare, ex Caps toggled sans être à appuyer) → fond CLR_MOD_ACTIVE
            //  • Touche Backspace (scancode 0x0E) → desactivee pendant les exercices, fond plus terne
            //    pour signaler visuellement qu'elle est inutile (cf. OnBackspace l. 1290).
            //    Court-circuite isPressed pour ne pas faire flasher la touche en cas d'appui reflexe.
            bool isDisabledKey = vk.IsContextual && vk.Scancode == 0x0E;
            uint bgColor, borderColor;
            int borderWidth = 1;
            if (isDisabledKey)
            {
                bgColor = CLR_KEY_DISABLED;
                borderColor = CLR_KEY_BORDER;
            }
            else if (isPressed)
            {
                bgColor = CLR_KEY_PRESSED;
                borderColor = CLR_KEY_BORDER;
            }
            else if ((isHighlighted || isStep2) && isModActive)
            {
                // À appuyer + activé → vert plein
                var (hlBorder, hlBg) = GetHighlightColors(isStep2, vk);
                bgColor = hlBg;
                borderColor = hlBorder;
                borderWidth = 2;
            }
            else if (isHighlighted || isStep2)
            {
                // À appuyer (pas encore activé) → contour seul
                bgColor = vk.IsContextual ? CLR_KEY_CTX : CLR_KEY;
                var (hlBorder, _) = GetHighlightColors(isStep2, vk);
                borderColor = hlBorder;
                borderWidth = 2;
            }
            else if (isModActive)
            {
                bgColor = CLR_MOD_ACTIVE;
                borderColor = CLR_KEY_BORDER;
            }
            else
            {
                bgColor = vk.IsContextual ? CLR_KEY_CTX : CLR_KEY;
                borderColor = CLR_KEY_BORDER;
            }

            // Dessiner la touche — cas spécial Entrée ISO (polygone en L inversé) :
            // partie haute pleine largeur (1.5u), partie basse réduite (1.25u alignée à droite).
            bool isIsoEnter = vk.Scancode == 0x1C && vk.H > VirtualKeyboard.KEY_H;
            var hBrush = Win32.CreateSolidBrush(bgColor);
            var hPen = Win32.CreatePen(0, borderWidth, borderColor);
            var hOldPen = Win32.SelectObject(hdc, hPen);
            var hOldBrush = Win32.SelectObject(hdc, hBrush);

            if (isIsoEnter)
            {
                // 6 points du L inversé. Référence : VirtualKeyboard.cs PaintContent.
                float stepY = vk.Y + VirtualKeyboard.KEY_H;
                float botStartY = vk.Y + VirtualKeyboard.KEY_H + VirtualKeyboard.ROW_GAP;
                float botX = vk.X + (vk.W - 1.25f);
                int yBase = kbTop + geo.OffsetY;
                int px_tl = kx;
                int py_tl = ky;
                int px_tr = kx + kw;
                int py_br = yBase + (int)((botStartY + VirtualKeyboard.KEY_H) * geo.Scale);
                int px_bl = geo.OffsetX + (int)(botX * geo.Scale);
                int py_step = yBase + (int)(stepY * geo.Scale);
                var pts = new Win32.POINT[]
                {
                    new() { x = px_tl, y = py_tl },
                    new() { x = px_tr, y = py_tl },
                    new() { x = px_tr, y = py_br },
                    new() { x = px_bl, y = py_br },
                    new() { x = px_bl, y = py_step },
                    new() { x = px_tl, y = py_step },
                };
                Win32.Polygon(hdc, pts, 6);
            }
            else
            {
                var keyRect = new Win32.RECT { left = kx, top = ky, right = kx + kw, bottom = ky + kh };
                Win32.FillRect(hdc, ref keyRect, hBrush);
                // Bordure rectangulaire (Polygon serait équivalent mais plus coûteux)
                Win32.MoveToEx(hdc, kx, ky, IntPtr.Zero);
                Win32.LineTo(hdc, kx + kw, ky);
                Win32.LineTo(hdc, kx + kw, ky + kh);
                Win32.LineTo(hdc, kx, ky + kh);
                Win32.LineTo(hdc, kx, ky);
            }

            Win32.SelectObject(hdc, hOldBrush);
            Win32.SelectObject(hdc, hOldPen);
            Win32.DeleteObject(hPen);
            Win32.DeleteObject(hBrush);

            // Barre CapsLock
            if (vk.Label == "Verr. Maj." && _mapper.CapsLockActive)
            {
                int barH = Math.Max(2, (int)(geo.Scale * 0.08f));
                var barRect = new Win32.RECT { left = kx, top = ky + kh - barH, right = kx + kw, bottom = ky + kh };
                var hBarBrush = Win32.CreateSolidBrush(CLR_CAPS_BAR);
                Win32.FillRect(hdc, ref barRect, hBarBrush);
                Win32.DeleteObject(hBarBrush);
            }

            // Contenu de la touche
            if (vk.IsContextual)
            {
                // Label centré, toujours en blanc cassé. Pour Entrée ISO, centrer le texte
                // dans la colonne droite (la partie commune du L) et non sur la pleine largeur.
                // Backspace desactivee : label en gris fonce pour signaler qu'elle est inutile.
                var hOldFont = Win32.SelectObject(hdc, _hFontCtx);
                Win32.SetTextColor(hdc, isDisabledKey ? 0x00606060u : CLR_CTX_TEXT);
                int ctxLeft = isIsoEnter
                    ? geo.OffsetX + (int)((vk.X + (vk.W - 1.25f)) * geo.Scale)
                    : kx;
                var labelRect = new Win32.RECT { left = ctxLeft, top = ky, right = kx + kw, bottom = ky + kh };
                Win32.DrawTextW(hdc, vk.Label, vk.Label.Length, ref labelRect,
                    Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
                Win32.SelectObject(hdc, hOldFont);
            }
            else if (vk.Scancode != 0 && _layout.Keys.TryGetValue(vk.Scancode, out var keyDef))
            {
                PaintKeyCharacters(hdc, kx, ky, kw, kh, keyDef, vk, geo.Scale);
            }

            // Badge highlight (1 ou 2)
            bool keepCapsStatusKey = vk.Label == "Verr. Maj."
                && _currentStep < _steps.Length
                && _steps[_currentStep].KeepCapsHighlight;
            if (isHighlighted && _highlightType == "step1" && !keepCapsStatusKey)
            {
                PaintBadge(hdc, kx + kw - S(12), ky + S(1), "1", CLR_HL_STEP1);
            }
            else if (((isHighlighted && _highlightType == "step2") || isStep2) && !keepCapsStatusKey)
            {
                PaintBadge(hdc, kx + kw - S(12), ky + S(1), "2", CLR_HL_STEP2);
            }
        }

        // Overlay « pause » quand la fenêtre n'a plus le focus clavier (option A).
        // Affiche apres debounce 250ms (TIMER_FOCUS_LOST_CONFIRM) pour eviter d'apparaitre
        // sur les blinks WM_KILLFOCUS / WM_SETFOCUS rapides causes par MoveWindow / repaint.
        if (_focusLostConfirmed)
            PaintFocusLostOverlay(hdc, cw, kbTop, kbBottom);
    }

    /// <summary>
    /// Assombrit la zone clavier via AlphaBlend (alpha 160 sur fond noir) et affiche
    /// « Cliquez pour reprendre » centré. Le clic dans la fenêtre redonne le focus
    /// (Windows envoie WM_SETFOCUS) et l'overlay disparaît.
    /// </summary>
    private void PaintFocusLostOverlay(IntPtr hdc, int cw, int top, int bottom)
    {
        // Bitmap source 1×1 noir pour AlphaBlend (msimg32.dll).
        var hdcMem = Win32.CreateCompatibleDC(hdc);
        var hBmp = Win32.CreateCompatibleBitmap(hdc, 1, 1);
        var hOldBmp = Win32.SelectObject(hdcMem, hBmp);
        var oneRect = new Win32.RECT { left = 0, top = 0, right = 1, bottom = 1 };
        var hBlackBrush = Win32.CreateSolidBrush(0x00000000u);
        Win32.FillRect(hdcMem, ref oneRect, hBlackBrush);
        Win32.DeleteObject(hBlackBrush);

        var blend = new Win32.BLENDFUNCTION
        {
            BlendOp = Win32.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 170,  // ~67 % opacité
            AlphaFormat = 0
        };
        Win32.AlphaBlend(hdc, 0, top, cw, bottom - top, hdcMem, 0, 0, 1, 1, blend);

        Win32.SelectObject(hdcMem, hOldBmp);
        Win32.DeleteObject(hBmp);
        Win32.DeleteDC(hdcMem);

        // Texte « Cliquez pour reprendre » centré
        var hOldFont = Win32.SelectObject(hdc, _hFontTitle);
        Win32.SetTextColor(hdc, 0x00FFFFFFu);
        Win32.SetBkMode(hdc, Win32.TRANSPARENT);
        var rc = new Win32.RECT { left = 0, top = top, right = cw, bottom = bottom };
        const string msg = "Cliquez pour reprendre";
        Win32.DrawTextW(hdc, msg, msg.Length, ref rc,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
        Win32.SelectObject(hdc, hOldFont);
    }

    private void PaintBadge(IntPtr hdc, int x, int y, string text, uint color)
    {
        int size = S(14);
        var hBrush = Win32.CreateSolidBrush(color);
        var rect = new Win32.RECT { left = x, top = y, right = x + size, bottom = y + size };
        Win32.FillRect(hdc, ref rect, hBrush);
        Win32.DeleteObject(hBrush);

        var hOldFont = Win32.SelectObject(hdc, _hFontBadge);
        Win32.SetTextColor(hdc, 0x00FFFFFF);
        Win32.DrawTextW(hdc, text, text.Length, ref rect, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
        Win32.SelectObject(hdc, hOldFont);
    }

    /// <summary>
    /// Dessine les caractères d'une touche selon sa famille.
    /// Reproduit la logique de tester/keyboard.js : updateLetterKeyDisplay (lettres a-z),
    /// updateAccentedLetterKeyDisplay (é è ç à rangée numérique), updateSymbolKeyDisplay (autres).
    /// Quand une touche morte est active, affiche le résultat de combinaison (vert) au centre
    /// de la zone haute et conserve le nom AZERTY de la touche en bas, comme le clavier virtuel.
    /// </summary>
    private void PaintKeyCharacters(IntPtr hdc, int kx, int ky, int kw, int kh,
        KeyDefinition keyDef, in VirtualKeyboard.VisualKey vk, float scale)
    {
        // Padding intérieur des touches — marge entre les caractères et les bords gauche/droit.
        // Ratio ajustable via learning-tweaks.json (PadRatio, defaut 0.14).
        int pad = Math.Max(4, (int)(scale * _tweaks.PadRatio));

        // ── 1. Touche morte active : afficher le résultat + le nom de touche ──
        if (_mapper.ActiveDeadKey != null && _layout.DeadKeys.TryGetValue(_mapper.ActiveDeadKey, out var dk))
        {
            PaintActiveDeadKeyCharacters(hdc, kx, ky, kw, kh, keyDef, vk, dk);
            return;
        }

        // Exercices 1 à 5 : affichage simplifié. Exercice 6 : simplifié + quelques aides langues.
        bool languageExercise = _currentStep == _steps.Length - 1;
        keyDef = FilterKeyForOnboarding(keyDef, vk.Scancode, languageExercise);

        // ── 2. Dispatcher selon la famille de touche ──
        if (AccentedNumericScancodes.Contains(vk.Scancode))
            PaintAccentedNumericKey(hdc, kx, ky, kw, kh, keyDef, pad);
        else if (LetterKeyScancodes.Contains(vk.Scancode) && IsLetterChar(keyDef.Base))
            PaintLetterKey(hdc, kx, ky, kw, kh, keyDef, pad);
        else
            PaintSymbolKey(hdc, kx, ky, kw, kh, keyDef, pad);
    }

    private void PaintActiveDeadKeyCharacters(IntPtr hdc, int kx, int ky, int kw, int kh,
        KeyDefinition keyDef, in VirtualKeyboard.VisualKey vk, DeadKeyDefinition dk)
    {
        // Même logique que VirtualKeyboard.GetDisplayChar(scancode) en mode touche morte :
        // la combinaison dépend du caractère réellement tapé, avec Smart Caps Lock.
        bool shift = _mapper.ShiftDown;
        bool caps = _mapper.CapsLockActive;
        bool isLetterKey = LetterKeyScancodes.Contains(vk.Scancode) && IsLetterChar(keyDef.Base);
        string? lookupChar;
        if (isLetterKey && (shift || caps))
            lookupChar = keyDef.Base?.ToUpperInvariant();
        else if (!isLetterKey && shift)
            lookupChar = keyDef.Shift;
        else
            lookupChar = keyDef.Base;

        string? result = lookupChar != null ? dk.Apply(lookupChar) : null;
        if (vk.Scancode == 0x39) result = dk.GetIsolated();

        int labelH = Math.Max(S(16), kh / 3);
        int labelTop = Math.Max(ky, ky + kh - labelH - S(2));

        if (!string.IsNullOrEmpty(result))
        {
            var hOldFont = Win32.SelectObject(hdc, _hFontCharMain);
            Win32.SetTextColor(hdc, CLR_DK_RESULT);
            var charRect = new Win32.RECT
            {
                left = kx,
                top = ky,
                right = kx + kw,
                bottom = Math.Max(ky + S(12), labelTop - S(1))
            };
            Win32.DrawTextW(hdc, result, result.Length, ref charRect,
                Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_NOCLIP);
            Win32.SelectObject(hdc, hOldFont);
        }

        var hOldLabelFont = Win32.SelectObject(hdc, _hFontCtx);
        Win32.SetTextColor(hdc, CLR_CHAR_BASE);
        var labelRect = new Win32.RECT
        {
            left = kx,
            top = labelTop,
            right = kx + kw,
            bottom = ky + kh - S(1)
        };
        Win32.DrawTextW(hdc, vk.Label, vk.Label.Length, ref labelRect,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_NOCLIP);
        Win32.SelectObject(hdc, hOldLabelFont);
    }

    private static bool IsLetterChar(string? s) => s != null && s.Length == 1 && char.IsLetter(s[0]);
    private static bool IsDeadKeyRef(string? s) => s != null && s.StartsWith("dk_");

    /// <summary>
    /// Lettre simple (KeyA-KeyN sauf accentuées) : caractère principal en top-left,
    /// AltGr en bottom-right (lettre, casse adaptée), Shift+AltGr en top-right uniquement
    /// si non-lettre et différent d'AltGr. Reproduit updateLetterKeyDisplay() web.
    /// </summary>
    private void PaintLetterKey(IntPtr hdc, int kx, int ky, int kw, int kh,
        KeyDefinition keyDef, int pad)
    {
        bool shift = _mapper.ShiftDown;
        bool altGr = _mapper.AltGrDown;
        bool caps = _mapper.CapsLockActive;

        // Caractère principal (top-left) selon shift/caps
        string? mainChar;
        if (caps && shift) mainChar = keyDef.CapsShift ?? keyDef.Base;
        else if (caps) mainChar = keyDef.Caps ?? keyDef.Base?.ToUpperInvariant();
        else if (shift) mainChar = keyDef.Shift ?? keyDef.Base?.ToUpperInvariant();
        else mainChar = keyDef.Base;

        // AltGr (bottom-right) — adapter casse si lettre
        string? altGrRaw = keyDef.AltGr;
        string? altGrCharToShow = altGrRaw;
        bool altGrIsLetter = IsLetterChar(altGrRaw);
        if (altGrIsLetter)
        {
            if (caps && shift) altGrCharToShow = keyDef.CapsShiftAltGr ?? altGrRaw;
            else if (caps) altGrCharToShow = keyDef.CapsAltGr ?? keyDef.ShiftAltGr ?? altGrRaw?.ToUpperInvariant();
            else if (shift) altGrCharToShow = keyDef.ShiftAltGr ?? altGrRaw?.ToUpperInvariant();
        }
        bool hasAltGrChar = !string.IsNullOrEmpty(altGrCharToShow) && altGrCharToShow != mainChar;

        // Shift+AltGr (top-right) — uniquement non-lettre et différent d'AltGr
        string? shiftAltGrChar = keyDef.ShiftAltGr;
        bool showShiftAltGr = !string.IsNullOrEmpty(shiftAltGrChar)
            && !IsLetterChar(shiftAltGrChar)
            && shiftAltGrChar != altGrRaw;

        // États actifs
        bool topLeftActive = !altGr;
        bool bottomRightActive = altGr && (!shift || (shift && altGrIsLetter && !showShiftAltGr));
        bool topRightActive = altGr && shift && showShiftAltGr;

        // Top-left : caractère principal (gros, aligné au top)
        DrawCharAt(hdc, kx + pad, ky, kx + kw / 2 + pad, ky + kh - pad,
            mainChar, topLeftActive, IsDeadKeyRef(mainChar), alignLeft: true, useMainFont: true, alignTop: true);

        // Bottom-right : AltGr discret (centré dans la moitié basse)
        if (hasAltGrChar)
            DrawCharAt(hdc, kx + kw / 2, ky + kh / 2, kx + kw - pad, ky + kh - pad,
                altGrCharToShow, bottomRightActive, IsDeadKeyRef(altGrRaw), alignLeft: false, useMainFont: false);

        // Top-right : Shift+AltGr non-lettre (top-aligned)
        if (showShiftAltGr)
            DrawCharAt(hdc, kx + kw / 2, ky, kx + kw - pad, ky + kh / 2 + pad,
                shiftAltGrChar, topRightActive, IsDeadKeyRef(shiftAltGrChar), alignLeft: false, useMainFont: false, alignTop: true);
    }

    /// <summary>
    /// Lettre accentuée rangée numérique (Digit2=é, Digit7=è, Digit9=ç, Digit0=à) :
    /// 4 quadrants spécifiques — chiffre top-left (Shift), lettre bottom-left (Base affectée par Caps),
    /// AltGr bottom-right, Shift+AltGr top-right. Reproduit updateAccentedLetterKeyDisplay() web.
    /// </summary>
    private void PaintAccentedNumericKey(IntPtr hdc, int kx, int ky, int kw, int kh,
        KeyDefinition keyDef, int pad)
    {
        bool shift = _mapper.ShiftDown;
        bool altGr = _mapper.AltGrDown;
        bool caps = _mapper.CapsLockActive;

        // Bottom-left : lettre (Caps fait passer à É/È/Ç/À)
        string? letter = caps ? (keyDef.Caps ?? keyDef.Base?.ToUpperInvariant()) : keyDef.Base;
        // Top-left : chiffre (Shift)
        string? digit = keyDef.Shift;
        string? altGr1 = keyDef.AltGr;
        string? altGr2 = keyDef.ShiftAltGr;

        // États actifs (Caps n'affecte que la casse, pas la position)
        bool letterActive = !altGr && !shift;
        bool digitActive = !altGr && shift;
        bool altGr1Active = altGr && !shift;
        bool altGr2Active = altGr && shift;

        // Quadrants bas
        DrawCharAt(hdc, kx + pad, ky + kh / 2, kx + kw / 2, ky + kh - pad,
            letter, letterActive, IsDeadKeyRef(keyDef.Base), alignLeft: true, useMainFont: false);
        DrawCharAt(hdc, kx + kw / 2, ky + kh / 2, kx + kw - pad, ky + kh - pad,
            altGr1, altGr1Active, IsDeadKeyRef(altGr1), alignLeft: false, useMainFont: false);
        // Quadrants hauts (top-aligned, rect étendu)
        DrawCharAt(hdc, kx + pad, ky, kx + kw / 2, ky + kh / 2 + pad,
            digit, digitActive, IsDeadKeyRef(digit), alignLeft: true, useMainFont: false, alignTop: true);
        DrawCharAt(hdc, kx + kw / 2, ky, kx + kw - pad, ky + kh / 2 + pad,
            altGr2, altGr2Active, IsDeadKeyRef(altGr2), alignLeft: false, useMainFont: false, alignTop: true);
    }

    /// <summary>
    /// Symbole / chiffre / ponctuation : 4 quadrants standard.
    /// Top-left = Shift, top-right = Shift+AltGr, bottom-left = Base, bottom-right = AltGr.
    /// Reproduit updateSymbolKeyDisplay() web.
    /// </summary>
    private void PaintSymbolKey(IntPtr hdc, int kx, int ky, int kw, int kh,
        KeyDefinition keyDef, int pad)
    {
        bool shift = _mapper.ShiftDown;
        bool altGr = _mapper.AltGrDown;

        bool baseActive = !altGr && !shift;
        bool shiftActive = !altGr && shift;
        bool altGrActive = altGr && !shift;
        bool shiftAltGrActive = altGr && shift;

        // Quadrants bas (Base, AltGr) : centrés verticalement dans la moitié basse.
        DrawCharAt(hdc, kx + pad, ky + kh / 2, kx + kw / 2, ky + kh - pad,
            keyDef.Base, baseActive, IsDeadKeyRef(keyDef.Base), alignLeft: true, useMainFont: false);
        DrawCharAt(hdc, kx + kw / 2, ky + kh / 2, kx + kw - pad, ky + kh - pad,
            keyDef.AltGr, altGrActive, IsDeadKeyRef(keyDef.AltGr), alignLeft: false, useMainFont: false);
        // Quadrants hauts (Shift, Shift+AltGr) : alignés au top, rect étendu vers le bas pour
        // permettre des fontes hautes sans clipping.
        DrawCharAt(hdc, kx + pad, ky, kx + kw / 2, ky + kh / 2 + pad,
            keyDef.Shift, shiftActive, IsDeadKeyRef(keyDef.Shift), alignLeft: true, useMainFont: false, alignTop: true);
        DrawCharAt(hdc, kx + kw / 2, ky, kx + kw - pad, ky + kh / 2 + pad,
            keyDef.ShiftAltGr, shiftAltGrActive, IsDeadKeyRef(keyDef.ShiftAltGr), alignLeft: false, useMainFont: false, alignTop: true);
    }

    /// <summary>
    /// Subclass du bouton « Quitter les exercices » pour tracker hover (WM_MOUSEMOVE
    /// + WM_MOUSELEAVE via TrackMouseEvent). Met à jour _quitHovered et invalide le
    /// bouton pour redraw via WM_DRAWITEM (DrawQuitButton).
    /// </summary>
    private IntPtr QuitButtonSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEMOVE:
                if (!_quitHovered)
                {
                    _quitHovered = true;
                    Win32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    var tme = new Win32.TRACKMOUSEEVENT
                    {
                        cbSize = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(),
                        dwFlags = Win32.TME_LEAVE,
                        hwndTrack = hWnd,
                        dwHoverTime = 0
                    };
                    Win32.TrackMouseEvent(ref tme);
                }
                break;
            case Win32.WM_MOUSELEAVE:
                _quitHovered = false;
                Win32.InvalidateRect(hWnd, IntPtr.Zero, false);
                break;
        }
        return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Dessine un bouton owner-draw avec effet hover « rouge clair ». Utilisé pour
    /// les boutons « Quitter les exercices » et « Passer cet exercice ».
    /// Hover : fond rouge sombre, bordure rouge clair, texte blanc.
    /// Normal : fond gris foncé, texte gris CLR_BTN_QUIT_TEXT, bordure CLR_KEY_BORDER.
    /// </summary>
    private void DrawHoverButton(in Win32.DRAWITEMSTRUCT dis, bool hovered, string label)
    {
        var rc = dis.rcItem;
        uint bgColor = hovered ? 0x003838C0u : 0x002A2A2Au;       // BGR : rouge sombre / gris
        var hBrush = Win32.CreateSolidBrush(bgColor);
        Win32.FillRect(dis.hDC, ref rc, hBrush);
        Win32.DeleteObject(hBrush);

        uint borderColor = hovered ? 0x005050E0u : CLR_KEY_BORDER;
        var hPen = Win32.CreatePen(0, 1, borderColor);
        var hOldPen = Win32.SelectObject(dis.hDC, hPen);
        var hOldBrush = Win32.SelectObject(dis.hDC, Win32.GetStockObject(Win32.NULL_BRUSH));
        Win32.RoundRect(dis.hDC, rc.left, rc.top, rc.right - 1, rc.bottom - 1, 6, 6);
        Win32.SelectObject(dis.hDC, hOldPen);
        Win32.SelectObject(dis.hDC, hOldBrush);
        Win32.DeleteObject(hPen);

        Win32.SelectObject(dis.hDC, _hFontButton);
        Win32.SetBkMode(dis.hDC, Win32.TRANSPARENT);
        Win32.SetTextColor(dis.hDC, hovered ? 0x00FFFFFFu : CLR_BTN_QUIT_TEXT);
        Win32.DrawTextW(dis.hDC, label, label.Length, ref rc,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
    }

    /// <summary>Subclass du bouton « Passer cet exercice » — même comportement que QuitButtonSubclassProc.</summary>
    private IntPtr SkipButtonSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEMOVE:
                if (!_skipHovered)
                {
                    _skipHovered = true;
                    Win32.InvalidateRect(hWnd, IntPtr.Zero, false);
                    var tme = new Win32.TRACKMOUSEEVENT
                    {
                        cbSize = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(),
                        dwFlags = Win32.TME_LEAVE,
                        hwndTrack = hWnd,
                        dwHoverTime = 0
                    };
                    Win32.TrackMouseEvent(ref tme);
                }
                break;
            case Win32.WM_MOUSELEAVE:
                _skipHovered = false;
                Win32.InvalidateRect(hWnd, IntPtr.Zero, false);
                break;
        }
        return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Dessine un caractère dans une cellule. Couleur : bleu vif si actif, gris dimmed sinon ;
    /// rouge si touche morte active, gris sinon. Convertit les "dk_*" en symboles via GetDeadKeySymbol.
    /// Police : _hFontCharMain (gros) si useMainFont, sinon _hFontCharSmall (petit).
    /// alignTop = true → caractère collé au top de la cellule (pour quadrants hauts) ;
    /// false → centré verticalement (pour quadrants bas / caractère unique au centre).
    /// </summary>
    private void DrawCharAt(IntPtr hdc, int left, int top, int right, int bottom,
        string? chr, bool isActive, bool isDeadKey, bool alignLeft, bool useMainFont,
        bool alignTop = false)
    {
        var disp = GetDisplayChar(chr);
        if (string.IsNullOrEmpty(disp)) return;

        _tweaks.CharOverrides.TryGetValue(disp, out var ovr);

        uint color = (isDeadKey, isActive) switch
        {
            (true, true)   => CLR_DK_CHAR,           // Touche morte active : rouge clair
            (true, false)  => CLR_CHAR_DIM,           // Touche morte non active : gris dimmed
            (false, true)  => CLR_CHAR_ACTIVE_BLUE,   // Caractère actif : bleu vif
            (false, false) => CLR_CHAR_DIM            // Inactif : gris dimmed
        };

        // Cas special : ◌ + suffixe non-combinant (˙ ˝ ˘ / − ˇ ˛). On rend le ◌ et le suffixe
        // en 2 passes separees pour pouvoir les positionner / dimensionner independamment.
        // Pour les combinants purs (̣ ̏ ̛ ̉ ̑), le suffixe doit rester avec une base pour
        // etre rendu, donc on tombe sur le path standard plus bas.
        bool isSplitDottedCircle = disp.Length == 2 && disp[0] == '◌' && !IsCombiningMark(disp[1]);
        if (isSplitDottedCircle)
        {
            string circle = "◌";
            string suffix = disp[1].ToString();
            // Pass 1 : ◌
            int circleSizePt = (ovr?.CircleFontSize) ?? ovr?.FontSize ?? (useMainFont ? _tweaks.FontSizeMain : _tweaks.FontSizeSmall);
            string circleFontName = ovr?.Font ?? "Segoe UI";
            int circleOffX = ovr?.CircleOffsetX is int cox ? S(cox) : 0;
            int circleOffY = ovr?.CircleOffsetY is int coy ? S(coy) : 0;
            DrawSingleChar(hdc, circle, left, top, right, bottom,
                circleOffX, circleOffY, circleSizePt, circleFontName, color, alignLeft, alignTop);
            // Pass 2 : suffixe (˙ ˝ ˘ / − ˇ ˛)
            int suffixSizePt = ovr?.FontSize ?? (useMainFont ? _tweaks.FontSizeMain : _tweaks.FontSizeSmall);
            string suffixFontName = ovr?.Font ?? "Segoe UI";
            int suffixOffX = ovr?.OffsetX is int oox ? S(oox) : 0;
            int suffixOffY = ovr?.OffsetY is int ooy ? S(ooy) : 0;
            DrawSingleChar(hdc, suffix, left, top, right, bottom,
                suffixOffX, suffixOffY, suffixSizePt, suffixFontName, color, alignLeft, alignTop);
            return;
        }

        // Path standard : 1 passe avec disp en entier (cas combinants purs ou caractere unique).
        int sizePt2 = ovr?.FontSize ?? (useMainFont ? _tweaks.FontSizeMain : _tweaks.FontSizeSmall);
        string fontName2 = ovr?.Font ?? "Segoe UI";
        int offX = ovr?.OffsetX is int x ? S(x) : 0;
        int offY = ovr?.OffsetY is int y ? S(y) : 0;
        // Si pas d'override sur fonte/taille, on utilise les fontes preconstruites pour eviter
        // de remplir le cache inutilement.
        IntPtr hFont = (ovr?.FontSize.HasValue == true || ovr?.Font != null)
            ? GetOrCreateCharFont(sizePt2, fontName2)
            : (useMainFont ? _hFontCharMain : _hFontCharSmall);
        DrawSingleCharWithFont(hdc, disp, left, top, right, bottom, offX, offY, hFont, color, alignLeft, alignTop);
    }

    private void DrawSingleChar(IntPtr hdc, string text, int left, int top, int right, int bottom,
        int offsetX, int offsetY, int sizePt, string fontName, uint color, bool alignLeft, bool alignTop)
    {
        IntPtr hFont = GetOrCreateCharFont(sizePt, fontName);
        DrawSingleCharWithFont(hdc, text, left, top, right, bottom, offsetX, offsetY, hFont, color, alignLeft, alignTop);
    }

    private static void DrawSingleCharWithFont(IntPtr hdc, string text, int left, int top, int right, int bottom,
        int offsetX, int offsetY, IntPtr hFont, uint color, bool alignLeft, bool alignTop)
    {
        var hOldFont = Win32.SelectObject(hdc, hFont);
        Win32.SetTextColor(hdc, color);
        var r = new Win32.RECT
        {
            left = left + offsetX,
            top = top + offsetY,
            right = right + offsetX,
            bottom = bottom + offsetY
        };
        uint vAlign = alignTop ? 0u : Win32.DT_VCENTER;
        uint flags = vAlign | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_NOCLIP
            | (alignLeft ? Win32.DT_LEFT : Win32.DT_RIGHT);
        Win32.DrawTextW(hdc, text, text.Length, ref r, flags);
        Win32.SelectObject(hdc, hOldFont);
    }

    /// <summary>
    /// Convertit une référence de couche en caractère affichable. Pour les références de
    /// touche morte ("dk_*"), retourne le caractère obtenu en pressant espace après la touche
    /// morte (= entrée " " dans la table dk du layout). Cohérent avec tester/deadkeys.js
    /// fonction getDeadKeySymbol(). Fallback sur la table hardcodée TrayApplication si la dk
    /// n'est pas dans le layout pour une raison quelconque.
    /// </summary>
    /// <summary>
    /// Touches mortes dont le symbole isole est peu reconnaissable visuellement comme
    /// un diacritique : on les prefixe avec ◌ (dotted circle) pour signaler que c'est une dk.
    /// Pour ^ ¨ ´ ` ~ on ne prefixe pas (familiers en touche morte).
    /// </summary>
    private static readonly HashSet<string> DkAlwaysWithDottedCircle = new()
    {
        "dk_dot_above",
        "dk_double_acute",
        "dk_breve",
        "dk_stroke",
        "dk_horizontal_stroke",
        "dk_caron",
        "dk_ogonek",
    };

    /// <summary>
    /// Touches mortes peu utilisées masquées sur le clavier virtuel des exercices.
    /// L'exercice 6 réaffiche uniquement quelques aides utiles aux mots étrangers.
    /// </summary>
    private static readonly HashSet<string> HiddenDeadKeysInOnboarding = new()
    {
        "dk_misc_symbols",
        "dk_dot_above",
        "dk_dot_below",
        "dk_double_acute",
        "dk_double_grave",
        "dk_horn",
        "dk_hook",
        "dk_breve",
        "dk_inverted_breve",
        "dk_stroke",
        "dk_horizontal_stroke",
        "dk_macron",
        "dk_extended_latin",
        "dk_cedilla",
        "dk_comma",
        "dk_phonetic",
        "dk_ring_above",
        "dk_scientific",
        "dk_caron",
        "dk_ogonek",
        "dk_cyrillic",
    };

    private static readonly HashSet<string> LanguageExerciseDeadKeys = new()
    {
        "dk_stroke",
    };

    /// <summary>
    /// Caractères directs (non-dead-key) masqués sur le clavier virtuel des exercices 1 à 5,
    /// identifiés par (scancode, layer). Layers : 0=Base, 1=Shift, 2=AltGr, 3=ShiftAltGr,
    /// 4=Caps, 5=CapsShift, 6=CapsAltGr, 7=CapsShiftAltGr.
    /// </summary>
    private static readonly HashSet<(uint scancode, int layer)> HiddenSlotsInOnboarding = new()
    {
        (0x56, 2), // B00 AltGr → ≤
        (0x56, 3), // B00 ShiftAltGr → ≥
        (0x17, 2), // D08 AltGr → ^
        (0x26, 2), // C09 AltGr → `
        (0x32, 2), // B07 AltGr → <
        (0x32, 3), // B07 ShiftAltGr → ¿
        (0x33, 2), // B08 AltGr → >
        (0x34, 2), // B09 AltGr → #
        (0x35, 2), // B10 AltGr → ¡
        (0x05, 2), // E04 AltGr → ’
        (0x05, 3), // E04 ShiftAltGr → ‘ (guillemet apostrophe simple ouvrant)
        (0x07, 3), // E06 ShiftAltGr → soft hyphen U+00AD
        (0x0B, 2), // E10 AltGr → @ (l'arobase principal sur E00 reste visible)
        (0x2C, 3), // B01 ShiftAltGr → “ (guillemet double ouvrant)
        (0x2D, 3), // B02 ShiftAltGr → ” (guillemet double fermant)
    };

    private static readonly HashSet<(uint scancode, int layer)> LanguageExerciseVisibleSlots = new()
    {
        (0x32, 3), // B07 ShiftAltGr → ¿
        (0x35, 2), // B10 AltGr → ¡
    };

    private static string? FilterOnboardingSlot(uint scancode, int layer, string? value, bool languageExercise)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith("dk_")
            && HiddenDeadKeysInOnboarding.Contains(value)
            && !(languageExercise && LanguageExerciseDeadKeys.Contains(value)))
            return null;
        if (HiddenSlotsInOnboarding.Contains((scancode, layer))
            && !(languageExercise && LanguageExerciseVisibleSlots.Contains((scancode, layer))))
            return null;
        return value;
    }

    private static KeyDefinition FilterKeyForOnboarding(KeyDefinition kd, uint scancode, bool languageExercise)
    {
        return new KeyDefinition
        {
            Position = kd.Position,
            Scancode = kd.Scancode,
            Base = FilterOnboardingSlot(scancode, 0, kd.Base, languageExercise),
            Shift = FilterOnboardingSlot(scancode, 1, kd.Shift, languageExercise),
            AltGr = FilterOnboardingSlot(scancode, 2, kd.AltGr, languageExercise),
            ShiftAltGr = FilterOnboardingSlot(scancode, 3, kd.ShiftAltGr, languageExercise),
            Caps = FilterOnboardingSlot(scancode, 4, kd.Caps, languageExercise),
            CapsShift = FilterOnboardingSlot(scancode, 5, kd.CapsShift, languageExercise),
            CapsAltGr = FilterOnboardingSlot(scancode, 6, kd.CapsAltGr, languageExercise),
            CapsShiftAltGr = FilterOnboardingSlot(scancode, 7, kd.CapsShiftAltGr, languageExercise),
        };
    }

    private string? GetDisplayChar(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        string? result;
        bool isDk = value.StartsWith("dk_");
        if (isDk)
        {
            // Source de vérité : Table[" "] de la touche morte (= caractère isolé).
            // IsNullOrWhiteSpace : certaines dk ont table[" "] = " " (espace), il faut tomber sur le fallback.
            result = null;
            if (_layout != null && _layout.DeadKeys.TryGetValue(value, out var dk))
            {
                var isolated = dk.GetIsolated();
                if (!string.IsNullOrWhiteSpace(isolated)) result = isolated;
            }
            // Fallback hardcodé (cohérent avec tester/deadkeys.js DEAD_KEY_SYMBOLS)
            if (string.IsNullOrWhiteSpace(result))
                result = TrayApplication.GetDeadKeySymbol(value);
        }
        else
        {
            result = value;
        }

        // Caracteres combinants (point souscrit, double accent grave, corne, crochet,
        // breve inversee, etc.) : sans glyphe propre, ils s'effacent quand affiches isoles.
        // On les prefixe avec ◌ (DOTTED CIRCLE U+25CC) pour qu'ils soient lisibles.
        if (!string.IsNullOrEmpty(result) && result.Length == 1 && IsCombiningMark(result[0]))
            return "◌" + result;

        // Touches mortes specifiques (point en chef, breve, caron, ogonek, etc.) : prefixe ◌
        // pour signaler qu'il s'agit d'une touche morte, pas d'un caractere ordinaire.
        if (isDk && !string.IsNullOrEmpty(result) && DkAlwaysWithDottedCircle.Contains(value))
            return "◌" + result;

        return result;
    }

    private static bool IsCombiningMark(char c) =>
        (c >= '\u0300' && c <= '\u036F') ||  // Combining Diacritical Marks
        (c >= '\u1AB0' && c <= '\u1AFF') ||  // Combining Diacritical Marks Extended
        (c >= '\u1DC0' && c <= '\u1DFF') ||  // Combining Diacritical Marks Supplement
        (c >= '\u20D0' && c <= '\u20FF') ||  // Combining Diacritical Marks for Symbols
        (c >= '\uFE20' && c <= '\uFE2F');    // Combining Half Marks

    private bool IsModifierActive(in VirtualKeyboard.VisualKey vk)
    {
        if (!vk.IsContextual) return false;
        // Quand AltGr est tenu, Windows \u00e9met aussi un phantom LCtrl (convention AltGr=Ctrl+Alt) :
        // on exclut ce phantom pour ne pas marquer Ctrl/Alt comme actifs visuellement.
        bool altGr = _mapper.AltGrDown;
        return vk.Label switch
        {
            "Maj \u21e7" => _mapper.ShiftDown,
            "AltGr" => altGr,
            "Ctrl" => _mapper.CtrlDown && !altGr,
            "Alt" => _mapper.AltDown && !altGr,
            "Verr. Maj." => _mapper.CapsLockActive,
            _ => false
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Dispose
    // ═══════════════════════════════════════════════════════════════
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _mapper.StateChanged -= OnStateChanged;
        _hook.RawKeyDown -= OnRawKeyDown;

        if (_quitSubclassProc != null && _hWndBtnQuit != IntPtr.Zero)
            Win32.RemoveWindowSubclass(_hWndBtnQuit, _quitSubclassProc, (UIntPtr)1);
        if (_skipSubclassProc != null && _hWndBtnSkip != IntPtr.Zero)
            Win32.RemoveWindowSubclass(_hWndBtnSkip, _skipSubclassProc, (UIntPtr)2);

        if (_hTooltip != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hTooltip);
            _hTooltip = IntPtr.Zero;
        }

        // Detruire la fenetre AVANT de liberer fontes/brushes : les WM_DESTROY etc. doivent
        // encore pouvoir router vers _wndProcDelegate.
        if (_hWnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }

        // Desenregistrer la classe Win32 maintenant que plus aucune fenetre ne l'utilise.
        // Sans cet appel, RegisterClassExW lors d'une future instance est ignore (classe
        // deja enregistree), donc la classe garde le delegate WndProc et le hbrBackground
        // de la 1ere instance. Le delegate peut etre collecte par GC + le brush peut etre
        // libere au Dispose => crash de la 2e instance des CreateWindowExW.
        Win32.UnregisterClassW(WND_CLASS_NAME, Win32.GetModuleHandleW(null));

        DestroyFonts();
        Win32.DeleteObject(_hBgBrush);
        Win32.DeleteObject(_hKbBgBrush);
    }
}

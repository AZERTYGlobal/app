// Clavier virtuel — affiche la disposition AZERTY Global en temps réel
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AZERTYGlobalPortable;

/// <summary>
/// Fenêtre Win32 affichant le clavier AZERTY Global.
/// Phase 1 : couches base et shift. Réactif aux modificateurs.
/// </summary>
sealed class VirtualKeyboard : IDisposable
{
    // ── Window messages (spécifiques VirtualKeyboard) ─────────────
    private const uint WM_GETMINMAXINFO = 0x0024;
    private const uint WM_SIZING = 0x0214;
    private const uint WM_MOUSELEAVE = 0x02A3;

    // ── Colors (COLORREF = 0x00BBGGRR) ───────────────────────────
    private const uint CLR_BG = 0x00201C18;         // Fond fenêtre (gris très foncé)
    private const uint CLR_KEY = 0x00484038;         // Touche normale (gris foncé chaud)
    private const uint CLR_KEY_BORDER = 0x00302820;  // Bordure touche
    private const uint CLR_KEY_PRESSED = 0x00D4A060;  // Touche enfoncée (bleu clair)
    private const uint CLR_KEY_CTX = 0x00383028;     // Touche contextuelle (plus foncé)
    private const uint CLR_LABEL = 0x0080D0F0;        // Label petit en bas (jaune)
    private const uint CLR_CHAR = 0x00F0EDE8;         // Caractère principal (blanc cassé)
    private const uint CLR_DK_CHAR = 0x000080FF;      // Caractère touche morte (orange)
    private const uint CLR_CAPS_BAR = 0x0000A5FF;     // Orange pour Caps Lock
    private const uint CLR_CTX_TEXT = 0x00B0A898;      // Texte touches contextuelles

    // ── Highlight search (COLORREF = 0x00BBGGRR) ────────────────
    private const uint CLR_HL_DIRECT = 0x0064C800;      // Vert (méthode directe)
    private const uint CLR_HL_DIRECT_BG = 0x00284018;   // Fond vert discret
    private const uint CLR_HL_DK = 0x003232DC;           // Rouge (activation touche morte)
    private const uint CLR_HL_DK_BG = 0x00282040;        // Fond rouge discret
    private const uint CLR_HL_STEP1 = 0x0000A5FF;        // Orange (étape 1)
    private const uint CLR_HL_STEP1_BG = 0x00283020;     // Fond orange discret
    private const uint CLR_HL_STEP2 = 0x004CB050;        // Vert (étape 2)
    private const uint CLR_HL_STEP2_BG = 0x00203818;     // Fond vert discret

    // ── Mapping Web API key code → scancode ─────────────────────
    private static readonly Dictionary<string, uint> KeyCodeToScancode = new()
    {
        ["Backquote"] = 0x29,
        ["Digit1"] = 0x02, ["Digit2"] = 0x03, ["Digit3"] = 0x04, ["Digit4"] = 0x05,
        ["Digit5"] = 0x06, ["Digit6"] = 0x07, ["Digit7"] = 0x08, ["Digit8"] = 0x09,
        ["Digit9"] = 0x0A, ["Digit0"] = 0x0B, ["Minus"] = 0x0C, ["Equal"] = 0x0D,
        ["KeyQ"] = 0x10, ["KeyW"] = 0x11, ["KeyE"] = 0x12, ["KeyR"] = 0x13,
        ["KeyT"] = 0x14, ["KeyY"] = 0x15, ["KeyU"] = 0x16, ["KeyI"] = 0x17,
        ["KeyO"] = 0x18, ["KeyP"] = 0x19, ["BracketLeft"] = 0x1A, ["BracketRight"] = 0x1B,
        ["KeyA"] = 0x1E, ["KeyS"] = 0x1F, ["KeyD"] = 0x20, ["KeyF"] = 0x21,
        ["KeyG"] = 0x22, ["KeyH"] = 0x23, ["KeyJ"] = 0x24, ["KeyK"] = 0x25,
        ["KeyL"] = 0x26, ["Semicolon"] = 0x27, ["Quote"] = 0x28, ["Backslash"] = 0x2B,
        ["IntlBackslash"] = 0x56,
        ["KeyZ"] = 0x2C, ["KeyX"] = 0x2D, ["KeyC"] = 0x2E, ["KeyV"] = 0x2F,
        ["KeyB"] = 0x30, ["KeyN"] = 0x31, ["KeyM"] = 0x32,
        ["Comma"] = 0x33, ["Period"] = 0x34, ["Slash"] = 0x35,
        ["Space"] = 0x39,
    };

    // ═══════════════════════════════════════════════════════════════
    // Structures spécifiques (tooltip)
    // ═══════════════════════════════════════════════════════════════
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

    // ═══════════════════════════════════════════════════════════════
    // Données de géométrie du clavier ISO
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Représente une touche visuelle sur le clavier.</summary>
    private record struct VisualKey(
        float X, float Y, float W, float H,   // Position/taille en unités (1u = largeur touche standard)
        uint Scancode,                          // 0 = touche contextuelle (non remappée)
        string Label,                           // Label fixe en bas (repère AZERTY)
        bool IsContextual                       // Tab, Shift, Ctrl, etc.
    );

    // Dimensions de référence (en unités de touche)
    private const float KEY_H = 1f;
    private const float KEY_GAP = 0.1f;
    private const float ROW_GAP = 0.1f;

    // Noms français des touches mortes
    private static readonly Dictionary<string, string> _deadKeyNamesFr = new()
    {
        ["dk_circumflex"] = "Accent circonflexe",
        ["dk_diaeresis"] = "Tréma",
        ["dk_acute"] = "Accent aigu",
        ["dk_grave"] = "Accent grave",
        ["dk_tilde"] = "Tilde",
        ["dk_dot_above"] = "Point en chef",
        ["dk_dot_below"] = "Point souscrit",
        ["dk_double_acute"] = "Double accent aigu",
        ["dk_double_grave"] = "Double accent grave",
        ["dk_horn"] = "Cornet",
        ["dk_hook"] = "Crochet en chef",
        ["dk_caron"] = "Háček",
        ["dk_ogonek"] = "Ogonek",
        ["dk_breve"] = "Brève",
        ["dk_inverted_breve"] = "Brève inversée",
        ["dk_stroke"] = "Barre oblique",
        ["dk_horizontal_stroke"] = "Barre horizontale",
        ["dk_macron"] = "Macron",
        ["dk_extended_latin"] = "Latin étendu",
        ["dk_cedilla"] = "Cédille",
        ["dk_comma"] = "Virgule souscrite",
        ["dk_phonetic"] = "Phonétique",
        ["dk_ring_above"] = "Rond en chef",
        ["dk_greek"] = "Grec",
        ["dk_cyrillic"] = "Cyrillique",
        ["dk_misc_symbols"] = "Symboles divers",
        ["dk_scientific"] = "Scientifique",
        ["dk_currencies"] = "Monnaies",
        ["dk_punctuation"] = "Ponctuation",
    };

    /// <summary>Retourne le nom français d'une touche morte.</summary>
    private static string GetDeadKeyFrenchName(string deadKeyId)
    {
        return _deadKeyNamesFr.TryGetValue(deadKeyId, out var name) ? name.ToUpperInvariant() : deadKeyId;
    }

    // Taille de la fenêtre par défaut
    private const int DEFAULT_WIDTH = 720;
    private const int DEFAULT_HEIGHT = 280;
    private const int MIN_WIDTH = 480;
    private const int MIN_HEIGHT = 187; // MIN_WIDTH / ASPECT_RATIO
    private const float ASPECT_RATIO = 720f / 280f; // ~2.57

    private static readonly VisualKey[] _visualKeys = BuildKeyLayout();

    private static VisualKey[] BuildKeyLayout()
    {
        var keys = new List<VisualKey>();
        float y = 0;

        // ── Rangée 1 : Chiffres (13 touches + Retour arrière) ──
        float x = 0;
        string[] row1Labels = { "²", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", ")", "=" };
        uint[] row1Scans = { 0x29, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D };
        for (int i = 0; i < 13; i++)
        {
            keys.Add(new VisualKey(x, y, 1f, KEY_H, row1Scans[i], row1Labels[i], false));
            x += 1f + KEY_GAP;
        }
        keys.Add(new VisualKey(x, y, 2f, KEY_H, 0x0E, "⌫", true)); // Backspace
        y += KEY_H + ROW_GAP;

        // ── Rangée 2 : AZER (Tab + 12 touches + Enter partiel) ──
        x = 0;
        keys.Add(new VisualKey(x, y, 1.5f, KEY_H, 0x0F, "Tab", true));
        x = 1.5f + KEY_GAP;
        string[] row2Labels = { "A", "Z", "E", "R", "T", "Y", "U", "I", "O", "P", "^", "$" };
        uint[] row2Scans = { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B };
        for (int i = 0; i < 12; i++)
        {
            keys.Add(new VisualKey(x, y, 1f, KEY_H, row2Scans[i], row2Labels[i], false));
            x += 1f + KEY_GAP;
        }
        // Entrée ISO (partie haute) — touche verticale sur 2 rangées
        keys.Add(new VisualKey(x, y, 1.5f, KEY_H * 2 + ROW_GAP, 0x1C, "Entrée", true));
        y += KEY_H + ROW_GAP;

        // ── Rangée 3 : QSDF (Verr.Maj + 12 touches) ──
        x = 0;
        keys.Add(new VisualKey(x, y, 1.75f, KEY_H, 0x3A, "Verr.Maj", true));
        x = 1.75f + KEY_GAP;
        string[] row3Labels = { "Q", "S", "D", "F", "G", "H", "J", "K", "L", "M", "ù", "*" };
        uint[] row3Scans = { 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2B };
        for (int i = 0; i < 12; i++)
        {
            keys.Add(new VisualKey(x, y, 1f, KEY_H, row3Scans[i], row3Labels[i], false));
            x += 1f + KEY_GAP;
        }
        // L'Entrée ISO est déjà dessinée (s'étend sur cette rangée)
        y += KEY_H + ROW_GAP;

        // ── Rangée 4 : WXCV (Shift G + B00 + 10 touches + Shift D) ──
        x = 0;
        keys.Add(new VisualKey(x, y, 1.25f, KEY_H, 0, "Maj ⇧", true)); // Left Shift
        x = 1.25f + KEY_GAP;
        string[] row4Labels = { "<", "W", "X", "C", "V", "B", "N", ",", ".", ":", "!" };
        uint[] row4Scans = { 0x56, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35 };
        for (int i = 0; i < 11; i++)
        {
            keys.Add(new VisualKey(x, y, 1f, KEY_H, row4Scans[i], row4Labels[i], false));
            x += 1f + KEY_GAP;
        }
        keys.Add(new VisualKey(x, y, 2.85f, KEY_H, 0, "Maj ⇧", true)); // Right Shift
        y += KEY_H + ROW_GAP;

        // ── Rangée 5 : Espace (Ctrl, Win, Alt, Espace, AltGr, Menu, Ctrl) ──
        x = 0;
        keys.Add(new VisualKey(x, y, 1.25f, KEY_H, 0x1D, "Ctrl", true));   // LCtrl
        x += 1.25f + KEY_GAP;
        keys.Add(new VisualKey(x, y, 1.25f, KEY_H, 0x5B, "Win", true));   // LWin (scancode 0x5B)
        x += 1.25f + KEY_GAP;
        keys.Add(new VisualKey(x, y, 1.25f, KEY_H, 0, "Alt", true));      // LAlt (état maintenu via _altDown)
        x += 1.25f + KEY_GAP;
        keys.Add(new VisualKey(x, y, 6.25f, KEY_H, 0x39, "Espace", false)); // Space
        x += 6.25f + KEY_GAP;
        keys.Add(new VisualKey(x, y, 1.25f, KEY_H, 0, "AltGr", true));     // RAlt (état maintenu via _altGrDown)
        x += 1.25f + KEY_GAP;
        keys.Add(new VisualKey(x, y, 1.25f, KEY_H, 0x5C, "Win", true));  // RWin (scancode 0x5C)
        x += 1.25f + KEY_GAP;
        keys.Add(new VisualKey(x, y, 1.25f, KEY_H, 0x5D, "Menu", true));  // Menu (scancode 0x5D)
        x += 1.25f + KEY_GAP;
        keys.Add(new VisualKey(x, y, 1.85f, KEY_H, 0x1D, "Ctrl", true));  // RCtrl (aligné bord droit)

        return keys.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════
    // Champs d'instance
    // ═══════════════════════════════════════════════════════════════
    private IntPtr _hWnd;
    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly Layout _layout;
    private readonly Dictionary<string, string> _charNames; // char → nom français

    private bool _visible;
    private bool _trackingMouse;
    private int _hoveredKeyIndex = -1;

    // État des modificateurs (mis à jour par TrayApplication depuis KeyMapper)
    private bool _shiftDown;
    private bool _altGrDown;
    private bool _ctrlDown;
    private bool _altDown;
    private bool _capsLockActive;
    private string? _activeDeadKey;

    // Animation d'appui sur touche
    private static readonly UIntPtr TIMER_KEYPRESS = (UIntPtr)1;
    private const uint KEYPRESS_DURATION_MS = 120;
    private uint _pressedScancode; // 0 = aucune touche pressée

    // Highlight de recherche (clavier virtuel)
    private static readonly UIntPtr TIMER_HIGHLIGHT_STEP2 = (UIntPtr)2;
    private static readonly UIntPtr TIMER_HIGHLIGHT_CLEAR = (UIntPtr)3;
    private readonly HashSet<uint> _highlightedScancodes = new();
    private readonly HashSet<string> _highlightedLabels = new();
    private string _highlightType = ""; // "direct", "dk", "step1", "step2"
    private CharacterSearch.MethodData? _pendingStep2; // Données pour l'étape 2 d'une séquence DK

    // Polices cachées (recréées sur WM_SIZE)
    private IntPtr _hCharFont;
    private IntPtr _hLabelFont;
    private IntPtr _hCtxFont;
    private int _cachedCw; // Largeur client quand les polices ont été créées
    private int _cachedCh;

    // Tooltip
    private IntPtr _hTooltip;

    public bool IsVisible => _visible;

    /// <summary>Crée ou recrée les polices selon la taille client actuelle.</summary>
    private void EnsureFonts(int cw, int ch)
    {
        if (cw == _cachedCw && ch == _cachedCh && _hCharFont != IntPtr.Zero)
            return;

        // Libérer les anciennes polices
        if (_hCharFont != IntPtr.Zero) Win32.DeleteObject(_hCharFont);
        if (_hLabelFont != IntPtr.Zero) Win32.DeleteObject(_hLabelFont);
        if (_hCtxFont != IntPtr.Zero) Win32.DeleteObject(_hCtxFont);

        float totalKeyW = 16.3f;
        float totalKeyH = 5 * KEY_H + 4 * ROW_GAP;
        int margin = 10;
        float scaleX = (cw - 2 * margin) / totalKeyW;
        float scaleY = (ch - 2 * margin) / totalKeyH;
        float scale = Math.Min(scaleX, scaleY);

        int charFontSize = Math.Max(14, (int)(scale * 0.72f));
        int labelFontSize = Math.Max(9, (int)(scale * 0.30f));
        int ctxFontSize = Math.Max(10, (int)(scale * 0.35f));

        _hCharFont = Win32.CreateFontW(charFontSize, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hLabelFont = Win32.CreateFontW(labelFontSize, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hCtxFont = Win32.CreateFontW(ctxFontSize, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");

        _cachedCw = cw;
        _cachedCh = ch;
    }

    public VirtualKeyboard(Layout layout, Dictionary<string, string>? charNames = null)
    {
        _layout = layout;
        _charNames = charNames ?? LoadCharacterNames();
        _wndProcDelegate = WndProc;

        var hInstance = Win32.GetModuleHandleW(null);
        var className = "AZERTYGlobal_VK";

        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            hCursor = Win32.LoadCursorW(IntPtr.Zero, (IntPtr)32512), // IDC_ARROW
            lpszClassName = className,
            style = 0x0020 // CS_OWNDC
        };
        Win32.RegisterClassExW(ref wc);

        // Calculer la taille de fenêtre pour obtenir la zone client souhaitée
        uint dwStyle = Win32.WS_POPUP | Win32.WS_THICKFRAME | Win32.WS_CAPTION | Win32.WS_SYSMENU;
        uint dwExStyle = Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        var adjustRect = new Win32.RECT { left = 0, top = 0, right = DEFAULT_WIDTH, bottom = DEFAULT_HEIGHT };
        Win32.AdjustWindowRectEx(ref adjustRect, dwStyle, false, dwExStyle);
        int windowW = adjustRect.right - adjustRect.left;
        int windowH = adjustRect.bottom - adjustRect.top;

        // Positionner en bas à droite de l'écran contenant le curseur
        int posX = 100, posY = 100;
        if (Win32.GetCursorPos(out var cursorPt))
        {
            const uint MONITOR_DEFAULTTONEAREST = 2;
            var hMon = Win32.MonitorFromPoint(cursorPt, MONITOR_DEFAULTTONEAREST);
            var mi = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
            if (Win32.GetMonitorInfo(hMon, ref mi))
            {
                posX = mi.rcWork.right - windowW - 10;
                posY = mi.rcWork.bottom - windowH - 10;
            }
        }

        _hWnd = Win32.CreateWindowExW(
            dwExStyle,
            className,
            "AZERTY Global",
            dwStyle,
            posX, posY, windowW, windowH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        CreateTooltip();
    }

    // ═══════════════════════════════════════════════════════════════
    // Tooltip Win32
    // ═══════════════════════════════════════════════════════════════
    private const uint CW_USEDEFAULT = 0x80000000;
    private const uint TTS_ALWAYSTIP = 0x01;
    private const uint TTS_NOPREFIX = 0x02;
    private const uint TTF_SUBCLASS = 0x0010;
    private const uint TTF_TRANSPARENT = 0x0100;
    private const uint TTM_ADDTOOLW = 0x0432;
    private const uint TTM_DELTOOLW = 0x0433;
    private const uint TTM_UPDATETIPTEXTW = 0x0439;
    private const uint TTM_TRACKACTIVATE = 0x0411;
    private const uint TTM_TRACKPOSITION = 0x0412;
    private const uint TTM_SETMAXTIPWIDTH = 0x0418;
    private const uint TTM_SETDELAYTIME = 0x0403;
    private const uint TTDT_INITIAL = 3;    // Délai avant apparition
    private const uint TTDT_RESHOW = 1;     // Délai pour réapparition (passage d'une touche à l'autre)

    private void CreateTooltip()
    {
        _hTooltip = Win32.CreateWindowExW(
            Win32.WS_EX_TOPMOST,
            "tooltips_class32",
            "",
            Win32.WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX,
            0, 0, 0, 0,
            _hWnd, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);

        // Permettre les tooltips multi-lignes
        Win32.SendMessageW(_hTooltip, TTM_SETMAXTIPWIDTH, IntPtr.Zero, (IntPtr)400);

        // Réduire les délais pour un affichage quasi-instantané
        Win32.SendMessageW(_hTooltip, TTM_SETDELAYTIME, (IntPtr)TTDT_INITIAL, (IntPtr)100);  // 100ms avant apparition
        Win32.SendMessageW(_hTooltip, TTM_SETDELAYTIME, (IntPtr)TTDT_RESHOW, (IntPtr)0);     // Immédiat entre touches

        // Ajouter un outil unique couvrant toute la fenêtre (on le repositionne dynamiquement)
        var ti = new TOOLINFOW
        {
            cbSize = (uint)Marshal.SizeOf<TOOLINFOW>(),
            uFlags = TTF_SUBCLASS | TTF_TRANSPARENT,
            hwnd = _hWnd,
            uId = (UIntPtr)1,
            rect = new Win32.RECT { left = 0, top = 0, right = DEFAULT_WIDTH, bottom = DEFAULT_HEIGHT },
            lpszText = ""
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TOOLINFOW>());
        Marshal.StructureToPtr(ti, ptr, false);
        Win32.SendMessageW(_hTooltip, TTM_ADDTOOLW, IntPtr.Zero, ptr);
        Marshal.FreeHGlobal(ptr);
    }

    private void UpdateTooltipText(string text)
    {
        var ti = new TOOLINFOW
        {
            cbSize = (uint)Marshal.SizeOf<TOOLINFOW>(),
            hwnd = _hWnd,
            uId = (UIntPtr)1,
            lpszText = text
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TOOLINFOW>());
        Marshal.StructureToPtr(ti, ptr, false);
        Win32.SendMessageW(_hTooltip, TTM_UPDATETIPTEXTW, IntPtr.Zero, ptr);
        Marshal.FreeHGlobal(ptr);
    }

    // ═══════════════════════════════════════════════════════════════
    // Chargement des noms de caractères
    // ═══════════════════════════════════════════════════════════════
    private static Dictionary<string, string> LoadCharacterNames()
    {
        var names = new Dictionary<string, string>();

        try
        {
            using var stream = typeof(VirtualKeyboard).Assembly.GetManifestResourceStream("character-index.json");
            if (stream == null) return names;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("characters", out var chars))
            {
                foreach (var entry in chars.EnumerateObject())
                {
                    if (entry.Value.TryGetProperty("unicodeNameFr", out var nameFr))
                    {
                        var frName = nameFr.GetString();
                        if (frName != null)
                            names[entry.Name] = frName;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Pas critique — on continue sans tooltips
        }

        return names;
    }

    // ═══════════════════════════════════════════════════════════════
    // Affichage / masquage
    // ═══════════════════════════════════════════════════════════════
    public void Toggle()
    {
        if (_visible) Hide();
        else Show();
    }

    public void Show()
    {
        Win32.ShowWindow(_hWnd, 8); // SW_SHOWNA (ne prend pas le focus)
        _visible = true;
        Invalidate();
    }

    public void Hide()
    {
        Win32.ShowWindow(_hWnd, 0); // SW_HIDE
        _visible = false;
    }

    /// <summary>Met à jour l'état et redessine si changement.</summary>
    public void UpdateState(bool shift, bool altGr, bool ctrl, bool alt, bool capsLock, string? deadKey)
    {
        if (_shiftDown == shift && _altGrDown == altGr && _ctrlDown == ctrl &&
            _altDown == alt && _capsLockActive == capsLock && _activeDeadKey == deadKey)
            return;

        _shiftDown = shift;
        _altGrDown = altGr;
        _ctrlDown = ctrl;
        _altDown = alt;
        _capsLockActive = capsLock;
        _activeDeadKey = deadKey;

        if (_visible)
            Invalidate();
    }

    /// <summary>Met en surbrillance les touches correspondant à une méthode de saisie.</summary>
    public void HighlightMethod(CharacterSearch.MethodData? method)
    {
        // Annuler les timers de highlight en cours
        Win32.KillTimer(_hWnd, TIMER_HIGHLIGHT_STEP2);
        Win32.KillTimer(_hWnd, TIMER_HIGHLIGHT_CLEAR);
        ClearHighlight();

        if (method == null || !_visible || string.IsNullOrEmpty(method.Key))
            return;

        if (method.Type == "direct" || method.Type == "deadkey_activation")
        {
            // Méthode directe ou activation de touche morte
            _highlightType = method.Type == "deadkey_activation" ? "dk" : "direct";
            AddKeyHighlight(method.Key, method.Layer);
            Invalidate();
        }
        else if (method.Type == "deadkey" && !string.IsNullOrEmpty(method.DkActivationKey))
        {
            // Séquence touche morte en 2 étapes
            // Étape 1 : activer la touche morte
            _highlightType = "step1";
            AddKeyHighlight(method.DkActivationKey, method.DkActivationLayer);
            _pendingStep2 = method;
            Invalidate();

            // Étape 2 après 2 secondes
            Win32.SetTimer(_hWnd, TIMER_HIGHLIGHT_STEP2, 2000, IntPtr.Zero);
        }
    }

    private void ClearHighlight()
    {
        bool wasHighlighted = _highlightedScancodes.Count > 0 || _highlightedLabels.Count > 0;
        _highlightedScancodes.Clear();
        _highlightedLabels.Clear();
        _highlightType = "";
        _pendingStep2 = null;
        if (wasHighlighted && _visible) Invalidate();
    }

    private void AddKeyHighlight(string keyCode, string layer)
    {
        // Ajouter la touche principale
        if (KeyCodeToScancode.TryGetValue(keyCode, out var scancode))
            _highlightedScancodes.Add(scancode);

        // Ajouter les modificateurs selon le layer
        switch (layer)
        {
            case "Shift":
                _highlightedLabels.Add("Maj ⇧");
                break;
            case "Caps":
                _highlightedLabels.Add("Verr.Maj");
                break;
            case "Caps+Shift":
                _highlightedLabels.Add("Verr.Maj");
                _highlightedLabels.Add("Maj ⇧");
                break;
            case "AltGr":
                _highlightedLabels.Add("AltGr");
                break;
            case "Shift+AltGr":
            case "AltGr+Shift":
                _highlightedLabels.Add("AltGr");
                _highlightedLabels.Add("Maj ⇧");
                break;
            case "Caps+AltGr":
                _highlightedLabels.Add("Verr.Maj");
                _highlightedLabels.Add("AltGr");
                break;
            case "Caps+Shift+AltGr":
            case "Caps+AltGr+Shift":
                _highlightedLabels.Add("Verr.Maj");
                _highlightedLabels.Add("AltGr");
                _highlightedLabels.Add("Maj ⇧");
                break;
        }
    }

    /// <summary>Vérifie si une touche doit être mise en surbrillance par la recherche.</summary>
    private bool IsKeyHighlighted(in VisualKey vk)
    {
        if (_highlightedScancodes.Count == 0 && _highlightedLabels.Count == 0)
            return false;
        if (vk.Scancode != 0 && _highlightedScancodes.Contains(vk.Scancode))
            return true;
        if (vk.IsContextual && _highlightedLabels.Contains(vk.Label))
            return true;
        return false;
    }

    /// <summary>Retourne les couleurs de bordure et fond pour le highlight actuel.</summary>
    private (uint border, uint bg) GetHighlightColors()
    {
        return _highlightType switch
        {
            "direct" => (CLR_HL_DIRECT, CLR_HL_DIRECT_BG),
            "dk" => (CLR_HL_DK, CLR_HL_DK_BG),
            "step1" => (CLR_HL_STEP1, CLR_HL_STEP1_BG),
            "step2" => (CLR_HL_STEP2, CLR_HL_STEP2_BG),
            _ => (CLR_HL_DIRECT, CLR_HL_DIRECT_BG),
        };
    }

    /// <summary>Notifie qu'une touche a été pressée (pour animation visuelle).</summary>
    public void NotifyKeyPress(uint scancode)
    {
        if (!_visible) return;
        _pressedScancode = scancode;
        Invalidate();
        // Timer pour effacer le highlight après KEYPRESS_DURATION_MS
        Win32.SetTimer(_hWnd, TIMER_KEYPRESS, KEYPRESS_DURATION_MS, IntPtr.Zero);
    }

    private void Invalidate()
    {
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    // ═══════════════════════════════════════════════════════════════
    // WndProc
    // ═══════════════════════════════════════════════════════════════
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_PAINT:
                OnPaint(hWnd);
                return IntPtr.Zero;

            case Win32.WM_ERASEBKGND:
                return (IntPtr)1; // Pas d'effacement — on peint tout dans WM_PAINT

            case Win32.WM_SIZE:
                Invalidate();
                return IntPtr.Zero;

            case Win32.WM_MOUSEMOVE:
                OnMouseMove(lParam);
                return IntPtr.Zero;

            case WM_MOUSELEAVE:
                _trackingMouse = false;
                if (_hoveredKeyIndex != -1)
                {
                    _hoveredKeyIndex = -1;
                    UpdateTooltipText("");
                }
                return IntPtr.Zero;

            case WM_SIZING:
                OnSizing(wParam, lParam);
                return (IntPtr)1;

            case Win32.WM_TIMER:
                var timerId = (UIntPtr)(ulong)wParam.ToInt64();
                if (timerId == TIMER_KEYPRESS)
                {
                    Win32.KillTimer(hWnd, TIMER_KEYPRESS);
                    _pressedScancode = 0;
                    Invalidate();
                }
                else if (timerId == TIMER_HIGHLIGHT_STEP2)
                {
                    Win32.KillTimer(hWnd, TIMER_HIGHLIGHT_STEP2);
                    // Passer à l'étape 2
                    if (_pendingStep2 != null)
                    {
                        var step2 = _pendingStep2;
                        _highlightedScancodes.Clear();
                        _highlightedLabels.Clear();
                        _highlightType = "step2";
                        AddKeyHighlight(step2.Key, step2.Layer);
                        _pendingStep2 = null;
                        Invalidate();
                        // Auto-clear après 2.5 secondes
                        Win32.SetTimer(hWnd, TIMER_HIGHLIGHT_CLEAR, 2500, IntPtr.Zero);
                    }
                }
                else if (timerId == TIMER_HIGHLIGHT_CLEAR)
                {
                    Win32.KillTimer(hWnd, TIMER_HIGHLIGHT_CLEAR);
                    ClearHighlight();
                }
                return IntPtr.Zero;

            case Win32.WM_CLOSE:
                Hide();
                return IntPtr.Zero; // Ne pas détruire, juste masquer

            case WM_GETMINMAXINFO:
                if (lParam != IntPtr.Zero)
                {
                    // Convertir les tailles client min en tailles fenêtre
                    var minRect = new Win32.RECT { left = 0, top = 0, right = MIN_WIDTH, bottom = MIN_HEIGHT };
                    Win32.AdjustWindowRectEx(ref minRect, Win32.WS_POPUP | Win32.WS_THICKFRAME | Win32.WS_CAPTION | Win32.WS_SYSMENU,
                        false, Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
                    var mmi = Marshal.PtrToStructure<Win32.MINMAXINFO>(lParam);
                    mmi.ptMinTrackSize = new Win32.POINT { x = minRect.right - minRect.left, y = minRect.bottom - minRect.top };
                    Marshal.StructureToPtr(mmi, lParam, false);
                }
                return IntPtr.Zero;
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>Contraint le resize pour garder le ratio d'aspect.</summary>
    private void OnSizing(IntPtr wParam, IntPtr lParam)
    {
        // wParam indique quel bord est déplacé (1=left, 2=right, 3=top, 6=bottom, etc.)
        var rc = Marshal.PtrToStructure<Win32.RECT>(lParam);
        int w = rc.right - rc.left;
        int h = rc.bottom - rc.top;
        int edge = wParam.ToInt32();

        // Calculer la bordure de la fenêtre (différence entre window rect et client rect)
        Win32.GetWindowRect(_hWnd, out var winRect);
        Win32.GetClientRect(_hWnd, out var cliRect);
        int borderW = (winRect.right - winRect.left) - (cliRect.right - cliRect.left);
        int borderH = (winRect.bottom - winRect.top) - (cliRect.bottom - cliRect.top);

        int clientW = w - borderW;
        int clientH = h - borderH;

        // WMSZ : 1=Left, 2=Right, 3=Top, 4=TopLeft, 5=TopRight, 6=Bottom, 7=BottomLeft, 8=BottomRight
        // Coins et bords horizontaux → largeur prioritaire (ajuster hauteur)
        // Bords purement verticaux (3=Top, 6=Bottom) → hauteur prioritaire (ajuster largeur)
        if (edge == 3 || edge == 6)
        {
            // Hauteur change → ajuster largeur
            clientW = (int)(clientH * ASPECT_RATIO);
        }
        else
        {
            // Largeur change → ajuster hauteur
            clientH = (int)(clientW / ASPECT_RATIO);
        }

        int newW = clientW + borderW;
        int newH = clientH + borderH;

        // Appliquer les contraintes selon les bords déplacés
        // Horizontal : bords gauches (1=Left, 4=TopLeft, 7=BottomLeft) ancrent à droite
        if (edge == 1 || edge == 4 || edge == 7)
            rc.left = rc.right - newW;
        else
            rc.right = rc.left + newW;

        // Vertical : bords hauts (3=Top, 4=TopLeft, 5=TopRight) ancrent en bas
        if (edge == 3 || edge == 4 || edge == 5)
            rc.top = rc.bottom - newH;
        else
            rc.bottom = rc.top + newH;

        Marshal.StructureToPtr(rc, lParam, false);
    }

    // ═══════════════════════════════════════════════════════════════
    // Rendu GDI (double buffering)
    // ═══════════════════════════════════════════════════════════════
    private void OnPaint(IntPtr hWnd)
    {
        var ps = new Win32.PAINTSTRUCT();
        var hdcPaint = Win32.BeginPaint(hWnd, out ps);
        Win32.GetClientRect(hWnd, out var clientRect);

        int cw = clientRect.right - clientRect.left;
        int ch = clientRect.bottom - clientRect.top;
        if (cw <= 0 || ch <= 0) { Win32.EndPaint(hWnd, ref ps); return; }

        // Double buffering
        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        var hdc = Win32.CreateCompatibleDC(hdcScreen);
        var hBmp = Win32.CreateCompatibleBitmap(hdcScreen, cw, ch);
        var hBmpOld = Win32.SelectObject(hdc, hBmp);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);

        try
        {
            PaintContent(hdc, ref clientRect, cw, ch);
        }
        catch (Exception ex)
        {
            // Ne pas crasher — log et afficher un fond noir
            var logPath = Path.Combine(AppContext.BaseDirectory, "error.log");
            try { File.AppendAllText(logPath, $"[{DateTime.Now:s}] OnPaint: {ex}\n"); } catch { }
        }

        // Copier le buffer sur l'écran
        Win32.BitBlt(hdcPaint, 0, 0, cw, ch, hdc, 0, 0, Win32.SRCCOPY);

        // Nettoyage double buffering
        Win32.SelectObject(hdc, hBmpOld);
        Win32.DeleteObject(hBmp);
        Win32.DeleteDC(hdc);

        Win32.EndPaint(hWnd, ref ps);
    }

    /// <summary>Dessine le contenu du clavier dans le DC mémoire.</summary>
    private void PaintContent(IntPtr hdc, ref Win32.RECT clientRect, int cw, int ch)
    {
        // Fond
        var hBgBrush = Win32.CreateSolidBrush(CLR_BG);
        Win32.FillRect(hdc, ref clientRect, hBgBrush);
        Win32.DeleteObject(hBgBrush);

        // Calculer les marges et l'échelle
        float totalKeyW = 16.3f; // Largeur max d'une rangée en unités (rangée 1 : 13×1 + 1×2 + gaps)
        float totalKeyH = 5 * KEY_H + 4 * ROW_GAP;
        int margin = 10;
        float scaleX = (cw - 2 * margin) / totalKeyW;
        float scaleY = (ch - 2 * margin) / totalKeyH;
        float scale = Math.Min(scaleX, scaleY);

        // Réserver de l'espace en bas pour l'indication de touche morte
        int bottomReserve = Math.Max(20, (int)(scale * 0.55f));

        // Centrer le clavier dans l'espace restant (hors réserve basse)
        float kbWidth = totalKeyW * scale;
        float kbHeight = totalKeyH * scale;
        int offsetX = margin + (int)((cw - 2 * margin - kbWidth) / 2);
        int offsetY = margin + (int)((ch - 2 * margin - bottomReserve - kbHeight) / 2);

        // Polices cachées (recréées si la taille a changé)
        EnsureFonts(cw, ch);
        var hCharFont = _hCharFont;
        var hLabelFont = _hLabelFont;
        var hCtxFont = _hCtxFont;
        int labelFontSize = Math.Max(9, (int)(scale * 0.30f));

        // Pré-créer les brushes et pen partagés (évite ~130 create/destroy par frame)
        var hBrushKey = Win32.CreateSolidBrush(CLR_KEY);
        var hBrushKeyCtx = Win32.CreateSolidBrush(CLR_KEY_CTX);
        var hBrushKeyPressed = Win32.CreateSolidBrush(CLR_KEY_PRESSED);
        var hBrushCapsBar = Win32.CreateSolidBrush(CLR_CAPS_BAR);
        var hPenBorder = Win32.CreatePen(0, 1, CLR_KEY_BORDER); // PS_SOLID

        // Highlight de recherche (créés à la demande)
        bool hasHighlight = _highlightedScancodes.Count > 0 || _highlightedLabels.Count > 0;
        var (hlBorderColor, hlBgColor) = GetHighlightColors();
        var hBrushHl = hasHighlight ? Win32.CreateSolidBrush(hlBgColor) : IntPtr.Zero;
        var hPenHl = hasHighlight ? Win32.CreatePen(0, 2, hlBorderColor) : IntPtr.Zero;

        Win32.SetBkMode(hdc, Win32.TRANSPARENT);

        // Dessiner chaque touche
        for (int i = 0; i < _visualKeys.Length; i++)
        {
            ref readonly var vk = ref _visualKeys[i];

            int kx = offsetX + (int)(vk.X * scale);
            int ky = offsetY + (int)(vk.Y * scale);
            int kw = (int)(vk.W * scale);
            int kh = (int)(vk.H * scale);

            // Couleur de fond de la touche
            bool isPressed = IsKeyVisuallyPressed(vk);
            bool isHighlighted = hasHighlight && IsKeyHighlighted(vk);
            IntPtr hKeyBrush;
            IntPtr hKeyPen;
            if (vk.Label == "Verr.Maj" && _capsLockActive && !isHighlighted)
                hKeyBrush = hBrushCapsBar;
            else if (isPressed)
                hKeyBrush = hBrushKeyPressed;
            else if (isHighlighted)
                hKeyBrush = hBrushHl;
            else
                hKeyBrush = vk.IsContextual ? hBrushKeyCtx : hBrushKey;
            hKeyPen = isHighlighted ? hPenHl : hPenBorder;

            var hOldBrush = Win32.SelectObject(hdc, hKeyBrush);
            var hOldPen = Win32.SelectObject(hdc, hKeyPen);

            // Touche Entrée ISO : forme en L inversé (haut large, bas étroit)
            bool isIsoEnter = vk.Scancode == 0x1C && vk.H > KEY_H;
            if (isIsoEnter)
            {
                // Partie haute : pleine largeur (1.5u)
                // Partie basse : réduite (1.25u), alignée à droite
                float stepY = vk.Y + KEY_H;             // cran au bas de la rangée 2 (avant le gap)
                float botStartY = vk.Y + KEY_H + ROW_GAP; // début rangée 3
                float botX = vk.X + (vk.W - 1.25f);       // retrait gauche partie basse
                int px_tl = kx;
                int py_tl = ky;
                int px_tr = kx + kw;
                int py_br = offsetY + (int)((botStartY + KEY_H) * scale);
                int px_bl = offsetX + (int)(botX * scale);
                int py_step = offsetY + (int)(stepY * scale);

                var pts = new Win32.POINT[]
                {
                    new() { x = px_tl,  y = py_tl },    // haut-gauche
                    new() { x = px_tr,  y = py_tl },    // haut-droite
                    new() { x = px_tr,  y = py_br },    // bas-droite
                    new() { x = px_bl,  y = py_br },    // bas-gauche (partie basse)
                    new() { x = px_bl,  y = py_step },  // coin intérieur du L (bas rangée 2)
                    new() { x = px_tl,  y = py_step },  // retour vers la gauche
                };
                Win32.Polygon(hdc, pts, 6);
            }
            else
            {
                Win32.RoundRect(hdc, kx, ky, kx + kw, ky + kh, 6, 6);
            }

            Win32.SelectObject(hdc, hOldBrush);
            Win32.SelectObject(hdc, hOldPen);

            if (vk.IsContextual)
            {
                // Touche contextuelle : label centré
                // Texte blanc sur fond coloré (CapsLock, pressé, ou highlight), sinon couleur normale
                uint ctxTextColor = (vk.Label == "Verr.Maj" && _capsLockActive) || isPressed || isHighlighted ? CLR_CHAR : CLR_CTX_TEXT;
                // Pour Entrée ISO, centrer le label dans la colonne droite (partie commune du L)
                int ctxLeft = isIsoEnter ? offsetX + (int)((vk.X + (vk.W - 1.25f)) * scale) : kx;
                var ctxRect = new Win32.RECT { left = ctxLeft, top = ky, right = kx + kw, bottom = ky + kh };
                Win32.SelectObject(hdc, hCtxFont);
                Win32.SetTextColor(hdc, ctxTextColor);
                Win32.DrawTextW(hdc, vk.Label, vk.Label.Length, ref ctxRect, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
            }
            else
            {
                // Touche de caractère : caractère principal centré
                string? displayChar = GetDisplayChar(vk.Scancode);
                bool isDkOutput = displayChar != null && displayChar.StartsWith("dk_");

                if (isDkOutput)
                {
                    // Utiliser le caractère isolé (espace → symbole) de la table des touches mortes
                    // Fallback au symbole hardcodé si GetIsolated() retourne un espace ou null
                    string? isolated = null;
                    if (_layout.DeadKeys.TryGetValue(displayChar!, out var dkDef))
                        isolated = dkDef.GetIsolated();
                    displayChar = (isolated != null && isolated.Trim().Length > 0)
                        ? isolated
                        : TrayApplication.GetDeadKeySymbol(displayChar!);
                }

                // Labels AZERTY affichés quand une touche morte est active
                bool showLabel = _activeDeadKey != null;

                if (displayChar != null && displayChar.Length > 0)
                {
                    int bottomOffset = showLabel ? labelFontSize + 2 : 0;
                    var charRect = new Win32.RECT { left = kx, top = ky, right = kx + kw, bottom = ky + kh - bottomOffset };
                    Win32.SelectObject(hdc, hCharFont);
                    // Texte sombre sur fond clair quand la touche est pressée
                    uint charColor = isPressed ? 0x00201C18 : (isDkOutput ? CLR_DK_CHAR : CLR_CHAR);
                    Win32.SetTextColor(hdc, charColor);
                    Win32.DrawTextW(hdc, displayChar, displayChar.Length, ref charRect, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
                }

                // Label en bas : quand touche morte active (toujours, même sans correspondance)
                if (showLabel)
                {
                    var labelRect = new Win32.RECT { left = kx, top = ky + kh - labelFontSize - 4, right = kx + kw, bottom = ky + kh - 1 };
                    Win32.SelectObject(hdc, hLabelFont);
                    Win32.SetTextColor(hdc, CLR_LABEL);
                    Win32.DrawTextW(hdc, vk.Label, vk.Label.Length, ref labelRect, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
                }
            }
        }

        // Indication touche morte active — afficher la description (ex: "Accent circonflexe")
        if (_activeDeadKey != null)
        {
            string dkText = GetDeadKeyFrenchName(_activeDeadKey);
            var dkRect = new Win32.RECT { left = offsetX, top = offsetY + (int)kbHeight + 2, right = offsetX + (int)kbWidth, bottom = ch - 2 };
            Win32.SelectObject(hdc, hCtxFont);
            Win32.SetTextColor(hdc, CLR_DK_CHAR);
            Win32.DrawTextW(hdc, dkText, dkText.Length, ref dkRect, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
        }

        // Nettoyage des objets GDI (polices cachées → pas de delete ici)
        Win32.DeleteObject(hBrushKey);
        Win32.DeleteObject(hBrushKeyCtx);
        Win32.DeleteObject(hBrushKeyPressed);
        Win32.DeleteObject(hBrushCapsBar);
        Win32.DeleteObject(hPenBorder);
        if (hBrushHl != IntPtr.Zero) Win32.DeleteObject(hBrushHl);
        if (hPenHl != IntPtr.Zero) Win32.DeleteObject(hPenHl);
    }

    /// <summary>
    /// Retourne le caractère à afficher pour un scancode selon l'état actuel.
    /// Quand une touche morte est active : retourne le résultat transformé, ou null si pas de correspondance.
    /// </summary>
    private string? GetDisplayChar(uint scancode)
    {
        if (!_layout.Keys.TryGetValue(scancode, out var keyDef))
            return null;

        string? output = keyDef.GetOutput(_shiftDown, _altGrDown, _capsLockActive);

        // Si une touche morte est active
        if (_activeDeadKey != null && output != null)
        {
            // Autre touche morte → masquer, sauf la touche morte active :
            // double-pression = GetIsolated() → caractère A, puis Apply(A) → caractère B
            if (output.StartsWith("dk_"))
            {
                if (output == _activeDeadKey && _layout.DeadKeys.TryGetValue(_activeDeadKey, out var selfDk))
                {
                    var isolated = selfDk.GetIsolated();
                    if (isolated != null)
                    {
                        var doubled = selfDk.Apply(isolated);
                        if (doubled != null)
                            return doubled;
                    }
                    return isolated; // fallback au caractère isolé
                }
                return null;
            }

            if (_layout.DeadKeys.TryGetValue(_activeDeadKey, out var dk))
            {
                var transformed = dk.Apply(output);
                if (transformed != null)
                    return transformed;
            }
            return null; // Pas de correspondance → pas de caractère (légende jaune affichée séparément)
        }

        return output;
    }

    /// <summary>Vérifie si une touche doit apparaître enfoncée.</summary>
    private bool IsKeyVisuallyPressed(in VisualKey vk)
    {
        // Animation d'appui (flash temporaire via RawKeyDown)
        if (vk.Scancode != 0 && vk.Scancode == _pressedScancode)
            return true;

        // Touches contextuelles : état maintenu
        if (vk.IsContextual)
        {
            return vk.Label switch
            {
                "Maj ⇧" => _shiftDown,
                "AltGr" => _altGrDown,
                "Ctrl" => _ctrlDown && !_altGrDown, // Ignorer le phantom LCtrl de AltGr
                "Alt" => _altDown && !_altGrDown,   // Ignorer si AltGr actif
                "Verr.Maj" => _capsLockActive,
                _ => false
            };
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // Hit testing + tooltip
    // ═══════════════════════════════════════════════════════════════
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

        Win32.GetClientRect(_hWnd, out var clientRect);
        int cw = clientRect.right;
        int ch = clientRect.bottom;

        float totalKeyW = 16.3f;
        float totalKeyH = 5 * KEY_H + 4 * ROW_GAP;
        int margin = 10;
        float scaleX = (cw - 2 * margin) / totalKeyW;
        float scaleY = (ch - 2 * margin) / totalKeyH;
        float scale = Math.Min(scaleX, scaleY);
        int bottomReserve = Math.Max(20, (int)(scale * 0.55f));
        float kbWidth = totalKeyW * scale;
        float kbHeight = totalKeyH * scale;
        int offsetX = margin + (int)((cw - 2 * margin - kbWidth) / 2);
        int offsetY = margin + (int)((ch - 2 * margin - bottomReserve - kbHeight) / 2);

        int hitIndex = -1;
        for (int i = 0; i < _visualKeys.Length; i++)
        {
            ref readonly var vk = ref _visualKeys[i];
            int kx = offsetX + (int)(vk.X * scale);
            int ky = offsetY + (int)(vk.Y * scale);
            int kw = (int)(vk.W * scale);
            int kh = (int)(vk.H * scale);
            if (mx >= kx && mx < kx + kw && my >= ky && my < ky + kh)
            {
                hitIndex = i;
                break;
            }
        }

        if (hitIndex != _hoveredKeyIndex)
        {
            _hoveredKeyIndex = hitIndex;
            if (hitIndex >= 0 && !_visualKeys[hitIndex].IsContextual)
            {
                string? ch2 = GetDisplayChar(_visualKeys[hitIndex].Scancode);
                if (ch2 != null && !ch2.StartsWith("dk_") && _charNames.TryGetValue(ch2, out var name))
                    UpdateTooltipText(name);
                else if (ch2 != null && ch2.StartsWith("dk_"))
                {
                    UpdateTooltipText(GetDeadKeyFrenchName(ch2));
                }
                else
                    UpdateTooltipText("");
            }
            else
            {
                UpdateTooltipText("");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Dispose
    // ═══════════════════════════════════════════════════════════════
    public void Dispose()
    {
        if (_hTooltip != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hTooltip);
            _hTooltip = IntPtr.Zero;
        }
        // Polices cachées
        if (_hCharFont != IntPtr.Zero) { Win32.DeleteObject(_hCharFont); _hCharFont = IntPtr.Zero; }
        if (_hLabelFont != IntPtr.Zero) { Win32.DeleteObject(_hLabelFont); _hLabelFont = IntPtr.Zero; }
        if (_hCtxFont != IntPtr.Zero) { Win32.DeleteObject(_hCtxFont); _hCtxFont = IntPtr.Zero; }
        if (_hWnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
    }
}

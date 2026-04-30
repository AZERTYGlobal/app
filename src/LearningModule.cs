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
    private record struct LearningStep(string Title, string Instruction, string Target, bool Skippable);

    private static readonly LearningStep[] _steps =
    {
        new("Votre premier É",
            "Activez Verr.Maj puis tapez é",
            "É", false),
        new("Majuscules et ponctuation",
            "Gardez Verr.Maj activé et tapez cette phrase",
            "GRÂCE À AZERTY GLOBAL, ÉCRIRE EN FRANÇAIS EST TRÈS FACILE !", false),
        new("Adresse e-mail",
            "Tapez cette adresse e-mail \u2014 le @ est sur la touche \u00b2 et le point est en acc\u00e8s direct",
            "jean.dupont@education.gouv.fr", false),
        new("Typographie fran\u00e7aise",
            "Tapez cette phrase avec les caract\u00e8res typographiques \u2014 suivez les indications du clavier",
            "L\u00e6titia dit \u00ab c'est un chef-d'\u0153uvre\u2026 \u00bb \u2014 personne ne la contredit.", false),
        new("Ligne de code",
            "Tapez cette ligne de code \u2014 les symboles sont accessibles via AltGr",
            "type Config = { items: string[]; sep: \"~\" | \"\\\\\" };", true),
        new("Mots \u00e9trangers",
            "Tapez ces mots \u00e9trangers \u2014 utilisez les touches mortes indiqu\u00e9es sur le clavier",
            "S\u00e3o Paulo, C\u00f3rdoba, Troms\u00f8, \u0141\u00f3d\u017a, luned\u00ec, Gr\u00f6\u00dfe", true),
    };

    // ── Window constants ────────────────────────────────────────────
    private const int BASE_WIN_W = 1100;
    private const int BASE_WIN_H = 600;

    // Layout vertical (base 96 DPI)
    private const int BASE_HEADER_H = 40;
    private const int BASE_INSTRUCTION_H = 30;
    private const int BASE_TARGET_H = 60;
    private const int BASE_STATUS_H = 25;
    private const int BASE_FOOTER_H = 70; // Légende clavier (haut) + boutons (bas)
    private const int BASE_MARGIN = 20;

    // ── Control IDs ─────────────────────────────────────────────────
    private const int IDC_BTN_QUIT = 4001;
    private const int IDC_BTN_SKIP = 4002;
    private const int IDC_BTN_FINISH = 4003;

    // ── Timer IDs ───────────────────────────────────────────────────
    private const uint TIMER_KEYPRESS = 8001;
    private const uint TIMER_TRANSITION = 8002;
    private const uint KEYPRESS_DURATION_MS = 120;
    private const uint TRANSITION_DURATION_MS = 800;

    // ── Colors (COLORREF = 0x00BBGGRR) ──────────────────────────────
    // Zones supérieures (thème clair)
    private const uint CLR_BG = 0x00F0F0F0;
    private const uint CLR_HEADER_TITLE = 0x00201C18;
    private const uint CLR_INSTRUCTION_DARK = 0x00CCCCCC;  // Réservé pour fond sombre (non utilisé actuellement)
    private const uint CLR_INSTRUCTION_LIGHT = 0x00404040; // Gris foncé sur fond clair (contraste ~9:1)
    private const uint CLR_TARGET_PENDING = 0x00AAAAAA;
    private const uint CLR_TARGET_CURRENT = 0x00FFFFFF;
    private const uint CLR_TARGET_CORRECT = 0x005EC522;
    private const uint CLR_TARGET_ERROR = 0x004444EF;
    private const uint CLR_STATUS = 0x00888888;
    private const uint CLR_PROGRESS_DONE = 0x00D47800;
    private const uint CLR_PROGRESS_TODO = 0x00C8C8C8;
    private const uint CLR_TRANSITION = 0x005EC522;
    private const uint CLR_BTN_QUIT_TEXT = 0x00999999;

    // Pill « Bonus » (cohérence avec OnboardingWindow CLR_PILL_*)
    private const uint CLR_PILL_BG = 0x00FBECD8;   // Orange pâle
    private const uint CLR_PILL_TEXT = 0x00201C18; // Brun très foncé

    // Clavier virtuel (thème sombre — identique à VirtualKeyboard.cs)
    private const uint CLR_KB_BG = 0x00201C18;
    private const uint CLR_KEY = 0x00484038;
    private const uint CLR_KEY_BORDER = 0x00302820;
    private const uint CLR_KEY_PRESSED = 0x00D4A060;
    private const uint CLR_KEY_CTX = 0x00383028;
    private const uint CLR_CHAR_ACTIVE = 0x00F0EDE8;
    private const uint CLR_CHAR_DIM = 0x00808080;
    private const uint CLR_CHAR_ALTGR_ACCENT = 0x00CC6600; // Bleu (BGR = #0066cc) — accent pour positions AltGr / Maj+AltGr
    private const uint CLR_DK_CHAR = 0x000080FF;
    private const uint CLR_CTX_TEXT = 0x00B0A898;
    private const uint CLR_MOD_ACTIVE = 0x00FFD9B3;
    private const uint CLR_CAPS_BAR = 0x0000A5FF;
    private const uint CLR_DK_RESULT = 0x00339900;

    // Highlight
    private const uint CLR_HL_DIRECT = 0x0064C800;
    private const uint CLR_HL_DIRECT_BG = 0x00284018;
    private const uint CLR_HL_STEP1 = 0x0000A5FF;
    private const uint CLR_HL_STEP1_BG = 0x00283020;
    private const uint CLR_HL_STEP2 = 0x004CB050;
    private const uint CLR_HL_STEP2_BG = 0x00203818;

    // ═══════════════════════════════════════════════════════════════
    // Champs d'instance
    // ═══════════════════════════════════════════════════════════════
    private IntPtr _hWnd;
    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly IntPtr _hWndOnboarding;

    // Références app
    private readonly KeyMapper _mapper;
    private readonly KeyboardHook _hook;
    private readonly Layout _layout;

    // Données de highlight (character-index.json)
    private readonly Dictionary<string, CharacterSearch.MethodData> _charMethods = new();
    private readonly Dictionary<string, (string key, string layer)> _dkActivations = new();

    // Layout clavier
    private readonly VirtualKeyboard.VisualKey[] _visualKeys;

    // État
    private int _currentStep;
    private int _cursorPosition;
    private bool _currentCharError;
    private bool _completed; // écran final
    private bool _inTransition;

    // Tracking touche morte pour Backspace
    private string? _previousDeadKey;
    private bool _deadKeyCancelledByBackspace;

    // Animation de frappe
    private uint _pressedScancode;

    // Highlight du prochain caractère
    private readonly HashSet<uint> _highlightedScancodes = new();
    private readonly HashSet<string> _highlightedLabels = new();
    private string _highlightType = ""; // "direct", "step1", "step2"
    private CharacterSearch.MethodData? _pendingStep2;

    // Contrôles
    private IntPtr _hWndBtnQuit;
    private IntPtr _hWndBtnSkip;
    private IntPtr _hWndBtnFinish;

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
    public Action? OnClosed;

    public LearningModule(IntPtr hWndOnboarding, KeyMapper mapper, KeyboardHook hook, Layout layout)
    {
        _hWndOnboarding = hWndOnboarding;
        _mapper = mapper;
        _hook = hook;
        _layout = layout;
        _wndProcDelegate = WndProc;
        _visualKeys = VirtualKeyboard.BuildKeyLayout();

        _hBgBrush = Win32.CreateSolidBrush(CLR_BG);
        _hKbBgBrush = Win32.CreateSolidBrush(CLR_KB_BG);

        // DPI initial
        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        int dpi = Win32.GetDeviceCaps(hdcScreen, 88);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        _dpiScale = dpi / 96f;

        LoadCharacterMethods();
        CreateFonts();
        CreateMainWindow();
        CreateControls();
        UpdateControlVisibility();

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

        // Highlight initial
        UpdateHighlight();
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
            JsonElement? fallback = null;
            foreach (var method in methods.EnumerateArray())
            {
                if (method.TryGetProperty("recommended", out var rec) && rec.GetBoolean())
                { recommended = method; break; }
                fallback ??= method;
            }
            var chosen = recommended ?? fallback;
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
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Polices
    // ═══════════════════════════════════════════════════════════════
    private void CreateFonts()
    {
        _hFontTitle = Win32.CreateFontW(-S(22), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontInstruction = Win32.CreateFontW(-S(15), 0, 0, 0, 400, 1, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontTarget = Win32.CreateFontW(-S(20), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontStatus = Win32.CreateFontW(-S(13), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontButton = Win32.CreateFontW(-S(14), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontCharMain = Win32.CreateFontW(S(18), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hFontCharSmall = Win32.CreateFontW(S(11), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hFontCtx = Win32.CreateFontW(S(12), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
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
        const string className = "AZERTYGlobal_Learning";

        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            hCursor = Win32.LoadCursorW(IntPtr.Zero, (IntPtr)32512),
            hbrBackground = _hBgBrush,
            lpszClassName = className
        };
        Win32.RegisterClassExW(ref wc);

        int winW = S(BASE_WIN_W);
        int winH = S(BASE_WIN_H);
        uint dwStyle = Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU;
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

        _hWnd = Win32.CreateWindowExW(dwExStyle, className, "AZERTY Global \u2014 Exercices",
            dwStyle,
            screenX + (screenW - windowW) / 2, screenY + (screenH - windowH) / 2,
            windowW, windowH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
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

        _hWndBtnQuit = Win32.CreateWindowExW(0, "BUTTON", "Quitter les exercices",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP,
            margin, 0, S(180), S(30),
            _hWnd, (IntPtr)IDC_BTN_QUIT, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnQuit, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);

        _hWndBtnSkip = Win32.CreateWindowExW(0, "BUTTON", "Passer cet exercice",
            Win32.WS_CHILD | Win32.WS_TABSTOP,
            0, 0, S(160), S(30),
            _hWnd, (IntPtr)IDC_BTN_SKIP, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnSkip, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);

        _hWndBtnFinish = Win32.CreateWindowExW(0, "BUTTON", "Terminer",
            Win32.WS_CHILD | Win32.WS_TABSTOP,
            0, 0, S(140), S(30),
            _hWnd, (IntPtr)IDC_BTN_FINISH, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnFinish, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);

        RepositionControls();
    }

    private void RepositionControls()
    {
        Win32.GetClientRect(_hWnd, out var cr);
        int cw = cr.right;
        int ch = cr.bottom;
        int margin = S(BASE_MARGIN);
        int footerY = ch - S(BASE_FOOTER_H);

        // Boutons positionnés en bas du footer (la légende prend les ~30 px du haut)
        int btnY = footerY + S(35);
        Win32.MoveWindow(_hWndBtnQuit, margin, btnY, S(180), S(30), true);
        Win32.MoveWindow(_hWndBtnSkip, cw - margin - S(160), btnY, S(160), S(30), true);
        Win32.MoveWindow(_hWndBtnFinish, cw - margin - S(140), btnY, S(140), S(30), true);
    }

    private void UpdateControlVisibility()
    {
        if (_completed)
        {
            Win32.ShowWindow(_hWndBtnQuit, 0);
            Win32.ShowWindow(_hWndBtnSkip, 0);
            Win32.ShowWindow(_hWndBtnFinish, 1);
        }
        else
        {
            Win32.ShowWindow(_hWndBtnQuit, 1);
            bool skippable = _currentStep < _steps.Length && _steps[_currentStep].Skippable;
            Win32.ShowWindow(_hWndBtnSkip, skippable ? 1 : 0);
            Win32.ShowWindow(_hWndBtnFinish, 0);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Show / Close
    // ═══════════════════════════════════════════════════════════════
    public void Show()
    {
        Win32.EnableWindow(_hWndOnboarding, false);
        Win32.ShowWindow(_hWnd, 1);

        // Prendre le focus
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

    private void Close()
    {
        _mapper.StateChanged -= OnStateChanged;
        _hook.RawKeyDown -= OnRawKeyDown;
        Win32.ShowWindow(_hWnd, 0);
        Win32.EnableWindow(_hWndOnboarding, true);
        Win32.SetForegroundWindow(_hWndOnboarding);
        OnClosed?.Invoke();
    }

    // ═══════════════════════════════════════════════════════════════
    // Événements KeyMapper / KeyboardHook
    // ═══════════════════════════════════════════════════════════════
    private void OnStateChanged()
    {
        // Tracking touche morte pour Backspace
        var currentDk = _mapper.ActiveDeadKey;
        if (_previousDeadKey != null && currentDk == null)
            _deadKeyCancelledByBackspace = true;
        _previousDeadKey = currentDk;

        // Mettre à jour le highlight (l'état des modificateurs a changé)
        UpdateHighlight();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void OnRawKeyDown(uint scancode)
    {
        _pressedScancode = scancode;
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_KEYPRESS, KEYPRESS_DURATION_MS, IntPtr.Zero);
    }

    // ═══════════════════════════════════════════════════════════════
    // WndProc
    // ═══════════════════════════════════════════════════════════════
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case Win32.WM_PAINT:
                    OnPaint(hWnd);
                    return IntPtr.Zero;

                case Win32.WM_ERASEBKGND:
                    return (IntPtr)1;

                case 0x0102: // WM_CHAR
                    OnChar((char)wParam.ToInt32());
                    return IntPtr.Zero;

                case Win32.WM_KEYDOWN:
                {
                    int vk = wParam.ToInt32();
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
        _deadKeyCancelledByBackspace = false;

        if (_completed || _inTransition) return;
        if (_currentStep >= _steps.Length) return;

        var target = _steps[_currentStep].Target;
        if (_cursorPosition >= target.Length) return;

        if (c == target[_cursorPosition])
        {
            _cursorPosition++;
            _currentCharError = false;

            if (_cursorPosition >= target.Length)
            {
                // Étape terminée — transition
                _inTransition = true;
                ClearHighlight();
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                Win32.SetTimer(_hWnd, (UIntPtr)TIMER_TRANSITION, TRANSITION_DURATION_MS, IntPtr.Zero);
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

    private void OnBackspace()
    {
        if (_completed || _inTransition) return;

        if (_deadKeyCancelledByBackspace)
        {
            _deadKeyCancelledByBackspace = false;
            return; // Le Backspace a annulé une touche morte, pas de recul
        }

        if (_cursorPosition > 0)
        {
            _cursorPosition--;
            _currentCharError = false;
            UpdateHighlight();
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        }
    }

    private void AdvanceToNextStep()
    {
        _currentStep++;
        _cursorPosition = 0;
        _currentCharError = false;

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
    }

    private void SkipStep()
    {
        if (_currentStep < _steps.Length && _steps[_currentStep].Skippable)
            AdvanceToNextStep();
    }

    // ═══════════════════════════════════════════════════════════════
    // Highlight du prochain caractère
    // ═══════════════════════════════════════════════════════════════
    private void ClearHighlight()
    {
        _highlightedScancodes.Clear();
        _highlightedLabels.Clear();
        _highlightType = "";
        _pendingStep2 = null;
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

        // Cas spécial : layer "Caps" — gestion dynamique
        if (method.Layer.StartsWith("Caps") && method.Type == "direct")
        {
            if (!_mapper.CapsLockActive)
            {
                // Verr.Maj pas actif → highlight Verr.Maj seul
                _highlightType = "direct";
                _highlightedLabels.Add("Verr.Maj");
                return;
            }
            // Verr.Maj actif → highlight la touche (sans montrer Verr.Maj)
            _highlightType = "direct";
            if (VirtualKeyboard.KeyCodeToScancode.TryGetValue(method.Key, out var sc))
                _highlightedScancodes.Add(sc);
            // Ajouter Shift si Caps+Shift
            if (method.Layer == "Caps+Shift" || method.Layer == "CapsShift")
                _highlightedLabels.Add("Maj \u21e7");
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
                // Étape 1 + étape 2 superposées
                _highlightType = "step1";
                AddKeyHighlight(method.DkActivationKey, method.DkActivationLayer);
                _pendingStep2 = method;
            }
        }
    }

    private void AddKeyHighlight(string keyCode, string layer)
    {
        if (VirtualKeyboard.KeyCodeToScancode.TryGetValue(keyCode, out var scancode))
            _highlightedScancodes.Add(scancode);

        switch (layer)
        {
            case "Shift": _highlightedLabels.Add("Maj \u21e7"); break;
            case "AltGr": _highlightedLabels.Add("AltGr"); break;
            case "Shift+AltGr": case "AltGr+Shift":
                _highlightedLabels.Add("AltGr"); _highlightedLabels.Add("Maj \u21e7"); break;
        }
    }

    private bool IsKeyHighlighted(in VirtualKeyboard.VisualKey vk)
    {
        if (_highlightedScancodes.Count == 0 && _highlightedLabels.Count == 0) return false;
        if (vk.Scancode != 0 && _highlightedScancodes.Contains(vk.Scancode)) return true;
        if (vk.IsContextual && _highlightedLabels.Contains(vk.Label)) return true;
        return false;
    }

    private bool IsStep2Key(in VirtualKeyboard.VisualKey vk)
    {
        if (_pendingStep2 == null || _highlightType != "step1") return false;
        if (VirtualKeyboard.KeyCodeToScancode.TryGetValue(_pendingStep2.Key, out var sc))
            if (vk.Scancode != 0 && vk.Scancode == sc) return true;
        // Vérifier les modificateurs de l'étape 2
        var layer2 = _pendingStep2.Layer;
        if (vk.IsContextual)
        {
            if ((layer2 == "Shift" || layer2 == "Shift+AltGr" || layer2 == "AltGr+Shift") && vk.Label == "Maj \u21e7") return true;
            if ((layer2 == "AltGr" || layer2 == "Shift+AltGr" || layer2 == "AltGr+Shift") && vk.Label == "AltGr") return true;
        }
        return false;
    }

    private (uint border, uint bg) GetHighlightColors(bool isStep2)
    {
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
            int kbTop = S(BASE_HEADER_H + BASE_INSTRUCTION_H + BASE_TARGET_H + BASE_STATUS_H);
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
            else
                PaintExercise(hdc, cw, kbTop);

            // Clavier virtuel
            PaintKeyboard(hdc, cw, kbTop, footerTop);

            // Légende du clavier (en haut du footer)
            PaintLegend(hdc, cw, footerTop);

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

        // Header — progression + titre
        var hOldFont = Win32.SelectObject(hdc, _hFontProgress);
        string progress = "";
        for (int i = 0; i < _steps.Length; i++)
            progress += i < _currentStep ? "\u25cf " : (i == _currentStep ? "\u25cf " : "\u25cb ");
        Win32.SetTextColor(hdc, CLR_PROGRESS_DONE);
        var progressRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + S(20) };
        Win32.DrawTextW(hdc, progress.TrimEnd(), progress.TrimEnd().Length, ref progressRect, 0);

        // Colorer les cercles : fait = bleu, en cours = bleu, à faire = gris
        // (Simplifié : tout en une couleur pour l'instant, le progress string gère visuellement)

        y += S(22);
        Win32.SelectObject(hdc, _hFontTitle);
        Win32.SetTextColor(hdc, CLR_HEADER_TITLE);
        string title = $"Exercice {_currentStep + 1}/{_steps.Length} \u2014 {_steps[_currentStep].Title}";
        var titleRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + S(26) };
        Win32.DrawTextW(hdc, title, title.Length, ref titleRect, 0);

        // Pill \u00ab Bonus \u00bb \u00e0 droite du titre si l'exercice est facultatif
        if (_steps[_currentStep].Skippable)
        {
            int titleW = GdiHelpers.MeasureSingleLineWidth(hdc, _hFontTitle, title);
            int pillX = margin + titleW + S(12);
            int pillPadX = S(8);
            int pillH = S(20);
            int pillTop = y + S(4); // l\u00e9ger d\u00e9calage pour aligner verticalement avec le titre
            const string pillText = "Bonus";

            Win32.SelectObject(hdc, _hFontBadge);
            int pillTextW = GdiHelpers.MeasureSingleLineWidth(hdc, _hFontBadge, pillText);
            int pillW = pillTextW + pillPadX * 2;
            var pillRect = new Win32.RECT { left = pillX, top = pillTop, right = pillX + pillW, bottom = pillTop + pillH };
            GdiHelpers.FillSolidRect(hdc, pillRect, CLR_PILL_BG);
            Win32.SetTextColor(hdc, CLR_PILL_TEXT);
            Win32.DrawTextW(hdc, pillText, -1, ref pillRect,
                Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        }

        // Instruction
        y = S(BASE_HEADER_H) + S(4);
        Win32.SelectObject(hdc, _hFontInstruction);
        Win32.SetTextColor(hdc, CLR_INSTRUCTION_LIGHT);
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
                // Souligné
                int underY = y + S(24);
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

        // Barre d'état
        y = S(BASE_HEADER_H + BASE_INSTRUCTION_H + BASE_TARGET_H) + S(2);
        Win32.SelectObject(hdc, _hFontStatus);
        Win32.SetTextColor(hdc, CLR_STATUS);
        string status = _mapper.CapsLockActive ? "Verr.Maj : ACTIF" : "Verr.Maj : inactif";
        if (_mapper.ActiveDeadKey != null)
            status += $"  \u2014  Touche morte : {TrayApplication.GetDeadKeySymbol(_mapper.ActiveDeadKey)}";
        var statusRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + S(20) };
        Win32.DrawTextW(hdc, status, status.Length, ref statusRect, 0);

        Win32.SelectObject(hdc, hOldFont);
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

    /// <summary>Affiche une légende centrée « Maj. — Verr. Maj. — AltGr — Touche morte » avec couleurs.</summary>
    private void PaintLegend(IntPtr hdc, int cw, int footerTop)
    {
        var hOldFont = Win32.SelectObject(hdc, _hFontStatus);
        try
        {
            var items = new (string Text, uint Color)[]
            {
                ("Maj.",         CLR_CHAR_ACTIVE),
                ("Verr. Maj.",   CLR_CHAR_ACTIVE),
                ("AltGr",        CLR_CHAR_ALTGR_ACCENT),
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

        foreach (var vk in _visualKeys)
        {
            int kx = (int)(geo.OffsetX + vk.X * geo.Scale) + 0; // offset ajusté dans la zone clavier
            int ky = kbTop + (int)(geo.OffsetY + vk.Y * geo.Scale);
            int kw = (int)(vk.W * geo.Scale) - 1;
            int kh = (int)(vk.H * geo.Scale) - 1;

            if (kw <= 0 || kh <= 0) continue;

            // Déterminer couleur de fond
            bool isPressed = _pressedScancode != 0 && vk.Scancode == _pressedScancode;
            bool isHighlighted = IsKeyHighlighted(vk);
            bool isStep2 = IsStep2Key(vk);
            bool isModActive = IsModifierActive(vk);

            uint bgColor, borderColor;
            if (isPressed)
            {
                bgColor = CLR_KEY_PRESSED;
                borderColor = CLR_KEY_BORDER;
            }
            else if (isHighlighted || isStep2)
            {
                var (hlBorder, hlBg) = GetHighlightColors(isStep2);
                bgColor = hlBg;
                borderColor = hlBorder;
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

            // Dessiner la touche
            var hBrush = Win32.CreateSolidBrush(bgColor);
            var keyRect = new Win32.RECT { left = kx, top = ky, right = kx + kw, bottom = ky + kh };
            Win32.FillRect(hdc, ref keyRect, hBrush);
            Win32.DeleteObject(hBrush);

            // Bordure
            var hPen = Win32.CreatePen(0, 1, borderColor);
            var hOldPen = Win32.SelectObject(hdc, hPen);
            // Rectangle border (pas FillRect car on veut juste le contour)
            Win32.MoveToEx(hdc, kx, ky, IntPtr.Zero);
            Win32.LineTo(hdc, kx + kw, ky);
            Win32.LineTo(hdc, kx + kw, ky + kh);
            Win32.LineTo(hdc, kx, ky + kh);
            Win32.LineTo(hdc, kx, ky);
            Win32.SelectObject(hdc, hOldPen);
            Win32.DeleteObject(hPen);

            // Barre CapsLock
            if (vk.Label == "Verr.Maj" && _mapper.CapsLockActive)
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
                // Label centré
                var hOldFont = Win32.SelectObject(hdc, _hFontCtx);
                Win32.SetTextColor(hdc, isModActive ? CLR_KB_BG : CLR_CTX_TEXT);
                var labelRect = new Win32.RECT { left = kx, top = ky, right = kx + kw, bottom = ky + kh };
                Win32.DrawTextW(hdc, vk.Label, vk.Label.Length, ref labelRect,
                    Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
                Win32.SelectObject(hdc, hOldFont);
            }
            else if (vk.Scancode != 0 && _layout.Keys.TryGetValue(vk.Scancode, out var keyDef))
            {
                PaintKeyCharacters(hdc, kx, ky, kw, kh, keyDef, vk, geo.Scale);
            }

            // Badge highlight (1 ou 2)
            if (isHighlighted && _highlightType == "step1")
            {
                PaintBadge(hdc, kx + kw - S(12), ky + S(1), "1", CLR_HL_STEP1);
            }
            if (isStep2)
            {
                PaintBadge(hdc, kx + kw - S(12), ky + S(1), "2", CLR_HL_STEP2);
            }
        }
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

    private void PaintKeyCharacters(IntPtr hdc, int kx, int ky, int kw, int kh,
        KeyDefinition keyDef, in VirtualKeyboard.VisualKey vk, float scale)
    {
        int pad = Math.Max(2, (int)(scale * 0.08f));

        // Déterminer si c'est une touche lettre (caractère principal est une lettre)
        bool isLetter = keyDef.Base != null && keyDef.Base.Length == 1 && char.IsLetter(keyDef.Base[0]);

        // Obtenir les caractères des 4 couches
        string? baseChar = keyDef.Base;
        string? shiftChar = keyDef.Shift;
        string? altGrChar = keyDef.AltGr;
        string? shiftAltGrChar = keyDef.ShiftAltGr;

        // Vérifier si une touche morte est active → afficher les résultats DK
        if (_mapper.ActiveDeadKey != null && _layout.DeadKeys.TryGetValue(_mapper.ActiveDeadKey, out var dk))
        {
            string? result = null;
            var activeChar = keyDef.GetOutput(false, false, false);
            if (activeChar != null) result = dk.Apply(activeChar);
            // Espace → symbole isolé
            if (vk.Scancode == 0x39) result = dk.GetIsolated();

            if (result != null)
            {
                var hOldFont = Win32.SelectObject(hdc, _hFontCharMain);
                Win32.SetTextColor(hdc, 0x00339900);
                var r = new Win32.RECT { left = kx, top = ky, right = kx + kw, bottom = ky + kh };
                Win32.DrawTextW(hdc, result, result.Length, ref r, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
                Win32.SelectObject(hdc, hOldFont);
            }
            else
            {
                // Pas de résultat → griser
                var hOldFont = Win32.SelectObject(hdc, _hFontCharSmall);
                Win32.SetTextColor(hdc, CLR_CHAR_DIM);
                var dispChar = GetDisplayChar(baseChar);
                if (dispChar != null)
                {
                    var r = new Win32.RECT { left = kx + pad, top = ky + kh / 2, right = kx + kw / 2, bottom = ky + kh - pad };
                    Win32.DrawTextW(hdc, dispChar, dispChar.Length, ref r, 0);
                }
                Win32.SelectObject(hdc, hOldFont);
            }
            return;
        }

        // Obtenir la couche active
        var activeOutput = keyDef.GetOutput(_mapper.ShiftDown, _mapper.AltGrDown, _mapper.CapsLockActive);

        if (isLetter)
        {
            // Touche lettre : caractère principal en gros, AltGr en petit
            var mainChar = GetDisplayChar(activeOutput);
            if (mainChar != null)
            {
                var hOldFont = Win32.SelectObject(hdc, _hFontCharMain);
                Win32.SetTextColor(hdc, CLR_CHAR_ACTIVE);
                var r = new Win32.RECT { left = kx + pad, top = ky + kh / 2 - pad, right = kx + kw / 2 + pad, bottom = ky + kh - pad };
                Win32.DrawTextW(hdc, mainChar, mainChar.Length, ref r, 0);
                Win32.SelectObject(hdc, hOldFont);
            }
            // AltGr en bas-droite (petit, accent bleu)
            var altGrDisp = GetDisplayChar(altGrChar);
            if (altGrDisp != null)
            {
                var hOldFont = Win32.SelectObject(hdc, _hFontCharSmall);
                Win32.SetTextColor(hdc, CLR_CHAR_ALTGR_ACCENT);
                var r = new Win32.RECT { left = kx + kw / 2, top = ky + kh / 2, right = kx + kw - pad, bottom = ky + kh - pad };
                Win32.DrawTextW(hdc, altGrDisp, altGrDisp.Length, ref r, Win32.DT_RIGHT);
                Win32.SelectObject(hdc, hOldFont);
            }
            // Shift+AltGr en haut-droite (petit, accent bleu)
            var shiftAltGrDisp = GetDisplayChar(shiftAltGrChar);
            if (shiftAltGrDisp != null)
            {
                var hOldFont = Win32.SelectObject(hdc, _hFontCharSmall);
                Win32.SetTextColor(hdc, CLR_CHAR_ALTGR_ACCENT);
                var r = new Win32.RECT { left = kx + kw / 2, top = ky + pad, right = kx + kw - pad, bottom = ky + kh / 2 };
                Win32.DrawTextW(hdc, shiftAltGrDisp, shiftAltGrDisp.Length, ref r, Win32.DT_RIGHT);
                Win32.SelectObject(hdc, hOldFont);
            }
        }
        else
        {
            // Touche symbole : 4 positions, active en couleur vive, AltGr en accent bleu
            // Bas-gauche : Base
            DrawKeyChar(hdc, kx + pad, ky + kh / 2, kx + kw / 2, ky + kh - pad,
                baseChar, activeOutput == baseChar, false, 0);
            // Haut-gauche : Shift
            DrawKeyChar(hdc, kx + pad, ky + pad, kx + kw / 2, ky + kh / 2,
                shiftChar, activeOutput == shiftChar, false, 0);
            // Bas-droite : AltGr (accent bleu quand inactif)
            DrawKeyChar(hdc, kx + kw / 2, ky + kh / 2, kx + kw - pad, ky + kh - pad,
                altGrChar, activeOutput == altGrChar, true, Win32.DT_RIGHT);
            // Haut-droite : Shift+AltGr (accent bleu quand inactif)
            DrawKeyChar(hdc, kx + kw / 2, ky + pad, kx + kw - pad, ky + kh / 2,
                shiftAltGrChar, activeOutput == shiftAltGrChar, true, Win32.DT_RIGHT);
        }
    }

    private void DrawKeyChar(IntPtr hdc, int left, int top, int right, int bottom,
        string? charStr, bool isActive, bool isAltGrPosition, uint extraFlags)
    {
        var disp = GetDisplayChar(charStr);
        if (disp == null) return;
        bool isDk = charStr != null && charStr.StartsWith("dk_");
        var hOldFont = Win32.SelectObject(hdc, _hFontCharSmall);
        uint color = isDk ? CLR_DK_CHAR
            : (isActive ? CLR_CHAR_ACTIVE
            : (isAltGrPosition ? CLR_CHAR_ALTGR_ACCENT : CLR_CHAR_DIM));
        Win32.SetTextColor(hdc, color);
        var r = new Win32.RECT { left = left, top = top, right = right, bottom = bottom };
        Win32.DrawTextW(hdc, disp, disp.Length, ref r, extraFlags);
        Win32.SelectObject(hdc, hOldFont);
    }

    private static string? GetDisplayChar(string? value)
    {
        if (value == null || value.Length == 0) return null;
        if (value.StartsWith("dk_")) return TrayApplication.GetDeadKeySymbol(value);
        return value;
    }

    private bool IsModifierActive(in VirtualKeyboard.VisualKey vk)
    {
        if (!vk.IsContextual) return false;
        return vk.Label switch
        {
            "Maj \u21e7" => _mapper.ShiftDown,
            "AltGr" => _mapper.AltGrDown,
            "Ctrl" => _mapper.CtrlDown,
            "Alt" => _mapper.AltDown,
            "Verr.Maj" => _mapper.CapsLockActive,
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

        DestroyFonts();
        Win32.DeleteObject(_hBgBrush);
        Win32.DeleteObject(_hKbBgBrush);

        if (_hWnd != IntPtr.Zero)
            Win32.DestroyWindow(_hWnd);
    }
}

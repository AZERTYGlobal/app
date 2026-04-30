// Fenêtre d'accueil — wizard 3 étapes affiché au premier lancement
using System.Runtime.InteropServices;

namespace AZERTYGlobal;

/// <summary>
/// Fenêtre Win32 d'onboarding en 3 étapes :
///   1. Les 5 améliorations + bandeau bêta
///   2. Comment utiliser l'application
///   3. Ressources, communauté et préférences
/// DPI-aware per-monitor v2 : recalcule polices et layout sur WM_DPICHANGED.
/// </summary>
sealed class OnboardingWindow : IDisposable
{
    // ── Window constants ─────────────────────────────────────────────
    private const uint BS_AUTOCHECKBOX = 0x0003;
    private const uint BM_GETCHECK = 0x00F0;
    private const uint BM_SETCHECK = 0x00F1;
    private const uint BST_CHECKED = 0x0001;
    private const uint SS_NOTIFY = 0x0100;

    // ── Control IDs ──────────────────────────────────────────────────
    private const int IDC_CHK_DONT_SHOW = 2001;
    private const int IDC_BTN_NEXT = 2002;
    private const int IDC_BTN_PREV = 2003;
    private const int IDC_LINK_GUIDE = 2004;
    private const int IDC_CHK_AUTOSTART = 2006;
    private const int IDC_LINK_BETA_BANNER = 2007;
    private const int IDC_LINK_BETA = 2008;
    private const int IDC_LINK_DISCORD = 2010;

    // Dimensions de base (96 DPI)
    private const int BASE_WIN_W = 560;
    private const int BASE_WIN_H = 810;
    private const float ONBOARDING_UI_SCALE = 0.75f;
    private const int BASE_MARGIN = 28;
    private const int BASE_BOTTOM_MARGIN = 52;
    private const int BASE_LINK_H = 24;
    private const int BASE_LINK_SPACING = 30;
    private const int BASE_BTN_H = 36;
    private const int BASE_BTN_W_NEXT_MIN = 140;
    private const int BASE_BTN_W_PREV = 120;
    private const int BASE_LINK_BANNER_W = 160;
    private const int BASE_BTN_TEXT_PAD = 28;

    // ── Colors (COLORREF = 0x00BBGGRR) ───────────────────────────────
    private const uint CLR_BG = 0x00DDDDDD;
    private const uint CLR_TITLE = 0x00201C18;
    private const uint CLR_FEATURE_TITLE = 0x00D47800;
    private const uint CLR_TEXT = 0x00333333;
    private const uint CLR_LINK = 0x00D47800;
    private const uint CLR_LINK_HOVER = 0x00FF9830;
    private const uint CLR_BANNER_BG = 0x00E8E8E8;
    private const uint CLR_BANNER_BORDER = 0x000078D4;
    private const uint CLR_BANNER_TEXT = 0x00333333;
    private const uint CLR_BANNER_TITLE = 0x000078D4;
    private const uint CLR_STEP_TITLE = 0x00D47800;
    private const uint CLR_HIGHLIGHT = 0x000078D4;
    private const uint CLR_PROGRESS_ACTIVE = 0x00D47800;
    private const uint CLR_PROGRESS_INACTIVE = 0x00C8C8C8;
    private const uint CLR_SECTION = 0x00D47800;
    private const uint CLR_PANEL_BG = 0x00EEEEEE;
    private const uint CLR_PANEL_BORDER = 0x00D1D1D1;
    private const uint CLR_NOTE_BG = 0x00D8F4FF;
    private const uint CLR_NOTE_BORDER = 0x007BC2EB;
    private const uint CLR_NOTE_ACCENT = 0x002A98E2;
    private const uint CLR_BADGE_BG = 0x00D47800;
    private const uint CLR_BADGE_TEXT = 0x00FFFFFF;
    private const uint CLR_PILL_BG = 0x00FBECD8;
    private const uint CLR_PILL_TEXT = 0x00201C18;
    private const uint CLR_WARNING_TEXT = 0x00174D6E;
    private const uint CLR_INLINE_HIGHLIGHT = 0x000078D4;
    private const uint CLR_SEPARATOR = 0x00D0D0D0;
    private const uint CLR_REASSURE = 0x00666666;
    private const uint ARGB_STEP_CIRCLE = 0xFF0078D4;
    private const uint ARGB_WHITE = 0xFFFFFFFF;

    // ── Colors ARGB pour GDI+ (0xAARRGGBB) ──────────────────────────

    // ═══════════════════════════════════════════════════════════════
    // Champs d'instance
    // ═══════════════════════════════════════════════════════════════
    private IntPtr _hWnd;
    private int _currentStep;
    private bool _learningModuleDone;
    private LearningModule? _learningModule;

    // Références passées par TrayApplication pour le LearningModule
    public KeyMapper? Mapper { get; set; }
    public KeyboardHook? Hook { get; set; }
    public Layout? AppLayout { get; set; }

    // Y du contenu (après le header) — recalculé à chaque OnPaint
    private int _contentY;

    // Contrôles — Navigation
    private IntPtr _hWndBtnNext;
    private IntPtr _hWndBtnPrev;

    // Contrôles — Étape 1
    private IntPtr _hWndLinkBetaBanner;

    // Contrôles — Étape 3
    private IntPtr _hWndLinkGuide;
    private IntPtr _hWndLinkBeta;
    private IntPtr _hWndLinkDiscord;
    private IntPtr _hWndChkAutoStart;
    private IntPtr _hWndChkDontShow;

    // Delegates (prevent GC)
    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly Win32.SUBCLASSPROC _linkSubclassProc;
    private IntPtr _hoveredLink;

    // GDI resources
    private readonly IntPtr _hBgBrush;
    private readonly IntPtr _hBannerBgBrush;
    private readonly IntPtr _hPanelBrush;

    // GDI+ resources
    private IntPtr _gdipToken;
    private IntPtr _gdipLogo;
    private IntPtr _gdipDiscord;
    private IntPtr _hIcon;

    private bool _visible;

    // DPI scaling — mutable, recalculé sur WM_DPICHANGED
    private float _dpiScale;
    private int S(int val) => (int)(val * _dpiScale * ONBOARDING_UI_SCALE);

    // Fonts — recréés sur changement de DPI
    private IntPtr _hFontTitle;
    private IntPtr _hFontSubtitle;
    private IntPtr _hFontText;
    private IntPtr _hFontFeatureDesc;
    private IntPtr _hFontBold;
    private IntPtr _hFontLink;
    private IntPtr _hFontSmall;
    private IntPtr _hFontButton;
    private IntPtr _hFontBannerBold;
    private IntPtr _hFontStepSummary;
    private IntPtr _hFontSection;
    private IntPtr _hFontPageTitle;
    private IntPtr _hFontLinkStrong;

    public bool IsVisible => _visible;

    public OnboardingWindow()
    {
        _wndProcDelegate = WndProc;
        _linkSubclassProc = LinkSubclassProc;
        _hBgBrush = Win32.CreateSolidBrush(CLR_BG);
        _hBannerBgBrush = Win32.CreateSolidBrush(CLR_BANNER_BG);
        _hPanelBrush = Win32.CreateSolidBrush(CLR_PANEL_BG);

        // DPI initial (moniteur principal — sera corrigé par GetDpiForWindow après création)
        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        int dpi = Win32.GetDeviceCaps(hdcScreen, 88);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        _dpiScale = dpi / 96f;

        // GDI+
        var gdipInput = new Win32.GdiplusStartupInput { GdiplusVersion = 1 };
        Win32.GdiplusStartup(out _gdipToken, ref gdipInput, IntPtr.Zero);

        _gdipLogo = GdiImageLoader.LoadFromEmbeddedResource(typeof(OnboardingWindow), "favicon-azerty-global.png");
        _gdipDiscord = GdiImageLoader.LoadFromEmbeddedResource(typeof(OnboardingWindow), "discord-icon.png");
        CreateFonts();
        CreateMainWindow();
        ApplyFontsToControls();

        // Corriger le DPI avec le vrai DPI du moniteur où la fenêtre est apparue
        try
        {
            int realDpi = Win32.GetDpiForWindow(_hWnd);
            if (realDpi > 0 && Math.Abs(realDpi / 96f - _dpiScale) > 0.01f)
            {
                _dpiScale = realDpi / 96f;
                RecreateFonts();
                RepositionControls();
                ResizeWindow();
            }
        }
        catch { /* GetDpiForWindow non disponible (Windows 8.1-) */ }
    }

    // ═══════════════════════════════════════════════════════════════
    // Polices
    // ═══════════════════════════════════════════════════════════════
    private void CreateFonts()
    {
        _hFontTitle = Win32.CreateFontW(-S(28), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSubtitle = Win32.CreateFontW(-S(18), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontText = Win32.CreateFontW(-S(17), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontFeatureDesc = Win32.CreateFontW(-S(16), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontBold = Win32.CreateFontW(-S(17), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontLink = Win32.CreateFontW(-S(16), 0, 0, 0, 400, 0, 1, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSmall = Win32.CreateFontW(-S(14), 0, 0, 0, 400, 1, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontButton = Win32.CreateFontW(-S(17), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontBannerBold = Win32.CreateFontW(-S(21), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontStepSummary = Win32.CreateFontW(-S(20), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSection = Win32.CreateFontW(-S(15), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontPageTitle = Win32.CreateFontW(-S(26), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontLinkStrong = Win32.CreateFontW(-S(16), 0, 0, 0, 700, 0, 1, 0, 0, 0, 0, 5, 0, "Segoe UI");
    }

    private void DestroyFonts()
    {
        Win32.DeleteObject(_hFontTitle);
        Win32.DeleteObject(_hFontSubtitle);
        Win32.DeleteObject(_hFontText);
        Win32.DeleteObject(_hFontFeatureDesc);
        Win32.DeleteObject(_hFontBold);
        Win32.DeleteObject(_hFontLink);
        Win32.DeleteObject(_hFontSmall);
        Win32.DeleteObject(_hFontButton);
        Win32.DeleteObject(_hFontBannerBold);
        Win32.DeleteObject(_hFontStepSummary);
        Win32.DeleteObject(_hFontSection);
        Win32.DeleteObject(_hFontPageTitle);
        Win32.DeleteObject(_hFontLinkStrong);
    }

    private void RecreateFonts()
    {
        DestroyFonts();
        CreateFonts();
        ApplyFontsToControls();
    }

    private void ApplyFontsToControls()
    {
        Win32.SendMessageW(_hWndBtnNext, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
        Win32.SendMessageW(_hWndBtnPrev, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
        Win32.SendMessageW(_hWndLinkBetaBanner, Win32.WM_SETFONT, _hFontLink, (IntPtr)1);
        Win32.SendMessageW(_hWndLinkGuide, Win32.WM_SETFONT, _hFontLinkStrong, (IntPtr)1);
        Win32.SendMessageW(_hWndLinkBeta, Win32.WM_SETFONT, _hFontLinkStrong, (IntPtr)1);
        Win32.SendMessageW(_hWndLinkDiscord, Win32.WM_SETFONT, _hFontLinkStrong, (IntPtr)1);
        Win32.SetWindowTextW(_hWndLinkDiscord, "Échanger avec les autres testeurs");
        Win32.SendMessageW(_hWndChkAutoStart, Win32.WM_SETFONT, _hFontBold, (IntPtr)1);
        Win32.SendMessageW(_hWndChkDontShow, Win32.WM_SETFONT, _hFontBold, (IntPtr)1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Redimensionnement et repositionnement
    // ═══════════════════════════════════════════════════════════════
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

    private void RepositionControls()
    {
        int margin = S(BASE_MARGIN);
        int winW = S(BASE_WIN_W);
        int bottomY = S(BASE_WIN_H) - S(BASE_BOTTOM_MARGIN);
        GetStep3Layout(_contentY, winW, out _, out _,
            out int linksX, out int linksWidth, out int linkStartY, out int linkRowH, out int linkControlHeight,
            out int checkboxX, out int checkboxWidth, out int checkboxY, out int checkboxSpacing, out int checkboxHeight);

        // Navigation — bouton « Suivant » dimensionné dynamiquement selon son texte courant
        var nextText = new System.Text.StringBuilder(64);
        Win32.GetWindowTextW(_hWndBtnNext, nextText, nextText.Capacity);
        var nextGeom = ComputeNextButtonGeometry(nextText.ToString(), winW, margin);
        Win32.MoveWindow(_hWndBtnNext, nextGeom.x, bottomY, nextGeom.width, S(BASE_BTN_H), true);
        Win32.MoveWindow(_hWndBtnPrev, margin, bottomY, S(BASE_BTN_W_PREV), S(BASE_BTN_H), true);

        // Étape 3 — 3 liens (Guide, Bêta, Discord) dans une grille fixe
        Win32.MoveWindow(_hWndLinkGuide, linksX, linkStartY, linksWidth, linkControlHeight, true);
        Win32.MoveWindow(_hWndLinkBeta, linksX, linkStartY + linkRowH, linksWidth, linkControlHeight, true);
        Win32.MoveWindow(_hWndLinkDiscord, linksX, linkStartY + linkRowH * 2, linksWidth, linkControlHeight, true);

        // Préférences
        Win32.MoveWindow(_hWndChkAutoStart, checkboxX, checkboxY, checkboxWidth, checkboxHeight, true);
        Win32.MoveWindow(_hWndChkDontShow, checkboxX, checkboxY + checkboxSpacing, checkboxWidth, checkboxHeight, true);
    }

    private void CreateMainWindow()
    {
        var hInstance = Win32.GetModuleHandleW(null);
        var className = "AZERTYGlobal_Onboarding";

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
        var adjustRect = new Win32.RECT { left = 0, top = 0, right = winW, bottom = winH };
        Win32.AdjustWindowRectEx(ref adjustRect, dwStyle, false, dwExStyle);
        int windowW = adjustRect.right - adjustRect.left;
        int windowH = adjustRect.bottom - adjustRect.top;

        Win32.GetCursorPos(out var cursorPt);
        var hMonitor = Win32.MonitorFromPoint(cursorPt, 0x00000001);
        var monInfo = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(hMonitor, ref monInfo);
        int screenX = monInfo.rcWork.left;
        int screenY = monInfo.rcWork.top;
        int screenW = monInfo.rcWork.right - monInfo.rcWork.left;
        int screenH = monInfo.rcWork.bottom - monInfo.rcWork.top;

        _hWnd = Win32.CreateWindowExW(dwExStyle, className, "AZERTY Global",
            dwStyle, screenX + (screenW - windowW) / 2, screenY + (screenH - windowH) / 2, windowW, windowH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        CreateControls();
        SetWindowIcon();
    }

    private void SetWindowIcon()
    {
        if (_gdipLogo == IntPtr.Zero) return;
        try
        {
            int size = 32;
            Win32.GdipCreateBitmapFromScan0(size, size, 0, 0x0026200A, IntPtr.Zero, out IntPtr bmp32);
            Win32.GdipGetImageGraphicsContext(bmp32, out IntPtr g);
            Win32.GdipSetSmoothingMode(g, 4);
            Win32.GdipSetInterpolationMode(g, 7);
            Win32.GdipDrawImageRectI(g, _gdipLogo, 0, 0, size, size);
            Win32.GdipDeleteGraphics(g);
            Win32.GdipCreateHBITMAPFromBitmap(bmp32, out IntPtr hBmp, 0x00000000);
            Win32.GdipDisposeImage(bmp32);
            var maskBits = new byte[size * size / 8];
            IntPtr hMask = Win32.CreateBitmap(size, size, 1, 1, maskBits);
            var iconInfo = new Win32.ICONINFO { fIcon = true, hbmMask = hMask, hbmColor = hBmp };
            _hIcon = Win32.CreateIconIndirect(ref iconInfo);
            Win32.DeleteObject(hMask);
            Win32.DeleteObject(hBmp);
            const uint WM_SETICON = 0x0080;
            Win32.SendMessageW(_hWnd, WM_SETICON, (IntPtr)0, _hIcon);
            Win32.SendMessageW(_hWnd, WM_SETICON, (IntPtr)1, _hIcon);
        }
        catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException) { }
    }

    // ═══════════════════════════════════════════════════════════════
    // Création des contrôles
    // ═══════════════════════════════════════════════════════════════
    private void CreateControls()
    {
        var hInstance = Win32.GetModuleHandleW(null);
        int margin = S(BASE_MARGIN);
        int winW = S(BASE_WIN_W);
        int bottomY = S(BASE_WIN_H) - S(BASE_BOTTOM_MARGIN);
        int linkH = S(28);

        // ══ Navigation ══ (largeur initiale = minimum ; ajustée dynamiquement dans UpdateStepVisibility)
        _hWndBtnNext = Win32.CreateWindowExW(0, "BUTTON", "Suivant",
            Win32.WS_CHILD | Win32.WS_VISIBLE | 0x0001 | Win32.WS_TABSTOP,
            winW - margin - S(BASE_BTN_W_NEXT_MIN), bottomY, S(BASE_BTN_W_NEXT_MIN), S(BASE_BTN_H),
            _hWnd, (IntPtr)IDC_BTN_NEXT, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnNext, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);

        _hWndBtnPrev = Win32.CreateWindowExW(0, "BUTTON", "Précédent",
            Win32.WS_CHILD | Win32.WS_TABSTOP,
            margin, bottomY, S(BASE_BTN_W_PREV), S(BASE_BTN_H),
            _hWnd, (IntPtr)IDC_BTN_PREV, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndBtnPrev, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);

        // ══ Étape 1 — Lien bandeau bêta ══
        // Position initiale temporaire — repositionné dynamiquement dans UpdateStepVisibility
        _hWndLinkBetaBanner = Win32.CreateWindowExW(0, "STATIC", "donnez votre avis",
            Win32.WS_CHILD | Win32.WS_VISIBLE | SS_NOTIFY | Win32.WS_TABSTOP,
            margin, 0, S(160), S(26),
            _hWnd, (IntPtr)IDC_LINK_BETA_BANNER, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndLinkBetaBanner, Win32.WM_SETFONT, _hFontLink, (IntPtr)1);
        Win32.SetWindowSubclass(_hWndLinkBetaBanner, _linkSubclassProc, (UIntPtr)10, IntPtr.Zero);

        // ══ Étape 3 — Liens et checkboxes ══
        // Positions initiales temporaires — repositionnés dans RepositionControls
        int y = 0;
        _hWndLinkGuide = Win32.CreateWindowExW(0, "STATIC", "Guide de prise en main",
            Win32.WS_CHILD | SS_NOTIFY | Win32.WS_TABSTOP, margin, y, S(200), linkH,
            _hWnd, (IntPtr)IDC_LINK_GUIDE, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndLinkGuide, Win32.WM_SETFONT, _hFontLinkStrong, (IntPtr)1);
        Win32.SetWindowSubclass(_hWndLinkGuide, _linkSubclassProc, (UIntPtr)1, IntPtr.Zero);

        _hWndLinkBeta = Win32.CreateWindowExW(0, "STATIC", "Donner son avis sur la bêta",
            Win32.WS_CHILD | SS_NOTIFY | Win32.WS_TABSTOP, margin, y, S(280), linkH,
            _hWnd, (IntPtr)IDC_LINK_BETA, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndLinkBeta, Win32.WM_SETFONT, _hFontLinkStrong, (IntPtr)1);
        Win32.SetWindowSubclass(_hWndLinkBeta, _linkSubclassProc, (UIntPtr)3, IntPtr.Zero);

        _hWndLinkDiscord = Win32.CreateWindowExW(0, "STATIC", "Discord — Échanger avec les testeurs",
            Win32.WS_CHILD | SS_NOTIFY | Win32.WS_TABSTOP, margin, y, S(380), linkH,
            _hWnd, (IntPtr)IDC_LINK_DISCORD, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndLinkDiscord, Win32.WM_SETFONT, _hFontLinkStrong, (IntPtr)1);
        Win32.SetWindowSubclass(_hWndLinkDiscord, _linkSubclassProc, (UIntPtr)5, IntPtr.Zero);

        _hWndChkAutoStart = Win32.CreateWindowExW(0, "BUTTON", "Lancer au démarrage de Windows",
            Win32.WS_CHILD | BS_AUTOCHECKBOX | Win32.WS_TABSTOP,
            margin, y, S(320), S(26),
            _hWnd, (IntPtr)IDC_CHK_AUTOSTART, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndChkAutoStart, Win32.WM_SETFONT, _hFontBold, (IntPtr)1);
        RefreshAutoStartCheckbox();

        _hWndChkDontShow = Win32.CreateWindowExW(0, "BUTTON", "Ne plus afficher cet écran au démarrage",
            Win32.WS_CHILD | BS_AUTOCHECKBOX | Win32.WS_TABSTOP,
            margin, y, S(280), S(26),
            _hWnd, (IntPtr)IDC_CHK_DONT_SHOW, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndChkDontShow, Win32.WM_SETFONT, _hFontBold, (IntPtr)1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Visibilité des contrôles selon l'étape
    // ═══════════════════════════════════════════════════════════════
    private void UpdateStepVisibility()
    {
        Win32.ShowWindow(_hWndLinkBetaBanner, _currentStep == 0 ? 1 : 0);

        int step3Vis = _currentStep == 2 ? 1 : 0;
        Win32.ShowWindow(_hWndLinkGuide, step3Vis);
        Win32.ShowWindow(_hWndLinkBeta, step3Vis);
        Win32.ShowWindow(_hWndLinkDiscord, step3Vis);
        Win32.ShowWindow(_hWndChkAutoStart, step3Vis);
        Win32.ShowWindow(_hWndChkDontShow, step3Vis);

        Win32.ShowWindow(_hWndBtnPrev, _currentStep > 0 ? 1 : 0);
        string nextText = _currentStep == 2 ? "C'est parti !"
            : (_currentStep == 0 && !_learningModuleDone ? "Essayer maintenant" : "Suivant");
        Win32.SetWindowTextW(_hWndBtnNext, nextText);

        // Redimensionner le bouton selon le nouveau texte
        int btnWinW = S(BASE_WIN_W);
        int btnMargin = S(BASE_MARGIN);
        int btnBottomY = S(BASE_WIN_H) - S(BASE_BOTTOM_MARGIN);
        var nextGeom = ComputeNextButtonGeometry(nextText, btnWinW, btnMargin);
        Win32.MoveWindow(_hWndBtnNext, nextGeom.x, btnBottomY, nextGeom.width, S(BASE_BTN_H), true);

        // Repositionner le lien bandeau bêta (étape 1)
        // Le positionnement précis est fait dans PaintStep1 après mesure du texte,
        // mais on doit initialiser la position ici pour que le lien soit dans la zone visible
        if (_currentStep == 0)
        {
            int margin = S(BASE_MARGIN);
            int bannerTextX = margin + S(14);
            int bannerLine2Y = _contentY + S(32);
            Win32.MoveWindow(_hWndLinkBetaBanner, bannerTextX, bannerLine2Y, S(160), S(26), true);
        }

        if (_currentStep == 2)
            RepositionControls();

        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    public void Show()
    {
        _currentStep = 0;
        RefreshAutoStartCheckbox();
        Win32.SendMessageW(_hWndChkDontShow, BM_SETCHECK,
            ConfigManager.ShowOnboardingAtStartup ? IntPtr.Zero : (IntPtr)BST_CHECKED, IntPtr.Zero);
        UpdateStepVisibility();
        Win32.ShowWindow(_hWnd, 1);
        Win32.SetForegroundWindow(_hWnd);
        _visible = true;
    }

    /// <summary>Remet l'onboarding à zéro (étape 1, exercice non fait) et réautorise son affichage automatique.</summary>
    public void ResetState()
    {
        _currentStep = 0;
        _learningModuleDone = false;
        ConfigManager.SetShowOnboardingAtStartup(true);
    }

    public void Close()
    {
        var checkState = Win32.SendMessageW(_hWndChkDontShow, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero);
        ConfigManager.SetShowOnboardingAtStartup(checkState != (IntPtr)BST_CHECKED);

        var autoStartState = Win32.SendMessageW(_hWndChkAutoStart, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero);
        bool autoStartSaved = AutoStart.Set(autoStartState == (IntPtr)BST_CHECKED);
        RefreshAutoStartCheckbox();
        if (!autoStartSaved)
            ShowAutoStartError();

        Win32.ShowWindow(_hWnd, 0);
        _visible = false;
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

            case Win32.WM_DPICHANGED:
            {
                int newDpi = (wParam.ToInt32() >> 16) & 0xFFFF;
                if (newDpi > 0)
                    _dpiScale = newDpi / 96f;
                RecreateFonts();
                var suggested = Marshal.PtrToStructure<Win32.RECT>(lParam);
                Win32.MoveWindow(_hWnd, suggested.left, suggested.top,
                    suggested.right - suggested.left, suggested.bottom - suggested.top, true);
                RepositionControls();
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                return IntPtr.Zero;
            }

            case Win32.WM_COMMAND:
                int id = wParam.ToInt32() & 0xFFFF;
                int code = (wParam.ToInt32() >> 16) & 0xFFFF;
                switch (id)
                {
                    case IDC_BTN_NEXT:
                        if (_currentStep == 0 && !_learningModuleDone)
                        {
                            // Étape 1 → lancer le mini-module d'apprentissage
                            // L'utilisateur reste sur l'étape 1 ; le bouton se transformera en
                            // « Suivant » une fois le module fermé (cf. callback OnClosed dans LaunchLearningModule).
                            LaunchLearningModule();
                        }
                        else if (_currentStep < 2) { _currentStep++; UpdateStepVisibility(); }
                        else Close();
                        break;
                    case IDC_BTN_PREV:
                        if (_currentStep > 0) { _currentStep--; UpdateStepVisibility(); }
                        break;
                    case IDC_LINK_BETA_BANNER: case IDC_LINK_BETA:
                        if (code == 0) OpenLink("https://azerty.global/beta"); break;
                    case IDC_LINK_GUIDE:
                        if (code == 0) OpenLink("https://azerty.global/guide"); break;
                    case IDC_LINK_DISCORD:
                        if (code == 0) OpenLink("https://discord.gg/nYknqshJz3"); break;
                }
                return IntPtr.Zero;

            case Win32.WM_CTLCOLORSTATIC:
            {
                IntPtr hdcStatic = wParam;
                IntPtr hCtrl = lParam;
                if (hCtrl == _hWndLinkGuide ||
                    hCtrl == _hWndLinkBeta || hCtrl == _hWndLinkDiscord)
                {
                    Win32.SetBkMode(hdcStatic, 1);
                    bool isActive = _hoveredLink == hCtrl || Win32.GetFocus() == hCtrl;
                    Win32.SetTextColor(hdcStatic, isActive ? CLR_LINK_HOVER : CLR_LINK);
                    return _hPanelBrush;
                }
                if (hCtrl == _hWndLinkBetaBanner)
                {
                    Win32.SetBkMode(hdcStatic, 1);
                    bool isActive = _hoveredLink == hCtrl || Win32.GetFocus() == hCtrl;
                    Win32.SetTextColor(hdcStatic, isActive ? CLR_LINK_HOVER : CLR_LINK);
                    return _hBannerBgBrush;
                }
                if (hCtrl == _hWndChkAutoStart || hCtrl == _hWndChkDontShow)
                {
                    Win32.SetBkMode(hdcStatic, 1);
                    Win32.SetTextColor(hdcStatic, CLR_TEXT);
                    return _hPanelBrush;
                }
                Win32.SetBkMode(hdcStatic, 1);
                Win32.SetTextColor(hdcStatic, 0x00888888);
                return _hBgBrush;
            }

            case Win32.WM_SETCURSOR:
                if (wParam == _hWndLinkGuide ||
                    wParam == _hWndLinkBeta || wParam == _hWndLinkDiscord ||
                    wParam == _hWndLinkBetaBanner)
                {
                    Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, (IntPtr)32649));
                    return (IntPtr)1;
                }
                break;

            case Win32.WM_KEYDOWN:
                if (wParam == (IntPtr)0x1B) // VK_ESCAPE
                {
                    Close();
                    return IntPtr.Zero;
                }
                break;

            case Win32.WM_CLOSE:
                Close();
                return IntPtr.Zero;
        }
        }
        catch (Exception ex)
        {
            ConfigManager.Log("Onboarding WndProc", ex);
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void RefreshAutoStartCheckbox()
    {
        Win32.SendMessageW(_hWndChkAutoStart, BM_SETCHECK,
            AutoStart.IsRegistered ? (IntPtr)BST_CHECKED : IntPtr.Zero, IntPtr.Zero);
    }

    private void ShowAutoStartError()
    {
        Win32.MessageBoxW(_hWnd,
            AutoStart.GetFailureMessage(),
            "AZERTY Global \u2014 Erreur", 0x10);
    }

    private void OpenLink(string url)
    {
        Win32.ShellExecuteW(IntPtr.Zero, "open", url, null, null, 1);
        Win32.SetWindowPos(_hWnd, (IntPtr)(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
    }

    private void LaunchLearningModule()
    {
        if (Mapper == null || Hook == null || AppLayout == null) return;

        _learningModule?.Dispose();
        _learningModule = new LearningModule(_hWnd, Mapper, Hook, AppLayout);
        _learningModule.OnClosed = () =>
        {
            _learningModuleDone = true;
            _learningModule?.Dispose();
            _learningModule = null;
            // Mettre à jour le bouton (« Essayer maintenant » → « Suivant ») et redessiner
            UpdateStepVisibility();
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
        };
        _learningModule.Show();
    }

    // ═══════════════════════════════════════════════════════════════
    // Sous-classe liens (hover)
    // ═══════════════════════════════════════════════════════════════
    private IntPtr LinkSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEMOVE:
                if (_hoveredLink != hWnd)
                {
                    _hoveredLink = hWnd;
                    Win32.InvalidateRect(hWnd, IntPtr.Zero, true);
                    var tme = new Win32.TRACKMOUSEEVENT
                    {
                        cbSize = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(),
                        dwFlags = Win32.TME_LEAVE,
                        hwndTrack = hWnd
                    };
                    Win32.TrackMouseEvent(ref tme);
                }
                break;
            case Win32.WM_MOUSELEAVE:
                if (_hoveredLink == hWnd)
                {
                    _hoveredLink = IntPtr.Zero;
                    Win32.InvalidateRect(hWnd, IntPtr.Zero, true);
                }
                break;
            case 0x0087: // WM_GETDLGCODE — permet au STATIC de recevoir les touches
                return (IntPtr)0x0004; // DLGC_WANTALLKEYS
            case Win32.WM_KEYDOWN:
                if (wParam == (IntPtr)0x0D) // VK_RETURN
                {
                    int ctrlId = Win32.GetDlgCtrlID(hWnd);
                    Win32.SendMessageW(_hWnd, Win32.WM_COMMAND, (IntPtr)ctrlId, hWnd);
                    return IntPtr.Zero;
                }
                break;
            case Win32.WM_SETFOCUS:
            case Win32.WM_KILLFOCUS:
                Win32.InvalidateRect(hWnd, IntPtr.Zero, true);
                break;
        }
        return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    // ═══════════════════════════════════════════════════════════════
    // Rendu — Dispatcher
    // ═══════════════════════════════════════════════════════════════
    private void OnPaint(IntPtr hWnd)
    {
        var hdcPaint = Win32.BeginPaint(hWnd, out var ps);
        Win32.GetClientRect(hWnd, out var clientRect);
        int cw = clientRect.right;
        int ch = clientRect.bottom;

        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        var hdc = Win32.CreateCompatibleDC(hdcScreen);
        var hBmp = Win32.CreateCompatibleBitmap(hdcScreen, cw, ch);
        var hBmpOld = Win32.SelectObject(hdc, hBmp);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);

        Win32.FillRect(hdc, ref clientRect, _hBgBrush);
        Win32.SetBkMode(hdc, 1);

        Win32.GdipCreateFromHDC(hdc, out IntPtr gfx);
        Win32.GdipSetSmoothingMode(gfx, 4);
        Win32.GdipSetInterpolationMode(gfx, 7);
        Win32.GdipSetTextRenderingHint(gfx, 5);


        int y = S(10);
        DrawHeader(hdc, gfx, cw, ref y);
        DrawProgressBar(hdc, cw, ref y);
        _contentY = y; // Stocker pour le positionnement des contrôles

        switch (_currentStep)
        {
            case 0: PaintStep1(hdc, cw, ch, y); break;
            case 1: PaintStep2(hdc, gfx, cw, ch, y); break;
            case 2: PaintStep3(hdc, gfx, cw, ch, y); break;
        }

        Win32.GdipDeleteGraphics(gfx);
        Win32.BitBlt(hdcPaint, 0, 0, cw, ch, hdc, 0, 0, Win32.SRCCOPY);
        Win32.SelectObject(hdc, hBmpOld);
        Win32.DeleteObject(hBmp);
        Win32.DeleteDC(hdc);
        Win32.EndPaint(hWnd, ref ps);
    }

    // ═══════════════════════════════════════════════════════════════
    // Barre de progression en haut (3 segments)
    // ═══════════════════════════════════════════════════════════════
    private void DrawProgressBar(IntPtr hdc, int cw, ref int y)
    {
        int margin = S(BASE_MARGIN);
        int barY = y + S(8);
        int barH = S(4);
        int barW = cw - margin * 2;
        int segW = barW / 3;

        var trackRect = new Win32.RECT { left = margin, top = barY, right = margin + barW, bottom = barY + barH };
        GdiHelpers.FillSolidRect(hdc, trackRect, CLR_PROGRESS_INACTIVE);

        for (int i = 0; i < 3; i++)
        {
            if (i > _currentStep) continue;
            int left = margin + i * segW + (i > 0 ? 1 : 0);
            int right = (i == 2) ? margin + barW : margin + (i + 1) * segW;
            var rect = new Win32.RECT { left = left, top = barY, right = right, bottom = barY + barH };
            GdiHelpers.FillSolidRect(hdc, rect, CLR_PROGRESS_ACTIVE);
        }

        y = barY + barH + S(16);
    }

    // ═══════════════════════════════════════════════════════════════
    // Header
    // ═══════════════════════════════════════════════════════════════
    private void DrawHeader(IntPtr hdc, IntPtr gfx, int cw, ref int y)
    {
        int margin = S(BASE_MARGIN);
        int logoSize = S(44);
        int textX = margin;
        int titleTop = y + S(4);
        int titleBottom = y + S(36);
        int subtitleTop = y + S(38);
        int subtitleBottom = y + S(62);
        int logoY = titleTop + Math.Max(0, (subtitleBottom - titleTop - logoSize) / 2);

        if (_gdipLogo != IntPtr.Zero)
        {
            Win32.GdipDrawImageRectI(gfx, _gdipLogo, margin, logoY, logoSize, logoSize);
            textX = margin + logoSize + S(12);
        }

        Win32.SelectObject(hdc, _hFontSubtitle);
        Win32.SetTextColor(hdc, 0x00888888);
        string versionText = "v" + Program.Version;
        int versionWidth = MeasureSingleLineWidth(hdc, _hFontSubtitle, versionText) + S(8);
        int versionLeft = cw - margin - versionWidth;
        var versionRect = new Win32.RECT
        {
            left = versionLeft,
            top = y + S(8),
            right = cw - margin,
            bottom = y + S(32)
        };
        Win32.DrawTextW(hdc, versionText, -1, ref versionRect,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        Win32.SelectObject(hdc, _hFontTitle);
        Win32.SetTextColor(hdc, CLR_TITLE);
        var titleRect = new Win32.RECT
        {
            left = textX,
            top = titleTop,
            right = Math.Max(textX, versionLeft - S(8)),
            bottom = titleBottom
        };
        Win32.DrawTextW(hdc, "AZERTY Global", -1, ref titleRect,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        Win32.SelectObject(hdc, _hFontSubtitle);
        Win32.SetTextColor(hdc, CLR_TEXT);
        var subtitleRect = new Win32.RECT
        {
            left = textX,
            top = subtitleTop,
            right = cw - margin,
            bottom = subtitleBottom
        };
        Win32.DrawTextW(hdc, "Votre clavier est maintenant amélioré.", -1, ref subtitleRect,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        int headerBottom = Math.Max(logoY + logoSize, subtitleRect.bottom) + S(2);
        y = headerBottom;

        var sepBrush = Win32.CreateSolidBrush(0x00D0D0D0);
        var sepRect = new Win32.RECT { left = margin, top = y + S(24), right = cw - margin, bottom = y + S(25) };
        Win32.FillRect(hdc, ref sepRect, sepBrush);
        Win32.DeleteObject(sepBrush);
        y += S(26);
    }

    // ═══════════════════════════════════════════════════════════════
    // Étape 1 — Les 5 améliorations + bandeau bêta
    // ═══════════════════════════════════════════════════════════════
    private void GetStep3Layout(int topY, int winW,
        out Win32.RECT resourcesPanel, out Win32.RECT prefsPanel,
        out int linksX, out int linksWidth, out int linkStartY, out int linkRowH, out int linkControlHeight,
        out int checkboxX, out int checkboxWidth, out int checkboxY, out int checkboxSpacing, out int checkboxHeight)
    {
        int margin = S(BASE_MARGIN);
        int panelWidth = winW - margin * 2;
        int panelPaddingX = S(18);
        int resourcePaddingTop = S(16);
        int resourcePaddingBottom = S(12);
        int prefsPaddingTop = S(16);
        int prefsPaddingBottom = S(16);
        int panelGap = S(14);

        IntPtr hdc = Win32.GetDC(_hWnd);
        try
        {
            int pageTitleHeight = MeasureSingleLineHeight(hdc, _hFontPageTitle);
            linkControlHeight = Math.Max(S(28), MeasureSingleLineHeight(hdc, _hFontLinkStrong) + S(6));
            linkRowH = linkControlHeight + S(2);
            // Le panel ressources contient désormais 3 liens (Guide, Bêta, Discord)
            int resourcesHeight = resourcePaddingTop + linkRowH * 3 + resourcePaddingBottom;
            int panelTop = topY + pageTitleHeight + S(12);

            resourcesPanel = new Win32.RECT
            {
                left = margin,
                top = panelTop,
                right = margin + panelWidth,
                bottom = panelTop + resourcesHeight
            };

            linksX = resourcesPanel.left + panelPaddingX;
            linksWidth = panelWidth - panelPaddingX * 2;
            linkStartY = resourcesPanel.top + resourcePaddingTop;

            checkboxX = margin + panelPaddingX;
            checkboxWidth = panelWidth - panelPaddingX * 2;
            checkboxHeight = Math.Max(S(26), MeasureSingleLineHeight(hdc, _hFontBold) + S(10));
            int prefsTitleHeight = MeasureSingleLineHeight(hdc, _hFontPageTitle);
            int prefsTop = resourcesPanel.bottom + panelGap + prefsTitleHeight + S(8);
            checkboxY = prefsTop + prefsPaddingTop;
            checkboxSpacing = checkboxHeight + S(10);
            int prefsHeight = prefsPaddingTop + checkboxHeight * 2 + S(10) + prefsPaddingBottom;
            prefsPanel = new Win32.RECT
            {
                left = margin,
                top = prefsTop,
                right = margin + panelWidth,
                bottom = prefsTop + prefsHeight
            };
        }
        finally
        {
            Win32.ReleaseDC(_hWnd, hdc);
        }
    }

    // Méthodes GDI factorisées dans GdiHelpers.cs — wrappers d'instance pour le DPI scaling
    private int MeasureTextHeight(IntPtr hdc, IntPtr hFont, string text, int width,
        uint format = Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX)
        => GdiHelpers.MeasureTextHeight(hdc, hFont, text, width, format);

    private int MeasureSingleLineWidth(IntPtr hdc, IntPtr hFont, string text)
        => GdiHelpers.MeasureSingleLineWidth(hdc, hFont, text);

    /// <summary>
    /// Mesure le texte du bouton « Suivant / Essayer maintenant / C'est parti ! » et
    /// retourne sa position X (alignée à droite) et sa largeur (≥ BASE_BTN_W_NEXT_MIN).
    /// </summary>
    private (int x, int width) ComputeNextButtonGeometry(string text, int winW, int margin)
    {
        IntPtr hdc = Win32.GetDC(_hWnd);
        try
        {
            int textWidth = MeasureSingleLineWidth(hdc, _hFontButton, text);
            int width = Math.Max(S(BASE_BTN_W_NEXT_MIN), textWidth + S(BASE_BTN_TEXT_PAD * 2));
            int x = winW - margin - width;
            return (x, width);
        }
        finally
        {
            Win32.ReleaseDC(_hWnd, hdc);
        }
    }

    private int MeasureSingleLineHeight(IntPtr hdc, IntPtr hFont)
        => GdiHelpers.MeasureSingleLineHeight(hdc, hFont);

    private void PaintStep1(IntPtr hdc, int cw, int ch, int y)
    {
        int margin = S(BASE_MARGIN);
        int bannerTextX = margin + S(14);

        // ── Bandeau bêta ──
        int bannerH = S(72);
        var bannerRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + bannerH };
        Win32.FillRect(hdc, ref bannerRect, _hBannerBgBrush);

        var borderBrush = Win32.CreateSolidBrush(CLR_BANNER_BORDER);
        var borderRect = new Win32.RECT { left = margin, top = y, right = margin + S(4), bottom = y + bannerH };
        Win32.FillRect(hdc, ref borderRect, borderBrush);
        Win32.DeleteObject(borderBrush);

        Win32.SelectObject(hdc, _hFontBannerBold);
        Win32.SetTextColor(hdc, CLR_BANNER_TITLE);
        int line1Y = y + S(9);
        var bannerLine1 = new Win32.RECT { left = bannerTextX, top = line1Y, right = cw - margin - S(8), bottom = line1Y + S(28) };
        Win32.DrawTextW(hdc, "Version bêta", -1, ref bannerLine1, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        // Ligne 2 : "Après quelques jours d'utilisation, " (GDI) + "donnez votre avis" (lien STATIC)
        int line2Y = line1Y + S(28);
        string prefix = "Après quelques jours d'utilisation, ";
        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, CLR_BANNER_TEXT);
        var prefixRect = new Win32.RECT { left = bannerTextX, top = line2Y, right = cw - margin, bottom = line2Y + S(24) };
        Win32.DrawTextW(hdc, prefix, -1, ref prefixRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        // Mesurer le préfixe et repositionner le lien
        var measurePrefix = new Win32.RECT { left = 0, top = 0, right = 9999, bottom = 9999 };
        Win32.DrawTextW(hdc, prefix, -1, ref measurePrefix, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_CALCRECT);
        Win32.SetWindowPos(_hWndLinkBetaBanner, IntPtr.Zero,
            bannerTextX + measurePrefix.right, line2Y, S(160), S(26),
            0x0004 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE

        y += bannerH + S(18);

        // ── Titre ──
        Win32.SelectObject(hdc, _hFontStepSummary);
        Win32.SetTextColor(hdc, CLR_TITLE);
        var stepTitleRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + S(28) };
        Win32.DrawTextW(hdc, "5 améliorations, 99 % de vos habitudes préservées", -1, ref stepTitleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += S(40);

        // ── Les 5 améliorations ──
        DrawFeatureWithHighlight(hdc, margin, cw, ref y, "1",
            "Verrouillage Majuscule intelligent",
            "Verr. Maj. + é è ç à \u2192 É È Ç À.");
        DrawFeature(hdc, margin, cw, ref y, "2",
            "Point en accès direct",
            "Le point et le point-virgule échangent leurs places.");
        DrawFeature(hdc, margin, cw, ref y, "3",
            "@ et # sur la touche en haut à gauche",
            "Accès direct sans AltGr.");
        DrawFeatureWithHighlight(hdc, margin, cw, ref y, "4",
            "Symboles de programmation accessibles",
            "{ } [ ] \\ | sur la rangée de repos avec AltGr.");
        DrawFeatureWithHighlight(hdc, margin, cw, ref y, "5",
            "Accents internationaux",
            "Accents aigu, grave et tilde sur la touche à droite du M.");

        // ── Mention rassurante (vie privée) ──
        y += S(8);
        Win32.SelectObject(hdc, _hFontSmall);
        Win32.SetTextColor(hdc, CLR_REASSURE);
        const string reassure = "Cette application améliore votre clavier. Aucune frappe n'est enregistrée ni transmise.";
        var reassureRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + S(40) };
        Win32.DrawTextW(hdc, reassure, -1, ref reassureRect,
            Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX);
    }

    private void DrawFeature(IntPtr hdc, int margin, int cw, ref int y, string number, string title, string description)
    {
        DrawStepCard(hdc, margin, cw, ref y, number, title, description);
    }

    // ═══════════════════════════════════════════════════════════════
    // Étape 2
    // ═══════════════════════════════════════════════════════════════
    private void PaintStep2(IntPtr hdc, IntPtr gfx, int cw, int ch, int y)
    {
        int margin = S(BASE_MARGIN);

        Win32.SelectObject(hdc, _hFontStepSummary);
        Win32.SetTextColor(hdc, CLR_TITLE);
        var stepTitleRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + S(28) };
        Win32.DrawTextW(hdc, "Comment utiliser AZERTY Global", -1, ref stepTitleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += S(38);

        DrawStepCard(hdc, margin, cw, ref y, "1",
            "L'icône AG est dans la barre des tâches",
            "Elle indique si le remapping est actif. Clic droit pour accéder aux options.");

        DrawToggleStepCard(hdc, margin, cw, ref y);

        DrawStepCardWithRuns(hdc, margin, cw, ref y, "3",
            "Explorez avec le clavier virtuel",
            GetShortcutRuns(null, "Ctrl + Maj + Q", " pour voir tous les caractères disponibles."));

        DrawStepCardWithRuns(hdc, margin, cw, ref y, "4",
            "Recherchez n'importe quel caractère",
            GetShortcutRuns(null, "Ctrl + Maj + W", " puis tapez le nom d'un caractère pour le copier et voir comment le taper sur le clavier virtuel."));
    }

    // ═══════════════════════════════════════════════════════════════
    // Étape 3
    // ═══════════════════════════════════════════════════════════════
    private void DrawBadge(IntPtr hdc, int x, int y, string number)
    {
        int badgeW = S(34);
        int badgeH = S(24);
        var badgeRect = new Win32.RECT { left = x, top = y, right = x + badgeW, bottom = y + badgeH };
        GdiHelpers.FillSolidRect(hdc, badgeRect, CLR_BADGE_BG);

        Win32.SelectObject(hdc, _hFontSmall);
        Win32.SetTextColor(hdc, CLR_BADGE_TEXT);
        Win32.DrawTextW(hdc, number, -1, ref badgeRect,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
    }

    private void DrawPill(IntPtr hdc, int x, int y, string text)
    {
        int pillPadX = S(10);
        int pillH = S(24);
        int pillW = MeasureSingleLineWidth(hdc, _hFontSmall, text) + pillPadX * 2;
        var pillRect = new Win32.RECT { left = x, top = y, right = x + pillW, bottom = y + pillH };
        GdiHelpers.FillSolidRect(hdc, pillRect, CLR_PILL_BG);

        Win32.SelectObject(hdc, _hFontSmall);
        Win32.SetTextColor(hdc, CLR_PILL_TEXT);
        Win32.DrawTextW(hdc, text, -1, ref pillRect,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
    }

    private (string Text, uint Color, IntPtr Font)[] GetStyledDescriptionRuns(string number, string description)
    {
        return number switch
        {
            "1" => new (string Text, uint Color, IntPtr Font)[]
            {
                ("Verr. Maj. + ", CLR_TEXT, _hFontText),
                ("\u00E9 \u00E8 \u00E7 \u00E0", CLR_INLINE_HIGHLIGHT, _hFontBold),
                (" \u2192 ", CLR_TEXT, _hFontText),
                ("\u00C9 \u00C8 \u00C7 \u00C0", CLR_INLINE_HIGHLIGHT, _hFontBold),
                (".", CLR_TEXT, _hFontText)
            },
            "4" => new (string Text, uint Color, IntPtr Font)[]
            {
                ("{ } [ ] \\ |", CLR_INLINE_HIGHLIGHT, _hFontBold),
                (" sur la rang\u00E9e de repos avec AltGr.", CLR_TEXT, _hFontText)
            },
            "5" => new (string Text, uint Color, IntPtr Font)[]
            {
                ("Accents aigu, grave et tilde sur la touche \u00E0 droite du M.", CLR_TEXT, _hFontText)
            },
            _ => new (string Text, uint Color, IntPtr Font)[]
            {
                (description, CLR_TEXT, _hFontText)
            }
        };
    }

    private (string Text, uint Color, IntPtr Font)[] GetShortcutRuns(string? prefix, string shortcut, string? suffix = null)
    {
        var runs = new System.Collections.Generic.List<(string Text, uint Color, IntPtr Font)>();

        if (!string.IsNullOrEmpty(prefix))
            runs.Add((prefix, CLR_TEXT, _hFontText));

        string[] keys = shortcut.Split(" + ");
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0)
                runs.Add((" + ", CLR_TEXT, _hFontText));

            runs.Add((keys[i], CLR_INLINE_HIGHLIGHT, _hFontBold));
        }

        if (!string.IsNullOrEmpty(suffix))
            runs.Add((suffix, CLR_TEXT, _hFontText));

        return runs.ToArray();
    }

    private void DrawStepCardWithRuns(IntPtr hdc, int margin, int cw, ref int y, string number, string title,
        params (string Text, uint Color, IntPtr Font)[] descriptionRuns)
    {
        int cardTop = y;
        int cardPaddingX = S(16);
        int cardPaddingY = S(12);
        int badgeW = S(34);
        int badgeGap = S(16);
        int contentWidth = cw - margin * 2;
        int textX = margin + cardPaddingX + badgeW + badgeGap;
        int textWidth = contentWidth - cardPaddingX * 2 - badgeW - badgeGap;
        int titleHeight = S(24);
        int descHeight = GdiHelpers.MeasureColoredRunsHeight(hdc, textWidth, S(22), descriptionRuns);
        int cardHeight = Math.Max(S(78), cardPaddingY * 2 + titleHeight + descHeight + S(4));

        var cardRect = new Win32.RECT { left = margin, top = cardTop, right = cw - margin, bottom = cardTop + cardHeight };
        GdiHelpers.DrawPanel(hdc, cardRect, CLR_PANEL_BG, CLR_PANEL_BORDER, CLR_BADGE_BG, S(4));
        DrawBadge(hdc, margin + cardPaddingX, cardTop + cardPaddingY, number);

        Win32.SelectObject(hdc, _hFontBold);
        Win32.SetTextColor(hdc, CLR_STEP_TITLE);
        var titleRect = new Win32.RECT
        {
            left = textX,
            top = cardTop + cardPaddingY - S(1),
            right = textX + textWidth,
            bottom = cardTop + cardPaddingY + titleHeight
        };
        Win32.DrawTextW(hdc, title, -1, ref titleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        GdiHelpers.DrawColoredRuns(hdc, textX, cardTop + cardPaddingY + titleHeight, textWidth, S(22), descriptionRuns);

        y += cardHeight + S(10);
    }

    private void DrawFeatureWithHighlight(IntPtr hdc, int margin, int cw, ref int y, string number, string title, string description)
    {
        DrawStepCardWithRuns(hdc, margin, cw, ref y, number, title, GetStyledDescriptionRuns(number, description));
    }

    private void DrawStepCard(IntPtr hdc, int margin, int cw, ref int y, string number, string title, string description)
    {
        int cardTop = y;
        int cardPaddingX = S(16);
        int cardPaddingY = S(12);
        int badgeW = S(34);
        int badgeGap = S(16);
        int contentWidth = cw - margin * 2;
        int textX = margin + cardPaddingX + badgeW + badgeGap;
        int textWidth = contentWidth - cardPaddingX * 2 - badgeW - badgeGap;
        int titleHeight = S(24);
        int descHeight = MeasureTextHeight(hdc, _hFontText, description, textWidth);
        int cardHeight = Math.Max(S(78), cardPaddingY * 2 + titleHeight + descHeight + S(4));

        var cardRect = new Win32.RECT { left = margin, top = cardTop, right = cw - margin, bottom = cardTop + cardHeight };
        GdiHelpers.DrawPanel(hdc, cardRect, CLR_PANEL_BG, CLR_PANEL_BORDER, CLR_BADGE_BG, S(4));
        DrawBadge(hdc, margin + cardPaddingX, cardTop + cardPaddingY, number);

        Win32.SelectObject(hdc, _hFontBold);
        Win32.SetTextColor(hdc, CLR_STEP_TITLE);
        var titleRect = new Win32.RECT
        {
            left = textX,
            top = cardTop + cardPaddingY - S(1),
            right = textX + textWidth,
            bottom = cardTop + cardPaddingY + titleHeight
        };
        Win32.DrawTextW(hdc, title, -1, ref titleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, CLR_TEXT);
        var descRect = new Win32.RECT
        {
            left = textX,
            top = cardTop + cardPaddingY + titleHeight,
            right = textX + textWidth,
            bottom = cardTop + cardHeight - cardPaddingY
        };
        Win32.DrawTextW(hdc, description, -1, ref descRect, Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX);

        y += cardHeight + S(10);
    }

    private void DrawToggleStepCard(IntPtr hdc, int margin, int cw, ref int y)
    {
        int cardTop = y;
        int cardPaddingX = S(16);
        int cardPaddingY = S(12);
        int badgeW = S(34);
        int badgeGap = S(16);
        int contentWidth = cw - margin * 2;
        int textX = margin + cardPaddingX + badgeW + badgeGap;
        int textWidth = contentWidth - cardPaddingX * 2 - badgeW - badgeGap;
        int titleHeight = S(24);
        var shortcutRuns = GetShortcutRuns("Raccourci : ", "Ctrl + Maj + Verr. Maj.");
        int shortcutHeight = GdiHelpers.MeasureColoredRunsHeight(hdc, textWidth, S(22), shortcutRuns);
        int cardHeight = Math.Max(S(78), cardPaddingY * 2 + titleHeight + shortcutHeight + S(10));

        var cardRect = new Win32.RECT { left = margin, top = cardTop, right = cw - margin, bottom = cardTop + cardHeight };
        GdiHelpers.DrawPanel(hdc, cardRect, CLR_PANEL_BG, CLR_PANEL_BORDER, CLR_BADGE_BG, S(4));
        DrawBadge(hdc, margin + cardPaddingX, cardTop + cardPaddingY, "2");

        Win32.SelectObject(hdc, _hFontBold);
        Win32.SetTextColor(hdc, CLR_STEP_TITLE);
        var titleRect = new Win32.RECT
        {
            left = textX,
            top = cardTop + cardPaddingY - S(1),
            right = textX + textWidth,
            bottom = cardTop + cardPaddingY + titleHeight
        };
        Win32.DrawTextW(hdc, "Activez / désactivez à tout moment", -1, ref titleRect,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        int lineY = cardTop + cardPaddingY + titleHeight + S(4);
        GdiHelpers.DrawColoredRuns(hdc, textX, lineY, textWidth, S(22), shortcutRuns);

        y += cardHeight + S(10);
    }

    private void PaintStep3(IntPtr hdc, IntPtr gfx, int cw, int ch, int y)
    {
        int margin = S(BASE_MARGIN);
        GetStep3Layout(y, cw, out var resourcesPanel, out var prefsPanel,
            out int linksX, out int linksWidth, out int linkStartY, out int linkRowH, out _,
            out int checkboxX, out int checkboxWidth, out int checkboxY, out int checkboxSpacing, out _);

        Win32.SelectObject(hdc, _hFontPageTitle);
        Win32.SetTextColor(hdc, CLR_TITLE);
        var stepTitleRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = resourcesPanel.top - S(12) };
        Win32.DrawTextW(hdc, "Ressources & communauté", -1, ref stepTitleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        var prefsTitleRect = new Win32.RECT
        {
            left = margin,
            top = resourcesPanel.bottom + S(14),
            right = cw - margin,
            bottom = prefsPanel.top - S(8)
        };
        Win32.DrawTextW(hdc, "Préférences", -1, ref prefsTitleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        GdiHelpers.DrawPanel(hdc, resourcesPanel, CLR_PANEL_BG, CLR_PANEL_BORDER, CLR_BADGE_BG, S(4));
        GdiHelpers.DrawPanel(hdc, prefsPanel, CLR_PANEL_BG, CLR_PANEL_BORDER, CLR_BADGE_BG, S(4));

        // Séparateurs entre les 3 liens (Guide, Bêta, Discord)
        for (int row = 1; row < 3; row++)
        {
            var rowSep = new Win32.RECT
            {
                left = linksX,
                top = linkStartY + row * linkRowH - S(6),
                right = linksX + linksWidth,
                bottom = linkStartY + row * linkRowH - S(5)
            };
            GdiHelpers.FillSolidRect(hdc, rowSep, 0x00E3E3E3);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Dispose
    // ═══════════════════════════════════════════════════════════════
    public void Dispose()
    {
        _learningModule?.Dispose();
        _learningModule = null;

        if (_hWndLinkBetaBanner != IntPtr.Zero) Win32.RemoveWindowSubclass(_hWndLinkBetaBanner, _linkSubclassProc, (UIntPtr)10);
        if (_hWndLinkGuide != IntPtr.Zero) Win32.RemoveWindowSubclass(_hWndLinkGuide, _linkSubclassProc, (UIntPtr)1);
        if (_hWndLinkBeta != IntPtr.Zero) Win32.RemoveWindowSubclass(_hWndLinkBeta, _linkSubclassProc, (UIntPtr)3);
        if (_hWndLinkDiscord != IntPtr.Zero) Win32.RemoveWindowSubclass(_hWndLinkDiscord, _linkSubclassProc, (UIntPtr)5);

        if (_hWnd != IntPtr.Zero) { Win32.DestroyWindow(_hWnd); _hWnd = IntPtr.Zero; }
        if (_hIcon != IntPtr.Zero) { Win32.DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        if (_gdipLogo != IntPtr.Zero) { Win32.GdipDisposeImage(_gdipLogo); _gdipLogo = IntPtr.Zero; }
        if (_gdipDiscord != IntPtr.Zero) { Win32.GdipDisposeImage(_gdipDiscord); _gdipDiscord = IntPtr.Zero; }
        if (_gdipToken != IntPtr.Zero) { Win32.GdiplusShutdown(_gdipToken); _gdipToken = IntPtr.Zero; }
        DestroyFonts();
        Win32.DeleteObject(_hBgBrush);
        Win32.DeleteObject(_hBannerBgBrush);
        Win32.DeleteObject(_hPanelBrush);
    }
}

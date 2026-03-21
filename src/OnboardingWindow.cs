// Fenêtre d'accueil — affichée au premier lancement
using System.Runtime.InteropServices;

namespace AZERTYGlobalPortable;

/// <summary>
/// Fenêtre Win32 d'onboarding avec fond blanc, logo, texte explicatif,
/// liens cliquables, case à cocher et bouton "C'est parti !".
/// Rendu anti-aliasé via GDI+ pour le logo et les cercles.
/// Toutes les dimensions sont scalées selon le DPI système.
/// </summary>
sealed class OnboardingWindow : IDisposable
{
    // ── Window constants (spécifiques OnboardingWindow) ───────────
    private const uint BS_AUTOCHECKBOX = 0x0003;
    private const uint BS_PUSHBUTTON = 0x0000;
    private const uint BM_GETCHECK = 0x00F0;
    private const uint BST_CHECKED = 0x0001;
    private const uint SS_NOTIFY = 0x0100;

    private const int IDC_CHECKBOX = 2001;
    private const int IDC_BUTTON = 2002;
    private const int IDC_LINK_GUIDE = 2003;
    private const int IDC_LINK_LESSONS = 2004;
    private const int IDC_CHECKBOX_AUTOSTART = 2005;

    // Dimensions de base (96 DPI) — scalées dynamiquement
    private const int BASE_WIN_W = 480;
    private const int BASE_WIN_H = 500;

    // ── Colors (COLORREF = 0x00BBGGRR) ───────────────────────────
    private const uint CLR_BG = 0x00DDDDDD;       // Fond gris
    private const uint CLR_TITLE = 0x00201C18;
    private const uint CLR_STEP_TITLE = 0x00D47800; // Bleu Windows (COLORREF de 0x0078D4)
    private const uint CLR_TEXT = 0x00333333;
    private const uint CLR_LINK = 0x00D47800;       // Bleu lien
    private const uint CLR_HIGHLIGHT = 0x000078D4;   // Orange #D47800 (COLORREF = BBGGRR)

    // ── Colors ARGB pour GDI+ (0xAARRGGBB) ──────────────────────
    private const uint ARGB_STEP_CIRCLE = 0xFF0078D4; // Bleu Windows
    private const uint ARGB_WHITE = 0xFFFFFFFF;

    // ═══════════════════════════════════════════════════════════════
    // Champs d'instance
    // ═══════════════════════════════════════════════════════════════
    private IntPtr _hWnd;
    private IntPtr _hWndCheckbox;
    private IntPtr _hWndCheckboxAutoStart;
    private IntPtr _hWndButton;
    private IntPtr _hWndLinkGuide;
    private IntPtr _hWndLinkLessons;
    private IntPtr _hWndTesterNote;
    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly IntPtr _hBgBrush;
    private IntPtr _gdipToken;
    private IntPtr _gdipLogo;  // GDI+ Image (pas HBITMAP)
    private IntPtr _hIcon;     // Icône fenêtre (favicon)
    private bool _visible;

    // DPI scaling
    private readonly float _dpiScale;
    private int S(int val) => (int)(val * _dpiScale); // Scale helper

    // Fonts (créés avec tailles scalées)
    private IntPtr _hFontTitle;
    private IntPtr _hFontSubtitle;
    private IntPtr _hFontText;
    private IntPtr _hFontBold;
    private IntPtr _hFontLink;
    private IntPtr _hFontSmall;
    private IntPtr _hFontButton;

    public bool IsVisible => _visible;

    public OnboardingWindow()
    {
        _wndProcDelegate = WndProc;
        _hBgBrush = Win32.CreateSolidBrush(CLR_BG);

        // Déterminer le DPI système
        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        int dpi = Win32.GetDeviceCaps(hdcScreen, 88); // LOGPIXELSX
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        _dpiScale = dpi / 96f;

        // Initialiser GDI+ (gardé vivant pour le rendu)
        var gdipInput = new Win32.GdiplusStartupInput { GdiplusVersion = 1 };
        Win32.GdiplusStartup(out _gdipToken, ref gdipInput, IntPtr.Zero);

        LoadLogo();

        // Créer les polices (tailles scalées par DPI)
        // quality=5 → CLEARTYPE_QUALITY pour un rendu net des petites polices
        _hFontTitle = Win32.CreateFontW(S(24), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSubtitle = Win32.CreateFontW(S(16), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontText = Win32.CreateFontW(S(16), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontBold = Win32.CreateFontW(S(17), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontLink = Win32.CreateFontW(S(16), 0, 0, 0, 600, 0, 1, 0, 0, 0, 0, 5, 0, "Segoe UI"); // underline
        _hFontSmall = Win32.CreateFontW(S(13), 0, 0, 0, 400, 1, 0, 0, 0, 0, 0, 5, 0, "Segoe UI"); // italic
        _hFontButton = Win32.CreateFontW(S(17), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");

        CreateMainWindow();
    }

    private void LoadLogo()
    {
        try
        {
            using var stream = typeof(OnboardingWindow).Assembly
                .GetManifestResourceStream("favicon-azerty-global.png");
            if (stream == null) return;

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);

            IntPtr hGlobal = Win32.GlobalAlloc(0x0042, (nuint)bytes.Length);
            IntPtr pGlobal = Win32.GlobalLock(hGlobal);
            Marshal.Copy(bytes, 0, pGlobal, bytes.Length);
            Win32.GlobalUnlock(hGlobal);

            Win32.CreateStreamOnHGlobal(hGlobal, true, out IntPtr pStream);
            Win32.GdipCreateBitmapFromStream(pStream, out _gdipLogo);
            Marshal.Release(pStream);
        }
        catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException)
        {
            // Logo non chargé — pas critique
        }
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

        int screenW = Win32.GetSystemMetrics(0);
        int screenH = Win32.GetSystemMetrics(1);

        _hWnd = Win32.CreateWindowExW(dwExStyle, className, "AZERTY Global Portable",
            dwStyle, (screenW - windowW) / 2, (screenH - windowH) / 2, windowW, windowH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        CreateControls();
        SetWindowIcon();
    }

    /// <summary>Crée une HICON à partir du logo GDI+ et l'applique à la fenêtre.</summary>
    private void SetWindowIcon()
    {
        if (_gdipLogo == IntPtr.Zero) return;
        try
        {
            // Créer un bitmap 32x32 GDI+ et y dessiner le logo
            int size = 32;
            Win32.GdipCreateBitmapFromScan0(size, size, 0, 0x0026200A, IntPtr.Zero, out IntPtr bmp32); // PixelFormat32bppARGB
            Win32.GdipGetImageGraphicsContext(bmp32, out IntPtr g);
            Win32.GdipSetSmoothingMode(g, 4);
            Win32.GdipSetInterpolationMode(g, 7);
            Win32.GdipDrawImageRectI(g, _gdipLogo, 0, 0, size, size);
            Win32.GdipDeleteGraphics(g);

            // Convertir en HBITMAP
            Win32.GdipCreateHBITMAPFromBitmap(bmp32, out IntPtr hBmp, 0x00000000);
            Win32.GdipDisposeImage(bmp32);

            // Créer le masque (tout opaque)
            var maskBits = new byte[size * size / 8];
            IntPtr hMask = Win32.CreateBitmap(size, size, 1, 1, maskBits);

            var iconInfo = new Win32.ICONINFO { fIcon = true, hbmMask = hMask, hbmColor = hBmp };
            _hIcon = Win32.CreateIconIndirect(ref iconInfo);

            Win32.DeleteObject(hMask);
            Win32.DeleteObject(hBmp);

            // WM_SETICON (ICON_SMALL=0, ICON_BIG=1)
            const uint WM_SETICON = 0x0080;
            Win32.SendMessageW(_hWnd, WM_SETICON, (IntPtr)0, _hIcon);
            Win32.SendMessageW(_hWnd, WM_SETICON, (IntPtr)1, _hIcon);
        }
        catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException)
        {
            // Icône non chargée — pas critique
        }
    }

    private void CreateControls()
    {
        int margin = S(30);
        int bottomY = S(BASE_WIN_H) - S(55);
        int winW = S(BASE_WIN_W);

        // ── Cases à cocher (en premier) ──
        int sectionY = bottomY - S(118);

        // Case à cocher — lancer au démarrage de Windows
        _hWndCheckboxAutoStart = Win32.CreateWindowExW(0, "BUTTON", "Lancer au démarrage de Windows",
            Win32.WS_CHILD | Win32.WS_VISIBLE | BS_AUTOCHECKBOX | Win32.WS_TABSTOP,
            margin, sectionY, S(300), S(24),
            _hWnd, (IntPtr)IDC_CHECKBOX_AUTOSTART, Win32.GetModuleHandleW(null), IntPtr.Zero);
        Win32.SendMessageW(_hWndCheckboxAutoStart, Win32.WM_SETFONT, _hFontText, (IntPtr)1);
        // Coché si déjà activé dans la config
        if (ConfigManager.AutoStartEnabled)
            Win32.SendMessageW(_hWndCheckboxAutoStart, 0x00F1, (IntPtr)BST_CHECKED, IntPtr.Zero);

        // Case à cocher — ne plus afficher
        _hWndCheckbox = Win32.CreateWindowExW(0, "BUTTON", "Ne plus afficher au démarrage",
            Win32.WS_CHILD | Win32.WS_VISIBLE | BS_AUTOCHECKBOX | Win32.WS_TABSTOP,
            margin, sectionY + S(26), S(260), S(24),
            _hWnd, (IntPtr)IDC_CHECKBOX, Win32.GetModuleHandleW(null), IntPtr.Zero);
        Win32.SendMessageW(_hWndCheckbox, Win32.WM_SETFONT, _hFontText, (IntPtr)1);
        // Coché par défaut — l'utilisateur décoche s'il veut revoir l'accueil
        Win32.SendMessageW(_hWndCheckbox, 0x00F1, (IntPtr)BST_CHECKED, IntPtr.Zero); // BM_SETCHECK

        // ── Liens cliquables (après les cases à cocher) ──
        int linkY = sectionY + S(58);
        _hWndLinkGuide = Win32.CreateWindowExW(0, "STATIC",
            "\u2192 Découvrir le guide complet",
            Win32.WS_CHILD | Win32.WS_VISIBLE | SS_NOTIFY,
            margin, linkY, S(210), S(22),
            _hWnd, (IntPtr)IDC_LINK_GUIDE, Win32.GetModuleHandleW(null), IntPtr.Zero);
        Win32.SendMessageW(_hWndLinkGuide, Win32.WM_SETFONT, _hFontLink, (IntPtr)1);

        _hWndLinkLessons = Win32.CreateWindowExW(0, "STATIC",
            "\u2192 S'entraîner avec les leçons de frappe",
            Win32.WS_CHILD | Win32.WS_VISIBLE | SS_NOTIFY,
            margin, linkY + S(26), S(280), S(22),
            _hWnd, (IntPtr)IDC_LINK_LESSONS, Win32.GetModuleHandleW(null), IntPtr.Zero);
        Win32.SendMessageW(_hWndLinkLessons, Win32.WM_SETFONT, _hFontLink, (IntPtr)1);

        // Note sous le lien leçons
        _hWndTesterNote = Win32.CreateWindowExW(0, "STATIC",
            "(Désactivez le portable pour utiliser le testeur)",
            Win32.WS_CHILD | Win32.WS_VISIBLE,
            margin + S(18), linkY + S(50), S(300), S(18),
            _hWnd, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
        Win32.SendMessageW(_hWndTesterNote, Win32.WM_SETFONT, _hFontSmall, (IntPtr)1);

        // Bouton "C'est parti !"
        _hWndButton = Win32.CreateWindowExW(0, "BUTTON", "C'est parti !",
            Win32.WS_CHILD | Win32.WS_VISIBLE | 0x0001 | Win32.WS_TABSTOP, // BS_DEFPUSHBUTTON
            winW - margin - S(150), bottomY - S(4), S(150), S(38),
            _hWnd, (IntPtr)IDC_BUTTON, Win32.GetModuleHandleW(null), IntPtr.Zero);
        Win32.SendMessageW(_hWndButton, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
    }

    public void Show()
    {
        Win32.ShowWindow(_hWnd, 1);
        Win32.SetForegroundWindow(_hWnd);
        _visible = true;
    }

    public void Close()
    {
        var checkState = Win32.SendMessageW(_hWndCheckbox, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero);
        if (checkState == (IntPtr)BST_CHECKED)
            ConfigManager.SetOnboardingDone();

        // Appliquer le choix de lancement automatique
        var autoStartState = Win32.SendMessageW(_hWndCheckboxAutoStart, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero);
        AutoStart.Set(autoStartState == (IntPtr)BST_CHECKED);

        Win32.ShowWindow(_hWnd, 0);
        _visible = false;
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
                return (IntPtr)1;

            case Win32.WM_COMMAND:
                int id = wParam.ToInt32() & 0xFFFF;
                int code = (wParam.ToInt32() >> 16) & 0xFFFF;
                switch (id)
                {
                    case IDC_BUTTON: Close(); break;
                    case IDC_LINK_GUIDE:
                        if (code == 0)
                        {
                            Win32.ShellExecuteW(IntPtr.Zero, "open", "https://azerty.global/guide", null, null, 1);
                            // Retirer le topmost pour que le navigateur passe devant
                            Win32.SetWindowPos(_hWnd, (IntPtr)(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040); // HWND_NOTOPMOST, SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW
                        }
                        break;
                    case IDC_LINK_LESSONS:
                        if (code == 0)
                        {
                            Win32.ShellExecuteW(IntPtr.Zero, "open", "https://azerty.global/?mode=lessons", null, null, 1);
                            Win32.SetWindowPos(_hWnd, (IntPtr)(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
                        }
                        break;
                }
                return IntPtr.Zero;

            case Win32.WM_CTLCOLORSTATIC:
                IntPtr hdcStatic = wParam;
                IntPtr hCtrl = lParam;
                if (hCtrl == _hWndLinkGuide || hCtrl == _hWndLinkLessons)
                {
                    Win32.SetBkMode(hdcStatic, 1);
                    Win32.SetTextColor(hdcStatic, CLR_LINK);
                    return _hBgBrush;
                }
                if (hCtrl == _hWndTesterNote)
                {
                    Win32.SetBkMode(hdcStatic, 1);
                    Win32.SetTextColor(hdcStatic, 0x00888888); // Gris discret
                    return _hBgBrush;
                }
                if (hCtrl == _hWndCheckbox || hCtrl == _hWndCheckboxAutoStart)
                {
                    Win32.SetBkMode(hdcStatic, 1);
                    Win32.SetTextColor(hdcStatic, CLR_TEXT);
                    return _hBgBrush;
                }
                break;

            case Win32.WM_SETCURSOR:
                if (wParam == _hWndLinkGuide || wParam == _hWndLinkLessons)
                {
                    Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, (IntPtr)32649)); // IDC_HAND
                    return (IntPtr)1;
                }
                break;

            case Win32.WM_NCHITTEST:
                // Empêcher le déplacement : toujours retourner HTCLIENT (1)
                // au lieu de HTCAPTION, même sur la barre de titre
                return (IntPtr)1;

            case Win32.WM_SYSCOMMAND:
                // Bloquer SC_MOVE (0xF010) pour empêcher le déplacement via menu système
                if ((wParam.ToInt32() & 0xFFF0) == 0xF010)
                    return IntPtr.Zero;
                break;

            case Win32.WM_CLOSE:
                Close();
                return IntPtr.Zero;
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ═══════════════════════════════════════════════════════════════
    // Rendu
    // ═══════════════════════════════════════════════════════════════
    private void OnPaint(IntPtr hWnd)
    {
        var hdcPaint = Win32.BeginPaint(hWnd, out var ps);
        Win32.GetClientRect(hWnd, out var clientRect);
        int cw = clientRect.right;
        int ch = clientRect.bottom;

        // Double buffering
        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        var hdc = Win32.CreateCompatibleDC(hdcScreen);
        var hBmp = Win32.CreateCompatibleBitmap(hdcScreen, cw, ch);
        var hBmpOld = Win32.SelectObject(hdc, hBmp);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);

        // Fond blanc
        Win32.FillRect(hdc, ref clientRect, _hBgBrush);
        Win32.SetBkMode(hdc, 1); // TRANSPARENT

        // Créer un contexte GDI+ à partir du HDC mémoire
        Win32.GdipCreateFromHDC(hdc, out IntPtr gfx);
        Win32.GdipSetSmoothingMode(gfx, 4);        // SmoothingModeAntiAlias
        Win32.GdipSetInterpolationMode(gfx, 7);    // InterpolationModeHighQualityBicubic
        Win32.GdipSetTextRenderingHint(gfx, 5);    // TextRenderingHintClearTypeGridFit

        int margin = S(30);
        int y = S(20);

        // ── Logo + Titre + Sous-titre (logo à gauche des deux lignes) ──
        int logoSize = S(48);
        int textX = margin;

        if (_gdipLogo != IntPtr.Zero)
        {
            Win32.GdipDrawImageRectI(gfx, _gdipLogo, margin, y, logoSize, logoSize);
            textX = margin + logoSize + S(14);
        }

        // Titre (première ligne à droite du logo)
        Win32.SelectObject(hdc, _hFontTitle);
        Win32.SetTextColor(hdc, CLR_TITLE);
        var titleRect = new Win32.RECT { left = textX, top = y + S(2), right = cw - margin, bottom = y + S(26) };
        Win32.DrawTextW(hdc, "AZERTY Global Portable", -1, ref titleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        // Sous-titre (deuxième ligne à droite du logo, collé sous le titre)
        Win32.SelectObject(hdc, _hFontSubtitle);
        Win32.SetTextColor(hdc, CLR_TEXT);
        var subtitleRect = new Win32.RECT { left = textX, top = y + S(28), right = cw - margin, bottom = y + S(48) };
        Win32.DrawTextW(hdc, "Votre clavier est maintenant amélioré.", -1, ref subtitleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += logoSize + S(10);

        // Ligne de séparation
        var sepBrush = Win32.CreateSolidBrush(0x00E0E0E0);
        var sepRect = new Win32.RECT { left = margin, top = y, right = cw - margin, bottom = y + 1 };
        Win32.FillRect(hdc, ref sepRect, sepBrush);
        Win32.DeleteObject(sepBrush);
        y += S(18);

        // ── Étapes ──
        DrawStep(hdc, gfx, margin, cw, ref y, "1",
            "Tapez normalement",
            "AZERTY Global Portable fonctionne par-dessus votre clavier actuel. Vos raccourcis (Ctrl+C, Ctrl+V\u2026) sont préservés.");

        DrawStep(hdc, gfx, margin, cw, ref y, "2",
            "Explorez avec le clavier virtuel",
            "Double-cliquez sur l'icône AG dans la barre des tâches pour voir tous les caractères disponibles.");

        DrawStepWithHighlight(hdc, gfx, margin, cw, ref y);

        // Nettoyage GDI+
        Win32.GdipDeleteGraphics(gfx);

        // Copier le buffer
        Win32.BitBlt(hdcPaint, 0, 0, cw, ch, hdc, 0, 0, Win32.SRCCOPY);
        Win32.SelectObject(hdc, hBmpOld);
        Win32.DeleteObject(hBmp);
        Win32.DeleteDC(hdc);

        Win32.EndPaint(hWnd, ref ps);
    }

    /// <summary>Dessine une étape numérotée avec cercle anti-aliasé via GDI+.</summary>
    private void DrawStep(IntPtr hdc, IntPtr gfx, int margin, int cw, ref int y, string number, string title, string description)
    {
        int circleSize = S(28);
        int textX = margin + circleSize + S(12);

        // Cercle bleu anti-aliasé via GDI+
        Win32.GdipCreateSolidFill(ARGB_STEP_CIRCLE, out IntPtr blueBrush);
        Win32.GdipFillEllipseI(gfx, blueBrush, margin, y, circleSize, circleSize);

        // Numéro blanc dans le cercle via GDI+ DrawString
        Win32.GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out IntPtr fontFamily);
        Win32.GdipCreateFont(fontFamily, 10f * _dpiScale, 1, 2, out IntPtr gdipFont); // Bold, Point unit
        Win32.GdipCreateStringFormat(0, 0, out IntPtr strFormat);
        Win32.GdipSetStringFormatAlign(strFormat, 1);     // Center
        Win32.GdipSetStringFormatLineAlign(strFormat, 1); // Center
        Win32.GdipCreateSolidFill(ARGB_WHITE, out IntPtr whiteBrush);

        var circleRect = new Win32.RectF { X = margin, Y = y, Width = circleSize, Height = circleSize };
        Win32.GdipDrawString(gfx, number, number.Length, gdipFont, ref circleRect, strFormat, whiteBrush);

        Win32.GdipDeleteBrush(whiteBrush);
        Win32.GdipDeleteBrush(blueBrush);
        Win32.GdipDeleteStringFormat(strFormat);
        Win32.GdipDeleteFont(gdipFont);
        Win32.GdipDeleteFontFamily(fontFamily);

        // Titre en gras + couleur bleue (GDI)
        Win32.SelectObject(hdc, _hFontBold);
        Win32.SetTextColor(hdc, CLR_STEP_TITLE);
        var titleRect = new Win32.RECT { left = textX, top = y + S(2), right = cw - margin, bottom = y + S(24) };
        Win32.DrawTextW(hdc, title, -1, ref titleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += S(26);

        // Description (GDI, wordwrap)
        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, CLR_TEXT);
        var descRect = new Win32.RECT { left = textX, top = y, right = cw - margin, bottom = y + S(60) };
        Win32.DrawTextW(hdc, description, -1, ref descRect, Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX);
        y += S(48);
    }

    /// <summary>Étape 3 avec le raccourci en couleur bleue.</summary>
    private void DrawStepWithHighlight(IntPtr hdc, IntPtr gfx, int margin, int cw, ref int y)
    {
        int circleSize = S(28);
        int textX = margin + circleSize + S(12);

        // Cercle bleu anti-aliasé via GDI+
        Win32.GdipCreateSolidFill(ARGB_STEP_CIRCLE, out IntPtr blueBrush);
        Win32.GdipFillEllipseI(gfx, blueBrush, margin, y, circleSize, circleSize);

        // Numéro blanc
        Win32.GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out IntPtr fontFamily);
        Win32.GdipCreateFont(fontFamily, 10f * _dpiScale, 1, 2, out IntPtr gdipFont);
        Win32.GdipCreateStringFormat(0, 0, out IntPtr strFormat);
        Win32.GdipSetStringFormatAlign(strFormat, 1);
        Win32.GdipSetStringFormatLineAlign(strFormat, 1);
        Win32.GdipCreateSolidFill(ARGB_WHITE, out IntPtr whiteBrush);
        var circleRect = new Win32.RectF { X = margin, Y = y, Width = circleSize, Height = circleSize };
        Win32.GdipDrawString(gfx, "3", 1, gdipFont, ref circleRect, strFormat, whiteBrush);
        Win32.GdipDeleteBrush(whiteBrush);
        Win32.GdipDeleteBrush(blueBrush);
        Win32.GdipDeleteStringFormat(strFormat);
        Win32.GdipDeleteFont(gdipFont);
        Win32.GdipDeleteFontFamily(fontFamily);

        // Titre
        Win32.SelectObject(hdc, _hFontBold);
        Win32.SetTextColor(hdc, CLR_STEP_TITLE);
        var titleRect = new Win32.RECT { left = textX, top = y + S(2), right = cw - margin, bottom = y + S(24) };
        Win32.DrawTextW(hdc, "Activez / désactivez à tout moment", -1, ref titleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += S(26);

        // "Raccourci : " en texte normal
        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, CLR_TEXT);
        string prefix = "Raccourci : ";
        var prefixRect = new Win32.RECT { left = textX, top = y, right = cw - margin, bottom = y + S(20) };
        Win32.DrawTextW(hdc, prefix, -1, ref prefixRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        // Mesurer la largeur du préfixe pour positionner le raccourci juste après
        var measureRect = new Win32.RECT { left = 0, top = 0, right = 9999, bottom = 9999 };
        Win32.DrawTextW(hdc, prefix, -1, ref measureRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_CALCRECT);

        // "Ctrl + Maj + Verr.Maj" en orange
        Win32.SelectObject(hdc, _hFontBold);
        Win32.SetTextColor(hdc, CLR_HIGHLIGHT);
        var shortcutRect = new Win32.RECT { left = textX + measureRect.right, top = y, right = cw - margin, bottom = y + S(20) };
        Win32.DrawTextW(hdc, "Ctrl + Maj + Verr.Maj", -1, ref shortcutRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += S(22);

        // Deuxième ligne
        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, CLR_TEXT);
        var line2Rect = new Win32.RECT { left = textX, top = y, right = cw - margin, bottom = y + S(20) };
        Win32.DrawTextW(hdc, "Ou clic droit sur l'icône AG \u2192 Désactiver", -1, ref line2Rect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += S(26);
    }

    // ═══════════════════════════════════════════════════════════════
    // Dispose
    // ═══════════════════════════════════════════════════════════════
    public void Dispose()
    {
        if (_hWnd != IntPtr.Zero) { Win32.DestroyWindow(_hWnd); _hWnd = IntPtr.Zero; }
        if (_hIcon != IntPtr.Zero) { Win32.DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        if (_gdipLogo != IntPtr.Zero) { Win32.GdipDisposeImage(_gdipLogo); _gdipLogo = IntPtr.Zero; }
        if (_gdipToken != IntPtr.Zero) { Win32.GdiplusShutdown(_gdipToken); _gdipToken = IntPtr.Zero; }
        Win32.DeleteObject(_hFontTitle);
        Win32.DeleteObject(_hFontSubtitle);
        Win32.DeleteObject(_hFontText);
        Win32.DeleteObject(_hFontBold);
        Win32.DeleteObject(_hFontLink);
        Win32.DeleteObject(_hFontSmall);
        Win32.DeleteObject(_hFontButton);
        Win32.DeleteObject(_hBgBrush);
    }
}

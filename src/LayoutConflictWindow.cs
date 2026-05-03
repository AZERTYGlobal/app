// Fenetre custom affichee quand l'app detecte qu'une disposition systeme AZERTY Global
// est deja active. Propose un choix eclaire entre garder l'app (post-login user-friendly)
// ou garder la disposition systeme (avant login : mot de passe Windows, etc.).
using System.Runtime.InteropServices;

namespace AZERTYGlobal;

/// <summary>
/// Mini-fenetre modale topmost qui explique le trade-off app vs disposition systeme,
/// puis demande a l'utilisateur de choisir : Quitter l'application | Garder l'app.
/// Remplace l'ancien MessageBox de TrayApplication.ShowLayoutConflictPopup pour permettre
/// un texte plus dense et un choix plus eclaire.
/// </summary>
sealed class LayoutConflictWindow : IDisposable
{
    private const int IDC_BTN_QUIT = 5101;
    private const int IDC_BTN_KEEP = 5102;

    // Nom de classe Win32. Defini en const pour partage entre CreateMainWindow et Dispose
    // (UnregisterClassW au Dispose pour eviter que la classe survive l'instance et garde
    // un pointeur vers _wndProcDelegate collecte par GC). Sans cela, une 2e instance creee
    // apres dispose de la 1ere (cas : conflit detecte au demarrage puis re-detecte apres
    // Ctrl+Shift) crashe au prochain WM_PAINT/WM_COMMAND. Pattern documente dans
    // LearningModule.cs:740 (bug Reset->Essayer post-1ere completion fixe en v0.9.7).
    private const string WND_CLASS_NAME = "AZERTYGlobal_LayoutConflict";

    private const int BASE_WIN_W = 560;
    private const int BASE_WIN_H = 440;

    // Couleurs alignees sur AboutWindow / SettingsWindow
    private const uint CLR_BG = 0x00DDDDDD;
    private const uint CLR_TITLE = 0x00201C18;
    private const uint CLR_TEXT = 0x00333333;
    private const uint CLR_HIGHLIGHT = 0x000078D4;
    private const uint CLR_SUBTLE = 0x00666666;

    private IntPtr _hWnd;
    private IntPtr _hWndBtnQuit;
    private IntPtr _hWndBtnKeep;

    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly IntPtr _hBgBrush;

    private readonly bool _isAtStartup;
    private readonly Action _onQuit;
    private readonly Action _onKeep;

    private float _dpiScale;
    private int S(int val) => (int)(val * _dpiScale);

    private IntPtr _hFontTitle;
    private IntPtr _hFontText;
    private IntPtr _hFontBold;
    private IntPtr _hFontButton;

    /// <param name="isAtStartup">
    /// true : detection au demarrage de l'app (« est deja installee »).
    /// false : detection apres switch Ctrl+Shift (« vient d'etre activee »).
    /// </param>
    /// <param name="onQuit">Callback appele si l'utilisateur choisit « Quitter l'application ».</param>
    /// <param name="onKeep">Callback appele si l'utilisateur choisit « Garder l'app » (ou ferme la fenetre).</param>
    public LayoutConflictWindow(bool isAtStartup, Action onQuit, Action onKeep)
    {
        _wndProcDelegate = WndProc;
        _hBgBrush = Win32.CreateSolidBrush(CLR_BG);
        _isAtStartup = isAtStartup;
        _onQuit = onQuit;
        _onKeep = onKeep;

        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        int dpi = Win32.GetDeviceCaps(hdcScreen, 88);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        _dpiScale = dpi / 96f;

        CreateFonts();
        CreateMainWindow();
        CreateControls();
        ApplyFontsToControls();

        try
        {
            int realDpi = Win32.GetDpiForWindow(_hWnd);
            if (realDpi > 0 && Math.Abs(realDpi / 96f - _dpiScale) > 0.01f)
            {
                _dpiScale = realDpi / 96f;
                RecreateFonts();
                ResizeWindow();
                RepositionControls();
            }
        }
        catch { }
    }

    private void CreateFonts()
    {
        _hFontTitle = Win32.CreateFontW(-S(20), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontText = Win32.CreateFontW(-S(14), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontBold = Win32.CreateFontW(-S(14), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontButton = Win32.CreateFontW(-S(14), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
    }

    private void DestroyFonts()
    {
        Win32.DeleteObject(_hFontTitle);
        Win32.DeleteObject(_hFontText);
        Win32.DeleteObject(_hFontBold);
        Win32.DeleteObject(_hFontButton);
    }

    private void RecreateFonts()
    {
        DestroyFonts();
        CreateFonts();
        ApplyFontsToControls();
    }

    private void ApplyFontsToControls()
    {
        Win32.SendMessageW(_hWndBtnQuit, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
        Win32.SendMessageW(_hWndBtnKeep, Win32.WM_SETFONT, _hFontButton, (IntPtr)1);
    }

    private void CreateMainWindow()
    {
        var hInstance = Win32.GetModuleHandleW(null);

        // hbrBackground = IntPtr.Zero : NE PAS reference _hBgBrush dans la WNDCLASSEXW.
        // La classe Win32 reste enregistree au-dela de la duree de vie de l'instance ;
        // si on libere _hBgBrush au Dispose, la classe garde un pointeur invalide → crash
        // a la 2e instance. L'effacement du fond est gere via WM_ERASEBKGND (return 1) +
        // FillRect dans OnPaint. Couple avec UnregisterClassW au Dispose pour permettre
        // la 2e instance avec un delegate WndProc frais.
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

        _hWnd = Win32.CreateWindowExW(dwExStyle, WND_CLASS_NAME,
            "AZERTY Global — Disposition système détectée",
            dwStyle, screenX + (screenW - windowW) / 2, screenY + (screenH - windowH) / 2, windowW, windowH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private void CreateControls()
    {
        var hInstance = Win32.GetModuleHandleW(null);

        _hWndBtnQuit = Win32.CreateWindowExW(0, "BUTTON", "Quitter l'application",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP | 0x0001 /* BS_DEFPUSHBUTTON */,
            0, 0, 0, 0,
            _hWnd, (IntPtr)IDC_BTN_QUIT, hInstance, IntPtr.Zero);

        _hWndBtnKeep = Win32.CreateWindowExW(0, "BUTTON", "Garder l'application",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP,
            0, 0, 0, 0,
            _hWnd, (IntPtr)IDC_BTN_KEEP, hInstance, IntPtr.Zero);

        RepositionControls();
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

    private void RepositionControls()
    {
        int winW = S(BASE_WIN_W);
        int winH = S(BASE_WIN_H);
        int margin = S(20);
        int btnW = S(180);
        int btnH = S(34);
        int btnGap = S(12);
        int totalBtnW = btnW * 2 + btnGap;
        int btnX = (winW - totalBtnW) / 2;
        int btnY = winH - margin - btnH;
        Win32.MoveWindow(_hWndBtnQuit, btnX, btnY, btnW, btnH, true);
        Win32.MoveWindow(_hWndBtnKeep, btnX + btnW + btnGap, btnY, btnW, btnH, true);
    }

    public void Show()
    {
        Win32.ShowWindow(_hWnd, 1);
        Win32.SetForegroundWindow(_hWnd);
    }

    private void Close(bool quit)
    {
        Win32.ShowWindow(_hWnd, 0);
        if (quit) _onQuit(); else _onKeep();
    }

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
                {
                    int id = wParam.ToInt32() & 0xFFFF;
                    switch (id)
                    {
                        case IDC_BTN_QUIT: Close(true); break;
                        case IDC_BTN_KEEP: Close(false); break;
                    }
                    return IntPtr.Zero;
                }

                case Win32.WM_KEYDOWN:
                    if (wParam == (IntPtr)0x1B) // VK_ESCAPE → choix non-destructif (garder)
                    {
                        Close(false);
                        return IntPtr.Zero;
                    }
                    break;

                case Win32.WM_CLOSE:
                    Close(false); // croix X = garder l'app
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            ConfigManager.Log("LayoutConflictWindow WndProc", ex);
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

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

        int margin = S(24);
        int x = margin;
        int y = S(20);
        int contentW = cw - margin * 2;

        // Titre
        Win32.SelectObject(hdc, _hFontTitle);
        Win32.SetTextColor(hdc, CLR_TITLE);
        var titleRect = new Win32.RECT { left = x, top = y, right = x + contentW, bottom = y + S(28) };
        Win32.DrawTextW(hdc, "Disposition système AZERTY Global détectée", -1, ref titleRect,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += S(36);

        // Intro variable selon origine
        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, CLR_TEXT);
        string introText = _isAtStartup
            ? "Une disposition système AZERTY Global est déjà installée sur cet ordinateur."
            : "Une disposition système AZERTY Global vient d'être activée sur cet ordinateur.";
        int introH = MeasureWrapped(hdc, _hFontText, introText, contentW);
        var introRect = new Win32.RECT { left = x, top = y, right = x + contentW, bottom = y + introH };
        Win32.DrawTextW(hdc, introText, -1, ref introRect,
            Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX);
        y += introH + S(14);

        // Question
        Win32.SelectObject(hdc, _hFontBold);
        Win32.SetTextColor(hdc, CLR_TITLE);
        var qRect = new Win32.RECT { left = x, top = y, right = x + contentW, bottom = y + S(20) };
        Win32.DrawTextW(hdc, "Quel est ton besoin ?", -1, ref qRect,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
        y += S(28);

        // Section 1 — avant login
        DrawOption(hdc, ref y, x, contentW,
            "▸ Taper avec AZERTY Global AVANT le login",
            "(mot de passe Windows, écran de verrouillage, UAC, BitLocker)",
            "→ Garde la disposition système et quitte cette application — elle fait double emploi et ne tourne pas avant le login de toute façon.");
        y += S(10);

        // Section 2 — confort post-login
        DrawOption(hdc, ref y, x, contentW,
            "▸ Profiter du clavier virtuel et de la recherche de caractère",
            null,
            "→ Utilise plutôt cette application. Enlève AZERTY Global de la liste des dispositions chargées dans les options de langue (Paramètres Windows → Heure et langue → Langue → Options de la langue concernée). N'oublie pas alors de cocher « Lancer au démarrage de Windows » dans cette application pour qu'elle soit toujours active après le login.");

        Win32.BitBlt(hdcPaint, 0, 0, cw, ch, hdc, 0, 0, Win32.SRCCOPY);
        Win32.SelectObject(hdc, hBmpOld);
        Win32.DeleteObject(hBmp);
        Win32.DeleteDC(hdc);
        Win32.EndPaint(hWnd, ref ps);
    }

    private void DrawOption(IntPtr hdc, ref int y, int x, int contentW,
        string heading, string? subline, string body)
    {
        Win32.SelectObject(hdc, _hFontBold);
        Win32.SetTextColor(hdc, CLR_HIGHLIGHT);
        int headH = MeasureWrapped(hdc, _hFontBold, heading, contentW);
        var headRect = new Win32.RECT { left = x, top = y, right = x + contentW, bottom = y + headH };
        Win32.DrawTextW(hdc, heading, -1, ref headRect,
            Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX);
        y += headH + S(2);

        if (!string.IsNullOrEmpty(subline))
        {
            Win32.SelectObject(hdc, _hFontText);
            Win32.SetTextColor(hdc, CLR_SUBTLE);
            int subH = MeasureWrapped(hdc, _hFontText, subline, contentW - S(16));
            var subRect = new Win32.RECT { left = x + S(16), top = y, right = x + contentW, bottom = y + subH };
            Win32.DrawTextW(hdc, subline, -1, ref subRect,
                Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX);
            y += subH + S(4);
        }

        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, CLR_TEXT);
        int bodyH = MeasureWrapped(hdc, _hFontText, body, contentW - S(16));
        var bodyRect = new Win32.RECT { left = x + S(16), top = y, right = x + contentW, bottom = y + bodyH };
        Win32.DrawTextW(hdc, body, -1, ref bodyRect,
            Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX);
        y += bodyH;
    }

    private static int MeasureWrapped(IntPtr hdc, IntPtr hFont, string text, int width)
    {
        Win32.SelectObject(hdc, hFont);
        var rect = new Win32.RECT { left = 0, top = 0, right = width, bottom = 9999 };
        Win32.DrawTextW(hdc, text, -1, ref rect,
            Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX | Win32.DT_CALCRECT);
        return rect.bottom - rect.top;
    }

    public void Dispose()
    {
        if (_hWnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
        DestroyFonts();
        Win32.DeleteObject(_hBgBrush);

        // UnregisterClassW pour permettre une 2e instance avec un delegate WndProc frais.
        // Sans cela, la classe garde un pointeur vers _wndProcDelegate de cette instance
        // (potentiellement collecte par GC apres ce Dispose) → crash a la 2e instanciation.
        Win32.UnregisterClassW(WND_CLASS_NAME, Win32.GetModuleHandleW(null));
    }
}

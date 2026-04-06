using System.Runtime.InteropServices;
using System.Text;
namespace AZERTYGlobal;

sealed class SettingsWindow : IDisposable
{
    private const uint BS_AUTOCHECKBOX = 0x0003;
    private const uint BM_GETCHECK = 0x00F0;
    private const uint BM_SETCHECK = 0x00F1;
    private const uint BST_CHECKED = 0x0001;
    private const uint SS_NOTIFY = 0x0100;
    private const uint ES_AUTOHSCROLL = 0x0080;
    private const uint ES_CENTER = 0x0001;
    private const uint ES_UPPERCASE = 0x0008;
    private const uint EN_CHANGE = 0x0300;
    private const uint EM_SETLIMITTEXT = 0x00C5;
    private const uint EM_SETREADONLY = 0x00CF;

    private const int DLGC_WANTALLKEYS = 0x0004;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const uint CLR_KEY_BORDER_FOCUS = 0x000078D4;
    private const string ShortcutCaptureHint = "Appuyez sur une touche autorisée";

    private const int IDC_EDIT_KEYBOARD = 3101;
    private const int IDC_EDIT_SEARCH = 3102;
    private const int IDC_CHK_AUTOSTART = 3103;
    private const int IDC_CHK_NOTIFICATIONS = 3104;
    private const int IDC_LINK_RESET = 3107;

    private const int BASE_WIN_W = 240;
    private const int BASE_WIN_H = 272;

    private const uint CLR_BG = 0x00DDDDDD;
    private const uint CLR_TITLE = 0x00201C18;
    private const uint CLR_TEXT = 0x00333333;
    private const uint CLR_MUTED = 0x00666666;
    private const uint CLR_VERSION = 0x00888888;
    private const uint CLR_PANEL_BG = 0x00EEEEEE;
    private const uint CLR_PANEL_BORDER = 0x00D1D1D1;
    private const uint CLR_PANEL_ACCENT = 0x00D47800;
    private const uint CLR_LINK = 0x00D47800;
    private const uint CLR_LINK_HOVER = 0x000078D4;
    private const uint CLR_INLINE_HIGHLIGHT = 0x000078D4;
    private const uint CLR_VALID = 0x00228B22;
    private const uint CLR_INVALID = 0x000000CC;
    private const uint CLR_KEY_BG = 0x00FAFAFA;
    private const uint CLR_KEY_BORDER = 0x00CBCBCB;
    private const uint CLR_KEY_BORDER_INVALID = 0x00A8A8FF;
    private const uint CLR_SEPARATOR = 0x00D7D7D7;
    private const uint CLR_SUBTITLE = 0x003A342E;

    private struct LayoutInfo
    {
        public int Margin;
        public int HeaderTitleX;
        public int HeaderTitleY;
        public int HeaderDividerY;
        public Win32.RECT LogoRect;
        public Win32.RECT ShortcutsPanel;
        public int ShortcutsLabelX;
        public int ShortcutsLabelWidth;
        public int ShortcutsShortcutX;
        public int ShortcutsShortcutWidth;
        public int KeyboardRowY;
        public int SearchRowY;
        public Win32.RECT KeyboardBoxRect;
        public Win32.RECT SearchBoxRect;
        public Win32.RECT KeyboardEditRect;
        public Win32.RECT SearchEditRect;
        public Win32.RECT ValidationRect;
        public Win32.RECT ResetRect;
        public Win32.RECT PreferencesPanel;
        public Win32.RECT AutoStartRect;
        public Win32.RECT NotificationsRect;
        // GuideRect et CloseButtonRect retirés — la croix système suffit
    }

    private IntPtr _hWnd;
    private IntPtr _hWndEditKeyboard;
    private IntPtr _hWndEditSearch;
    private IntPtr _hWndChkAutoStart;
    private IntPtr _hWndChkNotifications;
    private IntPtr _hWndLinkReset;
    private IntPtr _hWndValidation;

    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly Win32.SUBCLASSPROC _linkSubclassProc;
    private readonly Win32.SUBCLASSPROC _shortcutSubclassProc;
    private IntPtr _hoveredLink;
    private IntPtr _focusedShortcut;

    private readonly IntPtr _hBgBrush;
    private readonly IntPtr _hPanelBrush;
    private readonly IntPtr _hKeyBrush;

    private IntPtr _gdipToken;
    private IntPtr _gdipLogo;

    private bool _visible;
    private bool _keyboardValid = true;
    private bool _searchValid = true;
    private uint _keyboardVk;
    private uint _searchVk;
    private bool _showCaptureHint;
    private string _validationMessage = string.Empty;

    private float _dpiScale;
    private int S(int val) => (int)(val * _dpiScale);

    private IntPtr _hFontTitle;
    private IntPtr _hFontVersion;
    private IntPtr _hFontSubtitle;
    private IntPtr _hFontPanelTitle;
    private IntPtr _hFontText;
    private IntPtr _hFontBold;
    private IntPtr _hFontEdit;
    private IntPtr _hFontLink;
    private IntPtr _hFontLinkStrong;
    private IntPtr _hFontSmall;
    private IntPtr _hFontButton;

    public bool IsVisible => _visible;
    public Action? ShortcutChanged;

    public SettingsWindow()
    {
        _wndProcDelegate = WndProc;
        _linkSubclassProc = LinkSubclassProc;
        _shortcutSubclassProc = ShortcutSubclassProc;
        _hBgBrush = Win32.CreateSolidBrush(CLR_BG);
        _hPanelBrush = Win32.CreateSolidBrush(CLR_PANEL_BG);
        _hKeyBrush = Win32.CreateSolidBrush(CLR_KEY_BG);

        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        int dpi = Win32.GetDeviceCaps(hdcScreen, 88);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        _dpiScale = dpi / 96f;

        var gdipInput = new Win32.GdiplusStartupInput { GdiplusVersion = 1 };
        Win32.GdiplusStartup(out _gdipToken, ref gdipInput, IntPtr.Zero);
        _gdipLogo = GdiImageLoader.LoadFromEmbeddedResource(typeof(SettingsWindow), "favicon-azerty-global.png");
        LoadShortcutStateFromConfig();

        CreateFonts();
        CreateMainWindow();
        CreateControls();
        ApplyFontsToControls();
        RepositionControls();

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
        catch
        {
        }
    }

    private void CreateFonts()
    {
        _hFontTitle = Win32.CreateFontW(-S(18), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontVersion = Win32.CreateFontW(-S(9), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSubtitle = Win32.CreateFontW(-S(11), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontPanelTitle = Win32.CreateFontW(-S(13), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontText = Win32.CreateFontW(-S(11), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontBold = Win32.CreateFontW(-S(11), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontEdit = Win32.CreateFontW(-S(13), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontLink = Win32.CreateFontW(-S(11), 0, 0, 0, 400, 0, 1, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontLinkStrong = Win32.CreateFontW(-S(11), 0, 0, 0, 700, 0, 1, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSmall = Win32.CreateFontW(-S(9), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontButton = Win32.CreateFontW(-S(11), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
    }

    private void DestroyFonts()
    {
        Win32.DeleteObject(_hFontTitle);
        Win32.DeleteObject(_hFontVersion);
        Win32.DeleteObject(_hFontSubtitle);
        Win32.DeleteObject(_hFontPanelTitle);
        Win32.DeleteObject(_hFontText);
        Win32.DeleteObject(_hFontBold);
        Win32.DeleteObject(_hFontEdit);
        Win32.DeleteObject(_hFontLink);
        Win32.DeleteObject(_hFontLinkStrong);
        Win32.DeleteObject(_hFontSmall);
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
        Win32.SendMessageW(_hWndEditKeyboard, Win32.WM_SETFONT, _hFontEdit, (IntPtr)1);
        Win32.SendMessageW(_hWndEditSearch, Win32.WM_SETFONT, _hFontEdit, (IntPtr)1);
        Win32.SendMessageW(_hWndChkAutoStart, Win32.WM_SETFONT, _hFontBold, (IntPtr)1);
        Win32.SendMessageW(_hWndChkNotifications, Win32.WM_SETFONT, _hFontBold, (IntPtr)1);
        Win32.SendMessageW(_hWndLinkReset, Win32.WM_SETFONT, _hFontLink, (IntPtr)1);
        Win32.SendMessageW(_hWndValidation, Win32.WM_SETFONT, _hFontSmall, (IntPtr)1);
    }

    private void CreateMainWindow()
    {
        var hInstance = Win32.GetModuleHandleW(null);
        const string className = "AZERTYGlobal_Settings";

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
        var adjustRect = new Win32.RECT { left = 0, top = 0, right = winW, bottom = winH };
        Win32.AdjustWindowRectEx(ref adjustRect, dwStyle, false, 0);
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

        _hWnd = Win32.CreateWindowExW(0, className, "AZERTY Global — Paramètres",
            dwStyle, screenX + (screenW - windowW) / 2, screenY + (screenH - windowH) / 2, windowW, windowH,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private void CreateControls()
    {
        var hInstance = Win32.GetModuleHandleW(null);

        _hWndEditKeyboard = Win32.CreateWindowExW(0, "EDIT",
            ConfigManager.GetShortcutDisplayName(_keyboardVk),
            Win32.WS_CHILD | Win32.WS_VISIBLE | ES_AUTOHSCROLL | ES_CENTER | ES_UPPERCASE | Win32.WS_TABSTOP,
            0, 0, 0, 0,
            _hWnd, (IntPtr)IDC_EDIT_KEYBOARD, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndEditKeyboard, EM_SETREADONLY, (IntPtr)1, IntPtr.Zero);
        Win32.SetWindowSubclass(_hWndEditKeyboard, _shortcutSubclassProc, (UIntPtr)3, IntPtr.Zero);

        _hWndEditSearch = Win32.CreateWindowExW(0, "EDIT",
            ConfigManager.GetShortcutDisplayName(_searchVk),
            Win32.WS_CHILD | Win32.WS_VISIBLE | ES_AUTOHSCROLL | ES_CENTER | ES_UPPERCASE | Win32.WS_TABSTOP,
            0, 0, 0, 0,
            _hWnd, (IntPtr)IDC_EDIT_SEARCH, hInstance, IntPtr.Zero);
        Win32.SendMessageW(_hWndEditSearch, EM_SETREADONLY, (IntPtr)1, IntPtr.Zero);
        Win32.SetWindowSubclass(_hWndEditSearch, _shortcutSubclassProc, (UIntPtr)4, IntPtr.Zero);

        _hWndValidation = Win32.CreateWindowExW(0, "STATIC", "",
            Win32.WS_CHILD | Win32.WS_VISIBLE,
            0, 0, 0, 0,
            _hWnd, IntPtr.Zero, hInstance, IntPtr.Zero);

        _hWndLinkReset = Win32.CreateWindowExW(0, "STATIC", "Valeurs par défaut",
            Win32.WS_CHILD | Win32.WS_VISIBLE | SS_NOTIFY,
            0, 0, 0, 0,
            _hWnd, (IntPtr)IDC_LINK_RESET, hInstance, IntPtr.Zero);
        Win32.SetWindowSubclass(_hWndLinkReset, _linkSubclassProc, (UIntPtr)2, IntPtr.Zero);

        _hWndChkAutoStart = Win32.CreateWindowExW(0, "BUTTON", "Lancer au démarrage de Windows",
            Win32.WS_CHILD | Win32.WS_VISIBLE | BS_AUTOCHECKBOX | Win32.WS_TABSTOP,
            0, 0, 0, 0,
            _hWnd, (IntPtr)IDC_CHK_AUTOSTART, hInstance, IntPtr.Zero);
        RefreshAutoStartCheckbox();

        _hWndChkNotifications = Win32.CreateWindowExW(0, "BUTTON", "Notifications (activé / désactivé)",
            Win32.WS_CHILD | Win32.WS_VISIBLE | BS_AUTOCHECKBOX | Win32.WS_TABSTOP,
            0, 0, 0, 0,
            _hWnd, (IntPtr)IDC_CHK_NOTIFICATIONS, hInstance, IntPtr.Zero);
        if (ConfigManager.NotificationsEnabled)
            Win32.SendMessageW(_hWndChkNotifications, BM_SETCHECK, (IntPtr)BST_CHECKED, IntPtr.Zero);

    }

    private void ResizeWindow()
    {
        int winW = S(BASE_WIN_W);
        int winH = S(BASE_WIN_H);
        uint dwStyle = Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU;
        var adjustRect = new Win32.RECT { left = 0, top = 0, right = winW, bottom = winH };
        Win32.AdjustWindowRectEx(ref adjustRect, dwStyle, false, 0);
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
        LayoutInfo layout = GetLayout(winW, winH);

        Win32.MoveWindow(_hWndEditKeyboard,
            layout.KeyboardEditRect.left, layout.KeyboardEditRect.top,
            layout.KeyboardEditRect.right - layout.KeyboardEditRect.left,
            layout.KeyboardEditRect.bottom - layout.KeyboardEditRect.top, true);
        Win32.MoveWindow(_hWndEditSearch,
            layout.SearchEditRect.left, layout.SearchEditRect.top,
            layout.SearchEditRect.right - layout.SearchEditRect.left,
            layout.SearchEditRect.bottom - layout.SearchEditRect.top, true);

        Win32.MoveWindow(_hWndValidation,
            layout.ValidationRect.left, layout.ValidationRect.top,
            layout.ValidationRect.right - layout.ValidationRect.left,
            layout.ValidationRect.bottom - layout.ValidationRect.top, true);
        Win32.MoveWindow(_hWndLinkReset,
            layout.ResetRect.left, layout.ResetRect.top,
            layout.ResetRect.right - layout.ResetRect.left,
            layout.ResetRect.bottom - layout.ResetRect.top, true);

        Win32.MoveWindow(_hWndChkAutoStart,
            layout.AutoStartRect.left, layout.AutoStartRect.top,
            layout.AutoStartRect.right - layout.AutoStartRect.left,
            layout.AutoStartRect.bottom - layout.AutoStartRect.top, true);
        Win32.MoveWindow(_hWndChkNotifications,
            layout.NotificationsRect.left, layout.NotificationsRect.top,
            layout.NotificationsRect.right - layout.NotificationsRect.left,
            layout.NotificationsRect.bottom - layout.NotificationsRect.top, true);

    }

    private LayoutInfo GetLayout(int winW, int winH)
    {
        int margin = S(8);
        int contentWidth = winW - margin * 2;
        int headerTop = S(8);
        int logoSize = S(24);
        int panelPadX = S(9);

        IntPtr hdc = Win32.GetDC(_hWnd);
        try
        {
            int titleHeight = MeasureSingleLineHeight(hdc, _hFontTitle);
            int versionHeight = MeasureSingleLineHeight(hdc, _hFontVersion);
            int headerLineHeight = Math.Max(logoSize, Math.Max(titleHeight, versionHeight) + S(4));
            int logoY = headerTop + Math.Max(0, (headerLineHeight - logoSize) / 2);
            int headerTitleY = headerTop + Math.Max(0, (headerLineHeight - titleHeight) / 2);
            int headerBottom = headerTop + headerLineHeight + S(9);

            int shortcutsPanelTop = headerBottom + S(7);
            int panelTitleHeight = MeasureSingleLineHeight(hdc, _hFontPanelTitle);
            int textLineHeight = MeasureSingleLineHeight(hdc, _hFontText);
            int checkboxHeight = Math.Max(S(18), MeasureSingleLineHeight(hdc, _hFontBold));
            int linkHeight = MeasureSingleLineHeight(hdc, _hFontLinkStrong);
            int validationHeight = MeasureSingleLineHeight(hdc, _hFontSmall);

            int labelX = margin + panelPadX;
            int labelWidth = S(74);
            int keyOuterW = S(28);
            int keyOuterH = S(24);
            int keyOuterX = margin + contentWidth - panelPadX - keyOuterW;
            int shortcutX = labelX + labelWidth + S(6);
            int shortcutWidth = keyOuterX - shortcutX - S(8);

            int keyboardRowY = shortcutsPanelTop + S(30);
            int searchRowY = keyboardRowY + Math.Max(S(28), textLineHeight + S(11));

            var keyboardBoxRect = Rect(keyOuterX, keyboardRowY - S(4), keyOuterW, keyOuterH);
            var searchBoxRect = Rect(keyOuterX, searchRowY - S(4), keyOuterW, keyOuterH);
            var keyboardEditRect = Rect(keyOuterX + 1, keyboardRowY - S(3), keyOuterW - 2, keyOuterH - 2);
            var searchEditRect = Rect(keyOuterX + 1, searchRowY - S(3), keyOuterW - 2, keyOuterH - 2);
            int resetY = searchRowY + Math.Max(S(20), textLineHeight + S(7));
            var resetRect = Rect(labelX, resetY, S(118), Math.Max(S(18), linkHeight));

            bool showValidation = !string.IsNullOrEmpty(_validationMessage);
            int validationTop = showValidation ? resetRect.bottom + S(5) : resetRect.bottom;
            int currentValidationHeight = showValidation ? Math.Max(S(15), validationHeight) : 0;
            var validationRect = Rect(labelX, validationTop,
                contentWidth - panelPadX * 2, currentValidationHeight);

            int prefsTitleTop = (showValidation ? validationRect.bottom : resetRect.bottom) + S(10);
            int checkboxGap = S(6);
            var autoStartRect = Rect(labelX, prefsTitleTop + panelTitleHeight + S(9),
                contentWidth - panelPadX * 2, checkboxHeight);
            var notificationsRect = Rect(labelX, autoStartRect.bottom + checkboxGap,
                contentWidth - panelPadX * 2, checkboxHeight);

            int panelBottom = notificationsRect.bottom + S(12);
            var shortcutsPanel = Rect(margin, shortcutsPanelTop, contentWidth, panelBottom - shortcutsPanelTop);
            var preferencesPanel = Rect(margin, prefsTitleTop, contentWidth, panelBottom - prefsTitleTop);

            return new LayoutInfo
            {
                Margin = margin,
                HeaderTitleX = margin + logoSize + S(6),
                HeaderTitleY = headerTitleY,
                HeaderDividerY = headerBottom,
                LogoRect = Rect(margin, logoY, logoSize, logoSize),
                ShortcutsPanel = shortcutsPanel,
                ShortcutsLabelX = labelX,
                ShortcutsLabelWidth = labelWidth,
                ShortcutsShortcutX = shortcutX,
                ShortcutsShortcutWidth = shortcutWidth,
                KeyboardRowY = keyboardRowY,
                SearchRowY = searchRowY,
                KeyboardBoxRect = keyboardBoxRect,
                SearchBoxRect = searchBoxRect,
                KeyboardEditRect = keyboardEditRect,
                SearchEditRect = searchEditRect,
                ValidationRect = validationRect,
                ResetRect = resetRect,
                PreferencesPanel = preferencesPanel,
                AutoStartRect = autoStartRect,
                NotificationsRect = notificationsRect,
            };
        }
        finally
        {
            Win32.ReleaseDC(_hWnd, hdc);
        }
    }

    private static Win32.RECT Rect(int left, int top, int width, int height)
    {
        return new Win32.RECT
        {
            left = left,
            top = top,
            right = left + width,
            bottom = top + height
        };
    }

    public void Show()
    {
        LoadShortcutStateFromConfig();
        RefreshShortcutTexts();
        RefreshAutoStartCheckbox();
        Win32.SendMessageW(_hWndChkNotifications, BM_SETCHECK,
            ConfigManager.NotificationsEnabled ? (IntPtr)BST_CHECKED : IntPtr.Zero, IntPtr.Zero);
        SetValidationMessage(string.Empty);
        _keyboardValid = true;
        _searchValid = true;
        _focusedShortcut = IntPtr.Zero;

        RepositionControls();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
        Win32.ShowWindow(_hWnd, 1);
        Win32.SetForegroundWindow(_hWnd);
        _visible = true;
    }

    public void Close()
    {
        bool autoStart = Win32.SendMessageW(_hWndChkAutoStart, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)BST_CHECKED;
        bool autoStartSaved = AutoStart.Set(autoStart);
        RefreshAutoStartCheckbox();
        if (!autoStartSaved)
            ShowAutoStartError();

        bool notifications = Win32.SendMessageW(_hWndChkNotifications, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)BST_CHECKED;
        ConfigManager.SetNotifications(notifications);

        Win32.ShowWindow(_hWnd, 0);
        _visible = false;
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
                int code = (wParam.ToInt32() >> 16) & 0xFFFF;
                switch (id)
                {
                    case IDC_LINK_RESET:
                        if (code == 0)
                        {
                            _keyboardVk = 0x51;
                            _searchVk = 0x57;
                            ConfigManager.ShortcutVirtualKeyboardVk = _keyboardVk;
                            ConfigManager.ShortcutCharacterSearchVk = _searchVk;
                            RefreshShortcutTexts();
                            _keyboardValid = true;
                            _searchValid = true;
                            SetValidationMessage("Raccourcis réinitialisés ✓");
                            Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                            ShortcutChanged?.Invoke();
                        }
                        break;
                }
                return IntPtr.Zero;
            }

            case Win32.WM_CTLCOLORSTATIC:
            {
                IntPtr hdcStatic = wParam;
                IntPtr hCtrl = lParam;
                if (hCtrl == _hWndLinkReset)
                {
                    Win32.SetBkMode(hdcStatic, 1);
                    Win32.SetTextColor(hdcStatic, _hoveredLink == hCtrl ? CLR_LINK_HOVER : CLR_LINK);
                    return _hPanelBrush;
                }

                if (hCtrl == _hWndValidation)
                {
                    Win32.SetBkMode(hdcStatic, 1);
                    Win32.SetTextColor(hdcStatic, (_keyboardValid && _searchValid) ? CLR_VALID : CLR_INVALID);
                    return _hPanelBrush;
                }
                break;
            }

            case Win32.WM_CTLCOLORBTN:
            {
                IntPtr hdcButton = wParam;
                IntPtr hCtrl = lParam;
                if (hCtrl == _hWndChkAutoStart || hCtrl == _hWndChkNotifications)
                {
                    Win32.SetBkMode(hdcButton, 1);
                    Win32.SetTextColor(hdcButton, CLR_TEXT);
                    return _hPanelBrush;
                }
                break;
            }

            case Win32.WM_CTLCOLOREDIT:
            {
                IntPtr hdcEdit = wParam;
                IntPtr hCtrlEdit = lParam;
                if (hCtrlEdit == _hWndEditKeyboard)
                {
                    Win32.SetTextColor(hdcEdit, _keyboardValid ? CLR_TEXT : CLR_INVALID);
                    Win32.SetBkColor(hdcEdit, CLR_KEY_BG);
                    return _hKeyBrush;
                }

                if (hCtrlEdit == _hWndEditSearch)
                {
                    Win32.SetTextColor(hdcEdit, _searchValid ? CLR_TEXT : CLR_INVALID);
                    Win32.SetBkColor(hdcEdit, CLR_KEY_BG);
                    return _hKeyBrush;
                }
                break;
            }

            case Win32.WM_SETCURSOR:
                if (wParam == _hWndLinkReset)
                {
                    Win32.SetCursor(Win32.LoadCursorW(IntPtr.Zero, (IntPtr)32649));
                    return (IntPtr)1;
                }
                break;

            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
                if (_focusedShortcut == IntPtr.Zero && wParam == (IntPtr)VK_ESCAPE)
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
            ConfigManager.Log("Settings WndProc", ex);
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void LoadShortcutStateFromConfig()
    {
        _keyboardVk = ConfigManager.ShortcutVirtualKeyboardVk;
        _searchVk = ConfigManager.ShortcutCharacterSearchVk;
    }

    private void RefreshShortcutTexts()
    {
        RefreshShortcutText(_hWndEditKeyboard, _keyboardVk);
        RefreshShortcutText(_hWndEditSearch, _searchVk);
    }

    private void RefreshShortcutText(IntPtr hWndShortcut, uint vk)
    {
        Win32.SetWindowTextW(hWndShortcut, ConfigManager.GetShortcutDisplayName(vk));
    }

    private void SetValidationMessage(string text, bool captureHint = false)
    {
        _validationMessage = text;
        _showCaptureHint = captureHint;
        Win32.SetWindowTextW(_hWndValidation, text);
        if (_hWnd != IntPtr.Zero && _hWndValidation != IntPtr.Zero)
            RepositionControls();
    }

    private void ClearCaptureHintIfVisible()
    {
        if (_showCaptureHint)
            SetValidationMessage(string.Empty);
    }

    private void SetShortcutValidity(IntPtr hWndShortcut, bool valid)
    {
        if (hWndShortcut == _hWndEditKeyboard)
            _keyboardValid = valid;
        else if (hWndShortcut == _hWndEditSearch)
            _searchValid = valid;
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

    private bool IsModifierVirtualKey(uint vk)
    {
        return vk is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
    }

    private void CancelShortcutCapture(IntPtr hWndShortcut)
    {
        if (hWndShortcut == _hWndEditKeyboard)
        {
            _keyboardValid = true;
            RefreshShortcutText(hWndShortcut, _keyboardVk);
        }
        else
        {
            _searchValid = true;
            RefreshShortcutText(hWndShortcut, _searchVk);
        }

        ClearCaptureHintIfVisible();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private void ApplyCapturedShortcut(IntPtr hWndShortcut, uint vk)
    {
        bool isKeyboard = hWndShortcut == _hWndEditKeyboard;
        uint otherVk = isKeyboard ? _searchVk : _keyboardVk;

        if (!ConfigManager.IsShortcutAllowedVk(vk))
        {
            SetShortcutValidity(hWndShortcut, false);
            SetValidationMessage("Touche r\u00e9serv\u00e9e (conflit applications)");
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
            return;
        }

        if (vk == otherVk)
        {
            SetShortcutValidity(hWndShortcut, false);
            SetValidationMessage("D\u00e9j\u00e0 utilis\u00e9e");
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
            return;
        }

        SetShortcutValidity(hWndShortcut, true);

        if (isKeyboard)
        {
            _keyboardVk = vk;
            ConfigManager.ShortcutVirtualKeyboardVk = vk;
            RefreshShortcutText(hWndShortcut, _keyboardVk);
            SetValidationMessage("Raccourci clavier virtuel mis à jour ✓");
        }
        else
        {
            _searchVk = vk;
            ConfigManager.ShortcutCharacterSearchVk = vk;
            RefreshShortcutText(hWndShortcut, _searchVk);
            SetValidationMessage("Raccourci recherche mis à jour ✓");
        }

        ShortcutChanged?.Invoke();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private IntPtr ShortcutSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (msg)
        {
            case Win32.WM_GETDLGCODE:
            {
                IntPtr baseResult = Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
                if (lParam != IntPtr.Zero)
                {
                    var inputMsg = Marshal.PtrToStructure<Win32.MSG>(lParam);
                    if ((inputMsg.message == Win32.WM_KEYDOWN || inputMsg.message == Win32.WM_SYSKEYDOWN) &&
                        inputMsg.wParam == (IntPtr)VK_TAB)
                        return baseResult;
                }

                return (IntPtr)(baseResult.ToInt64() | DLGC_WANTALLKEYS);
            }

            case Win32.WM_SETFOCUS:
                _focusedShortcut = hWnd;
                SetShortcutValidity(hWnd, true);
                SetValidationMessage(ShortcutCaptureHint, true);
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                break;

            case Win32.WM_KILLFOCUS:
                if (_focusedShortcut == hWnd)
                    _focusedShortcut = IntPtr.Zero;
                SetShortcutValidity(hWnd, true);
                ClearCaptureHintIfVisible();
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                break;

            case Win32.WM_CHAR:
            case Win32.WM_PASTE:
            case Win32.WM_CUT:
            case Win32.WM_CLEAR:
            case Win32.WM_UNDO:
            case Win32.WM_CONTEXTMENU:
                return IntPtr.Zero;

            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
            {
                int vk = wParam.ToInt32();
                if ((lParam.ToInt64() & 0x40000000L) != 0)
                    return IntPtr.Zero;

                if (vk == VK_TAB)
                    return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);

                if (vk == VK_ESCAPE)
                {
                    if (_showCaptureHint)
                        CancelShortcutCapture(hWnd);
                    else
                        Close();
                    return IntPtr.Zero;
                }

                if (IsModifierVirtualKey((uint)vk))
                    return IntPtr.Zero;

                ApplyCapturedShortcut(hWnd, (uint)vk);
                return IntPtr.Zero;
            }
        }

        return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

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
        }

        return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void OnPaint(IntPtr hWnd)
    {
        var hdcPaint = Win32.BeginPaint(hWnd, out var ps);
        Win32.GetClientRect(hWnd, out var clientRect);
        int cw = clientRect.right;
        int ch = clientRect.bottom;
        LayoutInfo layout = GetLayout(cw, ch);

        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        var hdc = Win32.CreateCompatibleDC(hdcScreen);
        var hBmp = Win32.CreateCompatibleBitmap(hdcScreen, cw, ch);
        var hBmpOld = Win32.SelectObject(hdc, hBmp);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);

        Win32.FillRect(hdc, ref clientRect, _hBgBrush);
        Win32.SetBkMode(hdc, 1);

        Win32.GdipCreateFromHDC(hdc, out IntPtr gfx);
        if (gfx != IntPtr.Zero)
        {
            Win32.GdipSetSmoothingMode(gfx, 4);
            Win32.GdipSetInterpolationMode(gfx, 7);
            Win32.GdipSetTextRenderingHint(gfx, 4);
        }

        DrawHeader(hdc, gfx, layout, cw);
        GdiHelpers.DrawPanel(hdc, layout.ShortcutsPanel, CLR_PANEL_BG, CLR_PANEL_BORDER, 0, 0);
        PaintShortcutPanel(hdc, layout);
        PaintPreferencesPanel(hdc, layout);

        if (gfx != IntPtr.Zero)
            Win32.GdipDeleteGraphics(gfx);

        Win32.BitBlt(hdcPaint, 0, 0, cw, ch, hdc, 0, 0, Win32.SRCCOPY);
        Win32.SelectObject(hdc, hBmpOld);
        Win32.DeleteObject(hBmp);
        Win32.DeleteDC(hdc);
        Win32.EndPaint(hWnd, ref ps);
    }

    private void DrawHeader(IntPtr hdc, IntPtr gfx, LayoutInfo layout, int cw)
    {
        if (gfx != IntPtr.Zero && _gdipLogo != IntPtr.Zero)
        {
            Win32.GdipDrawImageRectI(gfx, _gdipLogo,
                layout.LogoRect.left, layout.LogoRect.top,
                layout.LogoRect.right - layout.LogoRect.left,
                layout.LogoRect.bottom - layout.LogoRect.top);
        }

        string version = $"v{Program.Version}";
        Win32.SelectObject(hdc, _hFontVersion);
        int versionHeight = MeasureSingleLineHeight(hdc, _hFontVersion);
        int titleHeight = MeasureSingleLineHeight(hdc, _hFontTitle);
        int versionTextWidth = MeasureSingleLineWidth(hdc, _hFontVersion, version);
        int versionWidth = versionTextWidth + S(24);
        int versionRight = cw - layout.Margin - S(6);
        int versionLeft = versionRight - versionWidth;
        int headerLineTop = Math.Min(layout.LogoRect.top, layout.HeaderTitleY);
        int headerLineBottom = Math.Max(layout.LogoRect.bottom, layout.HeaderTitleY + titleHeight);

        string title = "AZERTY Global";
        int titleRight = versionLeft - S(8);
        Win32.SelectObject(hdc, _hFontTitle);
        Win32.SetTextColor(hdc, CLR_TITLE);
        var titleRect = new Win32.RECT
        {
            left = layout.HeaderTitleX,
            top = layout.HeaderTitleY,
            right = titleRight,
            bottom = layout.HeaderTitleY + S(20)
        };
        Win32.DrawTextW(hdc, title, -1, ref titleRect,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        Win32.SetTextColor(hdc, CLR_VERSION);
        var versionRect = new Win32.RECT
        {
            left = versionLeft,
            top = headerLineTop - S(1),
            right = versionRight,
            bottom = headerLineBottom + S(3)
        };
        Win32.DrawTextW(hdc, version, -1, ref versionRect,
            Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        GdiHelpers.FillSolidRect(hdc, Rect(layout.Margin, layout.HeaderDividerY, cw - layout.Margin * 2, 1), CLR_SEPARATOR);
    }

    private void PaintShortcutPanel(IntPtr hdc, LayoutInfo layout)
    {
        int titleX = layout.ShortcutsPanel.left + S(12);
        int titleY = layout.ShortcutsPanel.top + S(8);
        Win32.SelectObject(hdc, _hFontPanelTitle);
        Win32.SetTextColor(hdc, CLR_LINK);
        var titleRect = new Win32.RECT
        {
            left = titleX,
            top = titleY,
            right = layout.ShortcutsPanel.right - S(12),
            bottom = titleY + S(20)
        };
        Win32.DrawTextW(hdc, "Raccourcis", -1, ref titleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        DrawShortcutRow(hdc, layout.ShortcutsLabelX, layout.ShortcutsLabelWidth, layout.KeyboardRowY,
            "Clavier virtuel", GetShortcutPrefixRuns(), layout.ShortcutsShortcutX, layout.ShortcutsShortcutWidth);
        DrawKeyBox(hdc, layout.KeyboardBoxRect, _keyboardValid, _focusedShortcut == _hWndEditKeyboard);

        DrawShortcutRow(hdc, layout.ShortcutsLabelX, layout.ShortcutsLabelWidth, layout.SearchRowY,
            "Recherche", GetShortcutPrefixRuns(), layout.ShortcutsShortcutX, layout.ShortcutsShortcutWidth);
        DrawKeyBox(hdc, layout.SearchBoxRect, _searchValid, _focusedShortcut == _hWndEditSearch);

        int dividerY = layout.PreferencesPanel.top - S(8);
        GdiHelpers.FillSolidRect(hdc, Rect(layout.ShortcutsPanel.left + S(12), dividerY,
            layout.ShortcutsPanel.right - layout.ShortcutsPanel.left - S(24), 1), CLR_SEPARATOR);
    }

    private void PaintPreferencesPanel(IntPtr hdc, LayoutInfo layout)
    {
        int titleX = layout.PreferencesPanel.left + S(12);
        int titleY = layout.PreferencesPanel.top;
        Win32.SelectObject(hdc, _hFontPanelTitle);
        Win32.SetTextColor(hdc, CLR_LINK);
        var titleRect = new Win32.RECT
        {
            left = titleX,
            top = titleY,
            right = layout.ShortcutsPanel.right - S(12),
            bottom = titleY + S(20)
        };
        Win32.DrawTextW(hdc, "Préférences", -1, ref titleRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
    }

    private void DrawShortcutRow(IntPtr hdc, int labelX, int labelWidth, int rowY,
        string label, (string Text, uint Color, IntPtr Font)[] shortcutRuns, int shortcutX, int shortcutWidth)
    {
        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, CLR_TEXT);
        var labelRect = new Win32.RECT
        {
            left = labelX,
            top = rowY + S(1),
            right = labelX + labelWidth,
            bottom = rowY + S(22)
        };
        Win32.DrawTextW(hdc, label, -1, ref labelRect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_VCENTER | Win32.DT_NOPREFIX);

        GdiHelpers.DrawColoredRuns(hdc, shortcutX, rowY + S(2), shortcutWidth, S(20), shortcutRuns);
    }

    private void DrawKeyBox(IntPtr hdc, Win32.RECT rect, bool valid, bool focused)
    {
        uint borderColor = !valid ? CLR_KEY_BORDER_INVALID : focused ? CLR_KEY_BORDER_FOCUS : CLR_KEY_BORDER;
        GdiHelpers.FillSolidRect(hdc, rect, borderColor);
        var innerRect = new Win32.RECT
        {
            left = rect.left + 1,
            top = rect.top + 1,
            right = rect.right - 1,
            bottom = rect.bottom - 1
        };
        GdiHelpers.FillSolidRect(hdc, innerRect, CLR_KEY_BG);
    }

    private void DrawPanelTitle(IntPtr hdc, int x, int y, int width, string title)
    {
        Win32.SelectObject(hdc, _hFontPanelTitle);
        Win32.SetTextColor(hdc, CLR_LINK);
        var rect = new Win32.RECT { left = x, top = y, right = x + width, bottom = y + S(22) };
        Win32.DrawTextW(hdc, title, -1, ref rect, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
    }

    // Méthodes GDI factorisées dans GdiHelpers.cs — wrappers d'instance pour le DPI scaling
    private int MeasureTextHeight(IntPtr hdc, IntPtr hFont, string text, int width,
        uint format = Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX)
        => GdiHelpers.MeasureTextHeight(hdc, hFont, text, width, format);

    private int MeasureSingleLineWidth(IntPtr hdc, IntPtr hFont, string text)
        => GdiHelpers.MeasureSingleLineWidth(hdc, hFont, text);

    private int MeasureSingleLineHeight(IntPtr hdc, IntPtr hFont)
        => GdiHelpers.MeasureSingleLineHeight(hdc, hFont);

    private (string Text, uint Color, IntPtr Font)[] GetShortcutPrefixRuns()
    {
        return new[]
        {
            ("Ctrl", CLR_INLINE_HIGHLIGHT, _hFontBold),
            (" + ", CLR_TEXT, _hFontText),
            ("Maj", CLR_INLINE_HIGHLIGHT, _hFontBold),
            (" + ", CLR_TEXT, _hFontText)
        };
    }

    private (string Text, uint Color, IntPtr Font)[] GetShortcutRuns(params string[] keys)
    {
        var runs = new List<(string Text, uint Color, IntPtr Font)>();
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0)
                runs.Add((" + ", CLR_TEXT, _hFontText));
            runs.Add((keys[i], CLR_INLINE_HIGHLIGHT, _hFontBold));
        }

        return runs.ToArray();
    }

    public void Dispose()
    {
        if (_hWndEditKeyboard != IntPtr.Zero)
            Win32.RemoveWindowSubclass(_hWndEditKeyboard, _shortcutSubclassProc, (UIntPtr)3);
        if (_hWndEditSearch != IntPtr.Zero)
            Win32.RemoveWindowSubclass(_hWndEditSearch, _shortcutSubclassProc, (UIntPtr)4);
        if (_hWndLinkReset != IntPtr.Zero)
            Win32.RemoveWindowSubclass(_hWndLinkReset, _linkSubclassProc, (UIntPtr)2);
        if (_hWnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }

        DestroyFonts();
        Win32.DeleteObject(_hBgBrush);
        Win32.DeleteObject(_hPanelBrush);
        Win32.DeleteObject(_hKeyBrush);

        if (_gdipLogo != IntPtr.Zero)
        {
            Win32.GdipDisposeImage(_gdipLogo);
            _gdipLogo = IntPtr.Zero;
        }
        if (_gdipToken != IntPtr.Zero)
        {
            Win32.GdiplusShutdown(_gdipToken);
            _gdipToken = IntPtr.Zero;
        }
    }
}

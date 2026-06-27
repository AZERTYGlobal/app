using System.Runtime.InteropServices;

namespace AZERTYGlobal;

internal sealed class LessonsWindow : IDisposable
{
    private const string WND_CLASS_NAME = "AZERTYGlobal_Lessons";
    private const int BASE_WIN_W = 1120;
    private const int BASE_WIN_H = 760;
    private const int BASE_MIN_W = 940;
    private const int BASE_MIN_H = 600;
    private const int BASE_SIDEBAR_W = 330;
    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;
    private const uint CS_DBLCLKS = 0x0008;
    private const uint TIMER_LINE_ADVANCE = 8301;
    private const uint TIMER_AUTO_HINT = 8302;
    private const uint TIMER_KEYPRESS = 8303;
    private const uint TIMER_HINT_CLEAR = 8304;
    private const uint TIMER_HINT_FLASH_CLEAR = 8305;
    private const uint TIMER_FREE_STATS = 8306;
    private const uint LINE_ADVANCE_MS = 300;
    private const uint KEYPRESS_DURATION_MS = 120;
    private const uint HINT_DIRECT_MS = 3000;
    private const uint HINT_DEADKEY_MS = 4500;
    private const uint HINT_FLASH_MS = 1000;
    private const uint FREE_STATS_REFRESH_MS = 1000;
    private const int PENDING_PHYSICAL_TEXT_TIMEOUT_MS = 1000;
    private const int FREE_PREVIEW_MAX_CHARS = 220;

    private const uint CLR_BG = 0x00201C18;
    private const uint CLR_PANEL = 0x00282018;
    private const uint CLR_PANEL_2 = 0x00302820;
    private const uint CLR_BORDER = 0x00484038;
    private const uint CLR_TEXT = 0x00F0EDE8;
    private const uint CLR_MUTED = 0x00A8A098;
    private const uint CLR_ACCENT = 0x000078D4;
    private const uint CLR_OK = 0x004CB050;
    private const uint CLR_BAD = 0x003232DC;
    private const uint CLR_CURRENT = 0x00D4A060;
    private const uint CLR_BUTTON = 0x00484038;
    private const uint CLR_BUTTON_HOT = 0x00585048;
    private const uint CLR_LESSON_ACCENT = 0x00D47800;

    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONWARNING = 0x00000030;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const int IDYES = 6;

    private enum WindowMode { Lessons, Free }

    private readonly record struct FreeVisualLine(int Start, int Length, int Width);

    private readonly Layout _layout;
    private readonly KeyMapper _mapper;
    private readonly KeyboardHook _hook;
    private readonly LessonCatalog _catalog;
    private readonly LessonProgressStore _progress;
    private readonly LessonHintProvider _hints;
    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly List<(Win32.RECT Rect, Action Action)> _clickActions = new();
    private readonly List<(Win32.RECT Rect, Action Action)> _doubleClickActions = new();
    private readonly List<(Win32.RECT Rect, string Tooltip, bool PreferAbove, bool Compact)> _hoverAreas = new();

    private IntPtr _hWnd;
    private bool _visible;
    private float _dpiScale = 1f;
    private float _windowScale = 1f;
    private int _baseClientW;
    private int _baseClientH;
    private int _nonClientW;
    private int _nonClientH;
    private bool _liveResizing;
    private IntPtr _resizeSnapshotBitmap;
    private int _resizeSnapshotW;
    private int _resizeSnapshotH;

    private IntPtr _hBgBrush;
    private IntPtr _hFontTitle;
    private IntPtr _hFontSubtitle;
    private IntPtr _hFontText;
    private IntPtr _hFontSmall;
    private IntPtr _hFontSidebarModule;
    private IntPtr _hFontSidebarLesson;
    private IntPtr _hFontMono;
    private IntPtr _hFontButton;
    private IntPtr _hFontIcon;
    private IntPtr _hFontEmoji;
    private IntPtr _hFontKeyboard;
    private IntPtr _hFontKeyboardSmall;
    private IntPtr _hFontKeyboardTiny;
    private IntPtr _hFontKeyboardContext;
    private IntPtr _hFontLessonLine;

    private WindowMode _mode = WindowMode.Lessons;
    private bool _settingsOpen;
    private int _moduleIndex;
    private int _lessonIndex;
    private int _exerciseIndex;
    private LessonTypingSession _session;
    private bool _showSummary;
    private LessonAttemptStats? _lastCompletedStats;
    private char? _hintCharacter;
    private LessonHintMethod? _hintMethod;
    private bool _hintBackspace;
    private bool _hintButtonActive;
    private int _consecutiveErrors;
    private uint _pressedScancode;
    private string? _pendingPhysicalText;
    private int _pendingPhysicalTextIndex;
    private long _pendingPhysicalTextTick;
    private bool _trackingMouseLeave;
    private int _focusedActionIndex = -1;
    private string? _hoverTooltip;
    private Win32.RECT _hoverTooltipAnchor;
    private bool _hoverTooltipPreferAbove;
    private bool _hoverTooltipCompact;

    private DateTimeOffset? _freeStartedAt;
    private int _freeChars;
    private int _freeBackspaces;
    private string _freePreview = "";
    private int _freeCursorIndex;
    private Win32.RECT _freePreviewRect;

    public LessonsWindow(Layout layout, KeyMapper mapper, KeyboardHook hook)
    {
        _layout = layout;
        _mapper = mapper;
        _hook = hook;
        _catalog = LessonCatalogLoader.LoadFromResource();
        _progress = new LessonProgressStore();
        _progress.SyncOnboardingProgress(_catalog, ConfigManager.LearningMaxStepCompleted);
        _hints = new LessonHintProvider();
        _wndProcDelegate = WndProc;
        _session = new LessonTypingSession(_catalog.Exercises[0]);

        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        int dpi = Win32.GetDeviceCaps(hdcScreen, 88);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        _dpiScale = dpi > 0 ? dpi / 96f : 1f;

        CreateFonts();
        CreateWindow();
        ConfigManager.WindowBoundsCleared += OnWindowBoundsCleared;
        PickInitialSelection();
        StartCurrentSession(savePosition: false);

        _mapper.StateChanged += OnMapperStateChanged;
        _hook.RawKeyDown += OnRawKeyDown;
    }

    public bool IsVisible => _visible;
    private bool _inputPaused;

    public void SetInputPaused(bool paused)
    {
        _inputPaused = paused;
    }

    public void Show()
    {
        if (!RestoreSavedBoundsIfVisible())
            CenterOnActiveMonitor();
        UpdateRenderScaleFromCurrentClient(force: true);
        _progress.SyncOnboardingProgress(_catalog, ConfigManager.LearningMaxStepCompleted);
        _mapper.RequestCapsLockOff();
        _mapper.SyncState();
        _visible = true;
        Win32.ShowWindow(_hWnd, 1);
        Win32.SetForegroundWindow(_hWnd);
        Win32.SetFocus(_hWnd);
        UpdateAutoHintIfEnabled();
        ArmFreeStatsTimerIfNeeded();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private int D(int value) => (int)Math.Round(value * _dpiScale);
    private int S(int value) => (int)Math.Round(value * _dpiScale * _windowScale);

    private LessonModule CurrentModule => _catalog.Modules[_moduleIndex];
    private LessonLesson CurrentLesson => CurrentModule.Lessons[_lessonIndex];
    private LessonExercise CurrentExercise => CurrentLesson.Exercises[_exerciseIndex];

    private void CreateFonts()
    {
        _hBgBrush = Win32.CreateSolidBrush(CLR_BG);
        CreateScaledFonts();
    }

    private void CreateScaledFonts()
    {
        _hFontTitle = Win32.CreateFontW(-S(22), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSubtitle = Win32.CreateFontW(-S(14), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontText = Win32.CreateFontW(-S(13), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSmall = Win32.CreateFontW(-S(12), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSidebarModule = Win32.CreateFontW(-S(15), 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontSidebarLesson = Win32.CreateFontW(-S(14), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontMono = Win32.CreateFontW(-S(18), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Consolas");
        _hFontLessonLine = Win32.CreateFontW(-S(14), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Consolas");
        _hFontButton = Win32.CreateFontW(-S(12), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
        _hFontIcon = Win32.CreateFontW(-S(18), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI Symbol");
        _hFontEmoji = Win32.CreateFontW(-S(17), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI Emoji");
        _hFontKeyboard = Win32.CreateFontW(S(28), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 4, 0, "Consolas");
        _hFontKeyboardSmall = Win32.CreateFontW(S(20), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Consolas");
        _hFontKeyboardTiny = Win32.CreateFontW(S(16), 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        _hFontKeyboardContext = Win32.CreateFontW(S(20), 0, 0, 0, 500, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
    }

    private void DestroyFonts()
    {
        DeleteObject(ref _hBgBrush);
        DestroyScaledFonts();
    }

    private void RecreateScaledFonts()
    {
        DestroyScaledFonts();
        CreateScaledFonts();
    }

    private void DestroyScaledFonts()
    {
        DeleteObject(ref _hFontTitle);
        DeleteObject(ref _hFontSubtitle);
        DeleteObject(ref _hFontText);
        DeleteObject(ref _hFontSmall);
        DeleteObject(ref _hFontSidebarModule);
        DeleteObject(ref _hFontSidebarLesson);
        DeleteObject(ref _hFontMono);
        DeleteObject(ref _hFontLessonLine);
        DeleteObject(ref _hFontButton);
        DeleteObject(ref _hFontIcon);
        DeleteObject(ref _hFontEmoji);
        DeleteObject(ref _hFontKeyboard);
        DeleteObject(ref _hFontKeyboardSmall);
        DeleteObject(ref _hFontKeyboardTiny);
        DeleteObject(ref _hFontKeyboardContext);
    }

    private static void DeleteObject(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        Win32.DeleteObject(handle);
        handle = IntPtr.Zero;
    }

    private void CreateWindow()
    {
        var hInstance = Win32.GetModuleHandleW(null);
        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            hCursor = Win32.LoadCursorW(IntPtr.Zero, (IntPtr)32512),
            hbrBackground = _hBgBrush,
            lpszClassName = WND_CLASS_NAME,
            style = CS_DBLCLKS
        };
        Win32.RegisterClassExW(ref wc);

        int winW = D(BASE_WIN_W);
        int winH = D(BASE_WIN_H);
        uint style = Win32.WS_OVERLAPPED | Win32.WS_CAPTION | Win32.WS_SYSMENU | Win32.WS_THICKFRAME | Win32.WS_CLIPCHILDREN;

        Win32.GetCursorPos(out var cursor);
        var monitor = Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
        var monInfo = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(monitor, ref monInfo);
        int screenW = monInfo.rcWork.right - monInfo.rcWork.left;
        int screenH = monInfo.rcWork.bottom - monInfo.rcWork.top;
        int x = monInfo.rcWork.left + Math.Max(0, (screenW - winW) / 2);
        int y = monInfo.rcWork.top + Math.Max(0, (screenH - winH) / 2);

        _hWnd = Win32.CreateWindowExW(0, WND_CLASS_NAME, "AZERTY Global — Leçons",
            style, x, y, winW, winH, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        CaptureBaseWindowMetrics();
        RestoreSavedBoundsIfVisible();
        UpdateRenderScaleFromCurrentClient(force: true);
        Win32.EnableDarkTitleBar(_hWnd);
    }

    private bool RestoreSavedBoundsIfVisible()
    {
        if (_hWnd == IntPtr.Zero) return false;
        if (!ConfigManager.TryGetWindowBounds(ConfigManager.LessonsWindowBoundsKey, out var rect))
            return false;
        if (!IsRectVisibleOnScreen(rect))
            return false;

        Win32.MoveWindow(_hWnd, rect.left, rect.top,
            rect.right - rect.left, rect.bottom - rect.top, false);
        return true;
    }

    private void CenterOnActiveMonitor()
    {
        if (_hWnd == IntPtr.Zero || !Win32.GetWindowRect(_hWnd, out var rect))
            return;

        int winW = Math.Max(1, rect.right - rect.left);
        int winH = Math.Max(1, rect.bottom - rect.top);
        Win32.GetCursorPos(out var cursor);
        var monitor = Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
        var monInfo = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref monInfo))
            return;

        int screenW = monInfo.rcWork.right - monInfo.rcWork.left;
        int screenH = monInfo.rcWork.bottom - monInfo.rcWork.top;
        int x = monInfo.rcWork.left + Math.Max(0, (screenW - winW) / 2);
        int y = monInfo.rcWork.top + Math.Max(0, (screenH - winH) / 2);
        Win32.MoveWindow(_hWnd, x, y, winW, winH, false);
    }

    private static bool IsRectVisibleOnScreen(Win32.RECT rect)
    {
        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        if (width < 100 || height < 80) return false;

        var center = new Win32.POINT
        {
            x = rect.left + width / 2,
            y = rect.top + height / 2
        };
        var monitor = Win32.MonitorFromPoint(center, 0);
        if (monitor == IntPtr.Zero) return false;

        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info)) return false;

        var work = info.rcWork;
        return rect.right > work.left + 40 &&
               rect.left < work.right - 40 &&
               rect.bottom > work.top + 40 &&
               rect.top < work.bottom - 40;
    }

    private void SaveWindowBounds()
    {
        if (_hWnd == IntPtr.Zero) return;
        if (Win32.GetWindowRect(_hWnd, out var rect) && IsRectVisibleOnScreen(rect))
            ConfigManager.SetWindowBounds(ConfigManager.LessonsWindowBoundsKey, rect);
    }

    private void OnWindowBoundsCleared(string key)
    {
        if (key != ConfigManager.LessonsWindowBoundsKey || _hWnd == IntPtr.Zero)
            return;
        CenterOnActiveMonitor();
        UpdateRenderScaleFromCurrentClient(force: true);
        if (_visible)
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private void CaptureBaseWindowMetrics()
    {
        if (Win32.GetClientRect(_hWnd, out var client))
        {
            _baseClientW = Math.Max(1, client.right - client.left);
            _baseClientH = Math.Max(1, client.bottom - client.top);
        }
        else
        {
            _baseClientW = D(BASE_WIN_W);
            _baseClientH = D(BASE_WIN_H);
        }

        if (Win32.GetWindowRect(_hWnd, out var window))
        {
            int outerW = Math.Max(1, window.right - window.left);
            int outerH = Math.Max(1, window.bottom - window.top);
            _nonClientW = Math.Max(0, outerW - _baseClientW);
            _nonClientH = Math.Max(0, outerH - _baseClientH);
        }
    }

    private void UpdateRenderScaleFromCurrentClient(bool force = false)
    {
        if (_baseClientW <= 0 || _baseClientH <= 0) return;
        if (!Win32.GetClientRect(_hWnd, out var client)) return;

        int clientW = Math.Max(1, client.right - client.left);
        int clientH = Math.Max(1, client.bottom - client.top);
        float nextScale = MathF.Max(0.1f, MathF.Min(clientW / (float)_baseClientW, clientH / (float)_baseClientH));
        if (!force && MathF.Abs(nextScale - _windowScale) < 0.01f) return;

        _windowScale = nextScale;
        RecreateScaledFonts();
    }

    private void ConstrainSizingRect(int edge, ref Win32.RECT rect)
    {
        if (_baseClientW <= 0 || _baseClientH <= 0) return;

        double ratio = _baseClientW / (double)_baseClientH;
        int clientW = Math.Max(1, rect.right - rect.left - _nonClientW);
        int clientH = Math.Max(1, rect.bottom - rect.top - _nonClientH);
        bool widthDriven = edge switch
        {
            WMSZ_LEFT or WMSZ_RIGHT => true,
            WMSZ_TOP or WMSZ_BOTTOM => false,
            _ => clientW / (double)clientH >= ratio
        };

        int newClientW;
        int newClientH;
        if (widthDriven)
        {
            newClientW = clientW;
            newClientH = (int)Math.Round(newClientW / ratio);
        }
        else
        {
            newClientH = clientH;
            newClientW = (int)Math.Round(newClientH * ratio);
        }

        EnforceMinimumClientSize(ref newClientW, ref newClientH, ratio);
        int newOuterW = newClientW + _nonClientW;
        int newOuterH = newClientH + _nonClientH;

        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT)
            rect.left = rect.right - newOuterW;
        else
            rect.right = rect.left + newOuterW;

        if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT)
            rect.top = rect.bottom - newOuterH;
        else
            rect.bottom = rect.top + newOuterH;
    }

    private void EnforceMinimumClientSize(ref int clientW, ref int clientH, double ratio)
    {
        int minW = D(BASE_MIN_W);
        int minH = D(BASE_MIN_H);
        if (clientW < minW)
        {
            clientW = minW;
            clientH = (int)Math.Round(clientW / ratio);
        }
        if (clientH < minH)
        {
            clientH = minH;
            clientW = (int)Math.Round(clientH * ratio);
        }
    }

    private void BeginLiveResize()
    {
        _liveResizing = true;
        CaptureResizeSnapshot();
    }

    private void EndLiveResize()
    {
        _liveResizing = false;
        DisposeResizeSnapshot();
        UpdateRenderScaleFromCurrentClient(force: true);
        SaveWindowBounds();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private void CaptureResizeSnapshot()
    {
        DisposeResizeSnapshot();
        if (!Win32.GetClientRect(_hWnd, out var rc)) return;

        int width = Math.Max(1, rc.right - rc.left);
        int height = Math.Max(1, rc.bottom - rc.top);
        IntPtr hdc = Win32.GetDC(_hWnd);
        if (hdc == IntPtr.Zero) return;

        IntPtr memDc = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            memDc = Win32.CreateCompatibleDC(hdc);
            _resizeSnapshotBitmap = Win32.CreateCompatibleBitmap(hdc, width, height);
            if (memDc == IntPtr.Zero || _resizeSnapshotBitmap == IntPtr.Zero)
            {
                DisposeResizeSnapshot();
                return;
            }

            oldBitmap = Win32.SelectObject(memDc, _resizeSnapshotBitmap);
            DrawWindowContents(memDc, rc);
            _resizeSnapshotW = width;
            _resizeSnapshotH = height;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero)
                Win32.SelectObject(memDc, oldBitmap);
            if (memDc != IntPtr.Zero)
                Win32.DeleteDC(memDc);
            Win32.ReleaseDC(_hWnd, hdc);
        }
    }

    private void DisposeResizeSnapshot()
    {
        DeleteObject(ref _resizeSnapshotBitmap);
        _resizeSnapshotW = 0;
        _resizeSnapshotH = 0;
    }

    private bool DrawLiveResizeSnapshot(IntPtr hdc, Win32.RECT rc)
    {
        if (_resizeSnapshotBitmap == IntPtr.Zero || _resizeSnapshotW <= 0 || _resizeSnapshotH <= 0)
            return false;

        int width = Math.Max(1, rc.right - rc.left);
        int height = Math.Max(1, rc.bottom - rc.top);
        IntPtr memDc = Win32.CreateCompatibleDC(hdc);
        if (memDc == IntPtr.Zero) return false;

        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            oldBitmap = Win32.SelectObject(memDc, _resizeSnapshotBitmap);
            Win32.SetStretchBltMode(hdc, Win32.HALFTONE);
            return Win32.StretchBlt(hdc, 0, 0, width, height, memDc, 0, 0, _resizeSnapshotW, _resizeSnapshotH, Win32.SRCCOPY);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
                Win32.SelectObject(memDc, oldBitmap);
            Win32.DeleteDC(memDc);
        }
    }

    private void PickInitialSelection()
    {
        if (_progress.LastModuleId != null && _progress.LastLessonId != null)
        {
            if (SelectExercise(_progress.LastModuleId, _progress.LastLessonId, _progress.LastExerciseIndex, savePosition: false))
                return;
        }

        foreach (var module in _catalog.Modules.Select((Module, Index) => (Module, Index)))
        {
            foreach (var lesson in module.Module.Lessons.Select((Lesson, Index) => (Lesson, Index)))
            {
                foreach (var exercise in lesson.Lesson.Exercises.Select((Exercise, Index) => (Exercise, Index)))
                {
                    if (!_progress.IsCompleted(exercise.Exercise))
                    {
                        _moduleIndex = module.Index;
                        _lessonIndex = lesson.Index;
                        _exerciseIndex = exercise.Index;
                        return;
                    }
                }
            }
        }
    }

    private bool SelectExercise(string moduleId, string lessonId, int exerciseIndex, bool savePosition)
    {
        for (int m = 0; m < _catalog.Modules.Count; m++)
        {
            if (!StringComparer.Ordinal.Equals(_catalog.Modules[m].Id, moduleId)) continue;
            for (int l = 0; l < _catalog.Modules[m].Lessons.Count; l++)
            {
                if (!StringComparer.Ordinal.Equals(_catalog.Modules[m].Lessons[l].Id, lessonId)) continue;
                if (exerciseIndex < 0 || exerciseIndex >= _catalog.Modules[m].Lessons[l].Exercises.Count)
                    return false;
                _moduleIndex = m;
                _lessonIndex = l;
                _exerciseIndex = exerciseIndex;
                StartCurrentSession(savePosition);
                return true;
            }
        }
        return false;
    }

    private void StartCurrentSession(bool savePosition)
    {
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_FREE_STATS);
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_LINE_ADVANCE);
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_CLEAR);
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_FLASH_CLEAR);
        _session = new LessonTypingSession(CurrentExercise);
        _showSummary = false;
        _lastCompletedStats = null;
        _hintCharacter = null;
        _hintMethod = null;
        _hintBackspace = false;
        _hintButtonActive = false;
        _consecutiveErrors = 0;
        ClearPendingPhysicalText();
        if (savePosition)
            _progress.SetLastPosition(CurrentExercise);
        UpdateAutoHintIfEnabled();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case Win32.WM_PAINT:
                    OnPaint();
                    return IntPtr.Zero;
                case Win32.WM_ERASEBKGND:
                    return (IntPtr)1;
                case Win32.WM_ENTERSIZEMOVE:
                    BeginLiveResize();
                    return IntPtr.Zero;
                case Win32.WM_EXITSIZEMOVE:
                    EndLiveResize();
                    return IntPtr.Zero;
                case Win32.WM_SIZING:
                    var sizingRect = Marshal.PtrToStructure<Win32.RECT>(lParam);
                    ConstrainSizingRect(wParam.ToInt32(), ref sizingRect);
                    Marshal.StructureToPtr(sizingRect, lParam, false);
                    return (IntPtr)1;
                case Win32.WM_SIZE:
                    if (!_liveResizing)
                    {
                        UpdateRenderScaleFromCurrentClient();
                        if (_visible)
                            SaveWindowBounds();
                    }
                    Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                    return IntPtr.Zero;
                case Win32.WM_GETMINMAXINFO:
                    var mmi = Marshal.PtrToStructure<Win32.MINMAXINFO>(lParam);
                    int minClientW = D(BASE_MIN_W);
                    int minClientH = D(BASE_MIN_H);
                    if (_baseClientW > 0 && _baseClientH > 0)
                    {
                        double ratio = _baseClientW / (double)_baseClientH;
                        EnforceMinimumClientSize(ref minClientW, ref minClientH, ratio);
                    }
                    mmi.ptMinTrackSize.x = minClientW + _nonClientW;
                    mmi.ptMinTrackSize.y = minClientH + _nonClientH;
                    Marshal.StructureToPtr(mmi, lParam, false);
                    return IntPtr.Zero;
                case Win32.WM_SETFOCUS:
                    Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case Win32.WM_LBUTTONUP:
                    OnClick(lParam);
                    return IntPtr.Zero;
                case Win32.WM_LBUTTONDBLCLK:
                    OnDoubleClick(lParam);
                    return IntPtr.Zero;
                case Win32.WM_MOUSEMOVE:
                    OnMouseMove(lParam);
                    return IntPtr.Zero;
                case Win32.WM_MOUSELEAVE:
                    OnMouseLeave();
                    return IntPtr.Zero;
                case Win32.WM_KEYDOWN:
                    if (_inputPaused)
                        return IntPtr.Zero;
                    OnKeyDown(wParam.ToInt32());
                    return IntPtr.Zero;
                case Win32.WM_CHAR:
                    if (_inputPaused)
                        return IntPtr.Zero;
                    OnChar((char)wParam.ToInt32());
                    return IntPtr.Zero;
                case Win32.WM_SYSCHAR:
                    if (_inputPaused)
                        return IntPtr.Zero;
                    OnChar((char)wParam.ToInt32());
                    return IntPtr.Zero;
                case Win32.WM_SYSDEADCHAR:
                    return IntPtr.Zero;
                case Win32.WM_SYSKEYDOWN:
                {
                    int vk = wParam.ToInt32();
                    if (vk == 0x73) // VK_F4, preserve Alt+F4.
                        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
                    if (_inputPaused)
                        return IntPtr.Zero;
                    OnKeyDown(vk);
                    return IntPtr.Zero;
                }
                case Win32.WM_SYSKEYUP:
                    return IntPtr.Zero;
                case Win32.WM_PASTE:
                case Win32.WM_CUT:
                case Win32.WM_CLEAR:
                    return IntPtr.Zero;
                case Win32.WM_TIMER:
                    OnTimer((uint)wParam.ToInt64());
                    return IntPtr.Zero;
                case Win32.WM_CLOSE:
                    Hide();
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            ConfigManager.Log("LessonsWindow.WndProc", ex);
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void OnPaint()
    {
        var hdc = Win32.BeginPaint(_hWnd, out var ps);
        IntPtr memDc = IntPtr.Zero;
        IntPtr memBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            Win32.GetClientRect(_hWnd, out var rc);
            int width = Math.Max(1, rc.right - rc.left);
            int height = Math.Max(1, rc.bottom - rc.top);
            if (_liveResizing && DrawLiveResizeSnapshot(hdc, rc))
                return;

            memDc = Win32.CreateCompatibleDC(hdc);
            if (memDc != IntPtr.Zero)
                memBitmap = Win32.CreateCompatibleBitmap(hdc, width, height);

            if (memDc != IntPtr.Zero && memBitmap != IntPtr.Zero)
            {
                oldBitmap = Win32.SelectObject(memDc, memBitmap);
                DrawWindowContents(memDc, rc);
                Win32.BitBlt(hdc, 0, 0, width, height, memDc, 0, 0, Win32.SRCCOPY);
            }
            else
            {
                DrawWindowContents(hdc, rc);
            }
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero)
                Win32.SelectObject(memDc, oldBitmap);
            if (memBitmap != IntPtr.Zero)
                Win32.DeleteObject(memBitmap);
            if (memDc != IntPtr.Zero)
                Win32.DeleteDC(memDc);
            Win32.EndPaint(_hWnd, ref ps);
        }
    }

    private void DrawWindowContents(IntPtr hdc, Win32.RECT rc)
    {
        GdiHelpers.FillSolidRect(hdc, rc, CLR_BG);
        Win32.SetBkMode(hdc, Win32.TRANSPARENT);
        _clickActions.Clear();
        _doubleClickActions.Clear();
        _hoverAreas.Clear();

        DrawHeader(hdc, rc);
        var body = new Win32.RECT { left = S(16), top = S(74), right = rc.right - S(16), bottom = rc.bottom - S(16) };
        var sidebar = new Win32.RECT { left = body.left, top = body.top, right = body.left + S(BASE_SIDEBAR_W), bottom = body.bottom };
        var content = new Win32.RECT { left = sidebar.right + S(14), top = body.top, right = body.right, bottom = body.bottom };
        DrawSidebar(hdc, sidebar);
        if (_settingsOpen)
            DrawSettingsContent(hdc, content);
        else if (_mode == WindowMode.Lessons)
            DrawLessonContent(hdc, content);
        else
            DrawFreeContent(hdc, content);

        DrawFocusRing(hdc);
        DrawHoverTooltip(hdc, rc);
    }

    private void DrawHeader(IntPtr hdc, Win32.RECT rc)
    {
        var titleRect = new Win32.RECT { left = S(20), top = S(12), right = rc.right / 2, bottom = S(44) };
        DrawText(hdc, _hFontTitle, "Leçons", titleRect, CLR_TEXT, Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE);

        int done = _progress.CountCompleted(_catalog);
        var progressRect = new Win32.RECT { left = S(120), top = S(20), right = rc.right / 2, bottom = S(44) };
        DrawText(hdc, _hFontSmall, $"{done}/{_catalog.TotalExerciseCount} exercices", progressRect, CLR_MUTED, Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE);

        int tabsWidth = S(252);
        int tabsLeft = Math.Max(S(220), (rc.right - tabsWidth) / 2);
        var lessonsTab = new Win32.RECT { left = tabsLeft, top = S(18), right = tabsLeft + S(122), bottom = S(48) };
        var freeTab = new Win32.RECT { left = tabsLeft + S(130), top = S(18), right = tabsLeft + S(252), bottom = S(48) };
        var tabsFrame = new Win32.RECT { left = lessonsTab.left - S(3), top = lessonsTab.top - S(3), right = freeTab.right + S(3), bottom = freeTab.bottom + S(3) };
        DrawRoundedBox(hdc, tabsFrame, CLR_PANEL_2, CLR_BORDER, S(8));
        DrawButton(hdc, lessonsTab, "Leçons", !_settingsOpen && _mode == WindowMode.Lessons, () => SwitchMode(WindowMode.Lessons));
        DrawButton(hdc, freeTab, "Libre", !_settingsOpen && _mode == WindowMode.Free, () => SwitchMode(WindowMode.Free));
        var settingsRect = new Win32.RECT { left = rc.right - S(136), top = S(18), right = rc.right - S(20), bottom = S(48) };
        DrawButton(hdc, settingsRect, "Paramètres", _settingsOpen, ToggleSettings);
    }

    private void DrawSidebar(IntPtr hdc, Win32.RECT rect)
    {
        GdiHelpers.DrawPanel(hdc, rect, CLR_PANEL, CLR_BORDER, 0, 0);
        int y = rect.top + S(14);
        int bottom = rect.bottom - S(12);

        for (int i = 0; i < _catalog.Modules.Count && y < bottom - S(34); i++)
        {
            var module = _catalog.Modules[i];
            var item = new Win32.RECT { left = rect.left + S(10), top = y, right = rect.right - S(10), bottom = y + S(36) };
            int completed = module.Lessons.SelectMany(l => l.Exercises).Count(_progress.IsCompleted);
            int total = module.Lessons.SelectMany(l => l.Exercises).Count();
            bool selectedModule = i == _moduleIndex;
            DrawSidebarModuleItem(hdc, item, $"{module.Title}  {completed}/{total}", selectedModule);
            int captured = i;
            AddClick(item, () => SelectModule(captured));
            y += S(40);

            if (!selectedModule)
            {
                y += S(4);
                continue;
            }

            for (int j = 0; j < module.Lessons.Count && y < bottom - S(30); j++)
            {
                var lesson = module.Lessons[j];
                var lessonItem = new Win32.RECT { left = rect.left + S(28), top = y, right = rect.right - S(10), bottom = y + S(32) };
                int lessonCompleted = lesson.Exercises.Count(_progress.IsCompleted);
                DrawSidebarLessonItem(hdc, lessonItem, $"{lesson.Title}  {lessonCompleted}/{lesson.Exercises.Count}", j == _lessonIndex);
                int capturedLesson = j;
                AddClick(lessonItem, () => SelectLesson(capturedLesson));
                y += S(34);
            }

            y += S(10);
        }

    }

    private void DrawSidebarModuleItem(IntPtr hdc, Win32.RECT rect, string text, bool selected)
    {
        GdiHelpers.FillSolidRect(hdc, rect, selected ? CLR_PANEL_2 : CLR_PANEL);
        if (selected)
            GdiHelpers.FillSolidRect(hdc, new Win32.RECT { left = rect.left, top = rect.top, right = rect.left + S(4), bottom = rect.bottom }, CLR_ACCENT);
        DrawText(hdc, _hFontSidebarModule, text, new Win32.RECT { left = rect.left + S(12), top = rect.top, right = rect.right - S(8), bottom = rect.bottom },
            CLR_TEXT, Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
    }

    private void DrawSidebarLessonItem(IntPtr hdc, Win32.RECT rect, string text, bool selected)
    {
        GdiHelpers.FillSolidRect(hdc, rect, selected ? CLR_PANEL_2 : CLR_PANEL);
        if (selected)
            GdiHelpers.FillSolidRect(hdc, new Win32.RECT { left = rect.left, top = rect.top, right = rect.left + S(4), bottom = rect.bottom }, CLR_LESSON_ACCENT);
        DrawText(hdc, _hFontSidebarLesson, text, new Win32.RECT { left = rect.left + S(12), top = rect.top, right = rect.right - S(8), bottom = rect.bottom },
            selected ? CLR_TEXT : CLR_MUTED, Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
    }

    private void DrawSettingsContent(IntPtr hdc, Win32.RECT rect)
    {
        GdiHelpers.DrawPanel(hdc, rect, CLR_PANEL, CLR_BORDER, 0, 0);
        int pad = S(22);
        int y = rect.top + pad;

        DrawText(hdc, _hFontTitle, "Paramètres", new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(34) }, CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        y += S(48);

        DrawText(hdc, _hFontSubtitle, "Leçons", new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(24) }, CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        y += S(30);
        DrawToggleRow(hdc, new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(42) },
            "Auto-indices", ConfigManager.LessonAutoHintsEnabled, ToggleAutoHints);
        y += S(50);
        DrawToggleRow(hdc, new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(42) },
            "Résumé après exercice", ConfigManager.LessonSummaryVisible, () => ToggleBool(ConfigManager.LessonSummaryVisible, ConfigManager.SetLessonSummaryVisible));
        y += S(50);

        DrawText(hdc, _hFontSubtitle, "Affichage", new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(24) }, CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        y += S(30);
        DrawToggleRow(hdc, new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(42) },
            "Stats du mode libre", ConfigManager.LessonFreeStatsVisible, () => ToggleBool(ConfigManager.LessonFreeStatsVisible, ConfigManager.SetLessonFreeStatsVisible));
        y += S(50);
        DrawToggleRow(hdc, new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(42) },
            "Clavier visuel", ConfigManager.LessonKeyboardVisible, () => ToggleBool(ConfigManager.LessonKeyboardVisible, ConfigManager.SetLessonKeyboardVisible));
        y += S(50);
        DrawToggleRow(hdc, new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(42) },
            "Marqueurs invisibles", ConfigManager.LessonInvisibleMarkersVisible, () => ToggleBool(ConfigManager.LessonInvisibleMarkersVisible, ConfigManager.SetLessonInvisibleMarkersVisible));
        y += S(62);

        DrawText(hdc, _hFontSubtitle, "Actions", new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(24) }, CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        y += S(32);
        var resetFree = new Win32.RECT { left = rect.left + pad, top = y, right = rect.left + pad + S(160), bottom = y + S(36) };
        var resetProgress = new Win32.RECT { left = resetFree.right + S(12), top = y, right = resetFree.right + S(196), bottom = y + S(36) };
        DrawButton(hdc, resetFree, "Reset stats libre", false, ResetFreeStats);
        DrawButton(hdc, resetProgress, "Reset progression", false, ResetProgress);
    }

    private void DrawToggleRow(IntPtr hdc, Win32.RECT row, string label, bool enabled, Action toggle)
    {
        DrawRoundedBox(hdc, row, CLR_PANEL_2, CLR_BORDER, S(6));
        DrawText(hdc, _hFontText, label, new Win32.RECT { left = row.left + S(14), top = row.top, right = row.right - S(96), bottom = row.bottom }, CLR_TEXT,
            Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);

        var stateRect = new Win32.RECT { left = row.right - S(138), top = row.top, right = row.right - S(72), bottom = row.bottom };
        DrawText(hdc, _hFontSmall, enabled ? "Activé" : "Désactivé", stateRect, enabled ? CLR_TEXT : CLR_MUTED,
            Win32.DT_RIGHT | Win32.DT_VCENTER | Win32.DT_SINGLELINE);

        var pill = new Win32.RECT { left = row.right - S(58), top = row.top + S(9), right = row.right - S(14), bottom = row.bottom - S(9) };
        DrawRoundedBox(hdc, pill, enabled ? CLR_ACCENT : CLR_BUTTON, enabled ? CLR_ACCENT : CLR_BORDER, S(12));
        var knob = enabled
            ? new Win32.RECT { left = pill.right - S(20), top = pill.top + S(2), right = pill.right - S(2), bottom = pill.bottom - S(2) }
            : new Win32.RECT { left = pill.left + S(2), top = pill.top + S(2), right = pill.left + S(20), bottom = pill.bottom - S(2) };
        DrawRoundedBox(hdc, knob, CLR_TEXT, CLR_TEXT, S(9));
        AddClick(row, toggle);
    }

    private void DrawLessonContent(IntPtr hdc, Win32.RECT rect)
    {
        GdiHelpers.DrawPanel(hdc, rect, CLR_PANEL, CLR_BORDER, 0, 0);
        int pad = S(18);
        int y = rect.top + pad;
        string exerciseLabel = $"Exercice {_exerciseIndex + 1}/{CurrentLesson.Exercises.Count}";
        var topLine = new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(22) };
        DrawText(hdc, _hFontSmall, exerciseLabel, topLine, CLR_MUTED, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        DrawText(hdc, _hFontSmall, BuildTypingStatus(), topLine, CLR_MUTED, Win32.DT_RIGHT | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
        y += S(24);
        DrawText(hdc, _hFontTitle, CurrentLesson.Title, new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(34) }, CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
        y += S(40);
        DrawText(hdc, _hFontText, FormatLessonInstruction(CurrentExercise.Instruction), new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(42) }, CLR_MUTED, Win32.DT_LEFT | Win32.DT_WORDBREAK);
        y += S(52);

        var keyboardRect = new Win32.RECT { left = rect.left + pad, top = rect.bottom - S(315), right = rect.right - pad, bottom = rect.bottom - S(16) };
        if (_showSummary && _lastCompletedStats != null)
        {
            MoveKeyboardCloserToInput(ref keyboardRect, y + S(116));
            DrawCompletedExerciseControls(hdc, rect, y);
            if (ConfigManager.LessonSummaryVisible)
                DrawSummary(hdc, rect, keyboardRect, y, _lastCompletedStats);
            else
                DrawCompletionNotice(hdc, rect, keyboardRect, y);
        }
        else
        {
            DrawTargetLine(hdc, rect, ref y);
            MoveKeyboardCloserToInput(ref keyboardRect, y);
        }

        if (ConfigManager.LessonKeyboardVisible)
            DrawKeyboard(hdc, keyboardRect, KeyboardRenderProfile.Lesson);
    }

    private void MoveKeyboardCloserToInput(ref Win32.RECT keyboardRect, int yAfterInput)
    {
        int height = keyboardRect.bottom - keyboardRect.top;
        int desiredTop = yAfterInput + S(34);
        if (desiredTop >= keyboardRect.top)
            return;

        keyboardRect.top = desiredTop;
        keyboardRect.bottom = desiredTop + height;
    }

    private void DrawCompletedExerciseControls(IntPtr hdc, Win32.RECT rect, int y)
    {
        var lineInfo = new Win32.RECT { left = rect.left + S(18), top = y, right = rect.right - S(18), bottom = y + S(24) };
        int iconAreaW = S(142);
        DrawText(hdc, _hFontSmall, "Exercice terminé", new Win32.RECT { left = lineInfo.left, top = lineInfo.top, right = lineInfo.right - iconAreaW - S(8), bottom = lineInfo.bottom },
            CLR_MUTED, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
        DrawLessonIconButtons(hdc, new Win32.RECT { left = lineInfo.right - iconAreaW, top = lineInfo.top - S(12), right = lineInfo.right, bottom = lineInfo.top + S(20) });
    }

    private void DrawTargetLine(IntPtr hdc, Win32.RECT rect, ref int y)
    {
        var lineInfo = new Win32.RECT { left = rect.left + S(18), top = y, right = rect.right - S(18), bottom = y + S(24) };
        int iconAreaW = S(142);
        var lineLabel = new Win32.RECT { left = lineInfo.left, top = lineInfo.top, right = lineInfo.right - iconAreaW - S(8), bottom = lineInfo.bottom };
        DrawText(hdc, _hFontSmall, $"Ligne {_session.LineIndex + 1}/{_session.TotalLines}", lineLabel, CLR_MUTED, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        DrawLessonIconButtons(hdc, new Win32.RECT { left = lineInfo.right - iconAreaW, top = lineInfo.top - S(12), right = lineInfo.right, bottom = lineInfo.top + S(20) });
        y += S(24);

        var targetBox = new Win32.RECT { left = rect.left + S(18), top = y, right = rect.right - S(18), bottom = y + S(44) };
        GdiHelpers.DrawPanel(hdc, targetBox, CLR_PANEL_2, CLR_BORDER, 0, 0);
        int lineScroll = CalculateLessonLineScrollOffset(hdc, targetBox);
        DrawTargetCharacters(hdc, targetBox, _session.GetCurrentLineSnapshot(), lineScroll);
        y += S(48);

        var typedBox = new Win32.RECT { left = rect.left + S(18), top = y, right = rect.right - S(18), bottom = y + S(40) };
        GdiHelpers.DrawPanel(hdc, typedBox, CLR_PANEL, CLR_BORDER, 0, 0);
        DrawTypedCharacters(hdc, typedBox, _session.GetTypedLineSnapshot(), lineScroll);

        y += S(44);
    }

    private int CalculateLessonLineScrollOffset(IntPtr hdc, Win32.RECT box)
    {
        string line = _session.CurrentLine;
        int cursor = Math.Clamp(_session.CursorPosition, 0, line.Length);
        int innerWidth = Math.Max(1, box.right - box.left - S(20));
        int contentWidth = MeasureLessonLinePrefixWidth(hdc, line, line.Length);
        int cursorX = MeasureLessonLinePrefixWidth(hdc, line, cursor);
        int margin = Math.Min(S(90), innerWidth / 4);
        int offset = Math.Max(0, cursorX - (innerWidth - margin));
        int maxOffset = Math.Max(0, contentWidth - innerWidth + S(8));
        return Math.Min(offset, maxOffset);
    }

    private int MeasureLessonLinePrefixWidth(IntPtr hdc, string text, int count)
    {
        int width = 0;
        int max = Math.Min(count, text.Length);
        for (int i = 0; i < max; i++)
            width += MeasureLessonCharacterWidth(hdc, text[i]);
        return width;
    }

    private int MeasureLessonCharacterWidth(IntPtr hdc, char ch)
    {
        string display = FormatVisibleCharacter(ch.ToString());
        return Math.Max(S(10), GdiHelpers.MeasureSingleLineWidth(hdc, _hFontLessonLine, display) + S(3));
    }

    private void DrawTargetCharacters(IntPtr hdc, Win32.RECT box, IReadOnlyList<LessonCharacterSnapshot> characters, int scrollOffset)
    {
        int x = box.left + S(10) - scrollOffset;
        int baselineY = box.top + S(7);

        foreach (var item in characters)
        {
            if (!DrawCharacterCell(hdc, box, item.Expected, item.State, ref x, baselineY, neutralCorrect: false))
                break;
        }
    }

    private void DrawTypedCharacters(IntPtr hdc, Win32.RECT box, IReadOnlyList<LessonTypedCharacterSnapshot> characters, int scrollOffset)
    {
        int x = box.left + S(10) - scrollOffset;
        int baselineY = box.top + S(5);

        foreach (var item in characters)
        {
            if (!DrawCharacterCell(hdc, box, item.Actual, item.State, ref x, baselineY, neutralCorrect: true))
                break;
        }

        if (!_session.IsLineComplete && !_session.IsExerciseComplete && x <= box.right - S(12))
        {
            var caret = new Win32.RECT { left = x + S(2), top = baselineY + S(4), right = x + S(5), bottom = baselineY + S(28) };
            GdiHelpers.FillSolidRect(hdc, caret, CLR_CURRENT);
        }
    }

    private bool DrawCharacterCell(IntPtr hdc, Win32.RECT box, char ch, LessonCharacterState state, ref int x, int y, bool neutralCorrect)
    {
        string display = FormatVisibleCharacter(ch.ToString());
        int width = MeasureLessonCharacterWidth(hdc, ch);
        int innerLeft = box.left + S(8);
        int innerRight = box.right - S(8);
        if (x + width < innerLeft)
        {
            x += width;
            return true;
        }
        if (x > innerRight)
            return false;

        var charRect = new Win32.RECT
        {
            left = Math.Max(x, innerLeft),
            top = y,
            right = Math.Min(x + width, innerRight),
            bottom = y + S(30)
        };
        if (charRect.right <= charRect.left)
            return false;

        bool underline = !neutralCorrect && state == LessonCharacterState.Current;

        uint textColor = state switch
        {
            LessonCharacterState.Correct => neutralCorrect ? CLR_TEXT : CLR_OK,
            LessonCharacterState.Wrong => neutralCorrect ? CLR_TEXT : CLR_BAD,
            LessonCharacterState.Current => neutralCorrect ? CLR_CURRENT : CLR_TEXT,
            _ => CLR_TEXT
        };
        DrawText(hdc, _hFontLessonLine, display, charRect, textColor, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
        if (underline)
        {
            int underlineY = Math.Min(charRect.bottom - S(3), y + S(28));
            GdiHelpers.FillSolidRect(hdc, new Win32.RECT { left = charRect.left + S(2), top = underlineY, right = charRect.right - S(2), bottom = underlineY + S(2) }, CLR_CURRENT);
        }
        x += width;
        return true;
    }

    private void DrawSummary(IntPtr hdc, Win32.RECT rect, Win32.RECT keyboardRect, int minTop, LessonAttemptStats stats)
    {
        int maxBottom = ConfigManager.LessonKeyboardVisible ? keyboardRect.top - S(10) : rect.bottom - S(22);
        int cardH = S(132);
        int top = Math.Max(minTop + S(40), maxBottom - cardH);
        int minCardH = S(92);
        if (maxBottom - top < minCardH)
            top = Math.Max(rect.top + S(8), maxBottom - minCardH);
        if (maxBottom <= top)
            return;

        var summaryRect = new Win32.RECT
        {
            left = rect.left + S(18),
            top = top,
            right = rect.right - S(18),
            bottom = maxBottom
        };

        DrawRoundedBox(hdc, summaryRect, CLR_PANEL_2, CLR_BORDER, S(8));

        int x = summaryRect.left + S(18);
        int yy = summaryRect.top + S(12);
        DrawText(hdc, _hFontSubtitle, "Exercice réussi", new Win32.RECT { left = x, top = yy, right = summaryRect.right - S(12), bottom = yy + S(24) },
            CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);

        int metricY = summaryRect.top + S(48);
        int contentRight = summaryRect.right - S(18);
        int gap = S(28);
        int metricW = Math.Max(S(80), (contentRight - x - gap * 2) / 3);
        DrawSummaryTextMetric(hdc, x, metricY, metricW, "Vitesse", FormatWpm(stats.Wpm), CLR_TEXT);
        DrawSummaryTextMetric(hdc, x + metricW + gap, metricY, metricW, "Précision", FormatNullable(stats.AccuracyPercent, "%"), GetAccuracyColor(stats.AccuracyPercent));
        DrawSummaryTextMetric(hdc, x + (metricW + gap) * 2, metricY, metricW, "Erreurs", stats.ErrorCount.ToString(), GetErrorColor(stats.ErrorCount));

        var hard = stats.GetHardestCharacters(3);
        string detailText = hard.Count == 0
            ? $"Temps : {stats.ElapsedSeconds:0}s    Aucun caractère difficile sur cette tentative."
            : $"Temps : {stats.ElapsedSeconds:0}s    À retravailler : " + string.Join("   ", hard.Select(ch => FormatVisibleCharacter(ch.ToString())));
        int detailTop = metricY + S(52);
        int detailBottom = summaryRect.bottom - S(10);
        if (detailBottom > detailTop + S(12))
        {
            DrawText(hdc, _hFontSmall, detailText, new Win32.RECT { left = x, top = detailTop, right = summaryRect.right - S(18), bottom = detailBottom },
                CLR_MUTED, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
        }
    }

    private void DrawSummaryTextMetric(IntPtr hdc, int x, int y, int width, string label, string value, uint valueColor)
    {
        DrawText(hdc, _hFontSmall, label, new Win32.RECT { left = x, top = y, right = x + width, bottom = y + S(16) },
            CLR_MUTED, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
        DrawText(hdc, _hFontSubtitle, value, new Win32.RECT { left = x, top = y + S(14), right = x + width, bottom = y + S(40) },
            valueColor, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
    }

    private void DrawCompletionNotice(IntPtr hdc, Win32.RECT rect, Win32.RECT keyboardRect, int minTop)
    {
        int bottom = ConfigManager.LessonKeyboardVisible ? keyboardRect.top - S(10) : rect.bottom - S(22);
        int top = Math.Max(minTop + S(46), bottom - S(64));
        var doneRect = new Win32.RECT { left = rect.left + S(18), top = top, right = rect.right - S(18), bottom = Math.Max(top + S(52), bottom) };
        DrawRoundedBox(hdc, doneRect, CLR_PANEL_2, CLR_BORDER, S(8));
        GdiHelpers.FillSolidRect(hdc, new Win32.RECT { left = doneRect.left, top = doneRect.top + S(8), right = doneRect.left + S(4), bottom = doneRect.bottom - S(8) }, CLR_ACCENT);
        DrawText(hdc, _hFontSubtitle, "Exercice réussi", new Win32.RECT { left = doneRect.left + S(18), top = doneRect.top, right = doneRect.right - S(18), bottom = doneRect.bottom },
            CLR_TEXT, Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE);
    }

    private void DrawLessonIconButtons(IntPtr hdc, Win32.RECT rect)
    {
        int size = Math.Min(S(30), rect.bottom - rect.top);
        int gap = S(6);
        int total = size * 4 + gap * 3;
        int x = rect.right - total;
        int y = rect.top + Math.Max(0, (rect.bottom - rect.top - size) / 2);

        DrawIconButton(hdc, new Win32.RECT { left = x, top = y, right = x + size, bottom = y + size }, "‹", "Précédent", false, PreviousExercise);
        x += size + gap;
        DrawIconButton(hdc, new Win32.RECT { left = x, top = y, right = x + size, bottom = y + size }, "›", "Suivant", false, NextExercise);
        x += size + gap;
        DrawIconButton(hdc, new Win32.RECT { left = x, top = y, right = x + size, bottom = y + size }, "↻", "Recommencer", false, () => StartCurrentSession(savePosition: true));
        x += size + gap;
        string hintTooltip = (!_hintBackspace && _hintMethod == null) ? "Indice" : FormatHintButtonText();
        DrawIconButton(hdc, new Win32.RECT { left = x, top = y, right = x + size, bottom = y + size }, "💡", hintTooltip,
            _hintButtonActive || ConfigManager.LessonAutoHintsEnabled, ShowHint, ToggleAutoHints);
    }

    private void DrawKeyboard(IntPtr hdc, Win32.RECT rect, KeyboardRenderProfile profile)
    {
        var state = new KeyboardRenderState
        {
            Shift = _mapper.ShiftDown,
            AltGr = _mapper.AltGrDown,
            Ctrl = _mapper.CtrlDown,
            Alt = _mapper.AltDown,
            CapsLock = _mapper.CapsLockActive,
            ActiveDeadKey = _mapper.ActiveDeadKey,
            PressedScancode = _pressedScancode,
            HintCharacter = _hintCharacter?.ToString(),
            ShowInvisibleMarkers = ConfigManager.LessonInvisibleMarkersVisible
        };
        if (profile == KeyboardRenderProfile.Lesson)
        {
            _hints.AddRequiredCharacters(CurrentLesson.Characters, state.LessonVisibleCharacters);
            _hints.AddRequiredCharacters(CurrentExercise.Content, state.LessonVisibleCharacters);
            if (_hintCharacter.HasValue)
            {
                state.LessonVisibleCharacters.Add(_hintCharacter.Value.ToString());
                _hints.AddRequiredDeadKey(_hintCharacter.Value, state.LessonVisibleCharacters);
            }
            ApplyHintHighlight(state);
        }
        KeyboardRenderer.Draw(hdc, rect, _layout, profile, state, _hFontKeyboard, _hFontKeyboardSmall, _hFontKeyboardTiny, _hFontKeyboardContext);
        AddKeyboardHoverAreas(rect, profile, state);
    }

    private void AddKeyboardHoverAreas(Win32.RECT rect, KeyboardRenderProfile profile, KeyboardRenderState state)
    {
        foreach (var hit in KeyboardRenderer.BuildHitTestRects(rect))
        {
            string tooltip = KeyboardRenderer.BuildTooltipText(_layout, profile, state, hit.Scancode, hit.Label);
            if (!string.IsNullOrWhiteSpace(tooltip))
                AddHover(hit.Rect, tooltip);
        }
    }

    private void DrawFreeContent(IntPtr hdc, Win32.RECT rect)
    {
        GdiHelpers.DrawPanel(hdc, rect, CLR_PANEL, CLR_BORDER, 0, 0);
        int pad = S(18);
        int y = rect.top + pad;
        DrawText(hdc, _hFontTitle, "Mode libre", new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(34) }, CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        y += S(42);
        DrawText(hdc, _hFontText, "Tape librement pour mesurer le rythme. Rien n'est enregistré après fermeture.", new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(28) }, CLR_MUTED, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        y += S(44);

        if (ConfigManager.LessonFreeStatsVisible)
        {
            string stats = BuildFreeStatsText();
            var resetStats = new Win32.RECT { left = rect.right - pad - S(116), top = y, right = rect.right - pad, bottom = y + S(32) };
            DrawText(hdc, _hFontSubtitle, stats, new Win32.RECT { left = rect.left + pad, top = y, right = resetStats.left - S(12), bottom = y + S(32) }, CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
            DrawButton(hdc, resetStats, "Reset stats", false, ResetFreeStats);
            y += S(46);
        }

        var preview = new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + S(140) };
        _freePreviewRect = preview;
        GdiHelpers.DrawPanel(hdc, preview, CLR_PANEL_2, CLR_BORDER, 0, 0);
        DrawFreePreviewText(hdc, preview);

        if (ConfigManager.LessonKeyboardVisible)
            DrawKeyboard(hdc, new Win32.RECT { left = rect.left + pad, top = rect.bottom - S(315), right = rect.right - pad, bottom = rect.bottom - S(16) }, KeyboardRenderProfile.Full);
    }

    private void DrawFreePreviewText(IntPtr hdc, Win32.RECT preview)
    {
        var lines = BuildFreeVisualLines(hdc, preview);
        int firstVisibleLine = GetFreeFirstVisibleLine(lines, preview);
        int left = FreeTextLeft(preview);
        int y = FreeTextTop(preview);
        int lineH = FreeLineHeight();

        if (_freePreview.Length == 0)
        {
            DrawText(hdc, _hFontMono, "...", new Win32.RECT { left = left, top = y, right = preview.right - S(12), bottom = y + lineH },
                CLR_MUTED, Win32.DT_LEFT | Win32.DT_SINGLELINE);
        }
        else
        {
            for (int i = firstVisibleLine; i < lines.Count; i++)
            {
                if (y + lineH > preview.bottom - S(8))
                    break;

                var line = lines[i];
                string text = _freePreview.Substring(line.Start, line.Length);
                DrawText(hdc, _hFontMono, text, new Win32.RECT { left = left, top = y, right = preview.right - S(12), bottom = y + lineH },
                    CLR_TEXT, Win32.DT_LEFT | Win32.DT_SINGLELINE);
                y += lineH;
            }
        }

        DrawFreeCaret(hdc, preview, lines);
    }

    private void DrawFreeCaret(IntPtr hdc, Win32.RECT preview, IReadOnlyList<FreeVisualLine> lines)
    {
        int lineIndex = FindFreeLineIndex(lines, _freeCursorIndex);
        int firstVisibleLine = GetFreeFirstVisibleLine(lines, preview);
        var line = lines[lineIndex];
        int offset = Math.Clamp(_freeCursorIndex - line.Start, 0, line.Length);
        string prefix = offset == 0 ? "" : _freePreview.Substring(line.Start, offset);
        int x = FreeTextLeft(preview) + GdiHelpers.MeasureSingleLineWidth(hdc, _hFontMono, prefix);
        int top = FreeTextTop(preview) + (lineIndex - firstVisibleLine) * FreeLineHeight() + S(4);
        int width = Math.Max(1, S(1));
        var caret = new Win32.RECT { left = x, top = top, right = x + width, bottom = top + S(18) };
        GdiHelpers.FillSolidRect(hdc, caret, CLR_CURRENT);
    }

    private List<FreeVisualLine> BuildFreeVisualLines(IntPtr hdc, Win32.RECT preview)
    {
        var lines = new List<FreeVisualLine>();
        int maxWidth = Math.Max(1, preview.right - preview.left - S(24));
        string text = _freePreview;

        if (text.Length == 0)
        {
            lines.Add(new FreeVisualLine(0, 0, 0));
            return lines;
        }

        int start = 0;
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n')
            {
                lines.Add(new FreeVisualLine(start, i - start, width));
                start = i + 1;
                width = 0;
                continue;
            }

            int charWidth = MeasureFreeCharWidth(hdc, ch);
            if (i > start && width + charWidth > maxWidth)
            {
                lines.Add(new FreeVisualLine(start, i - start, width));
                start = i;
                width = 0;
            }

            width += charWidth;
        }

        lines.Add(new FreeVisualLine(start, text.Length - start, width));
        return lines;
    }

    private int FindFreeLineIndex(IReadOnlyList<FreeVisualLine> lines, int cursorIndex)
    {
        int cursor = Math.Clamp(cursorIndex, 0, _freePreview.Length);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int lineEnd = line.Start + line.Length;
            if (cursor == line.Start ||
                (cursor > line.Start && cursor < lineEnd) ||
                (cursor == lineEnd && (i == lines.Count - 1 || lines[i + 1].Start > cursor)))
                return i;
        }
        return Math.Max(0, lines.Count - 1);
    }

    private int GetFreeVisibleLineCapacity(Win32.RECT preview)
    {
        int availableH = Math.Max(1, preview.bottom - S(8) - FreeTextTop(preview));
        return Math.Max(1, availableH / Math.Max(1, FreeLineHeight()));
    }

    private int GetFreeFirstVisibleLine(IReadOnlyList<FreeVisualLine> lines, Win32.RECT preview)
    {
        int visibleLineCount = GetFreeVisibleLineCapacity(preview);
        int cursorLine = FindFreeLineIndex(lines, _freeCursorIndex);
        int maxFirstLine = Math.Max(0, lines.Count - visibleLineCount);
        if (cursorLine < visibleLineCount)
            return 0;
        return Math.Clamp(cursorLine - visibleLineCount + 1, 0, maxFirstLine);
    }

    private int FindFreeIndexAtX(IntPtr hdc, FreeVisualLine line, int x)
    {
        if (line.Length <= 0)
            return line.Start;

        int width = 0;
        for (int i = 0; i < line.Length; i++)
        {
            int charWidth = MeasureFreeCharWidth(hdc, _freePreview[line.Start + i]);
            if (x < width + charWidth / 2)
                return line.Start + i;
            width += charWidth;
        }
        return line.Start + line.Length;
    }

    private void SetFreeCursorFromPoint(int x, int y)
    {
        var hdc = Win32.GetDC(_hWnd);
        try
        {
            var lines = BuildFreeVisualLines(hdc, _freePreviewRect);
            int firstVisibleLine = GetFreeFirstVisibleLine(lines, _freePreviewRect);
            int visibleLineIndex = Math.Clamp((y - FreeTextTop(_freePreviewRect)) / FreeLineHeight(), 0, GetFreeVisibleLineCapacity(_freePreviewRect) - 1);
            int lineIndex = Math.Clamp(firstVisibleLine + visibleLineIndex, 0, lines.Count - 1);
            int relativeX = Math.Max(0, x - FreeTextLeft(_freePreviewRect));
            _freeCursorIndex = FindFreeIndexAtX(hdc, lines[lineIndex], relativeX);
            EnsureFreeCursorInRange();
        }
        finally
        {
            Win32.ReleaseDC(_hWnd, hdc);
        }
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private bool MoveFreeCursorVertical(int direction)
    {
        if (_freePreviewRect.right <= _freePreviewRect.left)
            return false;

        var hdc = Win32.GetDC(_hWnd);
        try
        {
            var lines = BuildFreeVisualLines(hdc, _freePreviewRect);
            int currentLineIndex = FindFreeLineIndex(lines, _freeCursorIndex);
            int targetLineIndex = Math.Clamp(currentLineIndex + direction, 0, lines.Count - 1);
            if (targetLineIndex == currentLineIndex)
                return true;

            var currentLine = lines[currentLineIndex];
            int offset = Math.Clamp(_freeCursorIndex - currentLine.Start, 0, currentLine.Length);
            string prefix = offset == 0 ? "" : _freePreview.Substring(currentLine.Start, offset);
            int x = GdiHelpers.MeasureSingleLineWidth(hdc, _hFontMono, prefix);
            _freeCursorIndex = FindFreeIndexAtX(hdc, lines[targetLineIndex], x);
            EnsureFreeCursorInRange();
        }
        finally
        {
            Win32.ReleaseDC(_hWnd, hdc);
        }

        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        return true;
    }

    private int MeasureFreeCharWidth(IntPtr hdc, char ch)
        => GdiHelpers.MeasureSingleLineWidth(hdc, _hFontMono, ch.ToString());

    private int FreeTextLeft(Win32.RECT preview) => preview.left + S(12);
    private int FreeTextTop(Win32.RECT preview) => preview.top + S(12);
    private int FreeLineHeight() => S(26);

    private static bool IsPointInRect(Win32.RECT rect, int x, int y)
        => rect.right > rect.left && rect.bottom > rect.top &&
           x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;

    private void EnsureFreeCursorInRange()
    {
        _freeCursorIndex = Math.Clamp(_freeCursorIndex, 0, _freePreview.Length);
    }

    private bool MoveFreeCursorToLineBoundary(bool toEnd)
    {
        if (_freePreviewRect.right <= _freePreviewRect.left)
        {
            _freeCursorIndex = toEnd ? _freePreview.Length : 0;
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
            return true;
        }

        var hdc = Win32.GetDC(_hWnd);
        try
        {
            var lines = BuildFreeVisualLines(hdc, _freePreviewRect);
            var line = lines[FindFreeLineIndex(lines, _freeCursorIndex)];
            _freeCursorIndex = toEnd ? line.Start + line.Length : line.Start;
            EnsureFreeCursorInRange();
        }
        finally
        {
            Win32.ReleaseDC(_hWnd, hdc);
        }

        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        return true;
    }

    private bool HandleFreeModeKeyDown(int vk)
    {
        EnsureFreeCursorInRange();
        switch (vk)
        {
            case 0x25: // Left
                if (_freeCursorIndex > 0)
                    _freeCursorIndex--;
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                return true;

            case 0x27: // Right
                if (_freeCursorIndex < _freePreview.Length)
                    _freeCursorIndex++;
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                return true;

            case 0x26: // Up
                return MoveFreeCursorVertical(-1);

            case 0x28: // Down
                return MoveFreeCursorVertical(1);

            case 0x24: // Home
                return MoveFreeCursorToLineBoundary(toEnd: false);

            case 0x23: // End
                return MoveFreeCursorToLineBoundary(toEnd: true);

            case 0x08: // Backspace
                if (_freeCursorIndex > 0 && _freePreview.Length > 0)
                {
                    _freePreview = _freePreview.Remove(_freeCursorIndex - 1, 1);
                    _freeCursorIndex--;
                    _freeBackspaces++;
                    ArmFreeStatsTimerIfNeeded();
                }
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                return true;

            case 0x2E: // Delete
                if (_freeCursorIndex < _freePreview.Length)
                {
                    _freePreview = _freePreview.Remove(_freeCursorIndex, 1);
                    _freeBackspaces++;
                    ArmFreeStatsTimerIfNeeded();
                }
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                return true;

            default:
                return false;
        }
    }

    private void InsertFreeCharacter(char c)
    {
        EnsureFreeCursorInRange();
        _freeStartedAt ??= DateTimeOffset.UtcNow;
        _freeChars++;
        _freePreview = _freePreview.Insert(_freeCursorIndex, c.ToString());
        _freeCursorIndex++;
        TrimFreePreviewAroundCursor();
        ArmFreeStatsTimerIfNeeded();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void TrimFreePreviewAroundCursor()
    {
        if (_freePreview.Length <= FREE_PREVIEW_MAX_CHARS)
            return;

        int overflow = _freePreview.Length - FREE_PREVIEW_MAX_CHARS;
        if (_freeCursorIndex >= _freePreview.Length - overflow)
        {
            _freePreview = _freePreview[overflow..];
            _freeCursorIndex -= overflow;
        }
        else
        {
            _freePreview = _freePreview.Remove(Math.Max(0, _freePreview.Length - overflow), overflow);
        }
        EnsureFreeCursorInRange();
    }

    private string BuildFreeStatsText()
    {
        if (!_freeStartedAt.HasValue || _freeChars == 0)
            return "WPM : —    Caractères/min : —    Durée : 0s    Corrections : 0";
        double seconds = Math.Max(1, (DateTimeOffset.UtcNow - _freeStartedAt.Value).TotalSeconds);
        double minutes = seconds / 60d;
        int wpm = _freeChars >= 10 ? (int)Math.Round((_freeChars / 5d) / minutes) : 0;
        int cpm = (int)Math.Round(_freeChars / minutes);
        return $"WPM : {(wpm == 0 ? "—" : wpm)}    Caractères/min : {cpm}    Durée : {seconds:0}s    Corrections : {_freeBackspaces}";
    }

    private void ResetFreeStats()
    {
        _freeStartedAt = null;
        _freeChars = 0;
        _freeBackspaces = 0;
        _freePreview = "";
        _freeCursorIndex = 0;
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void DrawButton(IntPtr hdc, Win32.RECT rect, string text, bool active, Action action)
    {
        uint fill = active ? CLR_ACCENT : CLR_BUTTON_HOT;
        uint border = active ? CLR_ACCENT : CLR_BORDER;
        DrawRoundedBox(hdc, rect, fill, border, S(6));
        DrawText(hdc, _hFontButton, text, rect, CLR_TEXT, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
        AddClick(rect, action);
    }

    private void DrawIconButton(IntPtr hdc, Win32.RECT rect, string icon, string tooltip, bool active, Action action, Action? doubleClickAction = null)
    {
        uint fill = active ? CLR_ACCENT : CLR_BUTTON_HOT;
        uint border = active ? CLR_ACCENT : CLR_BORDER;
        DrawRoundedBox(hdc, rect, fill, border, S(6));
        DrawText(hdc, icon == "💡" ? _hFontEmoji : _hFontIcon, icon, rect, CLR_TEXT, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
        AddClick(rect, action);
        if (doubleClickAction != null)
            AddDoubleClick(rect, doubleClickAction);
        AddHover(rect, tooltip, preferAbove: true, compact: true);
    }

    private void DrawHoverTooltip(IntPtr hdc, Win32.RECT bounds)
    {
        if (string.IsNullOrEmpty(_hoverTooltip)) return;

        int pad = S(8);
        string[] lines = _hoverTooltip.Split('\n');
        int lineH = S(18);
        int textWidth = lines.Max(line => GdiHelpers.MeasureSingleLineWidth(hdc, _hFontSmall, line));
        int minWidth = _hoverTooltipCompact ? 0 : S(80);
        int width = Math.Max(minWidth, textWidth) + pad * 2;
        int height = Math.Max(S(26), lines.Length * lineH + pad);
        int anchorCenter = _hoverTooltipAnchor.left + (_hoverTooltipAnchor.right - _hoverTooltipAnchor.left) / 2;
        int left = Math.Max(bounds.left + S(8), Math.Min(anchorCenter - width / 2, bounds.right - width - S(8)));
        int top = _hoverTooltipPreferAbove
            ? _hoverTooltipAnchor.top - height - S(6)
            : _hoverTooltipAnchor.bottom + S(6);
        if (top < bounds.top + S(8))
            top = _hoverTooltipAnchor.bottom + S(6);
        if (top + height > bounds.bottom - S(8))
            top = _hoverTooltipAnchor.top - height - S(6);

        var rect = new Win32.RECT { left = left, top = top, right = left + width, bottom = top + height };
        DrawRoundedBox(hdc, rect, CLR_PANEL_2, CLR_BORDER, S(6));
        int y = rect.top + Math.Max(S(4), (height - lines.Length * lineH) / 2);
        foreach (var line in lines)
        {
            DrawText(hdc, _hFontSmall, line, new Win32.RECT { left = rect.left + pad, top = y, right = rect.right - pad, bottom = y + lineH },
                CLR_TEXT, Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_END_ELLIPSIS);
            y += lineH;
        }
    }

    private void DrawFocusRing(IntPtr hdc)
    {
        if (_focusedActionIndex < 0 || _clickActions.Count == 0)
            return;

        if (_focusedActionIndex >= _clickActions.Count)
            _focusedActionIndex = _clickActions.Count - 1;

        var rect = _clickActions[_focusedActionIndex].Rect;
        rect.left -= S(2);
        rect.top -= S(2);
        rect.right += S(2);
        rect.bottom += S(2);

        var pen = Win32.CreatePen(0, Math.Max(1, S(2)), CLR_CURRENT);
        var oldBrush = Win32.SelectObject(hdc, Win32.GetStockObject(Win32.NULL_BRUSH));
        var oldPen = Win32.SelectObject(hdc, pen);
        Win32.RoundRect(hdc, rect.left, rect.top, rect.right, rect.bottom, S(6), S(6));
        Win32.SelectObject(hdc, oldPen);
        Win32.SelectObject(hdc, oldBrush);
        Win32.DeleteObject(pen);
    }

    private void DrawRoundedBox(IntPtr hdc, Win32.RECT rect, uint fill, uint border, int radius)
    {
        var brush = Win32.CreateSolidBrush(fill);
        var pen = Win32.CreatePen(0, 1, border);
        var oldBrush = Win32.SelectObject(hdc, brush);
        var oldPen = Win32.SelectObject(hdc, pen);
        Win32.RoundRect(hdc, rect.left, rect.top, rect.right, rect.bottom, radius, radius);
        Win32.SelectObject(hdc, oldPen);
        Win32.SelectObject(hdc, oldBrush);
        Win32.DeleteObject(pen);
        Win32.DeleteObject(brush);
    }

    private void DrawText(IntPtr hdc, IntPtr font, string text, Win32.RECT rect, uint color, uint flags)
    {
        Win32.SelectObject(hdc, font);
        Win32.SetTextColor(hdc, color);
        Win32.DrawTextW(hdc, text, -1, ref rect, flags | Win32.DT_NOPREFIX);
    }

    private void AddClick(Win32.RECT rect, Action action)
    {
        _clickActions.Add((rect, action));
    }

    private void AddDoubleClick(Win32.RECT rect, Action action)
    {
        _doubleClickActions.Add((rect, action));
    }

    private void AddHover(Win32.RECT rect, string tooltip, bool preferAbove = false, bool compact = false)
    {
        _hoverAreas.Add((rect, tooltip, preferAbove, compact));
    }

    private void OnClick(IntPtr lParam)
    {
        int x = unchecked((short)((long)lParam & 0xFFFF));
        int y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
        foreach (var (rect, action) in _clickActions)
        {
            if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom)
            {
                action();
                return;
            }
        }
        if (_mode == WindowMode.Free && IsPointInRect(_freePreviewRect, x, y))
        {
            SetFreeCursorFromPoint(x, y);
            Win32.SetFocus(_hWnd);
            return;
        }
        Win32.SetFocus(_hWnd);
    }

    private void OnDoubleClick(IntPtr lParam)
    {
        int x = unchecked((short)((long)lParam & 0xFFFF));
        int y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
        foreach (var (rect, action) in _doubleClickActions)
        {
            if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom)
            {
                action();
                return;
            }
        }
    }

    private void OnMouseMove(IntPtr lParam)
    {
        int x = unchecked((short)((long)lParam & 0xFFFF));
        int y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));

        if (!_trackingMouseLeave)
        {
            var tme = new Win32.TRACKMOUSEEVENT
            {
                cbSize = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(),
                dwFlags = Win32.TME_LEAVE,
                hwndTrack = _hWnd,
                dwHoverTime = 0
            };
            _trackingMouseLeave = Win32.TrackMouseEvent(ref tme);
        }

        string? tooltip = null;
        var anchor = new Win32.RECT();
        bool preferAbove = false;
        bool compact = false;
        foreach (var (rect, candidate, candidatePreferAbove, candidateCompact) in _hoverAreas)
        {
            if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom)
            {
                tooltip = candidate;
                anchor = rect;
                preferAbove = candidatePreferAbove;
                compact = candidateCompact;
                break;
            }
        }

        if (!StringComparer.Ordinal.Equals(_hoverTooltip, tooltip) ||
            _hoverTooltipAnchor.left != anchor.left ||
            _hoverTooltipAnchor.top != anchor.top ||
            _hoverTooltipAnchor.right != anchor.right ||
            _hoverTooltipAnchor.bottom != anchor.bottom ||
            _hoverTooltipPreferAbove != preferAbove ||
            _hoverTooltipCompact != compact)
        {
            _hoverTooltip = tooltip;
            _hoverTooltipAnchor = anchor;
            _hoverTooltipPreferAbove = preferAbove;
            _hoverTooltipCompact = compact;
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        }
    }

    private void OnMouseLeave()
    {
        _trackingMouseLeave = false;
        if (_hoverTooltip == null) return;
        _hoverTooltip = null;
        _hoverTooltipAnchor = new Win32.RECT();
        _hoverTooltipPreferAbove = false;
        _hoverTooltipCompact = false;
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void MoveKeyboardFocus(int direction)
    {
        if (_clickActions.Count == 0)
        {
            _focusedActionIndex = -1;
            return;
        }

        if (_focusedActionIndex < 0 || _focusedActionIndex >= _clickActions.Count)
            _focusedActionIndex = direction < 0 ? _clickActions.Count - 1 : 0;
        else
            _focusedActionIndex = (_focusedActionIndex + direction + _clickActions.Count) % _clickActions.Count;

        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private bool ActivateFocusedAction()
    {
        if (_focusedActionIndex < 0 || _focusedActionIndex >= _clickActions.Count)
            return false;

        _clickActions[_focusedActionIndex].Action();
        return true;
    }

    private void OnKeyDown(int vk)
    {
        if (vk == 0x1B)
        {
            if (_settingsOpen)
            {
                _settingsOpen = false;
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
                return;
            }
            Hide();
            return;
        }
        if (vk == 0x09)
        {
            bool backwards = (Win32.GetKeyState(0x10) & unchecked((short)0x8000)) != 0;
            MoveKeyboardFocus(backwards ? -1 : 1);
            return;
        }
        if (vk == 0x0D || (_settingsOpen && vk == 0x20))
        {
            if (ActivateFocusedAction())
                return;
        }
        if (_settingsOpen) return;
        if (_mode == WindowMode.Free && HandleFreeModeKeyDown(vk))
            return;

        if (_mode == WindowMode.Lessons && vk == 0x08)
        {
            var result = _session.Backspace();
            if (result.Accepted)
            {
                ClearHint();
                UpdateAutoHintIfEnabled();
            }
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
            return;
        }
    }

    private void OnChar(char c)
    {
        if (_settingsOpen) return;
        c = ResolveTypedCharacter(c);
        if (char.IsControl(c)) return;
        if (_mode == WindowMode.Free)
        {
            InsertFreeCharacter(c);
            return;
        }

        if (_showSummary) return;
        var result = _session.TypeChar(c);
        if (!result.Accepted) return;
        ClearHint();
        if (result.WasError)
            RegisterError(result.Expected, result.Actual);
        else
            ResetErrorState();

        if (result.LineCompleted)
        {
            ResetErrorState();
            ClearHint();
            if (result.ExerciseCompleted)
                CompleteExercise();
            else
                Win32.SetTimer(_hWnd, (UIntPtr)TIMER_LINE_ADVANCE, LINE_ADVANCE_MS, IntPtr.Zero);
        }
        else
        {
            UpdateAutoHintIfEnabled();
        }

        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void OnTimer(uint timerId)
    {
        if (timerId == TIMER_LINE_ADVANCE)
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_LINE_ADVANCE);
            _session.AdvanceCompletedLine();
            ClearHint();
            UpdateAutoHintIfEnabled();
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        }
        else if (timerId == TIMER_AUTO_HINT)
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
            UpdateAutoHintIfEnabled();
        }
        else if (timerId == TIMER_KEYPRESS)
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_KEYPRESS);
            _pressedScancode = 0;
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        }
        else if (timerId == TIMER_HINT_CLEAR)
        {
            ClearHint();
            UpdateAutoHintIfEnabled();
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        }
        else if (timerId == TIMER_HINT_FLASH_CLEAR)
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_FLASH_CLEAR);
            _hintButtonActive = false;
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
        }
        else if (timerId == TIMER_FREE_STATS)
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_FREE_STATS);
            if (_visible && _mode == WindowMode.Free && !_settingsOpen && _freeStartedAt.HasValue && ConfigManager.LessonFreeStatsVisible)
            {
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                Win32.SetTimer(_hWnd, (UIntPtr)TIMER_FREE_STATS, FREE_STATS_REFRESH_MS, IntPtr.Zero);
            }
        }
    }

    private void CompleteExercise()
    {
        if (_showSummary) return;
        _lastCompletedStats = _session.Stats;
        _progress.RecordSuccess(CurrentExercise, _session.Stats);
        _showSummary = true;
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
        ClearHint();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private void RegisterError(char? expected, char? actual)
    {
        _consecutiveErrors++;
    }

    private void ResetErrorState()
    {
        _consecutiveErrors = 0;
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
    }

    private void ShowHint()
    {
        ShowHintCore(automatic: false);
    }

    private void ShowHintCore(bool automatic)
    {
        if (_session.NeedsBackspaceCorrection)
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
            if (_hintBackspace)
            {
                ArmHintTimers(clearAfterDuration: !automatic);
                Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
                return;
            }
            _hintBackspace = true;
            _hintCharacter = null;
            _hintMethod = null;
            _session.RecordHint();
            ArmHintTimers(clearAfterDuration: !automatic);
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
            return;
        }

        char? next = _session.GetNextExpectedCharacter();
        if (!next.HasValue) return;
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
        if (_hintCharacter == next.Value && _hintMethod != null)
        {
            ArmHintTimers(clearAfterDuration: !automatic);
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
            return;
        }
        var method = _hints.GetRecommendedMethod(next.Value);
        if (method == null) return;
        _hintBackspace = false;
        _hintCharacter = next.Value;
        _hintMethod = method;
        _session.RecordHint();
        ArmHintTimers(clearAfterDuration: !automatic);
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void ClearHint()
    {
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_CLEAR);
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_FLASH_CLEAR);
        _hintCharacter = null;
        _hintMethod = null;
        _hintBackspace = false;
        _hintButtonActive = false;
    }

    private void ArmHintTimers(bool clearAfterDuration)
    {
        _hintButtonActive = true;
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_FLASH_CLEAR);
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_HINT_FLASH_CLEAR, HINT_FLASH_MS, IntPtr.Zero);

        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_CLEAR);
        if (clearAfterDuration)
        {
            uint duration = string.Equals(_hintMethod?.Type, "deadkey", StringComparison.OrdinalIgnoreCase)
                ? HINT_DEADKEY_MS
                : HINT_DIRECT_MS;
            Win32.SetTimer(_hWnd, (UIntPtr)TIMER_HINT_CLEAR, duration, IntPtr.Zero);
        }
    }

    private void ApplyHintHighlight(KeyboardRenderState state)
    {
        if (_hintBackspace)
        {
            state.HighlightedScancodes.Add(0x0E);
            return;
        }

        if (_hintMethod == null) return;

        if (!string.IsNullOrEmpty(_hintMethod.DeadKeyToken))
            state.LessonVisibleCharacters.Add(_hintMethod.DeadKeyToken);

        var step = LessonHintProvider.GetCurrentStep(_hintMethod, _mapper.ActiveDeadKey);
        AddHintStepHighlight(state, step);
    }

    private static void AddHintStepHighlight(KeyboardRenderState state, LessonHintKeyStep step)
    {
        if (!string.IsNullOrEmpty(step.Key) &&
            VirtualKeyboard.KeyCodeToScancode.TryGetValue(step.Key, out var scancode))
            state.HighlightedScancodes.Add(scancode);

        AddLayerHighlights(state, step.Layer);
    }

    private static void AddLayerHighlights(KeyboardRenderState state, string? layer)
    {
        layer ??= "";
        if (layer.Contains("Shift", StringComparison.OrdinalIgnoreCase))
            state.HighlightedContextIds.Add(VirtualKeyboard.ContextShiftLeft);
        if (layer.Contains("AltGr", StringComparison.OrdinalIgnoreCase))
            state.HighlightedLabels.Add("AltGr");
        if (layer.Contains("Caps", StringComparison.OrdinalIgnoreCase))
            state.HighlightedLabels.Add("Verr. Maj.");
    }

    private void ToggleAutoHints()
    {
        ConfigManager.SetLessonAutoHints(!ConfigManager.LessonAutoHintsEnabled);
        ResetErrorState();
        if (ConfigManager.LessonAutoHintsEnabled)
            UpdateAutoHintIfEnabled();
        else
            ClearHint();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void ToggleSettings()
    {
        _settingsOpen = !_settingsOpen;
        if (_settingsOpen)
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_FREE_STATS);
        }
        else
        {
            UpdateAutoHintIfEnabled();
            ArmFreeStatsTimerIfNeeded();
        }
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private void ToggleBool(bool currentValue, Action<bool> setter)
    {
        setter(!currentValue);
        UpdateAutoHintIfEnabled();
        ArmFreeStatsTimerIfNeeded();
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private void SwitchMode(WindowMode mode)
    {
        _settingsOpen = false;
        _mode = mode;
        if (mode == WindowMode.Lessons)
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_FREE_STATS);
            UpdateAutoHintIfEnabled();
        }
        else
        {
            Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
            ClearHint();
            ArmFreeStatsTimerIfNeeded();
        }
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);
    }

    private void UpdateAutoHintIfEnabled()
    {
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
        if (!_visible ||
            _settingsOpen ||
            _mode != WindowMode.Lessons ||
            !ConfigManager.LessonAutoHintsEnabled ||
            _showSummary ||
            _session.IsLineComplete ||
            _session.IsExerciseComplete)
            return;

        ShowHintCore(automatic: true);
    }

    private void ArmFreeStatsTimerIfNeeded()
    {
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_FREE_STATS);
        if (!_visible ||
            _settingsOpen ||
            _mode != WindowMode.Free ||
            !_freeStartedAt.HasValue ||
            !ConfigManager.LessonFreeStatsVisible)
            return;

        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_FREE_STATS, FREE_STATS_REFRESH_MS, IntPtr.Zero);
    }

    private void ResetProgress()
    {
        int result = Win32.MessageBoxW(_hWnd,
            "Réinitialiser toute la progression des leçons ?\n\nLes préférences, comme les auto-indices, seront conservées.",
            "AZERTY Global — Leçons", MB_YESNO | MB_ICONWARNING);
        if (result != IDYES) return;
        _progress.ResetAll();
        StartCurrentSession(savePosition: true);
    }

    private void SelectModule(int index)
    {
        if (index < 0 || index >= _catalog.Modules.Count) return;
        _settingsOpen = false;
        _mode = WindowMode.Lessons;
        _moduleIndex = index;
        _lessonIndex = 0;
        _exerciseIndex = 0;
        StartCurrentSession(savePosition: true);
    }

    private void SelectLesson(int index)
    {
        if (index < 0 || index >= CurrentModule.Lessons.Count) return;
        _settingsOpen = false;
        _mode = WindowMode.Lessons;
        _lessonIndex = index;
        _exerciseIndex = 0;
        StartCurrentSession(savePosition: true);
    }

    private void PreviousExercise()
    {
        if (_exerciseIndex > 0)
        {
            _exerciseIndex--;
            StartCurrentSession(savePosition: true);
            return;
        }
        if (_lessonIndex > 0)
        {
            _lessonIndex--;
            _exerciseIndex = CurrentLesson.Exercises.Count - 1;
            StartCurrentSession(savePosition: true);
        }
    }

    private void NextExercise()
    {
        if (_exerciseIndex < CurrentLesson.Exercises.Count - 1)
        {
            _exerciseIndex++;
            StartCurrentSession(savePosition: true);
            return;
        }
        if (_lessonIndex < CurrentModule.Lessons.Count - 1)
        {
            _lessonIndex++;
            _exerciseIndex = 0;
            StartCurrentSession(savePosition: true);
            return;
        }
        if (_moduleIndex < _catalog.Modules.Count - 1)
        {
            _moduleIndex++;
            _lessonIndex = 0;
            _exerciseIndex = 0;
            StartCurrentSession(savePosition: true);
        }
    }

    private static string FormatLessonInstruction(string instruction)
    {
        return instruction
            .Replace("{ALTGR}", "AltGr", StringComparison.OrdinalIgnoreCase)
            .Replace("{SHIFT}", "Maj", StringComparison.OrdinalIgnoreCase)
            .Replace("{CAPS}", "Verr. Maj.", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildTypingStatus()
    {
        return CurrentExercise.TypingMode == LessonTypingMode.Strict
            ? "Mode initiation : retape le bon caractère pour corriger."
            : "Retour arrière autorisé. Le collage est bloqué.";
    }

    private string FormatHintButtonText()
    {
        if (_hintBackspace)
            return "Retour arrière -> corriger";

        if (_hintMethod == null || !_hintCharacter.HasValue)
            return "Indice";

        string target = FormatVisibleCharacter(_hintCharacter.Value.ToString());
        string key = FormatKeyName(_hintMethod.Key);
        string layer = FormatLayer(_hintMethod.Layer);

        if (string.Equals(_hintMethod.Type, "deadkey", StringComparison.OrdinalIgnoreCase))
        {
            string dead = !string.IsNullOrEmpty(_hintMethod.DeadKey)
                ? TrayApplication.GetDeadKeySymbol(_hintMethod.DeadKey)
                : "";
            string activationKey = FormatKeyName(_hintMethod.DkActivationKey);
            string activationLayer = FormatLayer(_hintMethod.DkActivationLayer);
            string activation = string.IsNullOrEmpty(activationLayer) ? activationKey : $"{activationLayer} + {activationKey}";
            string combo = string.IsNullOrEmpty(layer) ? key : $"{layer} + {key}";
            return string.IsNullOrEmpty(activation)
                ? $"{dead} puis {combo} -> {target}"
                : $"{activation} -> {dead} puis {combo} -> {target}";
        }

        string direct = string.IsNullOrEmpty(layer) ? key : $"{layer} + {key}";
        return $"{direct} -> {target}";
    }

    private string FormatVisibleCharacter(string value)
    {
        return ConfigManager.LessonInvisibleMarkersVisible
            ? KeyboardRenderer.DisplayInvisible(value)
            : value;
    }

    private static string FormatLayer(string? layer)
    {
        if (string.IsNullOrWhiteSpace(layer) || string.Equals(layer, "Base", StringComparison.OrdinalIgnoreCase))
            return "";

        return string.Join(" + ", layer.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.Equals(part, "Base", StringComparison.OrdinalIgnoreCase))
            .Select(part => part switch
            {
                "Shift" => "Maj",
                "Caps" => "Verr. Maj.",
                "AltGr" => "AltGr",
                _ => part
            }));
    }

    private static string FormatKeyName(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "";
        if (key.StartsWith("Key", StringComparison.OrdinalIgnoreCase) && key.Length == 4)
            return key[3].ToString().ToUpperInvariant();
        if (key.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) && key.Length == 6)
            return key[5].ToString();

        return key switch
        {
            "Space" => "Espace",
            "Minus" => "-",
            "Equal" => "=",
            "BracketLeft" => "[",
            "BracketRight" => "]",
            "Backslash" => "\\",
            "Semicolon" => ";",
            "Quote" => "'",
            "Comma" => ",",
            "Period" => ".",
            "Slash" => "/",
            "Backquote" => "@",
            _ => key
        };
    }

    private static string FormatNullable(int? value, string suffix = "")
    {
        return value.HasValue ? value.Value + suffix : "—";
    }

    private static string FormatWpm(int? value)
    {
        return value.HasValue ? $"{value.Value} WPM" : FormatNullable(value);
    }

    private static uint GetAccuracyColor(int? accuracy)
    {
        if (!accuracy.HasValue) return CLR_TEXT;
        if (accuracy.Value >= 95) return CLR_OK;
        if (accuracy.Value >= 85) return CLR_TEXT;
        return CLR_LESSON_ACCENT;
    }

    private static uint GetErrorColor(int errorCount)
    {
        if (errorCount == 0) return CLR_OK;
        if (errorCount <= 3) return CLR_TEXT;
        return CLR_LESSON_ACCENT;
    }

    private void OnMapperStateChanged()
    {
        if (_visible)
            Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void OnRawKeyDown(uint scancode)
    {
        if (_inputPaused) return;
        if (!_visible || _settingsOpen) return;
        _pressedScancode = scancode;
        CaptureExpectedTextForPhysicalKey(scancode);
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_KEYPRESS, KEYPRESS_DURATION_MS, IntPtr.Zero);
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, false);
    }

    private void CaptureExpectedTextForPhysicalKey(uint scancode)
    {
        ClearPendingPhysicalText();

        if (!_visible || _settingsOpen || _showSummary) return;
        if (_mode == WindowMode.Lessons && _session.IsExerciseComplete) return;
        if (!_layout.Keys.TryGetValue(scancode, out var keyDef)) return;

        string? output = keyDef.GetOutput(_mapper.ShiftDown, _mapper.AltGrDown, _mapper.CapsLockActive);
        if (string.IsNullOrEmpty(output)) return;

        string? expectedText = null;
        if (output.StartsWith("dk_", StringComparison.Ordinal))
        {
            // Une activation de touche morte seule ne produit pas de WM_CHAR.
            if (_mapper.ActiveDeadKey != null &&
                _layout.DeadKeys.TryGetValue(_mapper.ActiveDeadKey, out var activeDk))
            {
                var isolated = activeDk.GetIsolated();
                if (isolated != null)
                {
                    var newDk = _layout.DeadKeys.GetValueOrDefault(output);
                    expectedText = newDk?.Apply(isolated) ?? isolated;
                }
            }
        }
        else if (_mapper.ActiveDeadKey != null &&
            _layout.DeadKeys.TryGetValue(_mapper.ActiveDeadKey, out var dk))
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
            ClearPendingPhysicalText();
        return expected;
    }

    private void ClearPendingPhysicalText()
    {
        _pendingPhysicalText = null;
        _pendingPhysicalTextIndex = 0;
        _pendingPhysicalTextTick = 0;
    }

    private void Hide()
    {
        SaveWindowBounds();
        _visible = false;
        _settingsOpen = false;
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_HINT);
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_FREE_STATS);
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_CLEAR);
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_HINT_FLASH_CLEAR);
        _freePreview = "";
        _freeCursorIndex = 0;
        _freeChars = 0;
        _freeBackspaces = 0;
        _freeStartedAt = null;
        ClearPendingPhysicalText();
        Win32.ShowWindow(_hWnd, 0);
    }

    public void Dispose()
    {
        ConfigManager.WindowBoundsCleared -= OnWindowBoundsCleared;
        _mapper.StateChanged -= OnMapperStateChanged;
        _hook.RawKeyDown -= OnRawKeyDown;
        if (_hWnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
        Win32.UnregisterClassW(WND_CLASS_NAME, Win32.GetModuleHandleW(null));
        DisposeResizeSnapshot();
        DestroyFonts();
    }
}

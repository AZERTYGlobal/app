// Déclarations Win32 partagées — P/Invoke, structures et constantes communes
using System.Runtime.InteropServices;

namespace AZERTYGlobal;

/// <summary>
/// Structures Win32 partagées entre les fenêtres de l'application.
/// Évite la duplication de RECT, POINT, WNDCLASSEXW, PAINTSTRUCT, etc.
/// </summary>
static class Win32
{
    // ═══════════════════════════════════════════════════════════════
    // Structures
    // ═══════════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WNDPROC lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    // ═══════════════════════════════════════════════════════════════
    // Delegates
    // ═══════════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RectF { public float X, Y, Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    // ═══════════════════════════════════════════════════════════════
    // Delegates
    // ═══════════════════════════════════════════════════════════════

    public delegate IntPtr WNDPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    // ═══════════════════════════════════════════════════════════════
    // Constantes — Messages Windows
    // ═══════════════════════════════════════════════════════════════

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_CTLCOLORBTN = 0x0135;
    public const uint WM_GETDLGCODE = 0x0087;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_SIZING = 0x0214;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;
    public const uint WM_CTLCOLOREDIT = 0x0133;
    public const uint WM_CTLCOLORSTATIC = 0x0138;
    public const uint WM_PASTE = 0x0302;
    public const uint WM_CUT = 0x0300;
    public const uint WM_CLEAR = 0x0303;
    public const uint WM_UNDO = 0x0304;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;

    // ═══════════════════════════════════════════════════════════════
    // Constantes — Styles
    // ═══════════════════════════════════════════════════════════════

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_BORDER = 0x00800000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_TABSTOP = 0x00010000;
    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_VSCROLL = 0x00200000;

    // ═══════════════════════════════════════════════════════════════
    // Constantes — GDI / DrawText
    // ═══════════════════════════════════════════════════════════════

    public const int TRANSPARENT = 1;
    public const uint DT_LEFT = 0x00;
    public const uint DT_CENTER = 0x01;
    public const uint DT_VCENTER = 0x04;
    public const uint DT_BOTTOM = 0x08;
    public const uint DT_WORDBREAK = 0x10;
    public const uint DT_SINGLELINE = 0x20;
    public const uint DT_CALCRECT = 0x400;
    public const uint DT_NOPREFIX = 0x800;
    public const uint DT_END_ELLIPSIS = 0x8000;
    public const uint SRCCOPY = 0x00CC0020;
    public const uint TME_LEAVE = 0x02;

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Window
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int w, int h, IntPtr hParent, IntPtr hMenu, IntPtr hInst, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool bRepaint);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr ShellExecuteW(IntPtr hwnd, string op, string file,
        string? param, string? dir, int nShow);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — GDI
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(IntPtr hdc, uint crColor);

    [DllImport("gdi32.dll")]
    public static extern uint SetBkColor(IntPtr hdc, uint crColor);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight,
        uint italic, uint underline, uint strike, uint charset, uint outPrec,
        uint clipPrec, uint quality, uint pitchFamily, string faceName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(IntPtr hdc, string text, int count, ref RECT rc, uint format);

    [DllImport("user32.dll")]
    public static extern int FillRect(IntPtr hDC, ref RECT rc, IntPtr hbr);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy,
        IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bpp, byte[]? bits);

    [DllImport("user32.dll")]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

    [DllImport("gdi32.dll")]
    public static extern bool RoundRect(IntPtr hdc, int left, int top, int right, int bottom, int w, int h);

    [DllImport("gdi32.dll")]
    public static extern bool Polygon(IntPtr hdc, POINT[] apt, int cpt);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    public static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("gdi32.dll")]
    public static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Message loop
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Menu
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    public static extern bool TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Shell (tray icon)
    // ═══════════════════════════════════════════════════════════════

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Input
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    public static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out] System.Text.StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out] System.Text.StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Keyboard hook
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Focus / Thread
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern int GetDpiForWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Subclass (comctl32)
    // ═══════════════════════════════════════════════════════════════

    [DllImport("comctl32.dll")]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    public static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — Clipboard
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — GDI+
    // ═══════════════════════════════════════════════════════════════

    [DllImport("gdiplus.dll")]
    public static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);

    [DllImport("gdiplus.dll")]
    public static extern int GdiplusShutdown(IntPtr token);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateBitmapFromStream(IntPtr stream, out IntPtr bitmap);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDisposeImage(IntPtr image);

    [DllImport("gdiplus.dll")]
    public static extern int GdipGetImageWidth(IntPtr image, out uint width);

    [DllImport("gdiplus.dll")]
    public static extern int GdipGetImageHeight(IntPtr image, out uint height);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateFromHDC(IntPtr hdc, out IntPtr graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteGraphics(IntPtr graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetSmoothingMode(IntPtr graphics, int smoothingMode);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetInterpolationMode(IntPtr graphics, int interpolationMode);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDrawImageRectI(IntPtr graphics, IntPtr image, int x, int y, int w, int h);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateSolidFill(uint color, out IntPtr brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteBrush(IntPtr brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipFillEllipseI(IntPtr graphics, IntPtr brush, int x, int y, int w, int h);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateFontFamilyFromName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr collection, out IntPtr family);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteFontFamily(IntPtr family);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateFont(IntPtr family, float emSize, int style, int unit, out IntPtr font);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteFont(IntPtr font);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateStringFormat(int formatAttributes, int language, out IntPtr format);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteStringFormat(IntPtr format);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetStringFormatAlign(IntPtr format, int align);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetStringFormatLineAlign(IntPtr format, int align);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    public static extern int GdipDrawString(IntPtr graphics, string str, int length, IntPtr font,
        ref RectF layoutRect, IntPtr stringFormat, IntPtr brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetTextRenderingHint(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, IntPtr scan0, out IntPtr bitmap);

    [DllImport("gdiplus.dll")]
    public static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateHBITMAPFromBitmap(IntPtr bitmap, out IntPtr hbmReturn, uint background);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — OLE / COM streams
    // ═══════════════════════════════════════════════════════════════

    [DllImport("ole32.dll")]
    public static extern int CreateStreamOnHGlobal(IntPtr hGlobal, bool fDeleteOnRelease, out IntPtr ppstm);

    // ═══════════════════════════════════════════════════════════════
    // P/Invoke — DPI
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern int SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int fnObject);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
}

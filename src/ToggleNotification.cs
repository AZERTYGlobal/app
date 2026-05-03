// Mini-fenetre TOPMOST en haut a droite affichee 2s lors du toggle on/off d'AZERTY Global.
// Sert de retour visuel quand un jeu en borderless windowed couvre la barre des taches
// (icone tray invisible). En exclusive fullscreen, la fenetre n'apparait pas — angle mort
// accepte (cf. discussion smoke test 2026-05-03).
using System.Runtime.InteropServices;

namespace AZERTYGlobal;

internal sealed class ToggleNotification : IDisposable
{
    private const uint TIMER_AUTO_CLOSE = 0x9001;

    private const int BASE_W = 240;
    private const int BASE_H = 56;

    private const uint CLR_BG = 0x00302D2A;          // Fond sombre BGR ≈ #2A2D30
    private const uint CLR_ACTIVATED = 0x005EC522;   // Vert BGR ≈ #22C55E
    private const uint CLR_DEACTIVATED = 0x009A9A9A; // Gris BGR

    private IntPtr _hWnd;
    private readonly Win32.WNDPROC _wndProcDelegate;
    private readonly IntPtr _hBgBrush;
    private IntPtr _hFontText;

    private bool _currentActivated;
    private float _dpiScale;
    private int S(int v) => (int)(v * _dpiScale);

    public ToggleNotification()
    {
        _wndProcDelegate = WndProc;
        _hBgBrush = Win32.CreateSolidBrush(CLR_BG);

        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        int dpi = Win32.GetDeviceCaps(hdcScreen, 88);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);
        _dpiScale = dpi / 96f;

        CreateFonts();
        CreateMainWindow();
    }

    private void CreateFonts()
    {
        _hFontText = Win32.CreateFontW(-S(15), 0, 0, 0, 600, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
    }

    private void CreateMainWindow()
    {
        var hInstance = Win32.GetModuleHandleW(null);
        const string className = "AZERTYGlobal_ToggleNotif";

        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            hCursor = Win32.LoadCursorW(IntPtr.Zero, (IntPtr)32512),
            hbrBackground = IntPtr.Zero,
            lpszClassName = className
        };
        Win32.RegisterClassExW(ref wc);

        _hWnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED,
            className, "",
            Win32.WS_POPUP,
            0, 0, S(BASE_W), S(BASE_H),
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        // Opacite ~94% — fond opaque et lisible
        Win32.SetLayeredWindowAttributes(_hWnd, 0, 240, Win32.LWA_ALPHA);
    }

    public void Show(bool activated)
    {
        _currentActivated = activated;

        // Position en haut a droite de l'ecran principal, 16px de marge
        int screenW = Win32.GetSystemMetrics(Win32.SM_CXSCREEN);
        int margin = S(16);
        int x = screenW - S(BASE_W) - margin;
        int y = margin;

        Win32.MoveWindow(_hWnd, x, y, S(BASE_W), S(BASE_H), true);
        Win32.ShowWindow(_hWnd, Win32.SW_SHOWNOACTIVATE);
        Win32.InvalidateRect(_hWnd, IntPtr.Zero, true);

        // Auto-fermeture apres 2s — re-arm si appel rapide consecutif
        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_AUTO_CLOSE);
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_AUTO_CLOSE, 2000, IntPtr.Zero);
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

                case Win32.WM_TIMER:
                    if ((uint)wParam.ToInt64() == TIMER_AUTO_CLOSE)
                    {
                        Win32.KillTimer(hWnd, (UIntPtr)TIMER_AUTO_CLOSE);
                        Win32.ShowWindow(hWnd, 0);
                    }
                    return IntPtr.Zero;

                case Win32.WM_ERASEBKGND:
                    return (IntPtr)1;
            }
        }
        catch (Exception ex)
        {
            ConfigManager.Log("ToggleNotification WndProc", ex);
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

        Win32.SelectObject(hdc, _hFontText);
        Win32.SetTextColor(hdc, _currentActivated ? CLR_ACTIVATED : CLR_DEACTIVATED);
        string text = _currentActivated ? "AZERTY Global activé" : "AZERTY Global désactivé";
        Win32.DrawTextW(hdc, text, -1, ref clientRect,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

        Win32.BitBlt(hdcPaint, 0, 0, cw, ch, hdc, 0, 0, Win32.SRCCOPY);
        Win32.SelectObject(hdc, hBmpOld);
        Win32.DeleteObject(hBmp);
        Win32.DeleteDC(hdc);
        Win32.EndPaint(hWnd, ref ps);
    }

    public void Dispose()
    {
        if (_hWnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
        if (_hFontText != IntPtr.Zero)
        {
            Win32.DeleteObject(_hFontText);
            _hFontText = IntPtr.Zero;
        }
        if (_hBgBrush != IntPtr.Zero)
        {
            Win32.DeleteObject(_hBgBrush);
        }
    }
}

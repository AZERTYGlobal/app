// Application system tray — interface utilisateur via Win32 API natif (pas de WinForms)
using System.Runtime.InteropServices;

namespace AZERTYGlobalPortable;

/// <summary>
/// Gère l'icône dans la zone de notification et le menu contextuel.
/// Utilise directement l'API Win32 (Shell_NotifyIcon, CreatePopupMenu, etc.)
/// pour éviter la dépendance à WinForms et permettre le trimming/AOT.
/// </summary>
sealed class TrayApplication : IDisposable
{
    // ── Window messages (spécifiques TrayApplication) ────────────
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;

    // ── Menu IDs ────────────────────────────────────────────────────
    private const int IDM_TOGGLE = 1001;
    private const int IDM_CAPS = 1002;
    private const int IDM_SITE = 1003;
    private const int IDM_ABOUT = 1004;
    private const int IDM_KEYBOARD = 1006;
    private const int IDM_SEARCH = 1007;
    private const int IDM_AUTOSTART = 1008;
    private const int IDM_QUIT = 1005;

    // ── Shell_NotifyIcon ────────────────────────────────────────────
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x01;

    // ── Menu flags ──────────────────────────────────────────────────
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_CHECKED = 0x0008;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_BOTTOMALIGN = 0x0020;

    // ── MessageBox ──────────────────────────────────────────────────
    private const uint MB_OK = 0x00;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_ICONINFORMATION = 0x40;

    // ═══════════════════════════════════════════════════════════════
    // Champs d'instance
    // ═══════════════════════════════════════════════════════════════
    private IntPtr _hWnd;
    private IntPtr _hIcon;
    private Win32.NOTIFYICONDATAW _nid;
    private readonly Win32.WNDPROC _wndProcDelegate; // prevent GC
    private KeyboardHook? _hook;
    private KeyMapper? _mapper;
    private VirtualKeyboard? _virtualKeyboard;
    private CharacterSearch? _characterSearch;
    private OnboardingWindow? _onboarding;
    private bool _enabled = true;

    public TrayApplication()
    {
        // Garder une référence au delegate pour empêcher le GC de le collecter
        _wndProcDelegate = WndProcCallback;

        // Créer une fenêtre cachée pour recevoir les messages tray
        var hInstance = Win32.GetModuleHandleW(null);
        var className = "AZERTYGlobalPortable_Wnd";

        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            lpszClassName = className
        };
        Win32.RegisterClassExW(ref wc);

        _hWnd = Win32.CreateWindowExW(0, className, "AZERTY Global Portable",
            0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        // Icône tray
        _hIcon = CreateTextIcon("AG", true);

        _nid = new Win32.NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "AZERTY Global Portable v" + Program.Version,
            szInfo = "",
            szInfoTitle = ""
        };
        Win32.Shell_NotifyIconW(NIM_ADD, ref _nid);

        // Charger le layout et démarrer le hook
        try
        {
            LoadAndStart();

            // Premier lancement : onboarding. Lancements suivants : notification balloon.
#if DEBUG
            // En debug, toujours afficher l'onboarding pour faciliter les tests
            if (true)
#else
            if (!ConfigManager.OnboardingDone)
#endif
            {
                _onboarding = new OnboardingWindow();
                _onboarding.Show();
            }
            else
            {
                ShowBalloon("AZERTY Global Portable",
                    "est actif.\nCtrl+Maj+Verr.Maj pour activer/désactiver.");
            }
        }
        catch (Exception ex)
        {
            Win32.MessageBoxW(IntPtr.Zero,
                $"Erreur au chargement :\n\n{ex.Message}",
                "AZERTY Global — Erreur", MB_OK | MB_ICONERROR);
            Win32.PostQuitMessage(1);
        }
    }

    private void LoadAndStart()
    {
        var layout = LayoutLoader.LoadFromResource();
        _mapper = new KeyMapper(layout);
        _mapper.StateChanged += OnStateChanged;
        _mapper.ToggleRequested += OnToggle;
        // CharacterSearch charge character-index.json → partager les noms avec VirtualKeyboard
        _characterSearch = new CharacterSearch();
        _virtualKeyboard = new VirtualKeyboard(layout, _characterSearch.GetCharacterNames());
        _hook = new KeyboardHook(_mapper);
        _hook.RawKeyDown += OnKeyPressed;
        _hook.SearchRequested += () => _characterSearch?.Toggle();
        _hook.VirtualKeyboardRequested += () => _virtualKeyboard?.Toggle();
        _characterSearch.SelectionChanged += OnSearchSelectionChanged;
        _hook.Install();
        Win32.SetForegroundWindow(_hWnd);
    }

    // Timer IDs pour la réinstallation du hook après démarrage
    private const uint TIMER_REHOOK = 9001;
    private const uint TIMER_REHOOK_2 = 9002;
    private const uint TIMER_REHOOK_3 = 9003;

    // Message TaskbarCreated (Explorer restart / chargement tardif au boot)
    private readonly uint _wmTaskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");

    /// <summary>Boucle de messages principale.</summary>
    public void Run()
    {
        // Le hook LL peut ne pas recevoir de callbacks tant que la boucle de messages
        // n'est pas active, ou au démarrage de Windows quand le système n'est pas encore
        // prêt. On planifie plusieurs réinstallations progressives pour couvrir les deux cas.
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_REHOOK, 500, IntPtr.Zero);
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_REHOOK_2, 3000, IntPtr.Zero);
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_REHOOK_3, 8000, IntPtr.Zero);

        while (Win32.GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessageW(ref msg);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Gestion des messages Windows
    // ═══════════════════════════════════════════════════════════════
    private IntPtr WndProcCallback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_TRAYICON:
                    var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                    if (mouseMsg == Win32.WM_RBUTTONUP)
                        ShowContextMenu();
                    else if (mouseMsg == Win32.WM_LBUTTONDBLCLK)
                        _virtualKeyboard?.Toggle();
                    return IntPtr.Zero;

                case Win32.WM_COMMAND:
                    switch (wParam.ToInt32() & 0xFFFF)
                    {
                        case IDM_TOGGLE: OnToggle(); break;
                        case IDM_KEYBOARD: _virtualKeyboard?.Toggle(); break;
                        case IDM_SEARCH: _characterSearch?.Toggle(); break;
                        case IDM_AUTOSTART: OnToggleAutoStart(); break;
                        case IDM_SITE: Win32.ShellExecuteW(IntPtr.Zero, "open", "https://azerty.global", null, null, 1); break;
                        case IDM_ABOUT: OnAbout(); break;
                        case IDM_QUIT: OnExit(); break;
                    }
                    return IntPtr.Zero;

                case Win32.WM_TIMER:
                    var timerId = (uint)wParam.ToInt64();
                    if (timerId == TIMER_REHOOK || timerId == TIMER_REHOOK_2 || timerId == TIMER_REHOOK_3)
                    {
                        Win32.KillTimer(_hWnd, (UIntPtr)timerId);
                        ReinstallHook();
                    }
                    return IntPtr.Zero;

                case Win32.WM_DESTROY:
                    Win32.PostQuitMessage(0);
                    return IntPtr.Zero;

                default:
                    // TaskbarCreated : Explorer a (re)démarré — réenregistrer l'icône et le hook
                    if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
                    {
                        Win32.Shell_NotifyIconW(NIM_ADD, ref _nid);
                        ReinstallHook();
                        return IntPtr.Zero;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            var logDir = ConfigManager.IsPackaged
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AZERTY Global Portable")
                : AppContext.BaseDirectory;
            try { Directory.CreateDirectory(logDir); } catch { }
            File.AppendAllText(Path.Combine(logDir, "error.log"), $"[{DateTime.Now:s}] WndProc: {ex}\n");
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>Réinstalle le keyboard hook (nouveau SetWindowsHookEx).</summary>
    private void ReinstallHook()
    {
        if (_hook == null) return;
        _hook.Dispose();
        _hook = new KeyboardHook(_mapper!);
        _hook.RawKeyDown += OnKeyPressed;
        _hook.SearchRequested += () => _characterSearch?.Toggle();
        _hook.VirtualKeyboardRequested += () => _virtualKeyboard?.Toggle();
        _hook.Enabled = _enabled;
        _hook.Install();
        // Activer la fenêtre pour que le thread soit associé au système d'input
        // Sans cet appel, le hook LL est installé mais ne reçoit pas d'événements
        Win32.SetForegroundWindow(_hWnd);
    }

    // ═══════════════════════════════════════════════════════════════
    // Actions utilisateur
    // ═══════════════════════════════════════════════════════════════
    private void ShowContextMenu()
    {
        var hMenu = Win32.CreatePopupMenu();
        var kbdKey = ConfigManager.ShortcutVirtualKeyboard;
        var searchKey = ConfigManager.ShortcutCharacterSearch;
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_TOGGLE, _enabled ? "Désactiver" : "Activer");
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_KEYBOARD,
            _virtualKeyboard?.IsVisible == true ? $"Masquer le clavier virtuel\tCtrl+Maj+{kbdKey}" : $"Clavier virtuel\tCtrl+Maj+{kbdKey}");
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_SEARCH, $"Rechercher un caractère\tCtrl+Maj+{searchKey}");

        var capsText = _mapper?.CapsLockActive == true ? "Verr. Maj : Actif ⬤" : "Verr. Maj : Inactif";
        Win32.AppendMenuW(hMenu, MF_STRING | MF_GRAYED, IDM_CAPS, capsText);
        Win32.AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        uint autoStartFlags = MF_STRING | (ConfigManager.AutoStartEnabled ? MF_CHECKED : 0);
        Win32.AppendMenuW(hMenu, autoStartFlags, IDM_AUTOSTART, "Lancer au démarrage de Windows");
        Win32.AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_SITE, "Site azerty.global");
        Win32.AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_ABOUT, "À propos");
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_QUIT, "Quitter");

        Win32.GetCursorPos(out var pt);
        Win32.SetForegroundWindow(_hWnd);
        Win32.TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.x, pt.y, _hWnd, IntPtr.Zero);
        Win32.PostMessageW(_hWnd, 0, IntPtr.Zero, IntPtr.Zero); // WM_NULL — fermeture propre du menu
        Win32.DestroyMenu(hMenu);
    }

    private void OnToggle()
    {
        if (_hook == null) return;
        _enabled = !_enabled;
        _hook.Enabled = _enabled;

        // Resynchroniser l'état quand on réactive (CapsLock a pu changer pendant la désactivation)
        if (_enabled)
            _mapper?.SyncState();

        UpdateIcon();
        UpdateTooltip();

        ShowBalloon("AZERTY Global Portable", _enabled ? "est actif." : "est désactivé.");
    }

    private bool _lastCapsState;
    private void OnStateChanged()
    {
        bool caps = _mapper?.CapsLockActive == true;
        if (caps != _lastCapsState)
        {
            _lastCapsState = caps;
            UpdateIcon();
            UpdateTooltip();
        }
        RefreshVirtualKeyboard();
    }

    private void OnKeyPressed(uint scancode)
    {
        _virtualKeyboard?.NotifyKeyPress(scancode);
    }

    private void OnSearchSelectionChanged(CharacterSearch.MethodData? method)
    {
        _virtualKeyboard?.HighlightMethod(method);
    }

    private void RefreshVirtualKeyboard()
    {
        if (_mapper == null || _virtualKeyboard == null) return;
        _virtualKeyboard.UpdateState(
            _mapper.ShiftDown,
            _mapper.AltGrDown,
            _mapper.CtrlDown,
            _mapper.AltDown,
            _mapper.CapsLockActive,
            _mapper.ActiveDeadKey);
    }

    private void OnToggleAutoStart()
    {
        bool newState = !ConfigManager.AutoStartEnabled;
        AutoStart.Set(newState);
    }

    private void OnAbout()
    {
        Win32.MessageBoxW(IntPtr.Zero,
            "AZERTY Global Portable v" + Program.Version + "\n\n" +
            "Disposition clavier française améliorée\n" +
            "© 2017-2026 Antoine Olivier\n\n" +
            "Licence : EUPL 1.2\n" +
            "https://azerty.global",
            "À propos — AZERTY Global", MB_OK | MB_ICONINFORMATION);
    }

    private void OnExit()
    {
        Cleanup();
        Win32.DestroyWindow(_hWnd);
    }

    /// <summary>Nettoyage unique du hook, de l'icône tray et des handles GDI.</summary>
    private bool _cleaned;
    private void Cleanup()
    {
        if (_cleaned) return;
        _cleaned = true;

        _hook?.Dispose();
        _virtualKeyboard?.Dispose();
        _characterSearch?.Dispose();
        _onboarding?.Dispose();
        Win32.Shell_NotifyIconW(NIM_DELETE, ref _nid);
        if (_hIcon != IntPtr.Zero)
        {
            Win32.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Icône et notifications
    // ═══════════════════════════════════════════════════════════════
    private void UpdateIcon()
    {
        var oldIcon = _hIcon;

        // Déterminer le texte et le style de l'icône selon l'état
        bool active = _enabled;
        bool capsLock = _mapper?.CapsLockActive == true && _enabled;
        string iconText = "AG";

        _hIcon = CreateTextIcon(iconText, active, capsLock);

        _nid.hIcon = _hIcon;
        _nid.uFlags = NIF_ICON;
        Win32.Shell_NotifyIconW(NIM_MODIFY, ref _nid);

        if (oldIcon != IntPtr.Zero) Win32.DestroyIcon(oldIcon);
    }

    private void UpdateTooltip()
    {
        var parts = new List<string> { "AZERTY Global Portable v" + Program.Version };
        if (!_enabled)
            parts.Add("Inactif");
        else
        {
            parts.Add("Actif");
            if (_mapper?.CapsLockActive == true)
                parts.Add("Verr.Maj");
            if (_mapper?.ActiveDeadKey != null)
                parts.Add($"Touche morte : {GetDeadKeySymbol(_mapper.ActiveDeadKey)}");
        }
        _nid.szTip = string.Join(" — ", parts);
        _nid.uFlags = NIF_TIP;
        Win32.Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    /// <summary>Retourne le symbole d'affichage d'une touche morte.</summary>
    internal static string GetDeadKeySymbol(string deadKeyName)
    {
        return deadKeyName switch
        {
            "dk_circumflex"       => "^",
            "dk_diaeresis"        => "¨",
            "dk_acute"            => "´",
            "dk_grave"            => "`",
            "dk_tilde"            => "~",
            "dk_dot_above"        => "˙",
            "dk_dot_below"        => ".",
            "dk_double_acute"     => "˝",
            "dk_double_grave"     => "̏",
            "dk_horn"             => "̛",
            "dk_hook"             => "̉",
            "dk_caron"            => "ˇ",
            "dk_ogonek"           => "˛",
            "dk_breve"            => "˘",
            "dk_inverted_breve"   => "̑",
            "dk_stroke"           => "/",
            "dk_horizontal_stroke"=> "−",
            "dk_macron"           => "¯",
            "dk_extended_latin"   => "Ł",
            "dk_cedilla"          => "¸",
            "dk_comma"            => ",",
            "dk_phonetic"         => "ə",
            "dk_ring_above"       => "˚",
            "dk_greek"            => "α",
            "dk_cyrillic"         => "Я",
            "dk_misc_symbols"     => "§",
            "dk_scientific"       => "∑",
            "dk_currencies"       => "€",
            "dk_punctuation"      => "…",
            _ => "◆"
        };
    }

    private void ShowBalloon(string title, string text)
    {
        _nid.uFlags = NIF_INFO;
        _nid.szInfoTitle = title;
        _nid.szInfo = text;
        _nid.dwInfoFlags = NIIF_INFO;
        Win32.Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    /// <summary>
    /// Crée une icône 32x32 avec texte sur fond coloré.
    /// Bleu = actif, gris = inactif. Barre orange en bas si CapsLock.
    /// </summary>
    private static IntPtr CreateTextIcon(string text, bool active, bool capsLock = false)
    {
        const int size = 32;

        var hdcScreen = Win32.GetDC(IntPtr.Zero);
        var hdc = Win32.CreateCompatibleDC(hdcScreen);
        var hBitmap = Win32.CreateCompatibleBitmap(hdcScreen, size, size);
        var hBitmapOld = Win32.SelectObject(hdc, hBitmap);

        // Fond coloré (COLORREF = 0x00BBGGRR)
        uint bgColor = active ? 0x00D47800u : 0x00808080u; // Bleu Windows / Gris
        var hBrush = Win32.CreateSolidBrush(bgColor);
        var rect = new Win32.RECT { left = 0, top = 0, right = size, bottom = size };
        Win32.FillRect(hdc, ref rect, hBrush);
        Win32.DeleteObject(hBrush);

        // Indicateur CapsLock : barre orange en bas de l'icône
        if (capsLock)
        {
            var capsBar = new Win32.RECT { left = 0, top = size - 5, right = size, bottom = size };
            var hCapsBrush = Win32.CreateSolidBrush(0x0000A5FFu); // Orange (BBGGRR)
            Win32.FillRect(hdc, ref capsBar, hCapsBrush);
            Win32.DeleteObject(hCapsBrush);
        }

        // Adapter la taille de police : plus petite pour les symboles longs, plus grande pour "AG"
        int fontSize = text.Length <= 2 ? 22 : 18;
        // Remonter légèrement le texte si la barre CapsLock est présente
        var textRect = capsLock
            ? new Win32.RECT { left = 0, top = 0, right = size, bottom = size - 4 }
            : rect;

        var hFont = Win32.CreateFontW(fontSize, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 4, 0, "Segoe UI");
        var hFontOld = Win32.SelectObject(hdc, hFont);
        Win32.SetBkMode(hdc, Win32.TRANSPARENT);
        Win32.SetTextColor(hdc, 0x00FFFFFFu); // Blanc
        Win32.DrawTextW(hdc, text, text.Length, ref textRect, Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);

        Win32.SelectObject(hdc, hFontOld);
        Win32.DeleteObject(hFont);
        Win32.SelectObject(hdc, hBitmapOld);

        // Masque (tout noir = tout opaque)
        var maskBits = new byte[size * size / 8]; // 128 octets, tous à 0
        var hMask = Win32.CreateBitmap(size, size, 1, 1, maskBits);

        var iconInfo = new Win32.ICONINFO
        {
            fIcon = true,
            hbmMask = hMask,
            hbmColor = hBitmap
        };
        var hIcon = Win32.CreateIconIndirect(ref iconInfo);

        // Nettoyage GDI
        Win32.DeleteObject(hMask);
        Win32.DeleteObject(hBitmap);
        Win32.DeleteDC(hdc);
        Win32.ReleaseDC(IntPtr.Zero, hdcScreen);

        return hIcon;
    }

    public void Dispose()
    {
        Cleanup();
    }
}

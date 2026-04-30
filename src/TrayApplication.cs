// Application system tray — interface utilisateur via Win32 API natif (pas de WinForms)
using System.Runtime.InteropServices;

namespace AZERTYGlobal;

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
    private const uint WM_APP_SEARCH = WM_APP + 2;
    private const uint WM_APP_VKBD = WM_APP + 3;

    // ── Menu IDs ────────────────────────────────────────────────────
    private const int IDM_TOGGLE = 1001;
    private const int IDM_SITE = 1003;
    private const int IDM_KEYBOARD = 1006;
    private const int IDM_SEARCH = 1007;
    private const int IDM_BUG = 1009;
    private const int IDM_ONBOARDING = 1010;
    private const int IDM_SETTINGS = 1012;
    private const int IDM_SUPPORT = 1013;
    private const int IDM_FEEDBACK = 1014;
    private const int IDM_QUIT = 1005;
    // Sous-menu compatibilité jeu (v0.9.7)
    private const int IDM_COMPAT_AUTO = 1020;
    private const int IDM_COMPAT_FORCE_ON = 1021;
    private const int IDM_COMPAT_FORCE_OFF = 1022;
#if DEBUG
    private const int IDM_RESET_ONBOARDING = 1015;
#endif

    // ── Shell_NotifyIcon ────────────────────────────────────────────
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_INFO = 0x10;
    private const uint NIIF_INFO = 0x01;
    private const uint NIIF_WARNING = 0x02;

    // ── Menu flags ──────────────────────────────────────────────────
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_POPUP = 0x0010;
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
    private Layout? _layout;
    private VirtualKeyboard? _virtualKeyboard;
    private CharacterSearch? _characterSearch;
    private OnboardingWindow? _onboarding;
    private SettingsWindow? _settings;
    private bool _enabled = true;

    // Compatibilité jeux (v0.9.7) : couche de détection foreground + désactivation auto anti-cheat
    private readonly IWin32Api _win32Api = new RealWin32Api();
    private ForegroundMonitor? _foregroundMonitor;
    private bool _wasEnabledBeforeAutoDisable;
    private bool _autoDisabledForAntiCheat;

    public TrayApplication()
    {
        // Garder une référence au delegate pour empêcher le GC de le collecter
        _wndProcDelegate = WndProcCallback;

        // Créer une fenêtre cachée pour recevoir les messages tray
        var hInstance = Win32.GetModuleHandleW(null);
        var className = "AZERTYGlobal_Wnd";

        var wc = new Win32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            lpszClassName = className
        };
        Win32.RegisterClassExW(ref wc);

        _hWnd = Win32.CreateWindowExW(0, className, "AZERTY Global",
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
            szTip = "AZERTY Global v" + Program.Version,
            szInfo = "",
            szInfoTitle = ""
        };
        Win32.Shell_NotifyIconW(NIM_ADD, ref _nid);

        // Charger le layout et démarrer le hook
        try
        {
            LoadAndStart();
            CheckSystemLayout();

            // Premier lancement : onboarding. Lancements suivants : notification balloon.
#if DEBUG
            // En debug, toujours afficher l'onboarding pour faciliter les tests
            bool shouldShowOnboarding = true;
#else
            bool shouldShowOnboarding = ConfigManager.ShowOnboardingAtStartup;
#endif
            if (shouldShowOnboarding)
            {
                _onboarding = new OnboardingWindow();
                _onboarding.Mapper = _mapper;
                _onboarding.Hook = _hook;
                _onboarding.AppLayout = _layout;
                _onboarding.Show();
            }
            else
            {
                ShowBalloon("AZERTY Global",
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
        _layout = layout;
        _mapper = new KeyMapper(layout, _win32Api);
        _mapper.StateChanged += OnStateChanged;
        _mapper.ToggleRequested += OnToggle;
        _hook = new KeyboardHook(_mapper);
        _hook.RawKeyDown += OnKeyPressed;
        _hook.SearchRequested += () => Win32.PostMessageW(_hWnd, WM_APP_SEARCH, IntPtr.Zero, IntPtr.Zero);
        _hook.VirtualKeyboardRequested += () => Win32.PostMessageW(_hWnd, WM_APP_VKBD, IntPtr.Zero, IntPtr.Zero);
        _hook.LayoutMayHaveChanged += OnLayoutMayHaveChanged;
        _hook.Install();

        try
        {
            _characterSearch = new CharacterSearch();
            _characterSearch.SelectionChanged += OnSearchSelectionChanged;
        }
        catch (Exception ex)
        {
            ConfigManager.Log("Init CharacterSearch", ex);
            _characterSearch = null;
        }

        try
        {
            _virtualKeyboard = new VirtualKeyboard(layout, _characterSearch?.GetCharacterNames());
        }
        catch (Exception ex)
        {
            ConfigManager.Log("Init VirtualKeyboard", ex);
            _virtualKeyboard = null;
        }

        Win32.SetForegroundWindow(_hWnd);

        // Compatibilité jeux : instancier le ForegroundMonitor APRÈS création de la
        // fenêtre tray (HWND requis pour le SetTimer debounce). Mode dégradé si échec.
        try
        {
            _foregroundMonitor = new ForegroundMonitor(_win32Api, _hWnd);
            _foregroundMonitor.ForegroundChanged += OnForegroundChanged;
            _mapper.SetForegroundMonitor(_foregroundMonitor);
        }
        catch (Exception ex)
        {
            ConfigManager.Log("ForegroundMonitor init", ex);
            _foregroundMonitor = null;
        }

        // Audit overrides invalides : un override forceOn sur un process désormais
        // anti-cheat (liste mise à jour par release) doit être supprimé pour la sécurité utilisateur.
        AuditCompatibilityOverridesAtStartup();
    }

    /// <summary>
    /// Au démarrage, scanner les overrides utilisateur : si un override forceOn pointe
    /// sur un process désormais listé anti-cheat (mise à jour de la liste hardcodée),
    /// supprimer l'override + bulle d'avertissement (bypass NotificationsEnabled car
    /// c'est une notification de sécurité, pas de confort).
    /// </summary>
    private void AuditCompatibilityOverridesAtStartup()
    {
        try
        {
            var overrides = ConfigManager.GetAllCompatibilityOverrides();
            var conflicting = new List<string>();
            foreach (var (proc, mode) in overrides)
            {
                if (mode == "forceOn" && GameRegistry.IsAntiCheatProcess(proc, null))
                {
                    ConfigManager.SetCompatibilityOverride(proc, null);
                    conflicting.Add(proc);
                    ConfigManager.LogCompatCriticalEvent("OverrideInvalidCleanup",
                        $"removed forceOn for '{proc}' (now anti-cheat-listed)");
                }
            }
            if (conflicting.Count > 0)
            {
                var list = string.Join(", ", conflicting);
                // Bypass NotificationsEnabled : on utilise Shell_NotifyIconW directement
                ShowSecurityBalloon("Compatibilité jeu désactivée",
                    $"AZERTY Global a désactivé l'option de compatibilité pour : {list}. " +
                    "Ces jeux sont désormais protégés par un anti-cheat. AZERTY Global se mettra " +
                    "automatiquement en pause quand ils seront ouverts.");
            }
        }
        catch (Exception ex)
        {
            ConfigManager.Log("AuditCompatibilityOverridesAtStartup", ex);
        }
    }

    // Timer IDs pour la réinstallation du hook après démarrage
    private const uint TIMER_REHOOK = 9001;
    private const uint TIMER_REHOOK_2 = 9002;
    private const uint TIMER_REHOOK_3 = 9003;
    private const uint TIMER_SINGLECLICK = 9010;
    private const uint TIMER_LAYOUT_CHECK = 9020;

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

        int ret;
        while ((ret = Win32.GetMessageW(out var msg, IntPtr.Zero, 0, 0)) != 0)
        {
            if (ret == -1) break; // Erreur fatale — sortir de la boucle
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
                    else if (mouseMsg == Win32.WM_LBUTTONUP)
                        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_SINGLECLICK, Win32.GetDoubleClickTime(), IntPtr.Zero);
                    else if (mouseMsg == Win32.WM_LBUTTONDBLCLK)
                    {
                        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_SINGLECLICK);
                        if (_enabled) _virtualKeyboard?.Toggle();
                    }
                    return IntPtr.Zero;

                case WM_APP_SEARCH:
                    if (_enabled)
                        _characterSearch?.Toggle();
                    else
                        ShowBalloon("AZERTY Global", "est désactivé — Ctrl+Maj+Verr.Maj pour réactiver.");
                    return IntPtr.Zero;

                case WM_APP_VKBD:
                    if (_enabled)
                        _virtualKeyboard?.Toggle();
                    else
                        ShowBalloon("AZERTY Global", "est désactivé — Ctrl+Maj+Verr.Maj pour réactiver.");
                    return IntPtr.Zero;

                case Win32.WM_COMMAND:
                    switch (wParam.ToInt32() & 0xFFFF)
                    {
                        case IDM_TOGGLE: OnToggle(); break;
                        case IDM_KEYBOARD:
                            if (_enabled || _virtualKeyboard?.IsVisible == true)
                                _virtualKeyboard?.Toggle();
                            break;
                        case IDM_SEARCH:
                            if (_enabled || _characterSearch?.IsVisible == true)
                                _characterSearch?.Toggle();
                            break;
                        case IDM_SETTINGS:
                            if (_settings == null)
                            {
                                _settings = new SettingsWindow();
                                _settings.ShortcutChanged = () => _hook?.ReloadShortcuts();
                            }
                            _settings.Show();
                            break;
                        case IDM_SITE: Win32.ShellExecuteW(IntPtr.Zero, "open", "https://azerty.global", null, null, 1); break;
                        case IDM_FEEDBACK: Win32.ShellExecuteW(IntPtr.Zero, "open", "https://azerty.global/feedback", null, null, 1); break;
                        case IDM_BUG: OnReportBug(); break;
                        case IDM_SUPPORT: Win32.ShellExecuteW(IntPtr.Zero, "open", "https://azerty.global/soutien", null, null, 1); break;
                        case IDM_ONBOARDING:
                            if (_onboarding == null)
                                _onboarding = new OnboardingWindow();
                            _onboarding.Show();
                            break;
                        case IDM_COMPAT_AUTO:
                            ApplyCompatibilityOverride(null);
                            break;
                        case IDM_COMPAT_FORCE_ON:
                            ApplyCompatibilityOverride("forceOn");
                            break;
                        case IDM_COMPAT_FORCE_OFF:
                            ApplyCompatibilityOverride("forceOff");
                            break;
#if DEBUG
                        case IDM_RESET_ONBOARDING:
                            if (_onboarding == null)
                                _onboarding = new OnboardingWindow();
                            _onboarding.ResetState();
                            _onboarding.Show();
                            break;
#endif
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
                    else if (timerId == TIMER_SINGLECLICK)
                    {
                        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_SINGLECLICK);
                        ShowContextMenu();
                    }
                    else if (timerId == TIMER_LAYOUT_CHECK)
                    {
                        Win32.KillTimer(_hWnd, (UIntPtr)TIMER_LAYOUT_CHECK);
                        CheckForegroundLayout();
                    }
                    else if (timerId == ForegroundMonitor.TIMER_FOREGROUND_DEBOUNCE)
                    {
                        Win32.KillTimer(_hWnd, (UIntPtr)ForegroundMonitor.TIMER_FOREGROUND_DEBOUNCE);
                        _foregroundMonitor?.Recompute();
                    }
                    return IntPtr.Zero;

                case Win32.WM_INPUTLANGCHANGE:
                    // Le layout système a changé (ex: Win+Espace) → invalider le cache HKL
                    _foregroundMonitor?.Recompute();
                    break; // laisser DefWindowProc traiter aussi

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
            ConfigManager.Log("WndProc", ex);
        }

        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>Réinstalle le keyboard hook (nouveau SetWindowsHookEx).</summary>
    private void ReinstallHook()
    {
        if (_hook == null || _mapper == null) return;

        var oldHook = _hook;
        var newHook = new KeyboardHook(_mapper);
        try
        {
            newHook.RawKeyDown += OnKeyPressed;
            newHook.SearchRequested += () => Win32.PostMessageW(_hWnd, WM_APP_SEARCH, IntPtr.Zero, IntPtr.Zero);
            newHook.VirtualKeyboardRequested += () => Win32.PostMessageW(_hWnd, WM_APP_VKBD, IntPtr.Zero, IntPtr.Zero);
            newHook.LayoutMayHaveChanged += OnLayoutMayHaveChanged;
            newHook.Enabled = _enabled;
            newHook.Install();
            _hook = newHook;
            oldHook.Dispose();
        }
        catch
        {
            newHook.Dispose();
            throw;
        }

        // Activer la fenêtre pour que le thread soit associé au système d'input
        // Sans cet appel, le hook LL est installé mais ne reçoit pas d'événements
        Win32.SetForegroundWindow(_hWnd);
    }

    // ═══════════════════════════════════════════════════════════════
    // Détection double remapping
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Vérifie si un layout donné est AZERTY Global en testant le Smart Caps Lock.
    /// Sur AZERTY Global, Verr.Maj + é → É, + ç → Ç, + à → À.
    /// Sur AZERTY standard, Verr.Maj + é → 2, + ç → 9, + à → 0.
    /// Utilise ToUnicodeEx avec le HKL spécifié — aucun accès registre.
    /// </summary>
    private static bool IsLayoutAZERTYGlobal(IntPtr hkl)
    {
        var keyState = new byte[256];
        keyState[0x14] = 0x01; // VK_CAPITAL toggled ON
        var buf = new System.Text.StringBuilder(8);

        // 3 tests indépendants sur la signature du Smart Caps Lock
        (uint scancode, char expected)[] tests =
        {
            (0x03, 'É'), // Verr.Maj + é/2 → É (AZERTY Global) vs 2 (standard)
            (0x0A, 'Ç'), // Verr.Maj + ç/9 → Ç (AZERTY Global) vs 9 (standard)
            (0x0B, 'À'), // Verr.Maj + à/0 → À (AZERTY Global) vs 0 (standard)
        };

        foreach (var (scancode, expected) in tests)
        {
            uint vk = Win32.MapVirtualKeyExW(scancode, 1, hkl); // MAPVK_VSC_TO_VK
            if (vk == 0) return false;

            buf.Clear();
            int result = Win32.ToUnicodeEx(vk, scancode, keyState, buf, buf.Capacity, 0, hkl);
            if (result < 0) // touche morte inattendue → consommer
                Win32.ToUnicodeEx(vk, scancode, keyState, buf, buf.Capacity, 0, hkl);
            if (result != 1 || buf[0] != expected)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Avertit l'utilisateur si le layout système actif est déjà AZERTY Global.
    /// Appelé au démarrage (vérifie le layout de notre thread).
    /// </summary>
    private void CheckSystemLayout()
    {
        IntPtr hkl = Win32.GetKeyboardLayout(0);
        if (!IsLayoutAZERTYGlobal(hkl)) return;

        int result = Win32.MessageBoxW(_hWnd,
            "La disposition système AZERTY Global est déjà active " +
            "sur cet ordinateur.\n\n" +
            "L'application n'est pas nécessaire dans ce cas et pourrait " +
            "créer des conflits.\n\n" +
            "Voulez-vous quitter ?",
            "AZERTY Global", 0x24); // MB_YESNO | MB_ICONQUESTION

        if (result == 6) // IDYES
            OnExit();
    }

    /// <summary>
    /// Appelé quand Ctrl+Shift est relâché sans 3e touche.
    /// Planifie une vérification après 100ms (laisser Windows finir le switch).
    /// </summary>
    private void OnLayoutMayHaveChanged()
    {
        Win32.SetTimer(_hWnd, (UIntPtr)TIMER_LAYOUT_CHECK, 100, IntPtr.Zero);
    }

    /// <summary>
    /// Vérifie le layout du thread au premier plan après un potentiel Ctrl+Shift switch.
    /// </summary>
    private void CheckForegroundLayout()
    {
        IntPtr hwndFg = Win32.GetForegroundWindow();
        uint threadId = Win32.GetWindowThreadProcessId(hwndFg, IntPtr.Zero);
        IntPtr hkl = Win32.GetKeyboardLayout(threadId);

        if (!IsLayoutAZERTYGlobal(hkl)) return;

        int result = Win32.MessageBoxW(_hWnd,
            "La disposition système AZERTY Global vient d'être activée.\n\n" +
            "L'application n'est pas nécessaire dans ce cas et pourrait " +
            "créer des conflits.\n\n" +
            "Voulez-vous quitter ?",
            "AZERTY Global", 0x24); // MB_YESNO | MB_ICONQUESTION

        if (result == 6) // IDYES
            OnExit();
    }

    // ═══════════════════════════════════════════════════════════════
    // Actions utilisateur
    // ═══════════════════════════════════════════════════════════════
    private void ShowContextMenu()
    {
        var hMenu = Win32.CreatePopupMenu();
        var kbdKey = ConfigManager.GetShortcutDisplayName(ConfigManager.ShortcutVirtualKeyboardVk);
        var searchKey = ConfigManager.GetShortcutDisplayName(ConfigManager.ShortcutCharacterSearchVk);
        // Actions fréquentes
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_TOGGLE,
            _enabled ? "Désactiver\tCtrl+Maj+Verr.Maj" : "Activer\tCtrl+Maj+Verr.Maj");
        uint kbdFlags = _enabled || _virtualKeyboard?.IsVisible == true ? MF_STRING : MF_STRING | MF_GRAYED;
        Win32.AppendMenuW(hMenu, kbdFlags, IDM_KEYBOARD,
            _virtualKeyboard?.IsVisible == true ? $"Masquer le clavier virtuel\tCtrl+Maj+{kbdKey}" : $"Clavier virtuel\tCtrl+Maj+{kbdKey}");
        uint searchFlags = _enabled || _characterSearch?.IsVisible == true ? MF_STRING : MF_STRING | MF_GRAYED;
        Win32.AppendMenuW(hMenu, searchFlags, IDM_SEARCH, $"Rechercher un caractère\tCtrl+Maj+{searchKey}");
        Win32.AppendMenuW(hMenu, MF_SEPARATOR, 0, null);

        // Liens et info
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_ONBOARDING, "Fenêtre de bienvenue");
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_SITE, "Visiter le site web");
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_FEEDBACK, "Donner son avis sur AZERTY Global");
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_BUG, "Signaler un bug");
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_SUPPORT, "❤️ Soutenir le projet");
        Win32.AppendMenuW(hMenu, MF_SEPARATOR, 0, null);

        // Sous-menu compatibilité du process foreground (si détectable)
        var fgProc = _foregroundMonitor?.CurrentProcessName;
        if (!string.IsNullOrEmpty(fgProc))
        {
            var hSubMenu = Win32.CreatePopupMenu();
            Win32.AppendMenuW(hSubMenu, MF_STRING, IDM_COMPAT_AUTO, "Auto (détection automatique)");
            Win32.AppendMenuW(hSubMenu, MF_STRING, IDM_COMPAT_FORCE_ON, "Forcer compatibilité jeu");
            Win32.AppendMenuW(hSubMenu, MF_STRING, IDM_COMPAT_FORCE_OFF, "Forcer désactivation");

            // Marquer la radio active
            var ovr = ConfigManager.GetCompatibilityOverride(fgProc);
            uint activeId = ovr switch
            {
                "forceOn" => IDM_COMPAT_FORCE_ON,
                "forceOff" => IDM_COMPAT_FORCE_OFF,
                _ => IDM_COMPAT_AUTO
            };
            Win32.CheckMenuRadioItem(hSubMenu, IDM_COMPAT_AUTO, IDM_COMPAT_FORCE_OFF, activeId, Win32.MF_BYCOMMAND);

            Win32.AppendMenuW(hMenu, MF_STRING | MF_POPUP, (nuint)hSubMenu, $"Compatibilité « {fgProc} »");
            Win32.AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        }

        // Paramètres et fin
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_SETTINGS, "⚙️ Paramètres");
#if DEBUG
        Win32.AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(hMenu, MF_STRING, IDM_RESET_ONBOARDING, "🛠 [DEBUG] Réinitialiser onboarding");
#endif
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

        // Fermer le clavier virtuel et la recherche quand on désactive
        if (!_enabled)
        {
            if (_virtualKeyboard?.IsVisible == true) _virtualKeyboard.Hide();
            if (_characterSearch?.IsVisible == true) _characterSearch.Hide();
        }

        UpdateIcon();
        UpdateTooltip();

        ShowBalloon("AZERTY Global", _enabled ? "est actif." : "est désactivé.");
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

    private void OnReportBug()
    {
        var os = Environment.OSVersion;
        var winVer = os.Version.Build >= 22000 ? "11" : "10";
        var osVersion = $"Windows {winVer} ({os.Version.Build})";
        var url = $"https://azerty.global/bug?v={Uri.EscapeDataString(Program.Version)}&os={Uri.EscapeDataString(osVersion)}&src=app";
        Win32.ShellExecuteW(IntPtr.Zero, "open", url, null, null, 1);
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

        _foregroundMonitor?.Dispose(); _foregroundMonitor = null;
        _hook?.Dispose(); _hook = null;
        _virtualKeyboard?.Dispose(); _virtualKeyboard = null;
        _characterSearch?.Dispose(); _characterSearch = null;
        _onboarding?.Dispose(); _onboarding = null;
        _settings?.Dispose(); _settings = null;
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
        bool active = _enabled && !_autoDisabledForAntiCheat;
        bool capsLock = _mapper?.CapsLockActive == true && active;
        string iconText = "AG";

        _hIcon = CreateTextIcon(iconText, active, capsLock, _autoDisabledForAntiCheat);

        _nid.hIcon = _hIcon;
        _nid.uFlags = NIF_ICON;
        Win32.Shell_NotifyIconW(NIM_MODIFY, ref _nid);

        if (oldIcon != IntPtr.Zero) Win32.DestroyIcon(oldIcon);
    }

    private void UpdateTooltip()
    {
        var parts = new List<string> { "AZERTY Global v" + Program.Version };
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

    /// <summary>Retourne le symbole d'affichage d'une touche morte (partagé avec LearningModule).</summary>
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
        if (!ConfigManager.NotificationsEnabled) return;
        _nid.uFlags = NIF_INFO;
        _nid.szInfoTitle = title;
        _nid.szInfo = text;
        _nid.dwInfoFlags = NIIF_INFO;
        Win32.Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    /// <summary>
    /// Bulle de notification de sécurité. Bypass <see cref="ConfigManager.NotificationsEnabled"/>
    /// car cette information ne doit pas être manquée par l'utilisateur (anti-cheat,
    /// override invalide cleanup, etc.).
    /// </summary>
    private void ShowSecurityBalloon(string title, string text)
    {
        _nid.uFlags = NIF_INFO;
        _nid.szInfoTitle = title;
        _nid.szInfo = text;
        _nid.dwInfoFlags = NIIF_WARNING;
        Win32.Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    /// <summary>
    /// Handler du changement de mode foreground. Désactive auto si entrée dans un process
    /// anti-cheat (avec bulle explicative), réactive auto à la sortie (avec bulle de retour).
    /// Ne touche PAS <see cref="_enabled"/> qui reflète la volonté manuelle utilisateur :
    /// on n'agit que sur <see cref="KeyboardHook.Enabled"/>.
    /// </summary>
    private void OnForegroundChanged()
    {
        if (_foregroundMonitor == null || _hook == null || _mapper == null) return;
        var mode = _foregroundMonitor.CurrentMode;
        var procName = _foregroundMonitor.CurrentProcessName ?? "";

        if (mode == CompatibilityMode.DisabledAntiCheat && !_autoDisabledForAntiCheat)
        {
            // Entrée dans un process anti-cheat : désactivation auto
            if (_enabled && _hook.Enabled)
            {
                _wasEnabledBeforeAutoDisable = true;
                _mapper.ClearPassedThroughKeys(); // émet keyup synthétiques avant désactivation
                _hook.Enabled = false;
            }
            _autoDisabledForAntiCheat = true;
            UpdateIcon();
            ShowBalloon("AZERTY Global",
                $"désactivé temporairement pour {procName}\n(anti-cheat : injection de frappes interdite).");
            ConfigManager.LogCompatCriticalEvent("AntiCheatDetected",
                $"process={procName}, action=disable");
        }
        else if (mode != CompatibilityMode.DisabledAntiCheat && _autoDisabledForAntiCheat)
        {
            // Sortie d'un process anti-cheat : réactivation auto si on était actif avant
            _autoDisabledForAntiCheat = false;
            if (_wasEnabledBeforeAutoDisable && _enabled)
            {
                _hook.Enabled = true;
                _mapper.SyncState();
                ShowBalloon("AZERTY Global", "est de nouveau actif.");
            }
            _wasEnabledBeforeAutoDisable = false;
            UpdateIcon();
        }
    }

    /// <summary>
    /// Applique un override utilisateur (Auto/forceOn/forceOff) sur le process foreground actuel.
    /// Refuse forceOn sur process anti-cheat (sécurité utilisateur) avec bulle explicative.
    /// </summary>
    private void ApplyCompatibilityOverride(string? mode)
    {
        var proc = _foregroundMonitor?.CurrentProcessName;
        if (string.IsNullOrEmpty(proc)) return;

        if (mode == "forceOn" && GameRegistry.IsAntiCheatProcess(proc, _foregroundMonitor?.CurrentFullPath))
        {
            ShowSecurityBalloon("AZERTY Global",
                $"AZERTY Global ne peut pas être activé sur {proc} : son anti-cheat " +
                "pourrait considérer cela comme de la triche et bannir votre compte.");
            return;
        }

        ConfigManager.SetCompatibilityOverride(proc, mode);
        _foregroundMonitor?.Recompute();
    }

    /// <summary>
    /// Crée une icône 32x32 avec texte sur fond coloré.
    /// Bleu = actif, gris = inactif. Barre orange en bas si CapsLock.
    /// </summary>
    private static IntPtr CreateTextIcon(string text, bool active, bool capsLock = false, bool autoDisabled = false)
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

        // Indicateur "désactivé auto pour anti-cheat" : carré rouge en bas-droite
        if (autoDisabled)
        {
            var dot = new Win32.RECT { left = size - 12, top = size - 12, right = size - 2, bottom = size - 2 };
            var hRedBrush = Win32.CreateSolidBrush(0x000000FFu); // Rouge (BBGGRR)
            Win32.FillRect(hdc, ref dot, hRedBrush);
            Win32.DeleteObject(hRedBrush);
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

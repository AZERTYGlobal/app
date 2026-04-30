// Détection du process foreground et du mode de compatibilité courant.
//
// Mécanique :
// - SetWinEventHook(EVENT_SYSTEM_FOREGROUND) au démarrage (après création de la fenêtre tray)
// - Le callback est routé vers le thread de la boucle de messages (WINEVENT_OUTOFCONTEXT
//   sur le thread qui a appelé SetWinEventHook, soit notre thread principal)
// - Debounce 100 ms via SetTimer sur la fenêtre tray (pour absorber les rafales d'alt-tab)
// - À l'expiration du timer, TrayApplication appelle Recompute() qui :
//   1. lit le process foreground via IWin32Api.TryGetForegroundProcess
//   2. énumère les modules du process via IWin32Api.TryEnumProcessModules
//   3. calcule CurrentMode selon la liste anti-cheat / DLL signatures / overrides utilisateur
//   4. déclenche ForegroundChanged
//
// Mode dégradé : si SetWinEventHook retourne IntPtr.Zero (env MSIX restrictif rare),
// CurrentMode reste à Default et l'app fonctionne en comportement v0.9.6.

namespace AZERTYGlobal;

internal enum CompatibilityMode
{
    /// <summary>Comportement v0.9.6 — injection KEYEVENTF_UNICODE.</summary>
    Default,
    /// <summary>Process avec framework gaming détecté — injection combo native + Alt+code fallback.</summary>
    NativeCombo,
    /// <summary>Process protégé par anti-cheat kernel-level — désactivation totale.</summary>
    DisabledAntiCheat
}

internal sealed class ForegroundMonitor : IDisposable
{
    private readonly IWin32Api _api;
    private readonly IntPtr _trayHwnd;

    /// <summary>ID du timer Win32 utilisé pour le debounce. Doit être unique côté wndproc.</summary>
    public const uint TIMER_FOREGROUND_DEBOUNCE = 0xF00100;

    // Anti-GC : le delegate doit rester rooté tant que le hook est installé
    private Win32.WinEventDelegate? _winEventDelegate;
    private IntPtr _winEventHook = IntPtr.Zero;

    // Champs partagés entre threads — volatile pour les lectures depuis le keyboard hook thread.
    // Note : les écritures se font depuis le thread tray (callback WinEventHook ou Recompute manuel).
    private string? _currentProcessName;
    private string? _currentFullPath;
    private IntPtr _currentHkl;
    private volatile int _currentModeRaw; // CompatibilityMode encodé en int pour atomicité

    /// <summary>Nom court du process foreground (ex: "Minecraft.Windows.exe"). Null si pas de fenêtre foreground.</summary>
    public string? CurrentProcessName => _currentProcessName;

    /// <summary>Chemin complet du process foreground.</summary>
    public string? CurrentFullPath => _currentFullPath;

    /// <summary>HKL du layout natif du thread foreground.</summary>
    public IntPtr CurrentHkl => _currentHkl;

    /// <summary>Mode de compatibilité résolu pour le process foreground actuel.</summary>
    public CompatibilityMode CurrentMode => (CompatibilityMode)_currentModeRaw;

    /// <summary>Indique si le hook WinEvent a pu être installé. Si false, mode dégradé permanent (Default).</summary>
    public bool IsHookInstalled => _winEventHook != IntPtr.Zero;

    /// <summary>Déclenché à chaque changement effectif de mode (pas à chaque event foreground).</summary>
    public event Action? ForegroundChanged;

    /// <summary>
    /// Crée le monitor et installe le WinEventHook foreground.
    /// trayHwnd = HWND de la fenêtre tray (utilisé pour le SetTimer debounce). IntPtr.Zero
    /// désactive le debounce (utile pour les tests qui invoquent Recompute() directement).
    /// </summary>
    public ForegroundMonitor(IWin32Api api, IntPtr trayHwnd)
    {
        _api = api;
        _trayHwnd = trayHwnd;

        try
        {
            _winEventDelegate = OnWinEvent;
            _winEventHook = _api.SetWinEventHook(
                Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, _winEventDelegate);
            // Si retour IntPtr.Zero : mode dégradé. CurrentMode reste à Default.
        }
        catch (Exception ex)
        {
            ConfigManager.Log("ForegroundMonitor.ctor", ex);
            _winEventHook = IntPtr.Zero;
        }

        // Premier calcul initial (synchrone)
        Recompute();
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Programmer le debounce 100 ms si on a un HWND tray, sinon recompute immédiat (tests)
        if (_trayHwnd != IntPtr.Zero)
        {
            Win32.SetTimer(_trayHwnd, (UIntPtr)TIMER_FOREGROUND_DEBOUNCE, 100, IntPtr.Zero);
        }
        else
        {
            Recompute();
        }
    }

    /// <summary>
    /// Force le recalcul immédiat du process foreground et du mode.
    /// Appelé : (1) au démarrage, (2) à l'expiration du timer debounce, (3) sur WM_INPUTLANGCHANGE,
    /// (4) après modification d'un override utilisateur via le menu tray.
    /// </summary>
    public void Recompute()
    {
        try
        {
            string? processName = null;
            string? fullPath = null;
            IntPtr hkl = IntPtr.Zero;
            uint pid = 0;
            bool hasFg = _api.TryGetForegroundProcess(out processName, out fullPath, out hkl, out pid);

            CompatibilityMode mode = ResolveMode(processName, fullPath, pid, hasFg);

            // Snapshot atomique : on met à jour les champs volatiles dans un ordre stable
            _currentProcessName = processName;
            _currentFullPath = fullPath;
            _currentHkl = hkl;
            int oldMode = _currentModeRaw;
            int newMode = (int)mode;
            _currentModeRaw = newMode;

            if (oldMode != newMode && ConfigManager.CompatibilityDebugLog)
            {
                ConfigManager.LogCompatEvent("CompatMode",
                    $"{(CompatibilityMode)oldMode} → {mode} (process={processName ?? "<none>"})");
            }

            if (oldMode != newMode || (oldMode == newMode && processName != null))
            {
                // Toujours notifier sur changement effectif de process pour permettre à
                // TrayApplication de mettre à jour l'état UI (override changes, etc.)
                ForegroundChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            ConfigManager.Log("ForegroundMonitor.Recompute", ex);
        }
    }

    private CompatibilityMode ResolveMode(string? processName, string? fullPath, uint pid, bool hasFg)
    {
        if (!hasFg || string.IsNullOrEmpty(processName))
            return CompatibilityMode.Default;

        // Override utilisateur lu en premier (mais l'anti-cheat le surclasse pour la sécurité)
        var userOverride = ConfigManager.GetCompatibilityOverride(processName);

        // Anti-cheat : surclasse tout override forceOn (sécurité utilisateur)
        if (GameRegistry.IsAntiCheatProcess(processName, fullPath))
            return CompatibilityMode.DisabledAntiCheat;

        if (userOverride == "forceOn") return CompatibilityMode.NativeCombo;
        if (userOverride == "forceOff") return CompatibilityMode.DisabledAntiCheat;

        // Détection auto via modules chargés
        if (pid != 0 && _api.TryEnumProcessModules(pid, out var modules))
        {
            if (GameRegistry.HasGameFrameworkLoaded(modules))
                return CompatibilityMode.NativeCombo;
        }

        return CompatibilityMode.Default;
    }

    public void Dispose()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            try { _api.UnhookWinEvent(_winEventHook); } catch { }
            _winEventHook = IntPtr.Zero;
        }
        _winEventDelegate = null;
    }
}

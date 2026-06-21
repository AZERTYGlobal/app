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
    /// <summary>Process protégé ou explicitement désactivé — désactivation totale.</summary>
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

    // Snapshot immuable atomique pour cohérence cross-thread. Les 4 champs (processName,
    // fullPath, hkl, mode) sont mis à jour en une seule écriture de référence (atomique CLR
    // pour les types ref). Évite que le hook thread lise des combinaisons mixtes (ex. nouveau
    // processName / ancien hkl) pendant que le tray thread écrit séquentiellement.
    private sealed record class Snapshot(string? ProcessName, string? FullPath, IntPtr Hkl, CompatibilityMode Mode);
    private Snapshot? _snapshot;

    /// <summary>Nom court du process foreground (ex: "Minecraft.Windows.exe"). Null si pas de fenêtre foreground.</summary>
    public string? CurrentProcessName => _snapshot?.ProcessName;

    /// <summary>Chemin complet du process foreground.</summary>
    public string? CurrentFullPath => _snapshot?.FullPath;

    /// <summary>HKL du layout natif du thread foreground.</summary>
    public IntPtr CurrentHkl => _snapshot?.Hkl ?? IntPtr.Zero;

    /// <summary>Mode de compatibilité résolu pour le process foreground actuel.</summary>
    public CompatibilityMode CurrentMode => _snapshot?.Mode ?? CompatibilityMode.Default;

    /// <summary>
    /// Audit sécu 2026-05 SEV-A2-05 : lecture atomique des deux champs critiques
    /// pour EmitText. Évite la race entre lecture séquentielle de Mode puis Hkl
    /// (un OnWinEvent pouvait remplacer _snapshot entre les deux lectures, donnant
    /// mode/hkl discordants → faux caractère pendant alt-tab rapide).
    /// Un seul accès à _snapshot (déréf. atomique CLR sur ref types).
    /// </summary>
    public (CompatibilityMode Mode, IntPtr Hkl) GetEmitContext()
    {
        var snap = _snapshot;  // capture atomique
        return (snap?.Mode ?? CompatibilityMode.Default, snap?.Hkl ?? IntPtr.Zero);
    }

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

            // Ignorer les transitions vers les process shell Windows : effets de bord du clic
            // sur l'icône tray ou de la touche Win (zone de notification = explorer.exe,
            // recherche Windows = SearchHost.exe, menu Démarrer = StartMenuExperienceHost.exe,
            // etc.). Sans ça, le sous-menu « Compatibilité » afficherait ces process au lieu
            // du jeu/app qui était au foreground avant le clic. On conserve l'ancien snapshot
            // tant qu'on a déjà une valeur précédente. Note : on N'ignore PAS notre propre PID
            // — quand notre app (LearningModule, Settings, etc.) prend le focus, on veut le
            // mode Default pour nos propres frappes, sinon un mode NativeCombo hérité d'un jeu
            // antérieur ferait passer la saisie par combo native (avec dead keys natives qui
            // consommeraient '~' '^' etc.).
            bool isTransientShell = hasFg && processName != null && (
                string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "SearchHost.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processName, "TextInputHost.exe", StringComparison.OrdinalIgnoreCase));
            if (isTransientShell && _snapshot != null && !string.IsNullOrEmpty(_snapshot.ProcessName))
                return;

            CompatibilityMode mode = ResolveMode(processName, fullPath, pid, hasFg);

            // Snapshot atomique : une seule écriture de référence (atomique CLR sur ref types).
            var oldSnapshot = _snapshot;
            CompatibilityMode oldMode = oldSnapshot?.Mode ?? CompatibilityMode.Default;
            _snapshot = new Snapshot(processName, fullPath, hkl, mode);

            if (oldMode != mode && ConfigManager.CompatibilityDebugLog)
            {
                ConfigManager.LogCompatEvent("CompatMode",
                    $"{oldMode} → {mode} (process={ConfigManager.AnonymizeProcessName(processName)})");
            }

            if (oldMode != mode || (oldMode == mode && processName != null))
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
        if (!hasFg)
        {
            // Si un PID foreground existe mais que son nom/chemin est inaccessible,
            // on privilégie la sécurité utilisateur : ne pas laisser le hook actif
            // face à un process potentiellement protégé par anti-cheat.
            if (pid != 0)
                return CompatibilityMode.DisabledAntiCheat;
            return CompatibilityMode.Default;
        }

        if (string.IsNullOrEmpty(processName))
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

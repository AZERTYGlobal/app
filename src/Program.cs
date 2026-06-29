// AZERTY Global
// © 2017-2026 Antoine Olivier — Licence EUPL 1.2
// https://azerty.global

namespace AZERTYGlobal;

static class Program
{
    /// <summary>Version affichée partout (tooltip, À propos, etc.).</summary>
    internal const string Version = "1.0.0";

    [STAThread]
    static void Main()
    {
        // Déclarer l'app DPI-aware AVANT toute création de fenêtre
        try { Win32.SetProcessDpiAwarenessContext((IntPtr)(-4)); } // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        catch { try { Win32.SetProcessDPIAware(); } catch { } }   // Fallback Windows 8.1-
        // Empêcher les instances multiples — Audit sécu 2026-05 SEV-A2-03 :
        // préfixe Local\ explicite + qualif SID pour éviter qu'un autre process
        // user-land squatte le nom et bloque le démarrage (DoS trivial sans
        // préfixe). Local\ scope = current session uniquement, donc safe.
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "anon";
        var mutexName = $"Local\\AZERTYGlobalSingleInstance.{sid}";
        using var mutex = new Mutex(true, mutexName, out bool isNew);
        if (!isNew)
        {
            Win32.MessageBoxW(IntPtr.Zero,
                "AZERTY Global est déjà en cours d'exécution.",
                "AZERTY Global", 0x40); // MB_ICONINFORMATION
            return;
        }

        // Rotation log centralisée : si error.log > 5 Mo, renommer en error.log.old
        ConfigManager.RotateLogIfNeeded();

        // Gestion des erreurs fatales (le handler ne peut pas empêcher la terminaison)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var logPath = Path.Combine(ConfigManager.LogDirectory, "error.log");
            var safeMessage = ConfigManager.SanitizeException(e.ExceptionObject as Exception);
            try { Directory.CreateDirectory(ConfigManager.LogDirectory); } catch { }
            try
            {
                // Audit sécu 2026-05 SEV-A1-01 : sanitize au lieu de ex.ToString() complet.
                File.AppendAllText(logPath, $"[{DateTime.Now:s}] FATAL: {safeMessage}\n");
            }
            catch { }
            Win32.MessageBoxW(IntPtr.Zero,
                "Une erreur fatale est survenue. Le détail technique a été écrit dans error.log.",
                "Erreur fatale", 0x10); // MB_ICONERROR
        };

        using var app = new TrayApplication();
        app.Run();
    }
}

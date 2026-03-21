// AZERTY Global Portable
// © 2017-2026 Antoine Olivier — Licence EUPL 1.2
// https://azerty.global

namespace AZERTYGlobalPortable;

static class Program
{
    /// <summary>Version affichée partout (tooltip, À propos, etc.).</summary>
    internal const string Version = "0.9.1";

    [STAThread]
    static void Main()
    {
        // Déclarer l'app DPI-aware AVANT toute création de fenêtre
        try { Win32.SetProcessDpiAwarenessContext((IntPtr)(-4)); } // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        catch { try { Win32.SetProcessDPIAware(); } catch { } }   // Fallback Windows 8.1-
        // Empêcher les instances multiples
        using var mutex = new Mutex(true, "AZERTYGlobalPortable_SingleInstance", out bool isNew);
        if (!isNew)
        {
            Win32.MessageBoxW(IntPtr.Zero,
                "AZERTY Global Portable est déjà en cours d'exécution.",
                "AZERTY Global", 0x40); // MB_ICONINFORMATION
            return;
        }

        // Gestion des erreurs fatales (le handler ne peut pas empêcher la terminaison)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // En mode MSIX, BaseDirectory est en lecture seule → écrire dans LocalAppData
            var logDir = ConfigManager.IsPackaged
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AZERTY Global Portable")
                : AppContext.BaseDirectory;
            var logPath = Path.Combine(logDir, "error.log");
            // Rotation : si le log dépasse 1 Mo, on le tronque
            try
            {
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 1_048_576)
                    File.WriteAllText(logPath, "[Log tronqué — taille max atteinte]\n");
            }
            catch { /* Ignorer erreur de rotation */ }
            File.AppendAllText(logPath, $"[{DateTime.Now:s}] FATAL: {e.ExceptionObject}\n");
            Win32.MessageBoxW(IntPtr.Zero,
                e.ExceptionObject?.ToString() ?? "Erreur inconnue",
                "Erreur fatale", 0x10); // MB_ICONERROR
        };

        using var app = new TrayApplication();
        app.Run();
    }
}

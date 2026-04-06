// Gestion du lancement automatique au démarrage de Windows
using System.Runtime.InteropServices;

namespace AZERTYGlobal;

/// <summary>
/// Gère le lancement automatique au démarrage de Windows.
/// Mode portable (unpackaged) : raccourci .lnk dans le dossier Startup.
/// Mode MSIX (packaged) : API WinRT StartupTask déclarée dans le manifeste.
/// </summary>
static class AutoStart
{
    private const string ShortcutName = "AZERTY Global.lnk";

    private static string StartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    private static string ShortcutPath =>
        Path.Combine(StartupFolder, ShortcutName);

    // ──────────────────────────────────────────────
    //  COM GUIDs pour IShellLink / IPersistFile
    // ──────────────────────────────────────────────

    // CLSID_ShellLink = 00021401-0000-0000-C000-000000000046
    private static readonly Guid CLSID_ShellLink =
        new("00021401-0000-0000-C000-000000000046");

    // IID_IShellLinkW = 000214F9-0000-0000-C000-000000000046
    private static readonly Guid IID_IShellLinkW =
        new("000214F9-0000-0000-C000-000000000046");

    // IID_IPersistFile = 0000010B-0000-0000-C000-000000000046
    private static readonly Guid IID_IPersistFile =
        new("0000010B-0000-0000-C000-000000000046");

    private const uint CLSCTX_INPROC_SERVER = 1;
    private const int S_OK = 0;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
        ref Guid riid, out IntPtr ppv);

    // ──────────────────────────────────────────────
    //  IShellLinkW vtable offsets
    // ──────────────────────────────────────────────

    // IShellLinkW vtable (hérite IUnknown: 0=QI, 1=AddRef, 2=Release):
    //  3=GetPath, 4=GetIDList, 5=SetIDList,
    //  6=GetDescription, 7=SetDescription,
    //  8=GetWorkingDirectory, 9=SetWorkingDirectory,
    // 10=GetArguments, 11=SetArguments,
    // 12=GetHotkey, 13=SetHotkey,
    // 14=GetShowCmd, 15=SetShowCmd,
    // 16=GetIconLocation, 17=SetIconLocation,
    // 18=SetRelativePath, 19=Resolve, 20=SetPath
    private const int VT_IShellLink_SetDescription = 7;
    private const int VT_IShellLink_SetWorkingDirectory = 9;
    private const int VT_IShellLink_SetPath = 20;

    // ──────────────────────────────────────────────
    //  IPersistFile vtable offsets (IUnknown=0,1,2 + IPersist::GetClassID=3)
    // ──────────────────────────────────────────────
    //  4: IsDirty  5: Load  6: Save  7: SaveCompleted  8: GetCurFile

    private const int VT_IPersistFile_Save = 6;

    // Delegate types pour les méthodes COM
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SetPathDelegate(IntPtr self, string pszFile);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SetWorkingDirectoryDelegate(IntPtr self, string pszDir);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SetDescriptionDelegate(IntPtr self, string pszName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(IntPtr self, ref Guid riid, out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SaveDelegate(IntPtr self, string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

    /// <summary>
    /// Active ou désactive le lancement automatique et met à jour la config.
    /// Mode portable : raccourci .lnk dans le dossier Startup.
    /// Mode MSIX : API WinRT StartupTask.
    /// Retourne false si l'opération a échoué.
    /// </summary>
    public static bool Set(bool enabled)
    {
        try
        {
            if (ConfigManager.IsPackaged)
            {
                if (!SetStartupTask(enabled))
                    return false;
            }
            else
            {
                if (enabled)
                {
                    if (!Enable()) return false;
                }
                else
                    Disable();
            }

            bool finalState = IsRegistered;
            if (finalState != enabled)
                return false;

            ConfigManager.SetAutoStart(finalState);
            return true;
        }
        catch (Exception ex)
        {
            ConfigManager.Log($"AutoStart.Set({enabled})", ex);
            return false;
        }
    }

    /// <summary>Vérifie si le lancement automatique est enregistré.</summary>
    public static bool IsRegistered =>
        ConfigManager.IsPackaged ? IsStartupTaskEnabled() : File.Exists(ShortcutPath);

    public static string GetFailureMessage() =>
        ConfigManager.IsPackaged
            ? "Impossible d'enregistrer le lancement automatique.\nVérifiez l'autorisation dans Paramètres > Applications > Démarrage."
            : "Impossible d'enregistrer le lancement automatique.\nVérifiez les permissions du dossier Démarrage.";

    private static bool Enable()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        string? exeDir = Path.GetDirectoryName(exePath);

        // Créer le dossier Startup s'il n'existe pas
        string startupDir = StartupFolder;
        if (!Directory.Exists(startupDir))
            Directory.CreateDirectory(startupDir);

        // Créer le raccourci via COM IShellLink
        Guid clsid = CLSID_ShellLink;
        Guid iidShellLink = IID_IShellLinkW;

        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER,
            ref iidShellLink, out IntPtr pShellLink);
        if (hr != S_OK || pShellLink == IntPtr.Zero)
            return false;

        try
        {
            // Lire la vtable
            IntPtr vtable = Marshal.ReadIntPtr(pShellLink);

            // SetPath (slot 20)
            IntPtr pSetPath = Marshal.ReadIntPtr(vtable, VT_IShellLink_SetPath * IntPtr.Size);
            var setPath = Marshal.GetDelegateForFunctionPointer<SetPathDelegate>(pSetPath);
            hr = setPath(pShellLink, exePath);
            if (hr != S_OK) return false;

            // SetWorkingDirectory (slot 9)
            if (!string.IsNullOrEmpty(exeDir))
            {
                IntPtr pSetWorkDir = Marshal.ReadIntPtr(vtable, VT_IShellLink_SetWorkingDirectory * IntPtr.Size);
                var setWorkDir = Marshal.GetDelegateForFunctionPointer<SetWorkingDirectoryDelegate>(pSetWorkDir);
                hr = setWorkDir(pShellLink, exeDir);
                if (hr != S_OK) return false;
            }

            // SetDescription (slot 7)
            IntPtr pSetDesc = Marshal.ReadIntPtr(vtable, VT_IShellLink_SetDescription * IntPtr.Size);
            var setDesc = Marshal.GetDelegateForFunctionPointer<SetDescriptionDelegate>(pSetDesc);
            hr = setDesc(pShellLink, "AZERTY Global – Lancement automatique");
            if (hr != S_OK) return false;

            // QueryInterface pour IPersistFile
            IntPtr pQueryInterface = Marshal.ReadIntPtr(vtable, 0 * IntPtr.Size);
            var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(pQueryInterface);
            Guid iidPersistFile = IID_IPersistFile;
            hr = queryInterface(pShellLink, ref iidPersistFile, out IntPtr pPersistFile);
            if (hr != S_OK || pPersistFile == IntPtr.Zero)
                return false;

            try
            {
                // IPersistFile::Save (slot 6)
                IntPtr vtablePF = Marshal.ReadIntPtr(pPersistFile);
                IntPtr pSave = Marshal.ReadIntPtr(vtablePF, VT_IPersistFile_Save * IntPtr.Size);
                var save = Marshal.GetDelegateForFunctionPointer<SaveDelegate>(pSave);
                hr = save(pPersistFile, ShortcutPath, true);
                if (hr != S_OK) return false;
            }
            finally
            {
                IntPtr vtablePF = Marshal.ReadIntPtr(pPersistFile);
                IntPtr pReleasePF = Marshal.ReadIntPtr(vtablePF, 2 * IntPtr.Size);
                var releasePF = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(pReleasePF);
                releasePF(pPersistFile);
            }
        }
        finally
        {
            IntPtr vtable = Marshal.ReadIntPtr(pShellLink);
            IntPtr pRelease = Marshal.ReadIntPtr(vtable, 2 * IntPtr.Size);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(pRelease);
            release(pShellLink);
        }

        return true;
    }

    private static void Disable()
    {
        try
        {
            if (File.Exists(ShortcutPath))
                File.Delete(ShortcutPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier verrouillé ou accès refusé — on ignore
        }
    }

    // ──────────────────────────────────────────────
    //  Mode MSIX : API WinRT StartupTask
    // ──────────────────────────────────────────────

    /// <summary>TaskId déclaré dans AppxManifest.xml.</summary>
    private const string StartupTaskId = "AZERTYGlobalStartup";

    /// <summary>
    /// Active ou désactive la StartupTask MSIX.
    /// Retourne true si l'état final correspond à la demande.
    /// </summary>
    private static bool SetStartupTask(bool enabled)
    {
        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId)
                .GetAwaiter().GetResult();

            if (enabled)
            {
                // RequestEnableAsync retourne l'état réel après la demande.
                // Peut être Enabled, DisabledByUser, ou DisabledByPolicy.
                var state = task.RequestEnableAsync().GetAwaiter().GetResult();
                return state == Windows.ApplicationModel.StartupTaskState.Enabled;
            }
            else
            {
                task.Disable();
                return true;
            }
        }
        catch { return false; }
    }

    private static bool IsStartupTaskEnabled()
    {
        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId)
                .GetAwaiter().GetResult();
            return task.State == Windows.ApplicationModel.StartupTaskState.Enabled;
        }
        catch { return false; }
    }
}

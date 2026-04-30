// Abstraction des P/Invoke critiques utilisés par KeyMapper et ForegroundMonitor.
// Permet d'injecter une implémentation mock dans les tests unitaires.
//
// KeyboardHook n'utilise PAS cette interface : sa nature (SetWindowsHookEx) est
// intrinsèquement liée à un appel statique, et il est testé manuellement via
// smoke tests. Cf. plan v0.9.7 § « KeyboardHook n'est PAS refactoré ».

namespace AZERTYGlobal;

internal interface IWin32Api
{
    // Layout / clavier
    short VkKeyScanExW(char ch, IntPtr hkl);
    uint MapVirtualKeyExW(uint code, uint mapType, IntPtr hkl);
    short GetKeyState(int vk);
    IntPtr GetKeyboardLayout(uint threadId);

    // Injection de frappes
    uint SendInput(Win32.INPUT[] inputs);

    // Foreground / process inspection
    IntPtr GetForegroundWindow();

    /// <summary>
    /// Récupère le nom court (ex: "Minecraft.Windows.exe"), le chemin complet, le HKL
    /// du thread foreground et le PID. Encapsule la séquence
    /// GetForegroundWindow → GetWindowThreadProcessId → OpenProcess → GetModuleFileNameExW.
    /// Retourne false si pas de fenêtre foreground ou si OpenProcess échoue (process protégé).
    /// </summary>
    bool TryGetForegroundProcess(out string? processName, out string? fullPath, out IntPtr hkl, out uint pid);

    /// <summary>
    /// Énumère les modules (DLL) chargés dans un process. Retourne false si OpenProcess
    /// échoue (process protégé par anti-cheat ou privilèges insuffisants).
    /// </summary>
    bool TryEnumProcessModules(uint pid, out string[] moduleFileNames);

    // WinEvent (foreground change)
    IntPtr SetWinEventHook(uint eventMin, uint eventMax, Win32.WinEventDelegate cb);
    bool UnhookWinEvent(IntPtr hook);
}

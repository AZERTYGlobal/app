// Mock IWin32Api pour les tests d'intégration niveau 3.
//
// Limitations connues (cf. plan v0.9.7 §« Limitations du mock ») :
// - Timing exact des events Windows (debounce, ordering rapide) non reproductible
// - MapVirtualKeyExW pour HKL non installé : renvoie ce qu'on script, pas le vrai comportement
// - Ordre d'arrivée des EVENT_SYSTEM_FOREGROUND rapprochés non simulé
// - Insertion d'input physique entre deux events d'un batch SendInput impossible à mocker
// - Effet réel de KEYEVENTF_SCANCODE côté apps tierces non testable
//
// Les tests niveau 3 valident donc la LOGIQUE de construction des INPUT[],
// pas le comportement runtime de Windows.

using AZERTYGlobal;

namespace AZERTYGlobal.Tests;

internal sealed class MockWin32Api : IWin32Api
{
    // ── Recordings ──────────────────────────────────────────────────
    /// <summary>Tous les batches SendInput appelés depuis le mock, en ordre d'arrivée.</summary>
    public List<Win32.INPUT[]> SendInputCalls { get; } = new();

    /// <summary>Aplati toutes les séquences d'INPUT en un seul tableau (pour assertions).</summary>
    public Win32.INPUT[] AllInputs => SendInputCalls.SelectMany(b => b).ToArray();

    // ── Scripted responses pour clavier/layout ──────────────────────
    /// <summary>
    /// Mappings scriptés pour VkKeyScanExW. Clé (char, hkl) → résultat short.
    /// La valeur -1 signifie « caractère inaccessible » (déclenche fallback Alt+code dans la prod).
    /// </summary>
    public Dictionary<(char, IntPtr), short> VkKeyScanScript { get; } = new();

    /// <summary>Réponses scriptées pour MapVirtualKeyExW. Clé (vk, mapType, hkl) → scancode.</summary>
    public Dictionary<(uint, uint, IntPtr), uint> MapVirtualKeyScript { get; } = new();

    /// <summary>Réponses scriptées pour GetKeyState. Clé = vk → result short.</summary>
    public Dictionary<int, short> KeyStateScript { get; } = new();

    /// <summary>Layout courant retourné pour GetKeyboardLayout (peu importe le thread).</summary>
    public IntPtr CurrentHkl { get; set; } = (IntPtr)0x040C040C; // AZERTY FR par défaut

    public IntPtr ForegroundWindow { get; set; } = (IntPtr)0x12345678;

    // ── Foreground / process inspection ─────────────────────────────
    public string? ScriptedProcessName { get; set; }
    public string? ScriptedFullPath { get; set; }
    public uint ScriptedPid { get; set; } = 1234;
    public string[]? ScriptedModules { get; set; }

    // ── WinEventHook ────────────────────────────────────────────────
    public Win32.WinEventDelegate? CapturedWinEventDelegate { get; private set; }
    public IntPtr WinEventHookHandle { get; set; } = (IntPtr)0xCAFE;
    public bool ShouldFailSetWinEventHook { get; set; }
    public bool UnhookWinEventCalled { get; private set; }

    /// <summary>
    /// Simule un changement de foreground en invoquant le delegate capturé
    /// avec les valeurs scriptées (process / hkl / modules).
    /// </summary>
    public void SimulateForegroundChange(string? processName, string? fullPath, IntPtr hkl, string[]? modules = null)
    {
        ScriptedProcessName = processName;
        ScriptedFullPath = fullPath;
        ForegroundWindow = processName == null ? IntPtr.Zero : (IntPtr)0x12345678;
        CurrentHkl = hkl;
        ScriptedModules = modules;
        CapturedWinEventDelegate?.Invoke(IntPtr.Zero, Win32.EVENT_SYSTEM_FOREGROUND,
            ForegroundWindow, 0, 0, 0, 0);
    }

    // ── IWin32Api implementation ────────────────────────────────────

    public short VkKeyScanExW(char ch, IntPtr hkl) =>
        VkKeyScanScript.TryGetValue((ch, hkl), out var v) ? v : (short)-1;

    public uint MapVirtualKeyExW(uint code, uint mapType, IntPtr hkl) =>
        MapVirtualKeyScript.TryGetValue((code, mapType, hkl), out var v) ? v : 0;

    public short GetKeyState(int vk) =>
        KeyStateScript.TryGetValue(vk, out var v) ? v : (short)0;

    public IntPtr GetKeyboardLayout(uint threadId) => CurrentHkl;

    public uint SendInput(Win32.INPUT[] inputs)
    {
        SendInputCalls.Add(inputs.ToArray()); // copy défensif
        return (uint)inputs.Length;
    }

    public IntPtr GetForegroundWindow() => ForegroundWindow;

    public bool TryGetForegroundProcess(out string? processName, out string? fullPath, out IntPtr hkl, out uint pid)
    {
        processName = ScriptedProcessName;
        fullPath = ScriptedFullPath;
        hkl = CurrentHkl;
        pid = processName != null ? ScriptedPid : 0;
        return processName != null;
    }

    public bool TryEnumProcessModules(uint pid, out string[] moduleFileNames)
    {
        moduleFileNames = ScriptedModules ?? Array.Empty<string>();
        return ScriptedModules != null;
    }

    public IntPtr SetWinEventHook(uint eventMin, uint eventMax, Win32.WinEventDelegate cb)
    {
        if (ShouldFailSetWinEventHook) return IntPtr.Zero;
        CapturedWinEventDelegate = cb;
        return WinEventHookHandle;
    }

    public bool UnhookWinEvent(IntPtr hook)
    {
        UnhookWinEventCalled = true;
        return true;
    }
}

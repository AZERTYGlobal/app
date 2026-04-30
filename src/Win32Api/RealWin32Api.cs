// Implémentation prod de IWin32Api : délègue aux P/Invoke statiques de Win32.
using System.Runtime.InteropServices;
using System.Text;

namespace AZERTYGlobal;

internal sealed class RealWin32Api : IWin32Api
{
    public short VkKeyScanExW(char ch, IntPtr hkl) => Win32.VkKeyScanExW(ch, hkl);

    public uint MapVirtualKeyExW(uint code, uint mapType, IntPtr hkl) =>
        Win32.MapVirtualKeyExW(code, mapType, hkl);

    public short GetKeyState(int vk) => Win32.GetKeyState(vk);

    public IntPtr GetKeyboardLayout(uint threadId) => Win32.GetKeyboardLayout(threadId);

    public uint SendInput(Win32.INPUT[] inputs) =>
        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());

    public IntPtr GetForegroundWindow() => Win32.GetForegroundWindow();

    public bool TryGetForegroundProcess(out string? processName, out string? fullPath, out IntPtr hkl, out uint pid)
    {
        processName = null;
        fullPath = null;
        hkl = IntPtr.Zero;
        pid = 0;

        var hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        uint tid = Win32.GetWindowThreadProcessIdOut(hwnd, out pid);
        if (tid == 0 || pid == 0) return false;

        // Layout natif du thread foreground
        hkl = Win32.GetKeyboardLayout(tid);

        // Nom du process via OpenProcess + GetModuleFileNameExW
        IntPtr hProc = Win32.OpenProcess(
            Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.PROCESS_VM_READ, false, pid);
        if (hProc == IntPtr.Zero) return false;

        try
        {
            var sb = new StringBuilder(1024);
            uint len = Win32.GetModuleFileNameExW(hProc, IntPtr.Zero, sb, (uint)sb.Capacity);
            if (len == 0) return false;
            fullPath = sb.ToString();
            processName = System.IO.Path.GetFileName(fullPath);
            return true;
        }
        finally
        {
            Win32.CloseHandle(hProc);
        }
    }

    public bool TryEnumProcessModules(uint pid, out string[] moduleFileNames)
    {
        moduleFileNames = Array.Empty<string>();

        IntPtr hProc = Win32.OpenProcess(
            Win32.PROCESS_QUERY_LIMITED_INFORMATION | Win32.PROCESS_VM_READ, false, pid);
        if (hProc == IntPtr.Zero) return false;

        try
        {
            // Premier appel pour connaître la taille requise
            var modules = new IntPtr[1024];
            uint cb = (uint)(modules.Length * IntPtr.Size);
            if (!Win32.EnumProcessModulesEx(hProc, modules, cb, out uint needed, Win32.LIST_MODULES_ALL))
                return false;

            int count = (int)Math.Min(needed / IntPtr.Size, (uint)modules.Length);
            var names = new List<string>(count);
            var sb = new StringBuilder(1024);
            for (int i = 0; i < count; i++)
            {
                sb.Clear();
                uint len = Win32.GetModuleFileNameExW(hProc, modules[i], sb, (uint)sb.Capacity);
                if (len > 0)
                    names.Add(System.IO.Path.GetFileName(sb.ToString()));
            }
            moduleFileNames = names.ToArray();
            return true;
        }
        finally
        {
            Win32.CloseHandle(hProc);
        }
    }

    public IntPtr SetWinEventHook(uint eventMin, uint eventMax, Win32.WinEventDelegate cb) =>
        Win32.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, cb, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

    public bool UnhookWinEvent(IntPtr hook) => Win32.UnhookWinEvent(hook);
}

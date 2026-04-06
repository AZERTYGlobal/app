// Low-level keyboard hook — intercepte toutes les frappes sans droits admin
using System.Runtime.InteropServices;

namespace AZERTYGlobal;

/// <summary>
/// Hook clavier bas niveau (WH_KEYBOARD_LL).
/// Intercepte les frappes au niveau système et les redirige vers le KeyMapper.
/// </summary>
sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;

    // Flag pour identifier nos propres injections (éviter les boucles infinies)
    internal static readonly IntPtr INJECTED_FLAG = (IntPtr)0xA261;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly Win32.LowLevelKeyboardProc _proc;
    private readonly KeyMapper _mapper;
    private bool _enabled = true;

    // VK codes des raccourcis configurables (lus depuis config.json)
    private uint _vkSearch;
    private uint _vkVirtualKeyboard;

    // Détection changement de layout via Ctrl+Shift
    private bool _nonModifierPressed;

    /// <summary>Événement déclenché pour chaque keydown (scancode) — pour animation clavier virtuel.</summary>
    public event Action<uint>? RawKeyDown;

    /// <summary>Événement déclenché quand Ctrl+Maj+W est pressé (ouvrir recherche de caractère).</summary>
    public event Action? SearchRequested;

    /// <summary>Événement déclenché quand Ctrl+Maj+Q est pressé (ouvrir/fermer clavier virtuel).</summary>
    public event Action? VirtualKeyboardRequested;

    /// <summary>Événement déclenché quand Ctrl+Shift est relâché sans 3e touche (possible changement de layout système).</summary>
    public event Action? LayoutMayHaveChanged;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public KeyboardHook(KeyMapper mapper)
    {
        _mapper = mapper;
        _proc = HookCallback;
        ReloadShortcuts();
    }

    /// <summary>Recharge les raccourcis depuis la configuration.</summary>
    public void ReloadShortcuts()
    {
        _vkSearch = ConfigManager.ShortcutCharacterSearchVk;
        _vkVirtualKeyboard = ConfigManager.ShortcutVirtualKeyboardVk;
        // Sécurité : si les deux raccourcis sont identiques, désactiver le second
        if (_vkSearch != 0 && _vkSearch == _vkVirtualKeyboard)
            _vkVirtualKeyboard = 0;
        // Sécurité : ne pas entrer en conflit avec Verr.Maj (toggle on/off = 0x14)
        if (_vkSearch == 0x14) _vkSearch = 0;
        if (_vkVirtualKeyboard == 0x14) _vkVirtualKeyboard = 0;
    }

    public void Install()
    {
        // GetModuleHandle(null) retourne le handle du module principal — suffisant pour WH_KEYBOARD_LL
        // et compatible avec PublishTrimmed (pas de dépendance à System.Diagnostics.Process)
        _hookId = Win32.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, Win32.GetModuleHandleW(null), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("Impossible d'installer le hook clavier.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);

            // Ne pas traiter nos propres injections
            if (hookStruct.dwExtraInfo == INJECTED_FLAG)
                return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool isKeyDown = msg == (int)Win32.WM_KEYDOWN || msg == (int)Win32.WM_SYSKEYDOWN;
            bool isKeyUp = msg == (int)Win32.WM_KEYUP || msg == (int)Win32.WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                // ── Détection changement de layout via Ctrl+Shift ──
                // Windows change le layout du foreground thread quand Ctrl+Shift
                // est pressé et relâché SANS qu'une 3e touche ait été pressée.
                bool wasCtrlShift = _mapper.IsToggleShortcut();

                // Toujours tracker les modificateurs (même quand désactivé)
                // pour que Ctrl+Shift+CapsLock fonctionne dans tous les cas
                _mapper.TrackModifiers(hookStruct.vkCode, hookStruct.scanCode, hookStruct.flags, isKeyDown);

                bool isCtrlShift = _mapper.IsToggleShortcut();

                // Ctrl+Shift vient de s'activer → reset du flag
                if (!wasCtrlShift && isCtrlShift)
                    _nonModifierPressed = false;

                // Touche non-modificateur pressée pendant Ctrl+Shift → c'était un raccourci
                if (isKeyDown && isCtrlShift && !IsModifierKey(hookStruct.vkCode))
                    _nonModifierPressed = true;

                // Ctrl+Shift relâché sans touche intermédiaire → possible changement de layout
                if (wasCtrlShift && !isCtrlShift && !_nonModifierPressed)
                    LayoutMayHaveChanged?.Invoke();

                // Détecter Ctrl+Shift+CapsLock même quand désactivé
                if (hookStruct.vkCode == 0x14 && isKeyDown && _mapper.IsToggleShortcut())
                {
                    _mapper.HandleToggleShortcut();
                    return (IntPtr)1; // Bloquer le CapsLock — seul le toggle on/off est voulu
                }

                // Détecter Ctrl+Shift+<touche> → ouvrir/fermer la recherche de caractère
                if (_vkSearch != 0 && hookStruct.vkCode == _vkSearch && isKeyDown && _mapper.IsToggleShortcut())
                {
                    SearchRequested?.Invoke();
                    return (IntPtr)1;
                }

                // Détecter Ctrl+Shift+<touche> → ouvrir/fermer le clavier virtuel
                if (_vkVirtualKeyboard != 0 && hookStruct.vkCode == _vkVirtualKeyboard && isKeyDown && _mapper.IsToggleShortcut())
                {
                    VirtualKeyboardRequested?.Invoke();
                    return (IntPtr)1;
                }

                // Notifier le keydown pour animation du clavier virtuel
                if (isKeyDown)
                    RawKeyDown?.Invoke(hookStruct.scanCode);

                if (_enabled)
                {
                    bool handled = _mapper.ProcessKey(
                        hookStruct.vkCode,
                        hookStruct.scanCode,
                        hookStruct.flags,
                        isKeyDown);

                    if (handled)
                        return (IntPtr)1; // Bloquer la touche originale
                }
            }
        }

        return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(uint vkCode) => vkCode is
        0xA0 or 0xA1 or 0x10 or  // LShift, RShift, Shift
        0xA2 or 0xA3 or 0x11 or  // LCtrl, RCtrl, Ctrl
        0xA4 or 0xA5 or 0x12 or  // LAlt, RAlt, Alt
        0x5B or 0x5C;             // LWin, RWin

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}

// Low-level keyboard hook — intercepte toutes les frappes sans droits admin
using System.Runtime.InteropServices;

namespace AZERTYGlobalPortable;

/// <summary>
/// Hook clavier bas niveau (WH_KEYBOARD_LL).
/// Intercepte les frappes au niveau système et les redirige vers le KeyMapper.
/// </summary>
sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // Flag pour identifier nos propres injections (éviter les boucles infinies)
    public static readonly IntPtr INJECTED_FLAG = (IntPtr)0xA261;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly Win32.LowLevelKeyboardProc _proc;
    private readonly KeyMapper _mapper;
    private bool _enabled = true;

    // VK codes des raccourcis configurables (lus depuis config.json)
    private uint _vkSearch;
    private uint _vkVirtualKeyboard;

    /// <summary>Événement déclenché pour chaque keydown (scancode) — pour animation clavier virtuel.</summary>
    public event Action<uint>? RawKeyDown;

    /// <summary>Événement déclenché quand Ctrl+Maj+W est pressé (ouvrir recherche de caractère).</summary>
    public event Action? SearchRequested;

    /// <summary>Événement déclenché quand Ctrl+Maj+Q est pressé (ouvrir/fermer clavier virtuel).</summary>
    public event Action? VirtualKeyboardRequested;

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
        _vkSearch = ConfigManager.GetVkCode(ConfigManager.ShortcutCharacterSearch);
        _vkVirtualKeyboard = ConfigManager.GetVkCode(ConfigManager.ShortcutVirtualKeyboard);
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
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                // Toujours tracker les modificateurs (même quand désactivé)
                // pour que Ctrl+Shift+CapsLock fonctionne dans tous les cas
                _mapper.TrackModifiers(hookStruct.vkCode, hookStruct.scanCode, hookStruct.flags, isKeyDown);

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

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}

// Moteur de remapping — traduit les scancodes en caractères AZERTY Global
using System.Runtime.InteropServices;

namespace AZERTYGlobalPortable;

/// <summary>
/// Gère la logique de remapping clavier.
/// Reçoit les événements du hook et émet les caractères AZERTY Global.
/// </summary>
sealed class KeyMapper
{
    // Virtual key codes
    private const uint VK_SHIFT = 0x10;
    private const uint VK_CONTROL = 0x11;
    private const uint VK_MENU = 0x12;       // Alt
    private const uint VK_LSHIFT = 0xA0;
    private const uint VK_RSHIFT = 0xA1;
    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_LMENU = 0xA4;      // Left Alt
    private const uint VK_RMENU = 0xA5;      // Right Alt (AltGr)
    private const uint VK_CAPITAL = 0x14;     // Caps Lock
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;

    // Flags dans KBDLLHOOKSTRUCT.flags
    private const uint LLKHF_EXTENDED = 0x01;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private readonly Layout _layout;
    private string? _activeDeadKey;
    private bool _capsLockState;

    // État des modificateurs (tracké manuellement pour fiabilité)
    private bool _leftShiftDown;
    private bool _rightShiftDown;
    private bool _leftCtrlDown;
    private bool _rightCtrlDown;
    private bool _leftAltDown;
    private bool _rightAltDown; // AltGr

    // Touches passées en pass-through (compatibilité jeux) — ne pas bloquer leur keyup
    private readonly HashSet<uint> _passedThroughKeys = new();

    public bool CapsLockActive => _capsLockState;
    public string? ActiveDeadKey => _activeDeadKey;
    public bool ShiftDown => IsShiftDown;
    public bool AltGrDown => IsAltGrDown;
    public bool CtrlDown => IsCtrlDown;
    public bool AltDown => _leftAltDown;

    /// <summary>Événement déclenché quand l'état change (pour rafraîchir l'UI).</summary>
    public event Action? StateChanged;

    /// <summary>Événement déclenché quand Ctrl+Shift+CapsLock est pressé (toggle on/off).</summary>
    public event Action? ToggleRequested;

    public KeyMapper(Layout layout)
    {
        _layout = layout;
        // Lire l'état initial du Caps Lock
        _capsLockState = (Win32.GetKeyState(0x14) & 0x0001) != 0;
        // Vider le buffer de touche morte du layout Windows sous-jacent
        // (l'utilisateur a pu activer une DK système avant le démarrage du hook)
        FlushSystemDeadKey();
    }

    /// <summary>
    /// Resynchronise l'état interne avec l'état réel du système.
    /// À appeler quand le remapping est réactivé (l'utilisateur a pu
    /// changer le CapsLock pendant que le hook était désactivé).
    /// </summary>
    public void SyncState()
    {
        bool actualCaps = (Win32.GetKeyState(0x14) & 0x0001) != 0;
        bool changed = _capsLockState != actualCaps;
        _capsLockState = actualCaps;

        // Réinitialiser une éventuelle touche morte en attente
        if (_activeDeadKey != null)
        {
            _activeDeadKey = null;
            changed = true;
        }

        // Vider le buffer de touche morte du layout Windows sous-jacent
        // (l'utilisateur a pu activer une DK système pendant que le remapping était désactivé)
        FlushSystemDeadKey();

        if (changed)
            StateChanged?.Invoke();
    }

    /// <summary>
    /// Consomme toute touche morte en attente dans le buffer du layout Windows.
    /// Appelle ToUnicode deux fois : la première détecte la DK, la seconde la consomme.
    /// Nécessaire pour compenser les DK système (^, ¨, `, ~) de l'AZERTY trad sous-jacent
    /// qui peuvent rester en attente si le hook LL a subi un timeout (>300ms)
    /// ou si l'utilisateur a activé une DK pendant que le remapping était désactivé.
    /// </summary>
    private void FlushSystemDeadKey()
    {
        var buf = new System.Text.StringBuilder(8);
        var keyState = new byte[256];
        int result = Win32.ToUnicode(0x20, 0x39, keyState, buf, buf.Capacity, 0);
        if (result < 0)
        {
            // -1 = touche morte détectée, appeler une seconde fois pour la consommer
            Win32.ToUnicode(0x20, 0x39, keyState, buf, buf.Capacity, 0);
        }
    }

    /// <summary>
    /// Vérifie si le VK/scancode correspond à une touche morte sur le layout Windows
    /// sous-jacent et consomme l'état DK si c'est le cas.
    /// Appelé avant chaque émission de caractère pour éviter que le buffer DK système
    /// ne s'accumule (ex: si ^ sur AZERTY trad est traité par Windows malgré le hook).
    /// </summary>
    private void CompensateSystemDeadKey(uint vkCode, uint scanCode)
    {
        var buf = new System.Text.StringBuilder(8);
        var keyState = new byte[256];
        // Refléter les modificateurs actuels pour détecter correctement les DK
        // (ex: ¨ = Shift+^ sur AZERTY trad)
        if (IsShiftDown) { keyState[0x10] = 0x80; keyState[0xA0] = 0x80; }
        if (IsAltGrDown) { keyState[0xA5] = 0x80; keyState[0xA2] = 0x80; }

        int result = Win32.ToUnicode(vkCode, scanCode, keyState, buf, buf.Capacity, 0);
        if (result < 0)
        {
            // C'est une touche morte système — appeler une 2e fois pour consommer
            Win32.ToUnicode(vkCode, scanCode, keyState, buf, buf.Capacity, 0);
        }
        // Si result >= 0 et qu'il y avait une DK en attente d'un appel précédent,
        // ToUnicode l'a consommée en combinant — c'est le comportement voulu.
    }

    private bool IsShiftDown => _leftShiftDown || _rightShiftDown;
    // AltGr = Right Alt (envoie aussi un phantom LCtrl)
    // ou Right Ctrl + Left Alt (convention Windows : Ctrl+Alt = AltGr)
    private bool IsAltGrDown => _rightAltDown || (_rightCtrlDown && _leftAltDown);
    private bool IsCtrlDown => _leftCtrlDown || _rightCtrlDown;

    /// <summary>
    /// Met à jour l'état des modificateurs sans traiter la touche.
    /// Appelé par le hook même quand le remapping est désactivé.
    /// </summary>
    public void TrackModifiers(uint vkCode, uint scanCode, uint flags, bool isKeyDown)
    {
        bool oldShift = IsShiftDown;
        bool oldAltGr = IsAltGrDown;
        bool oldCtrl = IsCtrlDown;
        bool oldAlt = _leftAltDown;

        bool isExtended = (flags & LLKHF_EXTENDED) != 0;
        switch (vkCode)
        {
            case VK_LSHIFT: _leftShiftDown = isKeyDown; break;
            case VK_RSHIFT: _rightShiftDown = isKeyDown; break;
            case VK_LCONTROL: if (!isExtended) _leftCtrlDown = isKeyDown; break;
            case VK_RCONTROL: _rightCtrlDown = isKeyDown; break;
            case VK_LMENU: _leftAltDown = isKeyDown; break;
            case VK_RMENU: _rightAltDown = isKeyDown; break;
            case VK_SHIFT:
                if (scanCode == 0x2A) _leftShiftDown = isKeyDown;
                else if (scanCode == 0x36) _rightShiftDown = isKeyDown;
                break;
            case VK_CONTROL:
                if (isExtended) _rightCtrlDown = isKeyDown;
                else _leftCtrlDown = isKeyDown;
                break;
            case VK_MENU:
                if (isExtended) _rightAltDown = isKeyDown;
                else _leftAltDown = isKeyDown;
                break;
        }

        // Notifier si l'état d'un modificateur a changé (pour le clavier virtuel)
        if (IsShiftDown != oldShift || IsAltGrDown != oldAltGr ||
            IsCtrlDown != oldCtrl || _leftAltDown != oldAlt)
            StateChanged?.Invoke();
    }

    /// <summary>Vérifie si Ctrl+Shift sont enfoncés (pour le raccourci toggle).</summary>
    public bool IsToggleShortcut() => IsShiftDown && (_leftCtrlDown || _rightCtrlDown);

    /// <summary>Gère le raccourci Ctrl+Shift+CapsLock (toggle on/off du remapping).</summary>
    public void HandleToggleShortcut()
    {
        // Ne PAS toucher à _capsLockState — ce raccourci active/désactive le remapping,
        // pas le Caps Lock. Le CapsLock est bloqué dans le hook.
        ToggleRequested?.Invoke();
    }

    /// <summary>
    /// Resynchronise les modificateurs avec l'état physique réel des touches.
    /// Corrige les modificateurs "collés" quand un keyup est manqué pendant
    /// le traitement d'une séquence SendInput.
    /// </summary>
    private void CleanupStaleModifiers()
    {
        bool changed = false;

        // GetAsyncKeyState : bit 15 = touche physiquement enfoncée maintenant
        if (_leftShiftDown && (Win32.GetAsyncKeyState((int)VK_LSHIFT) & 0x8000) == 0)
        { _leftShiftDown = false; changed = true; }
        if (_rightShiftDown && (Win32.GetAsyncKeyState((int)VK_RSHIFT) & 0x8000) == 0)
        { _rightShiftDown = false; changed = true; }
        if (_leftCtrlDown && (Win32.GetAsyncKeyState((int)VK_LCONTROL) & 0x8000) == 0)
        { _leftCtrlDown = false; changed = true; }
        if (_rightCtrlDown && (Win32.GetAsyncKeyState((int)VK_RCONTROL) & 0x8000) == 0)
        { _rightCtrlDown = false; changed = true; }
        if (_leftAltDown && (Win32.GetAsyncKeyState((int)VK_LMENU) & 0x8000) == 0)
        { _leftAltDown = false; changed = true; }
        if (_rightAltDown && (Win32.GetAsyncKeyState((int)VK_RMENU) & 0x8000) == 0)
        { _rightAltDown = false; changed = true; }

        if (changed)
            StateChanged?.Invoke();
    }

    /// <summary>
    /// Traite un événement clavier. Retourne true si la touche a été gérée
    /// (et doit être bloquée), false sinon (laisser passer).
    /// </summary>
    public bool ProcessKey(uint vkCode, uint scanCode, uint flags, bool isKeyDown)
    {
        bool isExtended = (flags & LLKHF_EXTENDED) != 0;

        // Les modificateurs sont déjà trackés par TrackModifiers() en amont dans le hook.
        // Ici on les laisse simplement passer sans les bloquer.
        switch (vkCode)
        {
            case VK_LSHIFT: case VK_RSHIFT: case VK_SHIFT:
            case VK_LCONTROL: case VK_RCONTROL: case VK_CONTROL:
            case VK_LMENU: case VK_RMENU: case VK_MENU:
            case VK_LWIN: case VK_RWIN:
                return false;
        }

        // Resynchroniser les modificateurs uniquement pour les touches non-modificateur.
        // Ne PAS appeler pendant un événement modificateur : le hook fire AVANT que
        // GetAsyncKeyState ne reflète le nouvel état, ce qui effacerait le modificateur
        // que TrackModifiers vient de positionner.
        CleanupStaleModifiers();

        // Caps Lock : tracker l'état interne
        // Note : Ctrl+Shift+CapsLock (toggle on/off) est géré en amont dans le hook
        // et n'atteint jamais ProcessKey.
        if (vkCode == VK_CAPITAL)
        {
            if (isKeyDown)
            {
                _capsLockState = !_capsLockState;
                StateChanged?.Invoke();
            }
            return false; // Laisser Windows gérer la LED
        }

        // Backspace : annuler la touche morte active et laisser passer
        if (vkCode == 0x08 && isKeyDown && _activeDeadKey != null)
        {
            _activeDeadKey = null;
            StateChanged?.Invoke();
            return false; // Laisser Windows traiter le Backspace normalement
        }

        // Touches étendues (pavé numérique /, Enter, flèches, etc.) : laisser passer
        if (isExtended)
            return false;

        // Si Alt gauche est enfoncé SANS Right Ctrl → raccourcis système (Alt+Tab, Alt+F4, etc.)
        // Note : Right Ctrl + Left Alt = AltGr simulé → ne PAS laisser passer
        if (_leftAltDown && !_rightCtrlDown)
            return false;

        // Si Ctrl (gauche OU droit) est enfoncé SANS AltGr → remapper les raccourcis Ctrl+touche
        // Ex: Ctrl+A doit fonctionner selon la position AZERTY Global, pas le layout Windows
        // Exclut : AltGr réel (RAlt + phantom LCtrl) et AltGr simulé (RCtrl + LAlt)
        if (IsCtrlDown && !IsAltGrDown)
        {
            if (!_layout.Keys.TryGetValue(scanCode, out var ctrlKeyDef))
                return false;

            // Obtenir le caractère de base (sans modificateurs) pour trouver le VK
            var baseChar = ctrlKeyDef.Base;
            if (baseChar == null || baseChar.Length == 0 || baseChar.StartsWith("dk_"))
                return false;

            // Lettre A-Z → VK 0x41-0x5A
            char c = char.ToUpper(baseChar[0]);
            if (c >= 'A' && c <= 'Z')
            {
                SendVirtualKey((ushort)c, isKeyDown);
                return true;
            }

            // Chiffre 0-9 → VK 0x30-0x39
            if (c >= '0' && c <= '9')
            {
                SendVirtualKey((ushort)c, isKeyDown);
                return true;
            }

            // En AZERTY, les chiffres sont souvent en couche Shift — vérifier aussi
            var shiftChar = ctrlKeyDef.Shift;
            if (shiftChar != null && shiftChar.Length > 0 && !shiftChar.StartsWith("dk_"))
            {
                char sc = shiftChar[0];
                if (sc >= '0' && sc <= '9')
                {
                    SendVirtualKey((ushort)sc, isKeyDown);
                    return true;
                }
            }

            // Autres caractères : utiliser VkKeyScanW pour trouver le VK
            // dans le layout Windows actif (fonctionne pour -, =, [, ], etc.)
            short vkResult = Win32.VkKeyScanW(baseChar[0]);
            if (vkResult != -1)
            {
                byte vk = (byte)(vkResult & 0xFF);
                byte mods = (byte)((vkResult >> 8) & 0xFF);
                // N'utiliser que si aucun modificateur supplémentaire n'est requis
                // (sinon le VK correspondrait à une autre touche sur le layout Windows)
                if (vk != 0 && mods == 0)
                {
                    SendVirtualKey(vk, isKeyDown);
                    return true;
                }
            }

            // Pas de VK trouvé — laisser passer l'original
            return false;
        }

        // Ne traiter que les keydown pour l'émission de caractères.
        // Bloquer les keyup des touches remappées, mais laisser passer celles en pass-through.
        if (!isKeyDown)
        {
            if (_passedThroughKeys.Remove(scanCode))
                return false; // Laisser passer le keyup (touche identique au layout natif)
            return _layout.Keys.ContainsKey(scanCode);
        }

        // Chercher la touche dans le layout
        if (!_layout.Keys.TryGetValue(scanCode, out var keyDef))
            return false;

        // Compensation DK système : si cette touche est une touche morte sur le layout
        // Windows sous-jacent (ex: ^ ou ¨ sur AZERTY trad), consommer l'état DK avant
        // de traiter notre propre logique. Cela évite qu'un état résiduel (dû à un
        // timeout du hook LL ou à une désactivation temporaire) ne corrompe la suite.
        CompensateSystemDeadKey(vkCode, scanCode);

        // Obtenir le caractère selon les modificateurs
        string? output = keyDef.GetOutput(IsShiftDown, IsAltGrDown, _capsLockState);

        if (output == null || output == "")
            return true; // Bloquer quand même pour éviter que le layout Windows sous-jacent ne produise un caractère

        // Touche morte ?
        if (output.StartsWith("dk_"))
        {
            if (_activeDeadKey != null)
            {
                // Une touche morte est déjà active : résoudre comme touche morte + caractère isolé
                var activeDk = _layout.DeadKeys.GetValueOrDefault(_activeDeadKey);
                var isolated = activeDk?.GetIsolated();
                _activeDeadKey = null;

                if (isolated != null)
                {
                    // Chercher dans la table de la NOUVELLE touche morte
                    var newDk = _layout.DeadKeys.GetValueOrDefault(output);
                    var transformed = newDk?.Apply(isolated);
                    if (transformed != null)
                    {
                        SendUnicodeString(transformed);
                        StateChanged?.Invoke();
                        return true;
                    }
                    // Pas de correspondance : envoyer l'isolé de la première, activer la nouvelle
                    SendUnicodeString(isolated);
                }
            }
            _activeDeadKey = output;
            StateChanged?.Invoke();

            return true;
        }

        // Si une touche morte est active, appliquer la transformation
        if (_activeDeadKey != null)
        {
            var dk = _layout.DeadKeys.GetValueOrDefault(_activeDeadKey);
            _activeDeadKey = null;
            StateChanged?.Invoke();

            if (dk != null)
            {
                var transformed = dk.Apply(output);
                if (transformed != null)
                {
                    SendUnicodeString(transformed);

                    return true;
                }

                // Pas de correspondance : envoyer le diacritique isolé + le caractère
                var isolatedChar = dk.GetIsolated();
                if (isolatedChar != null)
                    SendUnicodeString(isolatedChar);
                SendUnicodeString(output);

                return true;
            }
        }

        // Caractère normal — si le layout Windows natif produit le même caractère,
        // laisser passer la touche originale (compatibilité jeux/DirectInput)
        if (CanPassThrough(scanCode, output))
        {
            _passedThroughKeys.Add(scanCode);
            return false;
        }

        SendUnicodeString(output);
        return true;
    }

    /// <summary>
    /// Vérifie si le layout Windows natif produit le même caractère pour ce scancode.
    /// Si oui, on peut laisser passer la touche originale (compatibilité jeux).
    /// </summary>
    private bool CanPassThrough(uint scanCode, string output)
    {
        // Seulement pour les caractères simples, pas AltGr (qui produit des chars spéciaux)
        if (output.Length != 1 || IsAltGrDown) return false;

        // VK correspondant à ce scancode sur le layout Windows
        uint vk = Win32.MapVirtualKeyW(scanCode, 1); // MAPVK_VSC_TO_VK
        if (vk == 0) return false;

        // VK+mods qui produisent notre caractère sur le layout Windows
        short vkForChar = Win32.VkKeyScanW(output[0]);
        if (vkForChar == -1) return false;

        byte charVk = (byte)(vkForChar & 0xFF);
        byte charMods = (byte)((vkForChar >> 8) & 0xFF);

        // Même VK et mêmes modificateurs → le layout Windows produit le même caractère
        bool needsShift = (charMods & 1) != 0;
        bool needsCtrl = (charMods & 2) != 0;
        bool needsAlt = (charMods & 4) != 0;

        return charVk == (byte)vk && needsShift == IsShiftDown && !needsCtrl && !needsAlt;
    }

    /// <summary>
    /// Envoie un virtual key (pour les raccourcis Ctrl+lettre).
    /// Le Ctrl est déjà enfoncé par l'utilisateur, on envoie juste la lettre remappée.
    /// </summary>
    private void SendVirtualKey(ushort vk, bool keyDown)
    {
        var input = new Win32.INPUT
        {
            type = INPUT_KEYBOARD,
            u = new Win32.INPUTUNION
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyDown ? 0u : KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = KeyboardHook.INJECTED_FLAG
                }
            }
        };
        Win32.SendInput(1, new[] { input }, Marshal.SizeOf<Win32.INPUT>());
    }

    /// <summary>
    /// Envoie une chaîne de caractères Unicode via SendInput.
    /// </summary>
    private void SendUnicodeString(string text)
    {
        var inputs = new List<Win32.INPUT>();

        foreach (char c in text)
        {
            // Key down
            inputs.Add(new Win32.INPUT
            {
                type = INPUT_KEYBOARD,
                u = new Win32.INPUTUNION
                {
                    ki = new Win32.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = KeyboardHook.INJECTED_FLAG
                    }
                }
            });

            // Key up
            inputs.Add(new Win32.INPUT
            {
                type = INPUT_KEYBOARD,
                u = new Win32.INPUTUNION
                {
                    ki = new Win32.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = KeyboardHook.INJECTED_FLAG
                    }
                }
            });
        }

        var inputArray = inputs.ToArray();
        Win32.SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf<Win32.INPUT>());
    }
}

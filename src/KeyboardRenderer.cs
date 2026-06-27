namespace AZERTYGlobal;

internal enum KeyboardRenderProfile
{
    Full,
    Onboarding,
    Lesson
}

internal sealed class KeyboardRenderState
{
    public bool Shift { get; init; }
    public bool AltGr { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool CapsLock { get; init; }
    public string? ActiveDeadKey { get; init; }
    public uint PressedScancode { get; init; }
    public HashSet<uint> HighlightedScancodes { get; } = new();
    public HashSet<string> HighlightedContextIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> HighlightedLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LessonVisibleCharacters { get; } = new(StringComparer.Ordinal);
    public string? HintCharacter { get; init; }
    public bool ShowInvisibleMarkers { get; init; } = true;
}

internal readonly record struct KeyboardHitTestResult(uint Scancode, string Label, Win32.RECT Rect);

internal static class KeyboardRenderer
{
    private const uint CLR_KEY = 0x003A3A3A;
    private const uint CLR_KEY_CONTEXT = 0x002D2D2D;
    private const uint CLR_KEY_BORDER = 0x00555555;
    private const uint CLR_KEY_PRESSED = 0x006A4A2A;
    private const uint CLR_KEY_DISABLED = 0x002A2A2A;
    private const uint CLR_MOD_ACTIVE = 0x009A5A1A;
    private const uint CLR_KEY_HIGHLIGHT_BORDER = 0x0064C800;
    private const uint CLR_CTX_TEXT = 0x00E0E0E0;
    private const uint CLR_KEY_LABEL = 0x0080D0F0;
    private const uint CLR_CHAR_ACTIVE_BLUE = 0x00FFB366;
    private const uint CLR_CHAR_DIM = 0x00999999;
    private const uint CLR_DK_CHAR = 0x006666FF;
    private const uint CLR_DK_ACTIVE_TEXT = 0x000080FF;
    private const uint CLR_CAPS_BAR = 0x0000A5FF;

    private static readonly HashSet<uint> LetterKeyScancodes = new()
    {
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,
        0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
        0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31
    };

    private static readonly HashSet<uint> AccentedNumericScancodes = new()
    {
        0x03, 0x08, 0x0A, 0x0B
    };

    private static readonly HashSet<string> HiddenDeadKeysInOnboarding = new(StringComparer.Ordinal)
    {
        "dk_misc_symbols",
        "dk_dot_above",
        "dk_dot_below",
        "dk_double_acute",
        "dk_double_grave",
        "dk_horn",
        "dk_hook",
        "dk_breve",
        "dk_inverted_breve",
        "dk_stroke",
        "dk_horizontal_stroke",
        "dk_macron",
        "dk_extended_latin",
        "dk_cedilla",
        "dk_comma",
        "dk_phonetic",
        "dk_ring_above",
        "dk_scientific",
        "dk_caron",
        "dk_ogonek",
        "dk_cyrillic",
    };

    private static readonly HashSet<string> DkAlwaysWithDottedCircle = new(StringComparer.Ordinal)
    {
        "dk_dot_above",
        "dk_double_acute",
        "dk_breve",
        "dk_stroke",
        "dk_horizontal_stroke",
        "dk_caron",
        "dk_ogonek",
    };

    private static readonly HashSet<(uint Scancode, int Layer)> HiddenSlotsInOnboarding = new()
    {
        (0x56, 2), (0x56, 3),
        (0x17, 2),
        (0x26, 2),
        (0x32, 2), (0x32, 3),
        (0x33, 2),
        (0x34, 2),
        (0x35, 2),
        (0x05, 2), (0x05, 3),
        (0x07, 3),
        (0x0B, 2),
        (0x2C, 3),
        (0x2D, 3),
    };

    private static readonly Dictionary<string, string> CharNamesOverride = new(StringComparer.Ordinal)
    {
        ["’"] = "APOSTROPHE TYPOGRAPHIQUE",
    };

    private static readonly Lazy<Dictionary<string, string>> CharacterNames = new(LoadCharacterNames);

    public static IReadOnlyList<VirtualKeyboard.VisualKey> VisualKeys => VirtualKeyboard.BuildKeyLayout();

    public static bool IsSlotVisible(
        KeyboardRenderProfile profile,
        uint scancode,
        int layer,
        string? value,
        IReadOnlySet<string>? lessonVisibleCharacters = null,
        string? hintCharacter = null)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (profile == KeyboardRenderProfile.Full) return true;

        if (profile == KeyboardRenderProfile.Onboarding)
            return IsOnboardingSlotVisible(scancode, layer, value);

        if (IsOnboardingSlotVisible(scancode, layer, value)) return true;
        if (StringComparer.Ordinal.Equals(value, hintCharacter)) return true;
        if (lessonVisibleCharacters?.Contains(value) == true) return true;
        if (value.StartsWith("dk_", StringComparison.Ordinal) &&
            lessonVisibleCharacters?.Contains("dk:" + value[3..]) == true)
            return true;

        return false;
    }

    public static Win32.RECT Draw(
        IntPtr hdc,
        Win32.RECT bounds,
        Layout layout,
        KeyboardRenderProfile profile,
        KeyboardRenderState state,
        IntPtr hFontMain,
        IntPtr hFontSmall,
        IntPtr hFontTiny,
        IntPtr hFontContext)
    {
        var visualKeys = VisualKeys;
        float maxRight = visualKeys.Max(k => k.X + k.W);
        float maxBottom = visualKeys.Max(k => k.Y + k.H);
        int width = Math.Max(1, bounds.right - bounds.left);
        int height = Math.Max(1, bounds.bottom - bounds.top);
        int statusHeight = state.ActiveDeadKey != null ? Math.Clamp(height / 13, 18, 28) : 0;
        float scale = Math.Min(width / maxRight, height / maxBottom);
        int keyboardW = (int)(maxRight * scale);
        int keyboardH = (int)(maxBottom * scale);
        int originX = bounds.left + (width - keyboardW) / 2;
        int originY = bounds.top + (height - keyboardH) / 2;

        Win32.SetBkMode(hdc, Win32.TRANSPARENT);
        foreach (var key in visualKeys)
        {
            var rect = ToRect(key, originX, originY, scale);
            DrawKey(hdc, rect, key, layout, profile, state, hFontMain, hFontSmall, hFontTiny, hFontContext);
        }

        if (state.ActiveDeadKey != null)
            DrawActiveDeadKeyStatus(hdc, new Win32.RECT { left = originX, top = bounds.bottom - statusHeight, right = originX + keyboardW, bottom = bounds.bottom }, state.ActiveDeadKey, hFontTiny);

        return new Win32.RECT
        {
            left = originX,
            top = originY,
            right = originX + keyboardW,
            bottom = originY + keyboardH
        };
    }

    public static IEnumerable<KeyboardHitTestResult> BuildHitTestRects(Win32.RECT bounds)
    {
        var visualKeys = VisualKeys;
        float maxRight = visualKeys.Max(k => k.X + k.W);
        float maxBottom = visualKeys.Max(k => k.Y + k.H);
        int width = Math.Max(1, bounds.right - bounds.left);
        int height = Math.Max(1, bounds.bottom - bounds.top);
        float scale = Math.Min(width / maxRight, height / maxBottom);
        int keyboardW = (int)(maxRight * scale);
        int keyboardH = (int)(maxBottom * scale);
        int originX = bounds.left + (width - keyboardW) / 2;
        int originY = bounds.top + (height - keyboardH) / 2;

        foreach (var key in visualKeys)
            yield return new KeyboardHitTestResult(key.Scancode, key.Label, ToRect(key, originX, originY, scale));
    }

    public static string BuildTooltipText(
        Layout layout,
        KeyboardRenderProfile profile,
        KeyboardRenderState state,
        uint scancode,
        string contextLabel)
    {
        if (scancode == 0 || !layout.Keys.TryGetValue(scancode, out var keyDef))
            return GetContextTooltip(contextLabel);

        DeadKeyDefinition? activeDk = null;
        if (state.ActiveDeadKey != null)
            layout.DeadKeys.TryGetValue(state.ActiveDeadKey, out activeDk);

        var sb = new System.Text.StringBuilder();
        AppendTooltipLayer(sb, "Base", keyDef.Base, activeDk, state.ShowInvisibleMarkers);
        AppendTooltipLayer(sb, "Maj", keyDef.Shift, activeDk, state.ShowInvisibleMarkers);
        AppendTooltipLayer(sb, "AltGr", keyDef.AltGr, activeDk, state.ShowInvisibleMarkers);
        AppendTooltipLayer(sb, "Maj+AltGr", keyDef.ShiftAltGr, activeDk, state.ShowInvisibleMarkers);
        return sb.ToString().TrimEnd('\n');
    }

    private static string GetContextTooltip(string label)
    {
        return label switch
        {
            "Tab" => "Tabulation",
            "⌫" => "Retour arrière",
            "Verr. Maj." => "Verrouillage Majuscule (Caps Lock)",
            "Maj ⇧" => "Majuscule (Shift)",
            "Entrée" => "Entrée",
            "Ctrl" => "Contrôle (Ctrl)",
            "Win" => "Touche Windows",
            "Alt" => "Alt",
            "AltGr" => "Alt droite (AltGr)",
            "Menu" => "Menu contextuel",
            _ => label
        };
    }

    private static void AppendTooltipLayer(System.Text.StringBuilder sb, string label, string? value, DeadKeyDefinition? activeDk, bool showInvisibleMarkers)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (activeDk != null)
        {
            if (value.StartsWith("dk_", StringComparison.Ordinal))
                return;
            var combined = activeDk.Apply(value);
            if (combined != null)
            {
                string combinedDisplay = DisplayInvisible(combined, showInvisibleMarkers);
                sb.Append(label).Append(" : ").Append(combinedDisplay);
                AppendCharacterName(sb, combined, combinedDisplay);
                sb.Append('\n');
            }
            return;
        }

        string display = GetDisplayChar(value, showInvisibleMarkers) ?? value;
        sb.Append(label).Append(" : ").Append(display);
        if (value.StartsWith("dk_", StringComparison.Ordinal))
            sb.Append(" — touche morte ").Append(VirtualKeyboard.GetDeadKeyFrenchName(value));
        else
            AppendCharacterName(sb, value, display);
        sb.Append('\n');
    }

    private static void AppendCharacterName(System.Text.StringBuilder sb, string value, string display)
    {
        if (CharNamesOverride.TryGetValue(display, out var overrideName))
        {
            sb.Append(" — ").Append(overrideName.ToUpperInvariant());
            return;
        }

        if (CharacterNames.Value.TryGetValue(value, out var name) ||
            CharacterNames.Value.TryGetValue(display, out name))
        {
            sb.Append(" — ").Append(name.ToUpperInvariant());
        }
    }

    private static Dictionary<string, string> LoadCharacterNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            string json;
            using (var stream = typeof(KeyboardRenderer).Assembly.GetManifestResourceStream("character-index.json"))
            {
                if (stream == null)
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "character-index.json");
                    if (!File.Exists(path))
                        return names;
                    json = File.ReadAllText(path);
                }
                else
                {
                    using var reader = new StreamReader(stream);
                    json = reader.ReadToEnd();
                }
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var characters = doc.RootElement.GetProperty("characters");
            foreach (var entry in characters.EnumerateObject())
            {
                if (entry.Name.StartsWith("dk:", StringComparison.Ordinal))
                    continue;
                if (entry.Value.TryGetProperty("unicodeNameFr", out var nameFr))
                {
                    var name = nameFr.GetString();
                    if (!string.IsNullOrEmpty(name))
                        names[entry.Name] = name;
                }
            }
        }
        catch
        {
            // Tooltips remain usable without names if the resource is unavailable.
        }

        return names;
    }

    private static void DrawKey(
        IntPtr hdc,
        Win32.RECT rect,
        VirtualKeyboard.VisualKey key,
        Layout layout,
        KeyboardRenderProfile profile,
        KeyboardRenderState state,
        IntPtr hFontMain,
        IntPtr hFontSmall,
        IntPtr hFontTiny,
        IntPtr hFontContext)
    {
        bool highlighted = key.Scancode != 0 && state.HighlightedScancodes.Contains(key.Scancode);
        if (!highlighted && key.ContextId != null && state.HighlightedContextIds.Contains(key.ContextId))
            highlighted = true;
        if (!highlighted && state.HighlightedLabels.Contains(key.Label))
            highlighted = true;
        bool pressed = key.Scancode != 0 && key.Scancode == state.PressedScancode;
        bool modifierActive = key.Label switch
        {
            "Maj ⇧" => state.Shift,
            "AltGr" => state.AltGr,
            "Ctrl" => state.Ctrl,
            "Alt" => state.Alt,
            "Verr. Maj." => state.CapsLock,
            _ => false
        };
        bool disabledBackspace = profile == KeyboardRenderProfile.Onboarding && key.IsContextual && key.Scancode == 0x0E;
        bool isoEnter = key.Scancode == 0x1C && key.H > VirtualKeyboard.KEY_H;

        uint fill = key.IsContextual ? CLR_KEY_CONTEXT : CLR_KEY;
        uint border = CLR_KEY_BORDER;
        int borderWidth = 1;
        if (disabledBackspace)
            fill = CLR_KEY_DISABLED;
        if (highlighted)
        {
            border = CLR_KEY_HIGHLIGHT_BORDER;
            borderWidth = 2;
        }
        if (pressed)
            fill = CLR_KEY_PRESSED;
        else if (modifierActive)
            fill = CLR_MOD_ACTIVE;

        var brush = Win32.CreateSolidBrush(fill);
        var pen = Win32.CreatePen(0, borderWidth, border);
        var oldBrush = Win32.SelectObject(hdc, brush);
        var oldPen = Win32.SelectObject(hdc, pen);

        if (isoEnter)
            DrawIsoEnter(hdc, rect, key);
        else
            DrawRectKey(hdc, rect, brush);

        Win32.SelectObject(hdc, oldPen);
        Win32.SelectObject(hdc, oldBrush);
        Win32.DeleteObject(pen);
        Win32.DeleteObject(brush);

        if (key.Label == "Verr. Maj." && state.CapsLock)
        {
            int barH = Math.Max(2, (rect.bottom - rect.top) / 18);
            var barRect = new Win32.RECT { left = rect.left, top = rect.bottom - barH, right = rect.right, bottom = rect.bottom };
            var barBrush = Win32.CreateSolidBrush(CLR_CAPS_BAR);
            Win32.FillRect(hdc, ref barRect, barBrush);
            Win32.DeleteObject(barBrush);
        }

        if (key.IsContextual || key.Scancode == 0 || !layout.Keys.TryGetValue(key.Scancode, out var def))
        {
            DrawContextKeyLabel(hdc, rect, key, isoEnter, disabledBackspace, hFontContext);
            return;
        }

        var filtered = FilterKeyForProfile(def, key.Scancode, profile, state);
        DrawKeyCharacters(hdc, rect, key, filtered, layout, state, hFontMain, hFontSmall, hFontTiny);
    }

    private static void DrawRectKey(IntPtr hdc, Win32.RECT rect, IntPtr brush)
    {
        Win32.FillRect(hdc, ref rect, brush);
        Win32.MoveToEx(hdc, rect.left, rect.top, IntPtr.Zero);
        Win32.LineTo(hdc, rect.right, rect.top);
        Win32.LineTo(hdc, rect.right, rect.bottom);
        Win32.LineTo(hdc, rect.left, rect.bottom);
        Win32.LineTo(hdc, rect.left, rect.top);
    }

    private static void DrawIsoEnter(IntPtr hdc, Win32.RECT rect, VirtualKeyboard.VisualKey key)
    {
        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        int stepY = rect.top + (int)(height * (VirtualKeyboard.KEY_H / key.H));
        int bottomLeft = rect.right - (int)(width * (1.25f / key.W));
        var pts = new Win32.POINT[]
        {
            new() { x = rect.left, y = rect.top },
            new() { x = rect.right, y = rect.top },
            new() { x = rect.right, y = rect.bottom },
            new() { x = bottomLeft, y = rect.bottom },
            new() { x = bottomLeft, y = stepY },
            new() { x = rect.left, y = stepY },
        };
        Win32.Polygon(hdc, pts, pts.Length);
    }

    private static void DrawContextKeyLabel(
        IntPtr hdc,
        Win32.RECT rect,
        VirtualKeyboard.VisualKey key,
        bool isoEnter,
        bool disabled,
        IntPtr hFont)
    {
        var labelRect = rect;
        if (isoEnter)
            labelRect.left = rect.right - (int)((rect.right - rect.left) * (1.25f / key.W));

        Win32.SelectObject(hdc, hFont);
        Win32.SetTextColor(hdc, disabled ? 0x00606060u : CLR_CTX_TEXT);
        Win32.DrawTextW(hdc, key.Label, -1, ref labelRect,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_END_ELLIPSIS);
    }

    private static void DrawKeyCharacters(
        IntPtr hdc,
        Win32.RECT rect,
        VirtualKeyboard.VisualKey key,
        KeyDefinition keyDef,
        Layout layout,
        KeyboardRenderState state,
        IntPtr hFontMain,
        IntPtr hFontSmall,
        IntPtr hFontTiny)
    {
        int kx = rect.left;
        int ky = rect.top;
        int kw = rect.right - rect.left;
        int kh = rect.bottom - rect.top;
        int pad = Math.Clamp(kw / 10, 4, 14);

        if ((state.Ctrl && !state.AltGr) || (state.Alt && !state.AltGr))
            return;

        if (state.ActiveDeadKey != null)
        {
            DrawActiveDeadKeyCharacter(hdc, rect, key, keyDef, layout, state, hFontMain, hFontTiny);
            return;
        }

        if (AccentedNumericScancodes.Contains(key.Scancode))
            PaintAccentedNumericKey(hdc, kx, ky, kw, kh, keyDef, pad, state, hFontMain, hFontSmall);
        else if (LetterKeyScancodes.Contains(key.Scancode) && IsLetterChar(keyDef.Base))
            PaintLetterKey(hdc, kx, ky, kw, kh, keyDef, pad, state, hFontMain, hFontSmall);
        else
            PaintSymbolKey(hdc, kx, ky, kw, kh, keyDef, pad, state, hFontMain, hFontSmall, hFontTiny);
    }

    private static void DrawActiveDeadKeyCharacter(
        IntPtr hdc,
        Win32.RECT rect,
        VirtualKeyboard.VisualKey key,
        KeyDefinition keyDef,
        Layout layout,
        KeyboardRenderState state,
        IntPtr hFontMain,
        IntPtr hFontTiny)
    {
        int h = rect.bottom - rect.top;
        int labelH = Math.Clamp(h / 4, 10, 16);
        var charRect = new Win32.RECT { left = rect.left, top = rect.top, right = rect.right, bottom = rect.bottom - labelH - 2 };
        string? output = GetActiveDeadKeyOutput(keyDef, layout, state);
        string? display = FormatActiveDeadKeyDisplay(output, state.ShowInvisibleMarkers);

        if (!string.IsNullOrEmpty(display))
        {
            var oldFont = Win32.SelectObject(hdc, hFontMain);
            Win32.SetTextColor(hdc, CLR_CTX_TEXT);
            Win32.DrawTextW(hdc, display, display.Length, ref charRect,
                Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_NOCLIP | Win32.DT_END_ELLIPSIS);
            Win32.SelectObject(hdc, oldFont);
        }

        var labelRect = new Win32.RECT { left = rect.left + 2, top = rect.bottom - labelH - 2, right = rect.right - 2, bottom = rect.bottom - 1 };
        var oldLabelFont = Win32.SelectObject(hdc, hFontTiny);
        Win32.SetTextColor(hdc, CLR_KEY_LABEL);
        Win32.DrawTextW(hdc, key.Label, key.Label.Length, ref labelRect,
            Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_END_ELLIPSIS);
        Win32.SelectObject(hdc, oldLabelFont);
    }

    private static string? GetActiveDeadKeyOutput(KeyDefinition keyDef, Layout layout, KeyboardRenderState state)
    {
        if (state.ActiveDeadKey == null)
            return null;

        string? output = keyDef.GetOutput(state.Shift, state.AltGr, state.CapsLock);
        if (output == null)
            return null;

        if (output.StartsWith("dk_", StringComparison.Ordinal))
        {
            if (output == state.ActiveDeadKey && layout.DeadKeys.TryGetValue(state.ActiveDeadKey, out var selfDk))
            {
                var isolated = selfDk.GetIsolated();
                if (isolated != null)
                    return selfDk.Apply(isolated) ?? isolated;
            }
            return null;
        }

        return layout.DeadKeys.TryGetValue(state.ActiveDeadKey, out var dk)
            ? dk.Apply(output)
            : null;
    }

    private static string? FormatActiveDeadKeyDisplay(string? value, bool showInvisibleMarkers)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        string display = DisplayInvisible(value, showInvisibleMarkers);
        return display.Length == 1 && IsCombiningMark(display[0]) ? "◌" + display : display;
    }

    private static void DrawActiveDeadKeyStatus(IntPtr hdc, Win32.RECT rect, string activeDeadKey, IntPtr hFont)
    {
        string name = VirtualKeyboard.GetDeadKeyFrenchName(activeDeadKey);
        string text = $"Touche morte active : {name}";
        var textRect = new Win32.RECT { left = rect.left, top = rect.top, right = rect.right - 4, bottom = rect.bottom };
        var oldFont = Win32.SelectObject(hdc, hFont);
        Win32.SetTextColor(hdc, CLR_DK_ACTIVE_TEXT);
        Win32.DrawTextW(hdc, text, text.Length, ref textRect,
            Win32.DT_RIGHT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_END_ELLIPSIS);
        Win32.SelectObject(hdc, oldFont);
    }

    private static void PaintLetterKey(
        IntPtr hdc,
        int kx,
        int ky,
        int kw,
        int kh,
        KeyDefinition keyDef,
        int pad,
        KeyboardRenderState state,
        IntPtr hFontMain,
        IntPtr hFontSmall)
    {
        string? mainChar;
        if (state.CapsLock && state.Shift) mainChar = keyDef.CapsShift ?? keyDef.Base;
        else if (state.CapsLock) mainChar = keyDef.Caps ?? keyDef.Base?.ToUpperInvariant();
        else if (state.Shift) mainChar = keyDef.Shift ?? keyDef.Base?.ToUpperInvariant();
        else mainChar = keyDef.Base;

        string? altGrRaw = keyDef.AltGr;
        string? altGrCharToShow = altGrRaw;
        bool altGrIsLetter = IsLetterChar(altGrRaw);
        if (altGrIsLetter)
        {
            if (state.CapsLock && state.Shift) altGrCharToShow = keyDef.CapsShiftAltGr ?? altGrRaw;
            else if (state.CapsLock) altGrCharToShow = keyDef.CapsAltGr ?? keyDef.ShiftAltGr ?? altGrRaw?.ToUpperInvariant();
            else if (state.Shift) altGrCharToShow = keyDef.ShiftAltGr ?? altGrRaw?.ToUpperInvariant();
        }
        bool hasAltGrChar = !string.IsNullOrEmpty(altGrCharToShow) && altGrCharToShow != mainChar;

        string? shiftAltGrChar = keyDef.ShiftAltGr;
        bool showShiftAltGr = !string.IsNullOrEmpty(shiftAltGrChar)
            && !IsLetterChar(shiftAltGrChar)
            && shiftAltGrChar != altGrRaw;

        bool topLeftActive = !state.AltGr;
        bool bottomRightActive = state.AltGr && (!state.Shift || (state.Shift && altGrIsLetter && !showShiftAltGr));
        bool topRightActive = state.AltGr && state.Shift && showShiftAltGr;

        DrawCharAt(hdc, kx + pad, ky, kx + kw / 2 + pad, ky + kh - pad,
            mainChar, topLeftActive, IsDeadKeyRef(mainChar), alignLeft: true, useMainFont: true, state.ShowInvisibleMarkers, hFontMain, hFontSmall, alignTop: true);

        if (hasAltGrChar)
            DrawCharAt(hdc, kx + kw / 2, ky + kh / 2, kx + kw - pad, ky + kh - pad,
                altGrCharToShow, bottomRightActive, IsDeadKeyRef(altGrRaw), alignLeft: false, useMainFont: false, state.ShowInvisibleMarkers, hFontMain, hFontSmall);

        if (showShiftAltGr)
            DrawCharAt(hdc, kx + kw / 2, ky, kx + kw - pad, ky + kh / 2 + pad,
                shiftAltGrChar, topRightActive, IsDeadKeyRef(shiftAltGrChar), alignLeft: false, useMainFont: false, state.ShowInvisibleMarkers, hFontMain, hFontSmall, alignTop: true);
    }

    private static void PaintAccentedNumericKey(
        IntPtr hdc,
        int kx,
        int ky,
        int kw,
        int kh,
        KeyDefinition keyDef,
        int pad,
        KeyboardRenderState state,
        IntPtr hFontMain,
        IntPtr hFontSmall)
    {
        string? letter = state.CapsLock ? (keyDef.Caps ?? keyDef.Base?.ToUpperInvariant()) : keyDef.Base;
        string? digit = keyDef.Shift;
        string? altGr1 = keyDef.AltGr;
        string? altGr2 = keyDef.ShiftAltGr;

        DrawCharAt(hdc, kx + pad, ky + kh / 2, kx + kw / 2, ky + kh - pad,
            letter, !state.AltGr && !state.Shift, IsDeadKeyRef(keyDef.Base), true, false, state.ShowInvisibleMarkers, hFontMain, hFontSmall);
        DrawCharAt(hdc, kx + kw / 2, ky + kh / 2, kx + kw - pad, ky + kh - pad,
            altGr1, state.AltGr && !state.Shift, IsDeadKeyRef(altGr1), false, false, state.ShowInvisibleMarkers, hFontMain, hFontSmall);
        DrawCharAt(hdc, kx + pad, ky, kx + kw / 2, ky + kh / 2 + pad,
            digit, !state.AltGr && state.Shift, IsDeadKeyRef(digit), true, false, state.ShowInvisibleMarkers, hFontMain, hFontSmall, alignTop: true);
        DrawCharAt(hdc, kx + kw / 2, ky, kx + kw - pad, ky + kh / 2 + pad,
            altGr2, state.AltGr && state.Shift, IsDeadKeyRef(altGr2), false, false, state.ShowInvisibleMarkers, hFontMain, hFontSmall, alignTop: true);
    }

    private static void PaintSymbolKey(
        IntPtr hdc,
        int kx,
        int ky,
        int kw,
        int kh,
        KeyDefinition keyDef,
        int pad,
        KeyboardRenderState state,
        IntPtr hFontMain,
        IntPtr hFontSmall,
        IntPtr hFontTiny)
    {
        if (keyDef.Scancode == 0x39)
        {
            PaintSpaceKey(hdc, kx, ky, kw, kh, keyDef, pad, state, hFontMain, hFontTiny);
            return;
        }

        DrawCharAt(hdc, kx + pad, ky + kh / 2, kx + kw / 2, ky + kh - pad,
            keyDef.Base, !state.AltGr && !state.Shift, IsDeadKeyRef(keyDef.Base), true, false, state.ShowInvisibleMarkers, hFontMain, hFontSmall);
        DrawCharAt(hdc, kx + kw / 2, ky + kh / 2, kx + kw - pad, ky + kh - pad,
            keyDef.AltGr, state.AltGr && !state.Shift, IsDeadKeyRef(keyDef.AltGr), false, false, state.ShowInvisibleMarkers, hFontMain, hFontSmall);
        DrawCharAt(hdc, kx + pad, ky, kx + kw / 2, ky + kh / 2 + pad,
            keyDef.Shift, !state.AltGr && state.Shift, IsDeadKeyRef(keyDef.Shift), true, false, state.ShowInvisibleMarkers, hFontMain, hFontSmall, alignTop: true);
        DrawCharAt(hdc, kx + kw / 2, ky, kx + kw - pad, ky + kh / 2 + pad,
            keyDef.ShiftAltGr, state.AltGr && state.Shift, IsDeadKeyRef(keyDef.ShiftAltGr), false, false, state.ShowInvisibleMarkers, hFontMain, hFontSmall, alignTop: true);
    }

    private static void PaintSpaceKey(
        IntPtr hdc,
        int kx,
        int ky,
        int kw,
        int kh,
        KeyDefinition keyDef,
        int pad,
        KeyboardRenderState state,
        IntPtr hFontMain,
        IntPtr hFontTiny)
    {
        int right = kx + kw - pad;
        int left = Math.Max(kx + kw / 2, right - Math.Max(150, kw / 3));
        int bottom = ky + kh - Math.Max(8, pad);
        int lineH = Math.Max(14, Math.Min(20, kh / 3));
        int gap = Math.Max(1, kh / 28);

        if (!string.IsNullOrEmpty(keyDef.ShiftAltGr))
        {
            DrawCharAt(hdc, left, bottom - (lineH * 2) - gap, right, bottom - lineH - gap,
                keyDef.ShiftAltGr, state.AltGr && state.Shift, IsDeadKeyRef(keyDef.ShiftAltGr), false, false, state.ShowInvisibleMarkers, hFontMain, hFontTiny);
        }

        if (!string.IsNullOrEmpty(keyDef.AltGr))
        {
            DrawCharAt(hdc, left, bottom - lineH, right, bottom,
                keyDef.AltGr, state.AltGr && !state.Shift, IsDeadKeyRef(keyDef.AltGr), false, false, state.ShowInvisibleMarkers, hFontMain, hFontTiny);
        }
    }

    private static void DrawCharAt(
        IntPtr hdc,
        int left,
        int top,
        int right,
        int bottom,
        string? chr,
        bool isActive,
        bool isDeadKey,
        bool alignLeft,
        bool useMainFont,
        bool showInvisibleMarkers,
        IntPtr hFontMain,
        IntPtr hFontSmall,
        bool alignTop = false)
    {
        var disp = GetDisplayChar(chr, showInvisibleMarkers);
        if (string.IsNullOrEmpty(disp)) return;

        uint color = (isDeadKey, isActive) switch
        {
            (true, true) => CLR_DK_CHAR,
            (true, false) => CLR_DK_CHAR,
            (false, true) => CLR_CHAR_ACTIVE_BLUE,
            (false, false) => CLR_CHAR_DIM
        };
        IntPtr hFont = useMainFont ? hFontMain : hFontSmall;
        var oldFont = Win32.SelectObject(hdc, hFont);
        Win32.SetTextColor(hdc, color);
        var r = new Win32.RECT { left = left, top = top, right = right, bottom = bottom };
        uint vAlign = alignTop ? 0u : Win32.DT_VCENTER;
        uint flags = vAlign | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_NOCLIP | (alignLeft ? Win32.DT_LEFT : Win32.DT_RIGHT);
        Win32.DrawTextW(hdc, disp, disp.Length, ref r, flags);
        Win32.SelectObject(hdc, oldFont);
    }

    private static KeyDefinition FilterKeyForProfile(
        KeyDefinition key,
        uint scancode,
        KeyboardRenderProfile profile,
        KeyboardRenderState state)
    {
        if (profile == KeyboardRenderProfile.Full) return key;

        return new KeyDefinition
        {
            Position = key.Position,
            Scancode = key.Scancode,
            Base = FilterSlot(profile, scancode, 0, key.Base, state),
            Shift = FilterSlot(profile, scancode, 1, key.Shift, state),
            AltGr = FilterSlot(profile, scancode, 2, key.AltGr, state),
            ShiftAltGr = FilterSlot(profile, scancode, 3, key.ShiftAltGr, state),
            Caps = FilterSlot(profile, scancode, 4, key.Caps, state),
            CapsShift = FilterSlot(profile, scancode, 5, key.CapsShift, state),
            CapsAltGr = FilterSlot(profile, scancode, 6, key.CapsAltGr, state),
            CapsShiftAltGr = FilterSlot(profile, scancode, 7, key.CapsShiftAltGr, state),
        };
    }

    private static string? FilterSlot(
        KeyboardRenderProfile profile,
        uint scancode,
        int layer,
        string? value,
        KeyboardRenderState state)
    {
        return IsSlotVisible(profile, scancode, layer, value, state.LessonVisibleCharacters, state.HintCharacter)
            ? value
            : null;
    }

    private static bool IsOnboardingSlotVisible(uint scancode, int layer, string value)
    {
        if (value.StartsWith("dk_", StringComparison.Ordinal) && HiddenDeadKeysInOnboarding.Contains(value))
            return false;
        if (HiddenSlotsInOnboarding.Contains((scancode, layer)))
            return false;
        return true;
    }

    private static string? GetDisplayChar(string? value, bool showInvisibleMarkers)
    {
        if (string.IsNullOrEmpty(value)) return null;

        string result;
        bool isDk = value.StartsWith("dk_", StringComparison.Ordinal);
        if (isDk)
        {
            result = TrayApplication.GetDeadKeySymbol(value);
        }
        else
        {
            result = DisplayInvisible(value, showInvisibleMarkers);
        }

        if (result.Length == 1 && IsCombiningMark(result[0]))
            return "◌" + result;
        if (isDk && DkAlwaysWithDottedCircle.Contains(value))
            return "◌" + result;

        return result;
    }

    internal static string DisplayInvisible(string value)
    {
        return DisplayInvisible(value, showMarkers: true);
    }

    private static string DisplayInvisible(string value, bool showMarkers)
    {
        if (!showMarkers)
            return value;

        return value switch
        {
            "\u202F" => "esp. ins. fine",
            "\u00A0" => "esp. ins.",
            "\u2011" => "‑",
            " " => " ",
            _ => value
        };
    }

    private static bool IsLetterChar(string? s) => s != null && s.Length == 1 && char.IsLetter(s[0]);

    private static bool IsDeadKeyRef(string? s) => s != null && s.StartsWith("dk_", StringComparison.Ordinal);

    private static bool IsCombiningMark(char c) =>
        (c >= '\u0300' && c <= '\u036F') ||
        (c >= '\u1AB0' && c <= '\u1AFF') ||
        (c >= '\u1DC0' && c <= '\u1DFF') ||
        (c >= '\u20D0' && c <= '\u20FF') ||
        (c >= '\uFE20' && c <= '\uFE2F');

    private static Win32.RECT ToRect(VirtualKeyboard.VisualKey key, int originX, int originY, float scale)
    {
        return new Win32.RECT
        {
            left = originX + (int)(key.X * scale),
            top = originY + (int)(key.Y * scale),
            right = originX + (int)((key.X + key.W) * scale) - 1,
            bottom = originY + (int)((key.Y + key.H) * scale) - 1
        };
    }
}

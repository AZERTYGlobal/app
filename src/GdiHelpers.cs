// Méthodes GDI partagées entre les fenêtres de l'application

namespace AZERTYGlobal;

/// <summary>
/// Utilitaires de rendu GDI/DrawText partagés entre OnboardingWindow et SettingsWindow.
/// Toutes les méthodes sont statiques — les valeurs DPI-dépendantes sont passées en paramètre.
/// </summary>
static class GdiHelpers
{
    internal static void FillSolidRect(IntPtr hdc, Win32.RECT rect, uint color)
    {
        var brush = Win32.CreateSolidBrush(color);
        Win32.FillRect(hdc, ref rect, brush);
        Win32.DeleteObject(brush);
    }

    /// <summary>
    /// Dessine un panneau avec bordure 1px et accent coloré à gauche.
    /// <paramref name="accentWidth"/> est la largeur de l'accent en pixels (typiquement S(4)).
    /// Si <paramref name="accentColor"/> vaut 0, l'accent n'est pas dessiné.
    /// </summary>
    internal static void DrawPanel(IntPtr hdc, Win32.RECT rect, uint backgroundColor, uint borderColor, uint accentColor, int accentWidth)
    {
        FillSolidRect(hdc, rect, borderColor);
        var innerRect = new Win32.RECT
        {
            left = rect.left + 1,
            top = rect.top + 1,
            right = rect.right - 1,
            bottom = rect.bottom - 1
        };
        FillSolidRect(hdc, innerRect, backgroundColor);
        if (accentColor != 0)
        {
            var accentRect = new Win32.RECT
            {
                left = rect.left,
                top = rect.top,
                right = rect.left + accentWidth,
                bottom = rect.bottom
            };
            FillSolidRect(hdc, accentRect, accentColor);
        }
    }

    internal static int MeasureTextHeight(IntPtr hdc, IntPtr hFont, string text, int width,
        uint format = Win32.DT_LEFT | Win32.DT_WORDBREAK | Win32.DT_NOPREFIX)
    {
        Win32.SelectObject(hdc, hFont);
        var measureRect = new Win32.RECT { left = 0, top = 0, right = width, bottom = 9999 };
        Win32.DrawTextW(hdc, text, -1, ref measureRect, format | Win32.DT_CALCRECT);
        return measureRect.bottom;
    }

    internal static int MeasureSingleLineWidth(IntPtr hdc, IntPtr hFont, string text)
    {
        Win32.SelectObject(hdc, hFont);
        var measureRect = new Win32.RECT { left = 0, top = 0, right = 9999, bottom = 9999 };
        Win32.DrawTextW(hdc, text, -1, ref measureRect,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX | Win32.DT_CALCRECT);
        return measureRect.right;
    }

    internal static int MeasureSingleLineHeight(IntPtr hdc, IntPtr hFont)
    {
        return MeasureTextHeight(hdc, hFont, "Ag", 9999,
            Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
    }

    /// <summary>
    /// Découpe des runs colorés en tokens (mot / espace) pour le word-wrap manuel.
    /// </summary>
    internal static List<(string Text, uint Color, IntPtr Font, bool IsSpace)> TokenizeColoredRuns(
        params (string Text, uint Color, IntPtr Font)[] runs)
    {
        var tokens = new List<(string Text, uint Color, IntPtr Font, bool IsSpace)>();
        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text))
                continue;

            var buffer = new System.Text.StringBuilder();
            bool currentIsSpace = char.IsWhiteSpace(run.Text[0]);

            foreach (char ch in run.Text)
            {
                bool isSpace = char.IsWhiteSpace(ch);
                if (buffer.Length > 0 && isSpace != currentIsSpace)
                {
                    tokens.Add((buffer.ToString(), run.Color, run.Font, currentIsSpace));
                    buffer.Clear();
                }

                buffer.Append(ch);
                currentIsSpace = isSpace;
            }

            if (buffer.Length > 0)
                tokens.Add((buffer.ToString(), run.Color, run.Font, currentIsSpace));
        }

        return tokens;
    }

    /// <summary>
    /// Mesure la hauteur nécessaire pour afficher des runs colorés avec word-wrap.
    /// <paramref name="fallbackLineHeight"/> est la hauteur de ligne par défaut si aucune police n'est trouvée.
    /// </summary>
    internal static int MeasureColoredRunsHeight(IntPtr hdc, int width, int fallbackLineHeight,
        params (string Text, uint Color, IntPtr Font)[] runs)
    {
        var tokens = TokenizeColoredRuns(runs);
        int lineHeight = 0;

        foreach (var run in runs)
        {
            if (run.Font == IntPtr.Zero)
                continue;

            lineHeight = Math.Max(lineHeight,
                MeasureTextHeight(hdc, run.Font, "Ag", width, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX));
        }

        if (lineHeight <= 0)
            lineHeight = fallbackLineHeight;

        int lineWidth = 0;
        int lines = 1;

        foreach (var token in tokens)
        {
            if (token.IsSpace && lineWidth == 0)
                continue;

            int tokenWidth = MeasureSingleLineWidth(hdc, token.Font, token.Text);
            if (!token.IsSpace && lineWidth > 0 && lineWidth + tokenWidth > width)
            {
                lines++;
                lineWidth = 0;
            }

            if (!(token.IsSpace && lineWidth == 0))
                lineWidth += tokenWidth;
        }

        return lines * lineHeight;
    }

    /// <summary>
    /// Dessine des runs colorés avec word-wrap manuel.
    /// <paramref name="fallbackLineHeight"/> est la hauteur de ligne par défaut si aucune police n'est trouvée.
    /// </summary>
    internal static void DrawColoredRuns(IntPtr hdc, int x, int y, int width, int fallbackLineHeight,
        params (string Text, uint Color, IntPtr Font)[] runs)
    {
        var tokens = TokenizeColoredRuns(runs);
        int lineHeight = 0;

        foreach (var run in runs)
        {
            if (run.Font == IntPtr.Zero)
                continue;

            lineHeight = Math.Max(lineHeight,
                MeasureTextHeight(hdc, run.Font, "Ag", width, Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX));
        }

        if (lineHeight <= 0)
            lineHeight = fallbackLineHeight;

        int cursorX = x;
        int cursorY = y;

        foreach (var token in tokens)
        {
            if (token.IsSpace && cursorX == x)
                continue;

            int tokenWidth = MeasureSingleLineWidth(hdc, token.Font, token.Text);
            if (!token.IsSpace && cursorX > x && cursorX + tokenWidth > x + width)
            {
                cursorX = x;
                cursorY += lineHeight;
            }

            if (token.IsSpace && cursorX == x)
                continue;

            var tokenRect = new Win32.RECT
            {
                left = cursorX,
                top = cursorY,
                right = x + width,
                bottom = cursorY + lineHeight
            };
            Win32.SelectObject(hdc, token.Font);
            Win32.SetTextColor(hdc, token.Color);
            Win32.DrawTextW(hdc, token.Text, -1, ref tokenRect,
                Win32.DT_LEFT | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);
            cursorX += tokenWidth;
        }
    }
}

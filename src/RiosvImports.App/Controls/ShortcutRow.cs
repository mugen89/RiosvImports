using System.Drawing.Drawing2D;

namespace RiosvImports.App.Controls;

/// <summary>
/// Един ред от списъка с клавиши: самите клавиши като капачки и обяснение
/// вдясно от тях.
///
/// Рисува се като едно цяло, вместо да се сглобява от надписи в поток.
/// Клавишите са с различна дължина — „Ctrl“ и „N“ — и подредбата в поток би
/// разместила обясненията едно спрямо друго. Тук те започват от една и съща
/// отвесна линия, така че окото слиза надолу по колона, а не търси всеки път.
/// </summary>
internal sealed class ShortcutRow : Control
{
    /// <summary>Откъде започва обяснението — една и съща отвесна за всички редове.</summary>
    private const int TextLeft = 210;

    private readonly string[] _keys;

    public ShortcutRow(string keys, string description)
    {
        // „Ctrl+Shift+N“ се разделя тук, а не при всяко повикване.
        _keys = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Text = description;
        Height = Theme.Scaled(32);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var background = new SolidBrush(Theme.BackdropOf(this)))
            g.FillRectangle(background, ClientRectangle);

        var left = 0;

        foreach (var key in _keys)
        {
            if (left > 0)
            {
                TextRenderer.DrawText(g, "+", Theme.Small,
                    new Rectangle(left, 0, 14, Height), Theme.InkFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                left += 14;
            }

            var width = TextRenderer.MeasureText(g, key, Theme.SmallBold).Width + 14;
            var cap = new Rectangle(left, 5, width, Height - 12);

            Theme.FillRounded(g, cap, 4, Theme.Surface, Theme.Border);

            TextRenderer.DrawText(g, key, Theme.SmallBold, cap, Theme.InkStrong,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);

            left += width;
        }

        TextRenderer.DrawText(g, Text, Theme.Base,
            new Rectangle(TextLeft, 0, Math.Max(20, Width - TextLeft), Height),
            Theme.InkStrong,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

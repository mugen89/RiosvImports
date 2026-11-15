using System.Drawing.Drawing2D;

namespace RiosvImports.App.Controls;

/// <summary>
/// Разделите на един отчет, рисувани на ръка.
///
/// Разделите на Windows Forms са единственото останало място, което издава
/// възрастта на набора: сиви трапецовидни езичета с рамка от друга епоха.
/// Рисуват се тук като плоски листчета с ивица под избраното — същият похват,
/// който ползва и превключвателят на видовете отчетност.
///
/// Освен вида има и полза: подтаблиците се казват дълго и еднообразно, затова
/// до името стои броят попълнени редове. Така се вижда къде има работа, без
/// да се влиза във всеки раздел.
/// </summary>
internal sealed class TabStrip : TabControl
{
    public TabStrip()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        Appearance = TabAppearance.Normal;
        ItemSize = new Size(Theme.Scaled(230), Theme.Scaled(34));
        Font = Theme.Base;
        Padding = new Point(0, 0);

    }

    /// <summary>Брой попълнени редове по индекс на раздел — показва се като значе.</summary>
    public Dictionary<int, int> Counts { get; } = [];

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Родният фон е сив и се вижда около листчетата; рисува се наш.
        using var brush = new SolidBrush(Theme.Canvas);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var selected = e.Index == SelectedIndex;
        var bounds = GetTabRect(e.Index);
        var body = new Rectangle(bounds.X + 2, bounds.Y + 3, bounds.Width - 4, bounds.Height - 4);

        using (var background = new SolidBrush(Theme.Canvas)) g.FillRectangle(background, bounds);

        if (selected)
        {
            Theme.FillRounded(g, body, Theme.RadiusSmall, Theme.AccentLight, Color.Transparent);

            using var stripe = new SolidBrush(Theme.Accent);
            g.FillRectangle(stripe, body.Left + 10, body.Bottom - 3, body.Width - 20, 3);
        }

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        // Значето се мери първо: то определя докъде има място за името.
        var badge = Rectangle.Empty;

        if (Counts.TryGetValue(e.Index, out var count) && count > 0)
        {
            var text = count.ToString();
            var size = TextRenderer.MeasureText(g, text, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding);

            badge = new Rectangle(body.Right - size.Width - 26, body.Y + 8, size.Width + 16, 18);

            Theme.FillRounded(g, badge, 9,
                selected ? Theme.Accent : Theme.Border, Color.Transparent);

            TextRenderer.DrawText(g, text, Theme.SmallBold, badge,
                selected ? Theme.OnAccent : Theme.InkMuted,
                flags | TextFormatFlags.HorizontalCenter);
        }

        var right = badge.IsEmpty ? body.Right - 10 : badge.Left - 6;

        TextRenderer.DrawText(g, TabPages[e.Index].Text, selected ? Theme.Bold : Theme.Base,
            new Rectangle(body.Left + 12, body.Y, right - body.Left - 12, body.Height),
            selected ? Theme.Accent : Theme.InkMuted, flags);
    }
}

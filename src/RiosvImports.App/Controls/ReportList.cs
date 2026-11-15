using System.Drawing.Drawing2D;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;

namespace RiosvImports.App.Controls;

/// <summary>Един ред в списъка с видове отчети.</summary>
internal sealed record ReportEntry(ReportNode Report)
{
    public string Title { get; } = ReportTitles.Short(Report);
    public string? Appendix { get; } = ReportTitles.Appendix(Report);
    public string Full { get; } = ReportTitles.Full(Report);

    /// <summary>Колко таблици се попълват — показва се, за да няма изненади.</summary>
    public int Tables { get; } = Report.SelfAndDescendants().Count(n => n.HasData);
}

/// <summary>
/// Списъкът с видове отчети, рисуван на ръка.
///
/// Обикновен списък с колони не върши работа: заглавията са дълги по ред и
/// половина, а номерът на приложението — това, по което хората наричат
/// отчетите помежду си — се губи в морето от текст. Тук той е значе отдясно,
/// а под заглавието стои колко таблици се попълват.
///
/// Остава истински ListBox, за да работят стрелките и търсенето по буква.
/// </summary>
internal sealed class ReportList : ListBox
{
    public ReportList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Theme.Scaled(58);
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Canvas;
        Font = Theme.Base;
        IntegralHeight = false;
    }

    public ReportEntry? Selected => SelectedItem as ReportEntry;

    public void Fill(IEnumerable<ReportEntry> entries)
    {
        BeginUpdate();
        Items.Clear();

        foreach (var entry in entries) Items.Add(entry);
        if (Items.Count > 0) SelectedIndex = 0;

        EndUpdate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || Items[e.Index] is not ReportEntry entry) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = Rectangle.Inflate(e.Bounds, -Theme.Scaled(6), -Theme.Scaled(3));

        using (var background = new SolidBrush(Theme.Canvas)) g.FillRectangle(background, e.Bounds);

        if (selected)
            Theme.FillRounded(g, bounds, Theme.RadiusSmall, Theme.AccentLight, Theme.Accent);

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        // Значето отдясно се мери първо: то определя докъде има място за заглавието.
        var badge = Rectangle.Empty;

        if (entry.Appendix is { } appendix)
        {
            var size = TextRenderer.MeasureText(g, appendix, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding);
            badge = new Rectangle(bounds.Right - size.Width - Theme.Scaled(26), bounds.Top + Theme.Scaled(15), size.Width + Theme.Scaled(18), Theme.Scaled(24));

            Theme.FillRounded(g, badge, Theme.Scaled(12),
                selected ? Theme.Canvas : Theme.Surface,
                selected ? Theme.Accent : Theme.Border);

            TextRenderer.DrawText(g, appendix, Theme.SmallBold, badge,
                selected ? Theme.Accent : Theme.InkMuted,
                flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        var right = badge.IsEmpty ? bounds.Right - Theme.Scaled(12) : badge.Left - Theme.Scaled(10);
        var textWidth = right - bounds.Left - Theme.Scaled(16);

        TextRenderer.DrawText(g, entry.Title, Theme.Bold,
            new Rectangle(bounds.Left + Theme.Scaled(14), bounds.Top + Theme.Scaled(8), textWidth, Theme.Scaled(22)),
            selected ? Theme.Accent : Theme.Ink, flags);

        var subtitle = entry.Tables == 1
            ? "една таблица"
            : $"{entry.Tables} таблици";

        TextRenderer.DrawText(g, subtitle, Theme.Small,
            new Rectangle(bounds.Left + Theme.Scaled(14), bounds.Top + Theme.Scaled(30), textWidth, Theme.Scaled(18)),
            selected ? Theme.InkMuted : Theme.InkFaint, flags);
    }
}

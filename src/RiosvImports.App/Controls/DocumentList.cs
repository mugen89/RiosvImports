using System.Drawing.Drawing2D;
using RiosvImports.Data;

namespace RiosvImports.App.Controls;

/// <summary>Един ред в списъка с отчети.</summary>
internal sealed record DocumentEntry(DocumentSummary Document, string Kind);

/// <summary>
/// Списъкът с отчети, рисуван на ръка.
///
/// Колонният изглед на Windows Forms е от друга епоха и не оставя място за
/// две нива текст в един ред. Тук името стои над вида, а състоянието е значе
/// с цвят — така подписаните се отделят от черновите с един поглед, без да се
/// чете.
/// </summary>
internal sealed class DocumentList : ListBox
{
    public DocumentList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Theme.Scaled(62);
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Canvas;
        Font = Theme.Base;
        IntegralHeight = false;
    }

    public DocumentEntry? Selected => SelectedItem as DocumentEntry;

    public void Fill(IEnumerable<DocumentEntry> entries)
    {
        var keep = SelectedIndex;

        BeginUpdate();
        Items.Clear();

        foreach (var entry in entries) Items.Add(entry);

        // Подредбата е по време на промяна: след запис редът се мести най-горе,
        // затова избраното се връща по позиция, а не по стойност.
        if (Items.Count > 0) SelectedIndex = Math.Clamp(keep, 0, Items.Count - 1);

        EndUpdate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || Items[e.Index] is not DocumentEntry entry) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = Rectangle.Inflate(e.Bounds, -Theme.Scaled(6), -Theme.Scaled(3));

        using (var background = new SolidBrush(Theme.Canvas)) g.FillRectangle(background, e.Bounds);

        Theme.FillRounded(g, bounds, Theme.RadiusSmall,
            selected ? Theme.AccentLight : Theme.SurfaceAlt,
            selected ? Theme.Accent : Theme.BorderSoft);

        var (label, ink, soft) = Status(entry.Document.Status);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        // Отдясно наляво: първо се мери значето, после датата, и каквото
        // остане е за името.
        var size = TextRenderer.MeasureText(g, label, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding);
        var badge = new Rectangle(bounds.Right - size.Width - Theme.Scaled(34), bounds.Top + Theme.Scaled(12), size.Width + Theme.Scaled(20), Theme.Scaled(24));

        Theme.FillRounded(g, badge, Theme.Scaled(12), selected ? Theme.Canvas : soft, Color.Transparent);
        TextRenderer.DrawText(g, label, Theme.SmallBold, badge, ink,
            flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, entry.Document.UpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy, HH:mm"),
            Theme.Small, new Rectangle(badge.Left - Theme.Scaled(130), bounds.Top + Theme.Scaled(34), Theme.Scaled(124), Theme.Scaled(18)),
            Theme.InkFaint, flags | TextFormatFlags.Right);

        var textWidth = badge.Left - bounds.Left - Theme.Scaled(150);

        TextRenderer.DrawText(g, entry.Document.Title, Theme.Bold,
            new Rectangle(bounds.Left + Theme.Scaled(16), bounds.Top + Theme.Scaled(10), textWidth, Theme.Scaled(22)),
            selected ? Theme.Accent : Theme.Ink, flags);

        // Видът и произходът вървят на един ред: и двете отговарят на
        // „какво е това“, а мястото стига само за един ред под името.
        TextRenderer.DrawText(g, $"{entry.Kind}   ·   {entry.Document.OriginLabel}", Theme.Small,
            new Rectangle(bounds.Left + Theme.Scaled(16), bounds.Top + Theme.Scaled(33), textWidth, Theme.Scaled(18)),
            selected ? Theme.InkMuted : Theme.InkFaint, flags);
    }

    private static (string Label, Color Ink, Color Soft) Status(DocumentStatus status) => status switch
    {
        DocumentStatus.Signed => ("Подписан", Theme.Ok, Theme.OkSoft),
        DocumentStatus.Checked => ("Проверен", Theme.Accent, Theme.AccentLight),
        _ => ("Чернова", Theme.InkMuted, Theme.Surface)
    };
}

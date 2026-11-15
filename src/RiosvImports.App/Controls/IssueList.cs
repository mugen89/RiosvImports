using System.Drawing.Drawing2D;
using RiosvImports.Core.Import;

namespace RiosvImports.App.Controls;

/// <summary>Един ред в списъка с проблеми.</summary>
internal sealed record IssueEntry(IssueSeverity Severity, string Location, string Message)
{
    public static IssueEntry From(Issue issue) => new(issue.Severity, issue.Location, issue.Message);

    public static IssueEntry Schema(string message) => new(IssueSeverity.Error, "Схема", message);
}

/// <summary>
/// Списъкът с намерените проблеми.
///
/// Беше обикновен ListView и това си личеше: заглавията на колоните му се
/// рисуват от Windows и остават светли, каквото и да им зададеш. При тъмна
/// тема цялата долна част на екрана светваше в бяло.
///
/// Тук всичко се рисува на ръка, както в останалите списъци: цветна отметка
/// за тежестта, мястото приглушено и самото съобщение — това, което се чете.
/// </summary>
internal sealed class IssueList : ListBox
{
    public IssueList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Theme.Scaled(34);
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Ink;
        Font = Theme.Base;
        IntegralHeight = false;
        HorizontalScrollbar = false;
    }

    public IssueEntry? Selected => SelectedItem as IssueEntry;

    public void Fill(IEnumerable<IssueEntry> entries)
    {
        BeginUpdate();
        Items.Clear();

        foreach (var entry in entries) Items.Add(entry);

        EndUpdate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || Items[e.Index] is not IssueEntry entry) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = e.Bounds;

        using (var background = new SolidBrush(selected ? Theme.AccentLight : Theme.Canvas))
            g.FillRectangle(background, bounds);

        var error = entry.Severity == IssueSeverity.Error;
        var accent = error ? Theme.Error : Theme.Warn;

        // Цветна ивица отляво вместо иконка: вижда се с крайчеца на окото и
        // не изисква място, което трябва на съобщението.
        using (var stripe = new SolidBrush(accent))
            g.FillRectangle(stripe, bounds.Left + Theme.Scaled(6), bounds.Top + Theme.Scaled(7), Theme.Scaled(3), bounds.Height - Theme.Scaled(14));

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        TextRenderer.DrawText(g, error ? "Грешка" : "Бележка", Theme.SmallBold,
            new Rectangle(bounds.Left + Theme.Scaled(18), bounds.Top, Theme.Scaled(70), bounds.Height), accent, flags);

        TextRenderer.DrawText(g, entry.Location, Theme.Small,
            new Rectangle(bounds.Left + Theme.Scaled(94), bounds.Top, Theme.Scaled(190), bounds.Height), Theme.InkFaint, flags);

        TextRenderer.DrawText(g, entry.Message, Theme.Base,
            new Rectangle(bounds.Left + Theme.Scaled(292), bounds.Top, bounds.Width - Theme.Scaled(300), bounds.Height),
            selected ? Theme.Ink : Theme.InkStrong, flags);
    }
}

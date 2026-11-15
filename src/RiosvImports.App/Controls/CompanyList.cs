using System.Drawing.Drawing2D;
using RiosvImports.Data;

namespace RiosvImports.App.Controls;

/// <summary>
/// Списъкът с фирми в настройките.
///
/// Име отгоре, ЕИК отдолу и приглушено: името е това, което човек чете, а
/// ЕИК-ът — това, което сверява. Обратната наредба кара окото да чете девет
/// цифри, преди да стигне до смисъла.
/// </summary>
internal sealed class CompanyList : ListBox
{
    public CompanyList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Theme.Scaled(46);
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Ink;
        Font = Theme.Base;
        IntegralHeight = false;
    }

    public Company? Selected => SelectedItem as Company;

    public void Fill(IEnumerable<Company> companies)
    {
        var keep = SelectedIndex;

        BeginUpdate();
        Items.Clear();

        foreach (var company in companies) Items.Add(company);
        if (Items.Count > 0) SelectedIndex = Math.Clamp(keep, 0, Items.Count - 1);

        EndUpdate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || Items[e.Index] is not Company company) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var bounds = Rectangle.Inflate(e.Bounds, -Theme.Scaled(4), -Theme.Scaled(2));

        using (var background = new SolidBrush(Theme.Canvas)) g.FillRectangle(background, e.Bounds);

        Theme.FillRounded(g, bounds, Theme.RadiusSmall,
            selected ? Theme.AccentLight : Theme.Canvas,
            selected ? Theme.Accent : Theme.BorderSoft);

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        TextRenderer.DrawText(g, company.Name, Theme.Bold,
            new Rectangle(bounds.Left + Theme.Scaled(14), bounds.Top + Theme.Scaled(5), bounds.Width - Theme.Scaled(28), Theme.Scaled(20)),
            selected ? Theme.Accent : Theme.Ink, flags);

        TextRenderer.DrawText(g, "ЕИК " + company.Eik, Theme.Small,
            new Rectangle(bounds.Left + Theme.Scaled(14), bounds.Top + Theme.Scaled(24), bounds.Width - Theme.Scaled(28), Theme.Scaled(18)),
            selected ? Theme.InkMuted : Theme.InkFaint, flags);
    }
}

using System.ComponentModel;
using RiosvImports.Data;

namespace RiosvImports.App.Controls;

/// <summary>
/// Разбивка с водоравни ленти: надпис, лента, брой.
///
/// Водоравни, а не изправени, защото надписите са имена на фирми и на отчети —
/// изправена лента ги събира под себе си и те или се завъртат, или се режат.
/// Тук името си стои на реда и се чете.
///
/// Лентите се мерят спрямо най-голямата стойност, а не спрямо сбора: въпросът
/// на таблото е „кое е най-много и колко изпъква“, а не „какъв дял е от
/// цялото“. За дялове има кръгова диаграма, която тук нарочно я няма.
/// </summary>
internal sealed class BarList : Control
{
    /// <summary>Височината на един ред. Отвън се смята колко място иска списъкът.</summary>
    public const int RowHeight = 30;

    private const int TrackHeight = 10;
    private const int ValueWidth = 46;
    private const int Gutter = 12;

    private IReadOnlyList<MetricSlice> _items = [];

    public BarList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Font = Theme.Base;
    }

    /// <summary>Колко широка е колоната с надписите.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LabelWidth { get; set; } = 200;

    /// <summary>Какво пише, когато няма нито един ред.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = string.Empty;

    /// <summary>Колко висок трябва да е списъкът за толкова редове.</summary>
    public static int HeightFor(int rows) => Math.Max(1, rows) * RowHeight;

    public void SetItems(IReadOnlyList<MetricSlice> items)
    {
        _items = items;
        Height = HeightFor(items.Count);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.BackdropOf(this));

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, Theme.Base,
                new Rectangle(0, 0, Width, RowHeight), Theme.InkFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            return;
        }

        // Нулата не бива да дели: при празен месец всички ленти са без
        // дължина, а редът пак трябва да се нарисува.
        var largest = Math.Max(1, _items.Max(item => item.Value));

        // Тясната колона с надписи изяжда лентата; на тесен прозорец надписът
        // отстъпва, за да остане какво да се сравнява.
        var labelWidth = Math.Clamp(LabelWidth, 80, Math.Max(80, Width / 2));
        var trackLeft = labelWidth + Gutter;
        var trackWidth = Math.Max(20, Width - trackLeft - ValueWidth - Gutter);

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var top = i * RowHeight;

            TextRenderer.DrawText(g, item.Label, Theme.Base,
                new Rectangle(0, top, labelWidth, RowHeight), Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var track = new Rectangle(trackLeft, top + (RowHeight - TrackHeight) / 2, trackWidth, TrackHeight);
            Theme.FillRounded(g, track, TrackHeight / 2, Theme.SurfaceAlt);

            var filled = (int)Math.Round(trackWidth * (double)item.Value / largest);

            // Един пиксел не се вижда като лента, а като прашинка. Стойност,
            // която я има, получава поне колкото да се разпознае, че я има.
            if (item.Value > 0) filled = Math.Max(filled, TrackHeight);

            if (filled > 0)
                Theme.FillRounded(g, track with { Width = filled }, TrackHeight / 2, Theme.Accent);

            TextRenderer.DrawText(g, item.Value.ToString(), Theme.Bold,
                new Rectangle(Width - ValueWidth, top, ValueWidth, RowHeight), Theme.InkMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

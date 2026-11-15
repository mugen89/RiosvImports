using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace RiosvImports.App.Controls;

/// <summary>
/// Превключвател с няколко дяла — като бутоните за изглед в Windows.
///
/// Ползва се вместо падащо меню, когато възможностите са две до четири и все
/// едно колко пъти се сменят. Разликата е съществена: падащото меню крие
/// какво още има, а тук и трите вида отчетност се виждат наведнъж и се сменят
/// с едно движение.
/// </summary>
internal sealed class SegmentedControl : Control
{
    private readonly List<string> _items = [];
    private int _selected;
    private int _hovered = -1;

    /// <summary>Иконите на дяловете. Празно значи надписи.</summary>
    private IReadOnlyList<Action<Graphics, Rectangle, Color>>? _icons;

    /// <summary>Иконата не се чете сама: името ѝ излиза под мишката.</summary>
    private readonly ToolTip _tip = new();

    public SegmentedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

        Font = Theme.Base;
        Height = Theme.Scaled(40);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Color.Transparent;
    }

    public event EventHandler? SelectedIndexChanged;

    public IReadOnlyList<string> Items => _items;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, _items.Count - 1));
            if (clamped == _selected) return;

            _selected = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetItems(IEnumerable<string> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = 0;
        Invalidate();
    }

    /// <summary>
    /// Рисува дяловете с икони вместо с надписи. Празно връща надписите.
    ///
    /// Надписите остават: те са името на дяла — под мишката и за всичко, което
    /// пита за него.
    /// </summary>
    public void ShowIcons(IReadOnlyList<Action<Graphics, Rectangle, Color>>? icons)
    {
        _icons = icons;
        _tip.Hide(this);
        Invalidate();
    }

    /// <summary>С икони ли са дяловете в момента.</summary>
    public bool ShowsIcons => _icons is not null;

    /// <summary>Колко голяма е иконата — расте с мащаба, както шрифтът.</summary>
    private static int IconSize => Theme.Scaled(22);

    private int SegmentWidth => _items.Count == 0 ? Width : (Width - 8) / _items.Count;

    /// <summary>
    /// Колко широк трябва да е превключвателят, за да се чете всеки дял цял.
    ///
    /// Мери се с удебеления шрифт: с него се рисува избраният, а избран може да
    /// бъде всеки. Шрифтът е в точки и расте с мащаба на екрана — мереното расте
    /// с него, а кръгло число в пиксели не расте и при 150 % дяловете излизат с
    /// многоточие. Така се видя петият склад, преди да е сложен.
    /// </summary>
    public int Needed => Measured(Theme.Scaled(ShowsIcons ? 18 : 20));

    /// <summary>
    /// Най-тясното, на което всеки дял пак се чете цял.
    ///
    /// За ред, в който мястото е на цена: четири разреза до петте склада на
    /// лаптоп не се побират с пълното <see cref="Needed"/> — дяловете са равни и
    /// „По код“ заема колкото „По получател“.
    /// </summary>
    public int Snug => Measured(Theme.Scaled(12));

    private int Measured(int padding) =>
        _items.Count == 0
            ? 0
            : _items.Count * ((ShowsIcons
                                  ? IconSize
                                  : _items.Max(item => TextRenderer.MeasureText(item, Theme.Bold).Width)) + padding) + 8;

    /// <summary>Побира ли се всеки дял цял — без многоточие накрая. Ползва се от тестовете.</summary>
    public bool Fits =>
        ShowsIcons
            ? IconSize <= SegmentWidth - 4
            : _items.All(item => TextRenderer.MeasureText(item, Theme.Bold).Width <= SegmentWidth - 4);

    private Rectangle SegmentAt(int index) =>
        new(4 + index * SegmentWidth, 4, SegmentWidth, Height - 8);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = IndexAt(e.Location);

        if (index != _hovered)
        {
            _hovered = index;
            Invalidate();

            // Показва се изрично: подсказката на контролата излиза веднъж при
            // влизане, а тук под мишката минават четири различни неща.
            if (ShowsIcons && index >= 0)
                _tip.Show(_items[index], this, SegmentAt(index).Left, Height + 2, 2500);
            else
                _tip.Hide(this);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = -1;
        _tip.Hide(this);
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();

        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();

        if (IndexAt(e.Location) is var index && index >= 0) SelectedIndex = index;

        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) { SelectedIndex = _selected - 1; e.Handled = true; }
        if (e.KeyCode == Keys.Right) { SelectedIndex = _selected + 1; e.Handled = true; }

        base.OnKeyDown(e);
    }

    protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
    protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }

    private int IndexAt(Point point)
    {
        for (var i = 0; i < _items.Count; i++)
            if (SegmentAt(i).Contains(point)) return i;

        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackdropOf(this));

        Theme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), Theme.Radius, Theme.Surface, Theme.Border);

        for (var i = 0; i < _items.Count; i++)
        {
            var bounds = SegmentAt(i);
            var selected = i == _selected;

            if (selected)
            {
                // Избраният дял е бяло листче върху сивата подложка — същият
                // похват, който Windows ползва за разделите си.
                Theme.FillRounded(g, bounds, Theme.RadiusSmall, Theme.Canvas, Theme.Border);

                using var underline = new SolidBrush(Theme.Accent);
                g.FillRectangle(underline,
                    bounds.Left + 14, bounds.Bottom - 4, bounds.Width - 28, 2);
            }
            else if (i == _hovered)
            {
                Theme.FillRounded(g, bounds, Theme.RadiusSmall, Theme.AccentSoft, Color.Transparent);
            }

            if (_icons is { } icons && i < icons.Count)
            {
                var size = IconSize;

                icons[i](g,
                    new Rectangle(bounds.Left + (bounds.Width - size) / 2,
                                  bounds.Top + (bounds.Height - size) / 2, size, size),
                    selected ? Theme.Accent : Theme.InkMuted);

                continue;
            }

            TextRenderer.DrawText(g, _items[i], selected ? Theme.Bold : Theme.Base, bounds,
                selected ? Theme.Accent : Theme.InkMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        if (!Focused) return;

        using var ring = Theme.RoundedRect(Rectangle.Inflate(SegmentAt(_selected), -2, -2), Theme.RadiusSmall);
        using var pen = new Pen(Theme.Accent) { DashStyle = DashStyle.Dot };
        g.DrawPath(pen, ring);
    }
}

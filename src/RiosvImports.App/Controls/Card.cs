using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace RiosvImports.App.Controls;

/// <summary>
/// Панел със заоблени ъгли и тънък кант.
///
/// Носещият елемент на всички екрани. Сянка няма нарочно — тя тежи на
/// пречертаването и на такъв екран изглежда като украса. Разделянето става
/// с кант и с празно място, което е и по-спокойно за гледане.
/// </summary>
internal class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Fill = Theme.Canvas;
        Outline = Theme.Border;
        Padding = new Padding(Theme.Gap);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Outline { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.Radius;

    /// <summary>Цветна ивица отляво — за състояние, което трябва да се хване с крайчеца на окото.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Stripe { get; set; } = Color.Transparent;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackdropOf(this));

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, bounds, Radius, Fill, Outline);

        if (Stripe == Color.Transparent) return;

        // Ивицата се изрязва по същия заоблен контур, за да не стърчи в ъглите.
        using var clip = Theme.RoundedRect(bounds, Radius);
        var saved = g.Save();

        g.SetClip(clip);
        using (var brush = new SolidBrush(Stripe)) g.FillRectangle(brush, 0, 0, 4, Height);

        g.Restore(saved);
    }
}

/// <summary>Карта, която се посочва и натиска — ред от списък, който отваря нещо.</summary>
internal sealed class ClickableCard : Card
{
    private bool _hovered;

    public ClickableCard()
    {
        Cursor = Cursors.Hand;
        Fill = Theme.SurfaceAlt;
        Outline = Theme.BorderSoft;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Fill = Theme.AccentSoft;
        Outline = Theme.AccentLight;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Fill = Theme.SurfaceAlt;
        Outline = Theme.BorderSoft;
        Invalidate();
        base.OnMouseLeave(e);
    }

    /// <summary>Щраква картата, както човек — ползва се от тестовете.</summary>
    internal void Clicked() => OnClick(EventArgs.Empty);

    /// <summary>
    /// Децата на картата не бива да поглъщат клика: надписите вътре са част от
    /// същата цел и посочването трябва да важи за цялата площ.
    /// </summary>
    public void MakeChildrenTransparent()
    {
        foreach (Control child in Controls)
        {
            child.Cursor = Cursors.Hand;
            child.Click += (_, e) => OnClick(e);
            child.MouseEnter += (_, e) => OnMouseEnter(e);
            child.MouseLeave += (_, e) =>
            {
                if (!ClientRectangle.Contains(PointToClient(MousePosition))) OnMouseLeave(e);
            };
        }

        _ = _hovered;
    }
}

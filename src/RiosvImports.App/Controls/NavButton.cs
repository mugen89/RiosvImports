using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace RiosvImports.App.Controls;

/// <summary>
/// Ред от лентата отляво: икона, надпис и ивица, която показва къде се намира
/// потребителят.
///
/// Ивицата отляво е по-добра от оцветен фон: вижда се от периферното зрение и
/// не спори с цвета на съдържанието вдясно.
/// </summary>
internal sealed class NavButton : Control
{
    private bool _hovered;
    private bool _active;

    public NavButton(string text, string glyph)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

        Text = text;
        Glyph = glyph;

        // През мащаба, както всичко нарисувано тук: при 150 % надписът е един
        // път и половина по-висок и 46 пиксела вече не го побират.
        Height = Theme.Scaled(46);
        Cursor = Cursors.Hand;
        Font = Theme.Big;
        TabStop = true;
        BackColor = Color.Transparent;
    }

    /// <summary>Удебеленият надпис на избрания бутон — един, а не нов при всяко рисуване.</summary>
    private Font? _bold;

    protected override void OnFontChanged(EventArgs e)
    {
        _bold?.Dispose();
        _bold = null;
        base.OnFontChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bold?.Dispose();
        base.Dispose(disposing);
    }

    private static int S(int pixels) => Theme.Scaled(pixels);

    public string Glyph { get; }

    /// <summary>
    /// Свит вид: само иконата, без надпис.
    ///
    /// Поле, а не свойство: проверката на Windows Forms иска всяко свойство за
    /// запис в наследник на Control да казва как се сериализира в дизайнера, а
    /// тази контрола не минава през дизайнер.
    /// </summary>
    internal bool Compact;

    /// <summary>Брояч отдясно — непрочетени съобщения, чернови и подобни.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Badge { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
    protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var background = new SolidBrush(Theme.BackdropOf(this)))
            g.FillRectangle(background, ClientRectangle);

        var body = Compact
            ? new Rectangle(S(6), S(3), Width - S(12), Height - S(6))
            : new Rectangle(S(10), S(3), Width - S(18), Height - S(6));

        if (_active)
            Theme.FillRounded(g, body, Theme.RadiusSmall, Theme.Canvas, Theme.Border);
        else if (_hovered)
            Theme.FillRounded(g, body, Theme.RadiusSmall, Theme.AccentSoft, Color.Transparent);

        if (_active)
        {
            using var stripe = new SolidBrush(Theme.Accent);
            using var path = Theme.RoundedRect(new Rectangle(0, S(11), S(5), Height - S(22)), 2);
            g.FillPath(stripe, path);
        }

        var color = _active ? Theme.Accent : _hovered ? Theme.InkStrong : Theme.InkMuted;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        if (Compact)
        {
            // Само иконата, по средата. Надписът се показва в подсказка —
            // свитата лента е за човек, който вече знае къде какво е.
            TextRenderer.DrawText(g, Glyph, Theme.Icon, body, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }
        else
        {
            // Иконата получава кутия по собствената си ширина, а не двайсет и
            // четири пиксела: при 150 % знакът е по-широк и се режеше отдясно.
            var icon = Math.Max(S(24),
                TextRenderer.MeasureText(g, Glyph, Theme.Icon, Size.Empty, TextFormatFlags.NoPadding).Width);
            var textLeft = body.Left + S(12) + icon + S(8);

            TextRenderer.DrawText(g, Glyph, Theme.Icon,
                new Rectangle(body.Left + S(12), 0, icon, Height), color, flags);

            _bold ??= new Font(Font, FontStyle.Bold);

            TextRenderer.DrawText(g, Text, _active ? _bold : Font,
                new Rectangle(textLeft, 0, Math.Max(S(10), body.Right - textLeft - S(34)), Height), color, flags);
        }

        if (Badge > 0)
        {
            var text = Badge > 99 ? "99+" : Badge.ToString();

            if (Compact)
            {
                // В свитата лента броячът е точка: числото не се побира до
                // иконата, а „има нещо там“ е цялото съобщение.
                var dot = new Rectangle(body.Right - S(12), body.Top + S(6), S(8), S(8));

                using var brush = new SolidBrush(Theme.Accent);
                g.FillEllipse(brush, dot);
            }
            else
            {
                var size = TextRenderer.MeasureText(g, text, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding);
                var tall = Math.Max(S(20), size.Height + S(4));
                var badge = new Rectangle(body.Right - size.Width - S(26), (Height - tall) / 2, size.Width + S(16), tall);

                Theme.FillRounded(g, badge, tall / 2, Theme.Accent, Color.Transparent);

                TextRenderer.DrawText(g, text, Theme.SmallBold, badge, Theme.OnAccent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        if (!Focused) return;

        using var ring = Theme.RoundedRect(Rectangle.Inflate(body, -2, -2), Theme.RadiusSmall);
        using var pen = new Pen(Theme.Accent) { DashStyle = DashStyle.Dot };
        g.DrawPath(pen, ring);
    }
}

using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace RiosvImports.App.Controls;

internal enum ButtonVariant
{
    /// <summary>Основното действие: плътен цвят, бял текст.</summary>
    Primary,

    /// <summary>Обичайното действие: бял фон и кант.</summary>
    Secondary,

    /// <summary>Действие в лента: без кант, докато не се посочи.</summary>
    Ghost,

    /// <summary>Необратимо действие.</summary>
    Danger
}

/// <summary>
/// Бутон със заоблени ъгли и състояния при посочване и натискане.
///
/// Windows Forms рисува бутоните така, както ги е рисувал преди двайсет
/// години. Тук рисуването е поето изцяло: това е разликата между програма,
/// която изглежда на възрастта си, и програма, за която се плаща.
///
/// Достъпността не се жертва за вида: показалецът от клавиатурата се вижда,
/// текстът остава истински текст, а размерите слушат мащаба на системата.
/// </summary>
internal sealed class AppButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public AppButton(string text, ButtonVariant variant = ButtonVariant.Secondary)
    {
        Variant = variant;
        Text = text;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = Theme.Base;
        Padding = new Padding(Theme.Scaled(14), 0, Theme.Scaled(14), 0);
        AutoSize = false;
        Height = Theme.Scaled(36);
        UseVisualStyleBackColor = false;
    }

    /// <summary>
    /// Копчето никога не е по-ниско от собствения си надпис.
    ///
    /// Височините по екраните са писани в пиксели при 100 % — „Height = 36“ на
    /// десетки места. При 150 % буквите стават един път и половина по-високи, а
    /// числото — не, и надписът се реже отгоре и отдолу. Вместо всяко от тези
    /// места да помни мащаба, копчето пази долната граница само: по шрифта, с
    /// който е написано. При 100 % тя е под всички заварени височини и нищо не
    /// се мени; при 150 % ги вдига точно колкото трябва.
    /// </summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        MinimumSize = new Size(0, Font.Height + Theme.Scaled(6));
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonVariant Variant { get; set; }

    /// <summary>Знак от иконовия шрифт пред текста. Празно означава само текст.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph { get; set; }

    /// <summary>Ъгловият радиус. Кръгъл бутон става с голяма стойност.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = Theme.RadiusSmall;

    /// <summary>
    /// Мери текст без ограничение по ширина.
    ///
    /// Ползва се двупараметровият вид нарочно. Този с „предложена“ големина е
    /// коварен: празна големина значи нула на широчина, а int.MaxValue
    /// прелива в основата и връща също толкова безсмислено число. И в двата
    /// случая мярката излиза няколко пъти по-малка от истинската, бутонът се
    /// оразмерява по нея и надписът се реже — при това без никаква грешка,
    /// която да го подскаже.
    /// </summary>
    private static int WidthOf(string text, Font font) =>
        TextRenderer.MeasureText(text, font).Width;

    /// <summary>Изчислява нужната ширина за текста и иконата.</summary>
    public AppButton FitWidth(int extra = 0)
    {
        var width = WidthOf(Text, Font) + Padding.Horizontal + extra;

        if (Glyph is not null) width += WidthOf(Glyph, Theme.Icon) + 8;

        Width = Math.Max(Height, width);
        return this;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
    protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackdropOf(this));

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var (fill, border, text) = Palette();

        using (var path = Theme.RoundedRect(bounds, Radius))
        {
            if (fill != Color.Transparent)
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }

            if (border != Color.Transparent)
            {
                using var pen = new Pen(border);
                g.DrawPath(pen, path);
            }

            // Показалецът от клавиатурата е отделен пръстен навътре, за да се
            // вижда и върху плътен фон.
            if (Focused && ShowFocusCues)
            {
                using var ring = Theme.RoundedRect(Rectangle.Inflate(bounds, -3, -3), Math.Max(2, Radius - 2));
                using var pen = new Pen(Variant == ButtonVariant.Primary ? Theme.OnAccent : Theme.Accent)
                {
                    DashStyle = DashStyle.Dot
                };

                g.DrawPath(pen, ring);
            }
        }

        DrawContent(g, text);
    }

    private void DrawContent(Graphics g, Color color)
    {
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        if (Glyph is null)
        {
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, color,
                flags | TextFormatFlags.HorizontalCenter);
            return;
        }

        // Бутон само с икона. Изчислението за икона и текст заедно би го
        // изместило с половин разстояние наляво — при квадратен бутон това
        // се вижда веднага.
        if (Text.Length == 0)
        {
            TextRenderer.DrawText(g, Glyph, Theme.Icon, ClientRectangle, color,
                flags | TextFormatFlags.HorizontalCenter);
            return;
        }

        // Иконата и текстът се измерват заедно и се центрират като едно цяло —
        // иначе бутоните в една лента се разминават на око.
        //
        // Мери се без ограничение и без многоточие — виж бележката при WidthOf.
        var glyphWidth = WidthOf(Glyph, Theme.Icon);
        var textWidth = WidthOf(Text, Font);

        var left = Math.Max(6, (Width - (glyphWidth + 8 + textWidth)) / 2);
        var textLeft = left + glyphWidth + 8;

        TextRenderer.DrawText(g, Glyph, Theme.Icon,
            new Rectangle(left, 0, glyphWidth, Height), color, flags);

        // Текстът получава всичко останало до десния кант: така при тесен
        // бутон се реже, а при достатъчен — никога.
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(textLeft, 0, Math.Max(10, Width - textLeft - 6), Height), color, flags);
    }

    private (Color Fill, Color Border, Color Text) Palette()
    {
        if (!Enabled)
            return (Theme.Surface, Theme.Border, Theme.InkFaint);

        return Variant switch
        {
            ButtonVariant.Primary => (
                _pressed ? Theme.AccentPressed : _hovered ? Theme.AccentHover : Theme.Accent,
                Color.Transparent,
                Theme.OnAccent),

            ButtonVariant.Secondary => (
                _pressed ? Theme.AccentLight : _hovered ? Theme.AccentSoft : Theme.Canvas,
                _hovered ? Theme.Accent : Theme.Border,
                _hovered ? Theme.Accent : Theme.InkStrong),

            ButtonVariant.Danger => (
                _pressed ? Theme.ErrorSoft : _hovered ? Theme.ErrorSoft : Theme.Canvas,
                _hovered ? Theme.Error : Theme.Border,
                Theme.Error),

            _ => (
                _pressed ? Theme.AccentLight : _hovered ? Theme.AccentSoft : Color.Transparent,
                Color.Transparent,
                _hovered ? Theme.Accent : Theme.InkStrong)
        };
    }
}

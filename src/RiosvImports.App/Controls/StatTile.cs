using System.ComponentModel;

namespace RiosvImports.App.Controls;

/// <summary>
/// Плочка с едно число: колко са, и какво са.
///
/// Числото се рисува, вместо да стои в надпис вътре в картата. Така плочката
/// е една контрола, а не три, подредбата ѝ не зависи от това дали числото е
/// едноцифрено или четирицифрено, и цветовете се четат при рисуване — тоест
/// следват темата без нищо да се пресъздава.
/// </summary>
internal sealed class StatTile : Card
{
    public StatTile(string caption)
    {
        Caption = caption;
        Fill = Theme.Surface;
        Outline = Theme.Border;
        Padding = new Padding(Theme.Gap, Theme.GapSmall, Theme.Gap, Theme.GapSmall);
    }

    /// <summary>Какво брои плочката — стои под числото, дребно.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get; set; }

    /// <summary>Цветът на числото. Взема се от темата при рисуване, не при сглобяване.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Color> Tint { get; set; } = () => Theme.Ink;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;

        var number = new Rectangle(Theme.Gap, 14, Width - 2 * Theme.Gap, 40);
        TextRenderer.DrawText(g, Value.ToString(), Theme.Display, number, Tint(),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var caption = new Rectangle(Theme.Gap, Height - 30, Width - 2 * Theme.Gap, 22);
        TextRenderer.DrawText(g, Caption, Theme.SmallBold, caption, Theme.InkFaint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

using System.Drawing.Drawing2D;

namespace RiosvImports.App.Controls;

/// <summary>
/// Иконите на изгледите в склада: код, клиент, получател, поотделно.
///
/// Рисувани, а не от шрифта с икони: в него няма нито баркод, нито камион, които
/// да са едни и същи на Windows 10 и 11, а шрифтът се сменя според машината
/// (<see cref="Theme.Icon"/>). Тук всяка икона е няколко линии върху решетка
/// 24×24, която расте с полето — затова е остра и при 150 %, и в тъмната тема,
/// защото цветът идва отвън.
/// </summary>
internal static class ViewIcons
{
    /// <summary>По код — баркод.</summary>
    public static void Code(Graphics g, Rectangle box, Color ink)
    {
        var grid = new Grid(box);
        using var brush = new SolidBrush(ink);

        // Широчините са нарочно неравни: равните чертички се четат като решетка.
        foreach (var (x, wide) in new[] { (3f, 2f), (6.5f, 1f), (8.5f, 2.5f), (12.5f, 1f), (14.5f, 1f), (16.5f, 2f), (20f, 1f) })
            g.FillRectangle(brush, grid.X(x), grid.Y(5f), grid.Size(wide), grid.Size(14f));
    }

    /// <summary>По клиент — човек.</summary>
    public static void Client(Graphics g, Rectangle box, Color ink)
    {
        var grid = new Grid(box);
        using var pen = grid.Pen(ink);

        g.DrawEllipse(pen, grid.X(8f), grid.Y(3.5f), grid.Size(8f), grid.Size(8f));
        g.DrawArc(pen, grid.X(4f), grid.Y(13.5f), grid.Size(16f), grid.Size(14f), 180f, 180f);
    }

    /// <summary>По получател — камион: отпадъкът излиза от площадката.</summary>
    public static void Receiver(Graphics g, Rectangle box, Color ink)
    {
        var grid = new Grid(box);

        using var pen = grid.Pen(ink);
        using var brush = new SolidBrush(ink);

        g.DrawRectangle(pen, grid.X(2f), grid.Y(6.5f), grid.Size(12f), grid.Size(10.5f));

        g.DrawLines(pen,
        [
            new PointF(grid.X(14f), grid.Y(10f)),
            new PointF(grid.X(18.5f), grid.Y(10f)),
            new PointF(grid.X(22f), grid.Y(13.5f)),
            new PointF(grid.X(22f), grid.Y(17f)),
            new PointF(grid.X(14f), grid.Y(17f))
        ]);

        foreach (var x in new[] { 5f, 15.5f })
            g.FillEllipse(brush, grid.X(x), grid.Y(15.5f), grid.Size(4f), grid.Size(4f));
    }

    /// <summary>Поотделно — редове един под друг, нищо не е събрано.</summary>
    public static void Each(Graphics g, Rectangle box, Color ink)
    {
        var grid = new Grid(box);

        using var pen = grid.Pen(ink);
        using var brush = new SolidBrush(ink);

        foreach (var y in new[] { 6f, 12f, 18f })
        {
            g.FillEllipse(brush, grid.X(3f), grid.Y(y - 1.25f), grid.Size(2.5f), grid.Size(2.5f));
            g.DrawLine(pen, grid.X(8.5f), grid.Y(y), grid.X(21f), grid.Y(y));
        }
    }

    /// <summary>Решетката 24×24, сложена върху полето на иконата.</summary>
    private readonly struct Grid(Rectangle box)
    {
        private readonly float _unit = Math.Min(box.Width, box.Height) / 24f;
        private readonly Rectangle _box = box;

        public float X(float x) => _box.X + x * _unit;

        public float Y(float y) => _box.Y + y * _unit;

        public float Size(float size) => size * _unit;

        /// <summary>Линия, която расте с иконата, но никога не става косъм.</summary>
        public Pen Pen(Color ink) => new(ink, Math.Max(1.4f, 1.7f * _unit))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
    }
}

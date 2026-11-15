using System.Drawing.Drawing2D;
using RiosvImports.Core.Balance;
using RiosvImports.Core.Localization;

namespace RiosvImports.App.Controls;

/// <summary>
/// Обща основа на диаграмите на таблото.
///
/// Всичко се рисува на ръка. Причината не е спорт: готовите библиотеки за
/// диаграми носят свои цветове, свои шрифтове и свое разбиране за отстояния,
/// а тук темата се сменя в движение и всеки пиксел трябва да я следва. Освен
/// това диаграмите тук са прости — линия, стълбче, кръг — и цената на едната
/// страна код е по-малка от цената на чужда зависимост.
///
/// Правилата са две. Първо: цветовете се четат ПРИ РИСУВАНЕ, никога при
/// сглобяване, иначе смяната на темата ги оставя от вчера. Второ: нищо не се
/// мери в пиксели по подразбиране — всичко тръгва от височината на шрифта,
/// защото на мащаб 150 % пикселът е друг.
/// </summary>
internal abstract class ChartBase : Control
{
    protected ChartBase()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Font = Theme.Base;
    }

    /// <summary>
    /// Какво пише, когато няма нито едно число.
    ///
    /// Поле, а не свойство: проверката на Windows Forms иска всяко свойство за
    /// запис в наследник на Control да казва как се сериализира в дизайнера, а
    /// тези контроли не минават през дизайнер.
    /// </summary>
    internal string EmptyText = string.Empty;

    /// <summary>Височината на един ред текст при сегашния шрифт и мащаб.</summary>
    protected static int Line => Theme.Base.Height;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.BackdropOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (IsEmpty)
        {
            TextRenderer.DrawText(g, EmptyText, Theme.Base,
                new Rectangle(0, 0, Width, Height), Theme.InkFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            return;
        }

        Draw(g);
    }

    protected abstract bool IsEmpty { get; }

    protected abstract void Draw(Graphics g);

    /// <summary>
    /// Кой месец е под показалеца — 0, ако никой.
    ///
    /// Диаграмите, които се четат по месеци, отговарят на този въпрос по един
    /// и същ начин: ивицата на месеца е широка колкото дванайсета от полето.
    /// </summary>
    protected static int MonthAt(Point where, Rectangle plot)
    {
        if (!plot.Contains(where)) return 0;

        var slot = Math.Max(1, plot.Width / BalanceMath.Months);
        var index = (where.X - plot.Left) / slot;

        return Math.Clamp(index + 1, 1, BalanceMath.Months);
    }

    /// <summary>Показалецът става ръка само там, където има какво да се натисне.</summary>
    protected void Clickable(bool yes) => Cursor = yes ? Cursors.Hand : Cursors.Default;

    /// <summary>Изписва количество както навсякъде другаде в програмата.</summary>
    protected static string Amount(decimal value) => Amounts.Text(value);

    /// <summary>Кратко число за тесни места: „1,2 хил.“ вместо „1234,567“.</summary>
    protected static string Short(decimal value) => Amounts.Short(value);

    protected static void Text(Graphics g, string text, Font font, Rectangle bounds, Color colour,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter) =>
        TextRenderer.DrawText(g, text, font, bounds, colour, flags | TextFormatFlags.NoPrefix);
}

/// <summary>
/// Салдото през годината: площ под линия.
///
/// Площ, а не само линия, защото въпросът е „колко има“, а не „как се мени“ —
/// запълненото под кривата се чете като количество. Нулевата линия се рисува
/// винаги: отрицателното салдо е новина и трябва да се вижда от коя страна е.
/// </summary>
internal sealed class TrendChart : ChartBase
{
    private IReadOnlyList<decimal> _values = [];

    /// <summary>Полето на графиката — пази се, за да се разбере къде е щракнато.</summary>
    private Rectangle _plot;

    /// <summary>Натиснат месец. Таблото го превръща в период.</summary>
    public event EventHandler<int>? MonthPicked;

    public void SetValues(IReadOnlyList<decimal> values)
    {
        _values = values;
        Invalidate();
    }

    protected override bool IsEmpty => _values.Count == 0 || _values.All(v => v == 0);

    protected override void Draw(Graphics g)
    {
        var bottom = Line + 6;
        var plot = new Rectangle(Line * 3, 4, Width - Line * 3 - 8, Height - bottom - 8);

        _plot = plot;

        if (plot.Width <= 10 || plot.Height <= 10) return;

        var high = _values.Max();
        var low = Math.Min(0m, _values.Min());

        if (high == low) high = low + 1;

        int Y(decimal value) =>
            plot.Bottom - (int)Math.Round((double)((value - low) / (high - low)) * plot.Height);

        int X(int index) =>
            plot.Left + (int)Math.Round(index * (double)plot.Width / Math.Max(1, _values.Count - 1));

        // Хоризонтите: три тънки линии, колкото окото да мери, без да брои.
        using (var faint = new Pen(Theme.BorderSoft))
        {
            for (var i = 0; i <= 2; i++)
            {
                var value = low + (high - low) * i / 2;
                var y = Y(value);

                g.DrawLine(faint, plot.Left, y, plot.Right, y);

                Text(g, Short(value), Theme.Small,
                    new Rectangle(0, y - Line, plot.Left - 6, Line * 2), Theme.InkFaint,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        var points = new Point[_values.Count];
        for (var i = 0; i < _values.Count; i++) points[i] = new Point(X(i), Y(_values[i]));

        // Площта се затваря до нулевата линия, а не до дъното на кутията:
        // иначе при отрицателно салдо запълненото сочи нагоре.
        var zero = Y(0);

        var area = new List<Point> { new(points[0].X, zero) };
        area.AddRange(points);
        area.Add(new Point(points[^1].X, zero));

        using (var fill = new SolidBrush(Color.FromArgb(48, Theme.Accent)))
            g.FillPolygon(fill, [.. area]);

        using (var pen = new Pen(Theme.Accent, 2f))
            g.DrawLines(pen, points);

        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, plot.Left, zero, plot.Right, zero);

        // Точките се рисуват само ако има място: при тесен екран дванайсет
        // кръгчета се сливат в дебела линия.
        if (plot.Width / Math.Max(1, _values.Count) >= 14)
        {
            using var dot = new SolidBrush(Theme.Accent);
            using var core = new SolidBrush(Theme.Canvas);

            foreach (var point in points)
            {
                g.FillEllipse(dot, point.X - 4, point.Y - 4, 8, 8);
                g.FillEllipse(core, point.X - 2, point.Y - 2, 4, 4);
            }
        }

        Months(g, plot, _values.Count, Height - bottom);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Clickable(!IsEmpty && MonthAt(e.Location, _plot) > 0);
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!IsEmpty && MonthAt(e.Location, _plot) is > 0 and var month)
            MonthPicked?.Invoke(this, month);

        base.OnMouseDown(e);
    }

    /// <summary>Имената на месеците под графиката — през един, ако е тясно.</summary>
    internal static void Months(Graphics g, Rectangle plot, int count, int top)
    {
        var step = plot.Width / Math.Max(1, count) < 26 ? 2 : 1;

        for (var i = 0; i < count; i += step)
        {
            var x = plot.Left + (int)Math.Round(i * (double)plot.Width / Math.Max(1, count - 1));

            Text(g, BalanceSheets.Month(i + 1), Theme.Small,
                new Rectangle(x - 18, top, 36, Theme.Base.Height + 4), Theme.InkFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>
/// Приходът и разходът за всеки месец: стълбчета нагоре и надолу от една ос.
///
/// Двете редици не се редят една до друга, а една срещу друга. Така се вижда
/// въпросът, който човек всъщност задава — прибрахме ли повече, отколкото
/// пуснахме — вместо да се сравняват две височини наум.
/// </summary>
internal sealed class FlowChart : ChartBase
{
    private IReadOnlyList<decimal> _up = [];
    private IReadOnlyList<decimal> _down = [];

    /// <summary>Надписите в легендата. Полета — виж бележката при EmptyText.</summary>
    internal string UpLabel = string.Empty;
    internal string DownLabel = string.Empty;

    private Rectangle _plot;

    /// <summary>Натиснат месец.</summary>
    public event EventHandler<int>? MonthPicked;

    public void SetValues(IReadOnlyList<decimal> up, IReadOnlyList<decimal> down)
    {
        _up = up;
        _down = down;
        Invalidate();
    }

    protected override bool IsEmpty =>
        _up.Concat(_down).All(v => v == 0);

    protected override void Draw(Graphics g)
    {
        var legend = Line + 8;
        var months = Line + 6;

        var plot = new Rectangle(Line * 3, legend, Width - Line * 3 - 8, Height - legend - months - 6);

        _plot = plot;

        if (plot.Width <= 10 || plot.Height <= 20) return;

        var largest = _up.Concat(_down).Select(Math.Abs).DefaultIfEmpty(0m).Max();
        if (largest == 0) largest = 1;

        var zero = plot.Top + plot.Height / 2;
        var half = plot.Height / 2 - 4;

        var slot = plot.Width / Math.Max(1, BalanceMath.Months);
        var bar = Math.Max(4, slot - Math.Max(6, slot / 3));

        using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, plot.Left, zero, plot.Right, zero);

        using var upBrush = new SolidBrush(Theme.Ok);
        using var downBrush = new SolidBrush(Theme.Warn);

        for (var month = 0; month < BalanceMath.Months; month++)
        {
            var centre = plot.Left + slot * month + slot / 2;

            var above = (int)Math.Round((double)(Math.Abs(Value(_up, month)) / largest) * half);
            var below = (int)Math.Round((double)(Math.Abs(Value(_down, month)) / largest) * half);

            if (above > 0)
                g.FillRectangle(upBrush, centre - bar / 2, zero - above, bar, above);

            if (below > 0)
                g.FillRectangle(downBrush, centre - bar / 2, zero + 1, bar, below);

            Text(g, BalanceSheets.Month(month + 1), Theme.Small,
                new Rectangle(centre - slot / 2, plot.Bottom + 2, slot, months), Theme.InkFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        Text(g, Short(largest), Theme.Small,
            new Rectangle(0, plot.Top, plot.Left - 6, Line * 2), Theme.InkFaint,
            TextFormatFlags.Right | TextFormatFlags.Top);

        Key(g, 0, Theme.Ok, UpLabel);
        Key(g, 1, Theme.Warn, DownLabel);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Clickable(!IsEmpty && MonthAt(e.Location, _plot) > 0);
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!IsEmpty && MonthAt(e.Location, _plot) is > 0 and var month)
            MonthPicked?.Invoke(this, month);

        base.OnMouseDown(e);
    }

    private static decimal Value(IReadOnlyList<decimal> series, int index) =>
        index < series.Count ? series[index] : 0m;

    private void Key(Graphics g, int index, Color colour, string label)
    {
        var width = TextRenderer.MeasureText(label, Theme.Small).Width + 26;
        var left = 4 + index * width;

        using (var brush = new SolidBrush(colour))
            g.FillRectangle(brush, left, 4, 10, 10);

        Text(g, label, Theme.Small,
            new Rectangle(left + 14, 0, width, Line + 6), Theme.InkMuted);
    }
}

/// <summary>
/// Какъв дял е всеки код от наличното — пръстен с легенда.
///
/// Пръстен, а не пълен кръг: средата остава за общото количество, което иначе
/// би трябвало да се търси в друга плочка. Кодовете над шест се събират в
/// „други“, защото окото не различава седем сектора, а легендата под тях
/// изяжда цялата плочка.
/// </summary>
internal sealed class ShareChart : ChartBase
{
    private const int Slices = 6;

    private IReadOnlyList<BalanceShare> _shares = [];
    private decimal _total;

    /// <summary>Редовете на легендата при последното рисуване — по тях се познава щракнатото.</summary>
    private (string Ewc, Rectangle Where)[] _legend = [];

    /// <summary>Натиснат код от легендата. „Други“ не се брои — то не е код.</summary>
    public event EventHandler<string>? CodePicked;

    public void SetShares(IReadOnlyList<BalanceShare> shares)
    {
        _shares = [.. shares.Where(s => s.Amount > 0)];
        _total = _shares.Sum(s => s.Amount);

        Invalidate();
    }

    protected override bool IsEmpty => _shares.Count == 0 || _total <= 0;

    protected override void Draw(Graphics g)
    {
        var size = Math.Min(Height - 8, Math.Max(80, Width / 2 - 8));
        var ring = new Rectangle(6, (Height - size) / 2, size, size);

        var shown = Parts();
        var start = -90f;

        for (var i = 0; i < shown.Count; i++)
        {
            var sweep = (float)((double)(shown[i].Amount / _total) * 360);

            using var brush = new SolidBrush(Theme.Series(i));
            g.FillPie(brush, ring, start, sweep);

            start += sweep;
        }

        // Дупката се запълва с фона на картата, а не с прозрачно: прозрачното
        // при рисуване е черно.
        var hole = Rectangle.Inflate(ring, -size / 4, -size / 4);

        using (var brush = new SolidBrush(Theme.BackdropOf(this)))
            g.FillEllipse(brush, hole);

        Text(g, Short(_total), Theme.Heading,
            new Rectangle(hole.Left, hole.Top + hole.Height / 2 - Line, hole.Width, Line * 2),
            Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        Text(g, Lang.T("тона", "tonnes"), Theme.Small,
            new Rectangle(hole.Left, hole.Top + hole.Height / 2 + 2, hole.Width, Line + 4),
            Theme.InkFaint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var left = ring.Right + Theme.Gap;
        var row = Line + 8;
        var top = Math.Max(4, (Height - shown.Count * row) / 2);

        _legend = new (string, Rectangle)[shown.Count];

        for (var i = 0; i < shown.Count; i++)
        {
            var y = top + i * row;

            // Само истинските кодове: последният ред може да е „други“.
            _legend[i] = (i < _shares.Count && shown[i].Name.Length > 0 ? shown[i].Ewc : string.Empty,
                new Rectangle(left, y, Width - left, row));

            using (var brush = new SolidBrush(Theme.Series(i)))
                g.FillRectangle(brush, left, y + row / 2 - 5, 10, 10);

            var percent = (int)Math.Round((double)(shown[i].Amount / _total) * 100);

            Text(g, $"{shown[i].Ewc}  ·  {percent}%", Theme.Small,
                new Rectangle(left + 16, y, Width - left - 20, row), Theme.Ink);
        }
    }

    private string CodeAt(Point where)
    {
        foreach (var (ewc, box) in _legend)
            if (box.Contains(where)) return ewc;

        return string.Empty;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Clickable(CodeAt(e.Location).Length > 0);
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (CodeAt(e.Location) is { Length: > 0 } code) CodePicked?.Invoke(this, code);

        base.OnMouseDown(e);
    }

    /// <summary>Най-големите кодове, а опашката — в един общ дял.</summary>
    private List<BalanceShare> Parts()
    {
        if (_shares.Count <= Slices) return [.. _shares];

        var head = _shares.Take(Slices - 1).ToList();
        var rest = _shares.Skip(Slices - 1).Sum(s => s.Amount);

        head.Add(new BalanceShare(Lang.T("други", "other"), string.Empty, rest, 0));

        return head;
    }
}

/// <summary>
/// Подредба с водоравни ленти: кой код тежи най-много.
///
/// Като <see cref="BarList"/>, но с количества вместо броеве и с кода отделно
/// от името: кодът е кратък и се сравнява, името е дълго и се чете.
/// </summary>
internal sealed class RankChart : ChartBase
{
    private IReadOnlyList<BalanceShare> _items = [];

    /// <summary>Височината на реда при последното рисуване — за да се намери редът под мишката.</summary>
    private int _row = 1;

    /// <summary>Натиснат код. Таблото го превръща в сито по код.</summary>
    public event EventHandler<string>? CodePicked;

    public void SetItems(IReadOnlyList<BalanceShare> items)
    {
        _items = items;
        Invalidate();
    }

    protected override bool IsEmpty => _items.Count == 0;

    protected override void Draw(Graphics g)
    {
        var row = Math.Max(Line + 12, (Height - 4) / Math.Max(1, _items.Count));
        _row = row;

        var largest = _items.Select(i => Math.Abs(i.Amount)).DefaultIfEmpty(0m).Max();

        if (largest == 0) largest = 1;

        var labels = Math.Min(Width / 2, Line * 14);
        var values = Line * 5;
        var track = Math.Max(20, Width - labels - values - Theme.Gap);

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var y = 2 + i * row;

            if (y + row > Height) break;

            Text(g, item.Ewc, Theme.Bold,
                new Rectangle(0, y, Line * 4, row), Theme.Ink);

            Text(g, item.Name, Theme.Small,
                new Rectangle(Line * 4, y, labels - Line * 4 - 6, row), Theme.InkFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var width = (int)Math.Round((double)(Math.Abs(item.Amount) / largest) * track);
            var bar = new Rectangle(labels, y + row / 2 - 5, Math.Max(2, width), 10);

            using (var back = new SolidBrush(Theme.SurfaceAlt))
                g.FillRectangle(back, labels, bar.Y, track, bar.Height);

            using (var brush = new SolidBrush(item.Amount < 0 ? Theme.Error : Theme.Accent))
                g.FillRectangle(brush, bar);

            Text(g, Amount(item.Amount), Theme.Base,
                new Rectangle(labels + track + 6, y, values - 8, row),
                item.Amount < 0 ? Theme.Error : Theme.Ink,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Кодът на реда под показалеца, или празно.</summary>
    private string CodeAt(Point where)
    {
        var index = (where.Y - 2) / Math.Max(1, _row);

        return index >= 0 && index < _items.Count ? _items[index].Ewc : string.Empty;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Clickable(CodeAt(e.Location).Length > 0);
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (CodeAt(e.Location) is { Length: > 0 } code) CodePicked?.Invoke(this, code);

        base.OnMouseDown(e);
    }
}

/// <summary>
/// Топлинна карта: кодовете по редове, месеците по колони, количеството —
/// като плътност на цвета.
///
/// Отговаря на въпрос, на който таблицата отговаря бавно: кога през годината е
/// било натоварено и по кои кодове. Числата тук нарочно ги няма — за числа
/// има таблица.
/// </summary>
internal sealed class HeatChart : ChartBase
{
    private IReadOnlyList<(string Ewc, decimal[] Months)> _rows = [];
    private decimal _largest;

    private int _row = 1;
    private int _head;
    private int _labels;
    private int _cell = 1;

    /// <summary>Натисната клетка: кой код и кой месец.</summary>
    public event EventHandler<(string Ewc, int Month)>? CellPicked;

    public void SetRows(IReadOnlyList<(string Ewc, decimal[] Months)> rows)
    {
        _rows = rows;
        _largest = rows.SelectMany(r => r.Months).Select(Math.Abs).DefaultIfEmpty(0m).Max();

        Invalidate();
    }

    protected override bool IsEmpty => _rows.Count == 0 || _largest == 0;

    protected override void Draw(Graphics g)
    {
        var head = Line + 6;
        var labels = Line * 5;

        var row = Math.Max(Line + 4, (Height - head - 4) / Math.Max(1, _rows.Count));
        var cell = Math.Max(6, (Width - labels - 4) / BalanceMath.Months);

        (_head, _labels, _row, _cell) = (head, labels, row, cell);

        for (var month = 0; month < BalanceMath.Months; month++)
        {
            Text(g, BalanceSheets.Month(month + 1), Theme.Small,
                new Rectangle(labels + month * cell, 0, cell, head), Theme.InkFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            var y = head + i * row;
            if (y + row > Height) break;

            Text(g, _rows[i].Ewc, Theme.Small,
                new Rectangle(0, y, labels - 6, row), Theme.Ink);

            for (var month = 0; month < BalanceMath.Months; month++)
            {
                var value = _rows[i].Months[month];
                var box = new Rectangle(labels + month * cell + 1, y + 1, cell - 2, row - 2);

                if (value == 0)
                {
                    using var empty = new SolidBrush(Theme.SurfaceAlt);
                    g.FillRectangle(empty, box);
                    continue;
                }

                var strength = (double)(Math.Abs(value) / _largest);
                var colour = value < 0 ? Theme.Error : Theme.Accent;

                using var brush = new SolidBrush(
                    Color.FromArgb(40 + (int)Math.Round(strength * 200), colour));

                g.FillRectangle(brush, box);
            }
        }
    }

    /// <summary>Коя клетка е под показалеца. Празен код значи „никоя“.</summary>
    private (string Ewc, int Month) CellAt(Point where)
    {
        var index = (where.Y - _head) / Math.Max(1, _row);
        if (index < 0 || index >= _rows.Count) return (string.Empty, 0);

        var month = (where.X - _labels) / Math.Max(1, _cell) + 1;

        // Щракването върху името на кода избира целия ред, а не месец.
        return (_rows[index].Ewc, month is >= 1 and <= BalanceMath.Months ? month : 0);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Clickable(CellAt(e.Location).Ewc.Length > 0);
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (CellAt(e.Location) is { Ewc.Length: > 0 } cell) CellPicked?.Invoke(this, cell);

        base.OnMouseDown(e);
    }
}

/// <summary>
/// Няколко едри числа един до друг.
///
/// Плочка, а не четири отделни: числата тук се четат заедно — начално плюс
/// образувано минус предадено дава крайното, — и разделени по картите
/// връзката между тях се губи.
/// </summary>
internal sealed class NumbersChart : ChartBase
{
    private IReadOnlyList<(string Caption, decimal Value, Func<Color> Tint)> _items = [];

    /// <summary>Натиснато число — по ред отляво надясно.</summary>
    public event EventHandler<int>? Picked;

    public void SetItems(IReadOnlyList<(string Caption, decimal Value, Func<Color> Tint)> items)
    {
        _items = items;
        Invalidate();
    }

    protected override bool IsEmpty => _items.Count == 0;

    protected override void Draw(Graphics g)
    {
        var width = Width / Math.Max(1, _items.Count);

        for (var i = 0; i < _items.Count; i++)
        {
            var (caption, value, tint) = _items[i];
            var left = i * width;

            if (i > 0)
            {
                using var pen = new Pen(Theme.BorderSoft);
                g.DrawLine(pen, left, 6, left, Height - 6);
            }

            Text(g, Short(value), Theme.Display,
                new Rectangle(left + Theme.Gap, 2, width - Theme.Gap * 2, Height - Line - 8),
                tint(), TextFormatFlags.Left | TextFormatFlags.Bottom);

            Text(g, caption, Theme.SmallBold,
                new Rectangle(left + Theme.Gap, Height - Line - 6, width - Theme.Gap * 2, Line + 4),
                Theme.InkFaint);
        }
    }

    private int At(Point where)
    {
        if (_items.Count == 0) return -1;

        var index = where.X / Math.Max(1, Width / _items.Count);

        return index >= 0 && index < _items.Count ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Clickable(At(e.Location) >= 0);
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (At(e.Location) is >= 0 and var index) Picked?.Invoke(this, index);

        base.OnMouseDown(e);
    }
}

/// <summary>
/// Списък с редове „нещо · число“ — за плочките, които изброяват, а не мерят.
///
/// Рисуван, а не ListBox: родният списък се боядисва трудно и в тъмна тема
/// остава бял, а тук няма нищо за избиране — само за четене.
/// </summary>
internal sealed class NoteChart : ChartBase
{
    private IReadOnlyList<(string Text, string Value, Func<Color> Tint)> _lines = [];

    public void SetLines(IReadOnlyList<(string Text, string Value, Func<Color> Tint)> lines)
    {
        _lines = lines;
        Invalidate();
    }

    protected override bool IsEmpty => _lines.Count == 0;

    protected override void Draw(Graphics g)
    {
        var row = Line + 12;

        for (var i = 0; i < _lines.Count; i++)
        {
            var y = i * row;
            if (y + row > Height) break;

            var (text, value, tint) = _lines[i];
            var width = TextRenderer.MeasureText(value, Theme.Bold).Width + 8;

            Text(g, text, Theme.Base,
                new Rectangle(0, y, Width - width - 8, row), Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            Text(g, value, Theme.Bold,
                new Rectangle(Width - width, y, width, row), tint(),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}

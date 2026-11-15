using RiosvImports.App.Controls;
using RiosvImports.Core.Balance;
using RiosvImports.Core.Localization;

namespace RiosvImports.App.Shell;

/// <summary>
/// Таблото: същите числа, но за човек, който няма да чете таблица.
///
/// Управителят пита три неща — колко имаме, накъде върви и какво иска
/// внимание. Таблицата отговаря и на трите, но чак след като някой я прочете
/// ред по ред. Тук отговорът е на един поглед.
///
/// Всяка плочка е в рамка и има своя височина в РЕДОВЕ, не в пиксели: на
/// различна разделителна способност и при мащаб 125 % или 150 % рамката се
/// разтяга заедно с шрифта, вместо да реже съдържанието си. Колоните се
/// нагаждат по ширината — на широк екран три, на лаптоп две, на тесен прозорец
/// една, — а всяка плочка може да се изключи: това, което не интересува
/// управителя, не бива да заема място.
/// </summary>
internal sealed class BalanceDashboard : Panel
{
    /// <summary>Под тази ширина колоните стават по-малко.</summary>
    private const int MinimumColumn = 360;

    private readonly Dictionary<BalanceTile, Card> _tiles = [];
    private readonly Dictionary<BalanceTile, Control> _content = [];

    private readonly Label _empty = new();

    private IReadOnlySet<string> _shown = new HashSet<string>();

    /// <summary>Движението по кодове — за топлинната карта.</summary>
    private IReadOnlyDictionary<string, decimal[]> _rows = new Dictionary<string, decimal[]>();

    /// <summary>Натиснат код — таблото пита страницата да го покаже.</summary>
    public event EventHandler<string>? CodePicked;

    /// <summary>Натиснат месец — става период от един месец.</summary>
    public event EventHandler<int>? MonthPicked;

    /// <summary>Натиснато число от „Накратко“ — води към съответния раздел.</summary>
    public event EventHandler<BalanceSheet?>? SheetPicked;

    public BalanceDashboard()
    {
        BackColor = Theme.Canvas;
        AutoScroll = true;
        Padding = new Padding(0, 0, Theme.Gap, Theme.Gap);

        foreach (var tile in BalanceTiles.All) Controls.Add(Build(tile));

        _empty.AutoSize = false;
        _empty.Font = Theme.Big;
        _empty.ForeColor = Theme.InkFaint;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.BackColor = Theme.Canvas;
        _empty.Visible = false;

        Controls.Add(_empty);

        _shown = new HashSet<string>(BalanceTiles.All.Select(BalanceTiles.Key), StringComparer.Ordinal);
    }

    // ------------------------------------------------------------- плочки

    /// <summary>Една плочка: рамка, заглавие, ред пояснение и съдържание.</summary>
    private Card Build(BalanceTile tile)
    {
        var card = new Card
        {
            Fill = Theme.Surface,
            Outline = Theme.Border,
            Padding = new Padding(Theme.Gap, Theme.GapSmall, Theme.Gap, Theme.Gap)
        };

        var content = Content(tile);
        content.Dock = DockStyle.Fill;

        // Закачените отгоре се редят наопаки: заглавието влиза последно, за да
        // застане най-горе.
        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Small.Height + 6,
            Text = BalanceTiles.Hint(tile),
            Font = Theme.Small,
            ForeColor = Theme.InkFaint,
            BackColor = Color.Transparent
        };

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Heading.Height + 6,
            Text = BalanceTiles.Title(tile),
            Font = Theme.Heading,
            ForeColor = Theme.Ink,
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };

        Listen(content);

        card.Controls.Add(content);
        card.Controls.Add(hint);
        card.Controls.Add(title);

        _tiles[tile] = card;
        _content[tile] = content;

        return card;
    }

    /// <summary>
    /// Свързва щракванията по диаграмите с въпросите, които те задават.
    ///
    /// Плочката показва отговор; щракването по него пита „а защо“. Кодът води
    /// към реда му в таблицата, месецът стеснява периода, а едрото число —
    /// към раздела, от който идва.
    /// </summary>
    private void Listen(Control content)
    {
        switch (content)
        {
            case TrendChart trend:
                trend.MonthPicked += (_, month) => MonthPicked?.Invoke(this, month);
                break;

            case FlowChart flow:
                flow.MonthPicked += (_, month) => MonthPicked?.Invoke(this, month);
                break;

            case RankChart rank:
                rank.CodePicked += (_, code) => CodePicked?.Invoke(this, code);
                break;

            case ShareChart share:
                share.CodePicked += (_, code) => CodePicked?.Invoke(this, code);
                break;

            case HeatChart heat:
                heat.CellPicked += (_, cell) =>
                {
                    // Месецът се подава пръв: той стеснява периода, а кодът
                    // после стеснява редовете в него.
                    if (cell.Month > 0) MonthPicked?.Invoke(this, cell.Month);

                    CodePicked?.Invoke(this, cell.Ewc);
                };
                break;

            case NumbersChart numbers:
                numbers.Picked += (_, index) => SheetPicked?.Invoke(this, index switch
                {
                    1 => BalanceSheet.Generated,
                    2 => BalanceSheet.Submitted,
                    _ => null
                });
                break;
        }
    }

    private static Control Content(BalanceTile tile) => tile switch
    {
        BalanceTile.Numbers => new NumbersChart(),
        BalanceTile.Trend => new TrendChart { EmptyText = Lang.T("Няма салдо за показване.", "No balance to show.") },
        BalanceTile.Flow => new FlowChart
        {
            UpLabel = Lang.T("образувано", "generated"),
            DownLabel = Lang.T("предадено", "submitted"),
            EmptyText = Lang.T("Няма движение през годината.", "No movement during the year.")
        },
        BalanceTile.Share => new ShareChart { EmptyText = Lang.T("Няма налично количество.", "Nothing on hand.") },
        BalanceTile.Heat => new HeatChart { EmptyText = Lang.T("Няма движение за карта.", "No movement to map.") },
        BalanceTile.TopClosing or BalanceTile.TopGenerated => new RankChart
        {
            EmptyText = Lang.T("Няма количества.", "No quantities.")
        },
        _ => new NoteChart { EmptyText = Lang.T("Няма какво да се покаже.", "Nothing to show.") }
    };

    // ------------------------------------------------------------- пълнене

    /// <summary>
    /// Слага числата по плочките. Нищо не смята само.
    ///
    /// <paramref name="rows"/> е движението по кодове — таблото го получава
    /// наготово от същата таблица, която рисува и мрежата, вместо да го
    /// извежда наново от редовете.
    /// </summary>
    public void SetStats(
        BalanceStats stats,
        IReadOnlyList<string> reports,
        IReadOnlyDictionary<string, decimal[]> rows)
    {
        _rows = rows;

        _empty.Visible = stats.IsEmpty;
        _empty.Text = Lang.T(
            "За този обхват няма какво да се покаже." + Environment.NewLine +
            "Отметнете фирма и изберете година с проверена отчетна книга.",

            "There is nothing to show for this scope." + Environment.NewLine +
            "Tick a company and pick a year with a checked book.");

        Numbers(stats);
        Trend(stats);
        Flow(stats);
        Ranks(stats);
        Share(stats);
        Heat(stats);
        Sheets(stats);
        Attention(stats);
        Sources(stats, reports);

        PerformLayout();
        Invalidate(true);
    }

    private void Numbers(BalanceStats stats)
    {
        var generated = stats.Sheets.GetValueOrDefault(BalanceSheet.Generated);
        var submitted = stats.Sheets.GetValueOrDefault(BalanceSheet.Submitted);

        ((NumbersChart)_content[BalanceTile.Numbers]).SetItems(
        [
            (Lang.T("Начално", "Opening"), stats.Opening, () => Theme.InkMuted),
            (Lang.T("Образувано", "Generated"), generated, () => Theme.Ok),
            (Lang.T("Предадено", "Submitted"), submitted, () => Theme.Warn),
            (Lang.T("В края", "Closing"), stats.Closing, () => stats.Closing < 0 ? Theme.Error : Theme.Accent)
        ]);
    }

    private void Trend(BalanceStats stats) =>
        ((TrendChart)_content[BalanceTile.Trend]).SetValues(stats.Running);

    private void Flow(BalanceStats stats) =>
        ((FlowChart)_content[BalanceTile.Flow]).SetValues(stats.Generated, stats.Submitted);

    private void Ranks(BalanceStats stats)
    {
        ((RankChart)_content[BalanceTile.TopClosing]).SetItems(stats.ByClosing);
        ((RankChart)_content[BalanceTile.TopGenerated]).SetItems(stats.ByGenerated);
    }

    private void Share(BalanceStats stats) =>
        ((ShareChart)_content[BalanceTile.Share]).SetShares(stats.ByClosing);

    /// <summary>
    /// Топлинната карта показва движението, не салдото: въпросът ѝ е „кога е
    /// било натоварено“, а салдото стои високо и в най-спокойния месец.
    /// </summary>
    private void Heat(BalanceStats stats)
    {
        var rows = stats.ByClosing
            .Take(8)
            .Select(share => (share.Ewc, Movement(share.Ewc)))
            .Where(row => row.Item2.Any(v => v != 0))
            .ToList();

        ((HeatChart)_content[BalanceTile.Heat]).SetRows(rows);
    }

    /// <summary>Движението по един код — таблото го носи наготово от таблицата.</summary>
    private decimal[] Movement(string ewc) =>
        _rows.TryGetValue(ewc, out var months) ? months : new decimal[BalanceMath.Months];

    private void Sheets(BalanceStats stats)
    {
        List<(string, string, Func<Color>)> lines =
        [
            .. BalanceSheets.All.Select(sheet =>
            (
                BalanceSheets.Title(sheet),
                Amounts.Text(stats.Sheets.GetValueOrDefault(sheet)) + " " + Lang.T("т", "t"),
                (Func<Color>)(() => BalanceSheets.CrossSign(sheet) switch
                {
                    > 0 => Theme.Ok,
                    < 0 => Theme.Warn,
                    _ => Theme.InkMuted
                })
            ))
        ];

        lines.Add((
            Lang.T("Най-натоварен месец", "Busiest month"),
            stats.BusiestMonth == 0 ? "—" : BalanceSheets.Month(stats.BusiestMonth),
            () => Theme.Ink));

        ((NoteChart)_content[BalanceTile.Sheets]).SetLines(lines);
    }

    private void Attention(BalanceStats stats)
    {
        List<(string, string, Func<Color>)> lines = [];

        foreach (var share in stats.Negative.Take(4))
        {
            lines.Add((
                share.Ewc + " — " + Lang.T("отрицателно салдо", "negative balance"),
                Amounts.Text(share.Amount) + " " + Lang.T("т", "t"),
                () => Theme.Error));
        }

        foreach (var share in stats.Idle.Take(4))
        {
            lines.Add((
                share.Ewc + " — " + Lang.T("стои без движение", "has not moved"),
                Amounts.Text(share.Amount) + " " + Lang.T("т", "t"),
                () => Theme.Warn));
        }

        if (lines.Count == 0 && !stats.IsEmpty)
        {
            lines.Add((
                Lang.T("Нищо не иска внимание.", "Nothing wants attention."),
                "✓", () => Theme.Ok));
        }

        ((NoteChart)_content[BalanceTile.Attention]).SetLines(lines);
    }

    private void Sources(BalanceStats stats, IReadOnlyList<string> reports)
    {
        List<(string, string, Func<Color>)> lines =
        [
            .. reports.Take(5).Select(report =>
                (report, string.Empty, (Func<Color>)(() => Theme.InkMuted)))
        ];

        if (reports.Count > 5)
            lines.Add((Lang.T($"и още {reports.Count - 5}", $"and {reports.Count - 5} more"), string.Empty, () => Theme.InkFaint));

        lines.Add((Lang.T("Кодове в баланса", "Codes in the balance"),
            stats.Codes.ToString(), () => Theme.Ink));

        ((NoteChart)_content[BalanceTile.Sources]).SetLines(lines);
    }

    // ------------------------------------------------------------- подредба

    /// <summary>Кои плочки се виждат. Ключовете идват от настройките.</summary>
    public void ShowTiles(IReadOnlySet<string> keys)
    {
        _shown = keys;

        foreach (var (tile, card) in _tiles)
            card.Visible = keys.Contains(BalanceTiles.Key(tile));

        PerformLayout();
    }

    /// <summary>
    /// Повдига щракване по код и по месец — ползва се от тестовете.
    ///
    /// Самото попадение е геометрия и се проверява с очи; тук се проверява
    /// другото — че натиснатото стига до страницата.
    /// </summary>
    internal void RaiseCodePicked(string code) => CodePicked?.Invoke(this, code);

    internal void RaiseMonthPicked(int month) => MonthPicked?.Invoke(this, month);

    /// <summary>Ключовете на видимите плочки — ползва се от тестовете.</summary>
    internal List<string> ShownTiles() =>
    [
        .. _tiles.Where(pair => pair.Value.Visible).Select(pair => BalanceTiles.Key(pair.Key))
    ];

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var width = ClientSize.Width - Padding.Horizontal;
        if (width <= 0) return;

        if (_empty.Visible)
        {
            _empty.Bounds = new Rectangle(0, 0, width, Math.Max(120, ClientSize.Height - 40));
            foreach (var card in _tiles.Values) card.Visible = false;
            return;
        }

        foreach (var (tile, card) in _tiles)
            card.Visible = _shown.Contains(BalanceTiles.Key(tile));

        var columns = Math.Clamp((width + Theme.Gap) / MinimumColumn, 1, 3);
        var column = (width - (columns - 1) * Theme.Gap) / columns;

        var unit = Theme.Base.Height + 6;
        var head = Theme.Heading.Height + Theme.Small.Height + 12 + Theme.Gap + Theme.GapSmall;

        var x = 0;
        var y = 0;
        var used = 0;
        var tallest = 0;

        foreach (var tile in BalanceTiles.All)
        {
            var card = _tiles[tile];
            if (!card.Visible) continue;

            var span = Math.Min(BalanceTiles.Span(tile), columns);

            // Плочка, която не се побира в остатъка от реда, слиза на нов ред.
            if (used > 0 && used + span > columns)
            {
                x = 0;
                y += tallest + Theme.Gap;
                used = 0;
                tallest = 0;
            }

            var tileWidth = column * span + Theme.Gap * (span - 1);
            var tileHeight = head + BalanceTiles.Rows(tile) * unit;

            card.Bounds = new Rectangle(x, y, tileWidth, tileHeight);

            x += tileWidth + Theme.Gap;
            used += span;
            tallest = Math.Max(tallest, tileHeight);

            if (used < columns) continue;

            x = 0;
            y += tallest + Theme.Gap;
            used = 0;
            tallest = 0;
        }
    }
}

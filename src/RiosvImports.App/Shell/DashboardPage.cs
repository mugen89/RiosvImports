using RiosvImports.App.Controls;
using RiosvImports.Core;
using RiosvImports.Core.Localization;
using RiosvImports.Data;

namespace RiosvImports.App.Shell;

/// <summary>
/// Таблото: докъде е стигнала работата по всички отчети наведнъж.
///
/// Началният екран отговаря на „с какво да продължа“, списъкът — на „къде е
/// еди-кой си отчет“. Оставаше без отговор третият въпрос, който счетоводител
/// с десет фирми си задава в края на месеца: „готово ли е всичко“. Тук той се
/// вижда без броене — колко са, по колко на фирма и на вид, кога е пипано
/// последно и кое стои недовършено.
///
/// Числата се смятат в <see cref="DashboardStats"/>, а тук само се рисуват.
///
/// <para>
/// ИЗВАДЕНО ОТ ЛЕНТАТА ОТЛЯВО (16.09.2026) — по искане, докато се реши какво
/// точно да казва екранът. Кодът остава цял и се проверява от тестовете; за да
/// се върне, стига един ред в <c>ShellForm.BuildPages</c>. Таблото в „Баланс“
/// (<see cref="BalanceDashboard"/>) е друго нещо и си остава на мястото.
/// </para>
/// </summary>
internal sealed class DashboardPage : ContentPage
{
    /// <summary>След колко дни без пипане недовършеният отчет излиза в списъка.</summary>
    private const int IdleAfterDays = 7;

    private readonly Workspace _workspace;
    private readonly ImportService _service;

    private readonly FlowLayoutPanel _stack = new();
    private readonly TableLayoutPanel _tiles = new();

    private readonly StatTile _total = new(Lang.T("Общо отчети", "Reports in total"));
    private readonly StatTile _draft = new(Lang.T("Чернови", "Drafts"));
    private readonly StatTile _checked = new(Lang.T("Проверени", "Checked"));
    private readonly StatTile _signed = new(Lang.T("Подписани", "Signed"));

    private readonly Label _progress = new();

    private readonly BarList _byCompany = new();
    private readonly BarList _byReport = new();
    private readonly BarList _byMonth = new();

    private readonly Card _companyCard = new();
    private readonly Card _reportCard = new();
    private readonly Card _monthCard = new();

    private readonly FlowLayoutPanel _idle = new();

    public DashboardPage(ImportService service, Workspace workspace) : base(1100)
    {
        _service = service;
        _workspace = workspace;

        _stack.Dock = DockStyle.Fill;
        _stack.FlowDirection = FlowDirection.TopDown;
        _stack.WrapContents = false;
        _stack.AutoScroll = true;
        _stack.BackColor = Theme.Canvas;

        _stack.Controls.Add(BuildTitle());
        _stack.Controls.Add(BuildTiles());
        _stack.Controls.Add(Section(Lang.T("Работа по месеци", "Work by month")));
        _stack.Controls.Add(Wrap(_monthCard, _byMonth, 100));
        _stack.Controls.Add(Section(Lang.T("По фирма", "By company")));
        _stack.Controls.Add(Wrap(_companyCard, _byCompany, 240));
        _stack.Controls.Add(Section(Lang.T("По вид отчет", "By report type")));
        _stack.Controls.Add(Wrap(_reportCard, _byReport, 380));
        _stack.Controls.Add(Section(Lang.T("Чака внимание", "Waiting on you")));
        _stack.Controls.Add(BuildIdle());

        Controls.Add(_stack);
    }

    /// <summary>Отваря списъка с отчети — таблото само сочи, работата става там.</summary>
    public event EventHandler? GoToWork;

    // ------------------------------------------------------------- устройство

    /// <summary>
    /// Картите следват ширината на страницата — същото, което прави и
    /// началният екран.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        // Отстоянията вляво и вдясно се занулят: подредбата в поток ги брои
        // към съдържанието и при постоянните три пиксела по подразбиране се
        // появява воден плъзгач, без нищо да е излязло извън прозореца.
        var width = Math.Max(320, _stack.ClientSize.Width - 2);

        foreach (Control child in _stack.Controls) Stretch(child, width);
        foreach (Control card in _idle.Controls) Stretch(card, width);
    }

    private static void Stretch(Control control, int width)
    {
        if (control.Margin.Left != 0 || control.Margin.Right != 0)
            control.Margin = new Padding(0, control.Margin.Top, 0, control.Margin.Bottom);

        if (control.Width != width) control.Width = width;
    }

    private Control BuildTitle()
    {
        var host = new Panel
        {
            Width = ContentWidth,
            Height = Theme.Scaled(82),
            BackColor = Theme.Canvas,
            Margin = new Padding(0, 0, 0, Theme.GapSmall)
        };

        // Закачените отгоре се редят наопаки: последният добавен застава
        // най-горе. Затова редът с дела влиза преди заглавието.
        _progress.Dock = DockStyle.Top;
        _progress.Height = Theme.Scaled(26);
        _progress.Font = Theme.Base;
        _progress.ForeColor = Theme.InkMuted;

        host.Controls.Add(_progress);

        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(44),
            Text = Lang.T("Табло", "Dashboard"),
            Font = Theme.Display,
            ForeColor = Theme.Ink
        });

        return host;
    }

    /// <summary>
    /// Четирите плочки. В таблица, а не в поток: така четвъртинките се делят
    /// поравно при всяка ширина, вместо последната да пада на нов ред.
    /// </summary>
    private Control BuildTiles()
    {
        _tiles.Width = ContentWidth;
        _tiles.Height = Theme.Scaled(96);
        _tiles.ColumnCount = 4;
        _tiles.RowCount = 1;
        _tiles.BackColor = Theme.Canvas;
        _tiles.Margin = new Padding(0, 0, 0, 0);

        for (var i = 0; i < 4; i++)
            _tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        _total.Tint = () => Theme.Ink;
        _draft.Tint = () => Theme.InkMuted;
        _checked.Tint = () => Theme.Warn;
        _signed.Tint = () => Theme.Ok;

        var tiles = new[] { _total, _draft, _checked, _signed };

        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];

            tile.Dock = DockStyle.Fill;
            tile.Margin = new Padding(
                i == 0 ? 0 : Theme.GapSmall / 2, 0,
                i == tiles.Length - 1 ? 0 : Theme.GapSmall / 2, 0);

            _tiles.Controls.Add(tile, i, 0);
        }

        return _tiles;
    }

    /// <summary>Слага разбивка в карта и ѝ дава височина по броя редове.</summary>
    private static Card Wrap(Card card, BarList bars, int labelWidth)
    {
        card.Fill = Theme.Surface;
        card.Outline = Theme.Border;
        card.Padding = new Padding(Theme.Gap, Theme.GapSmall, Theme.Gap, Theme.GapSmall);
        card.Margin = new Padding(0, 0, 0, 0);
        card.Height = BarList.HeightFor(1) + 2 * Theme.GapSmall;

        bars.Dock = DockStyle.Fill;
        bars.LabelWidth = labelWidth;

        card.Controls.Add(bars);

        return card;
    }

    private Control BuildIdle()
    {
        _idle.Width = ContentWidth;
        _idle.AutoSize = true;
        _idle.FlowDirection = FlowDirection.TopDown;
        _idle.WrapContents = false;
        _idle.BackColor = Theme.Canvas;

        return _idle;
    }

    private Label Section(string text) => new()
    {
        Text = text,
        Font = Theme.SmallBold,
        ForeColor = Theme.InkFaint,
        Width = ContentWidth,
        Height = Theme.Scaled(32),
        Margin = new Padding(0, Theme.GapLarge, 0, Theme.GapSmall),
        TextAlign = ContentAlignment.BottomLeft
    };

    // ------------------------------------------------------------- пълнене

    public new void Refresh()
    {
        var stats = DashboardStats.Build(
            _workspace.Documents.List(),
            _workspace.Companies.List(),
            ReportName,
            DateTime.Now,
            IdleAfterDays);

        _total.Value = stats.Total;
        _draft.Value = stats.Draft;
        _checked.Value = stats.Checked;
        _signed.Value = stats.Signed;

        foreach (Control tile in _tiles.Controls) tile.Invalidate();

        _progress.Text = stats.Total == 0
            ? Lang.T("Още няма отчети — щом се появят, тук ще пише докъде са стигнали.",
                     "No reports yet — once there are, this line says how far along they are.")
            : Lang.T($"Подписани са {stats.Signed} от общо {stats.Total} ({stats.SignedPercent}%).",
                     $"{stats.Signed} of {stats.Total} reports signed ({stats.SignedPercent}%).");

        Fill(_monthCard, _byMonth, stats.ByMonth, Lang.T("Няма пипани отчети.", "No reports touched."));
        Fill(_companyCard, _byCompany, stats.ByCompany, Lang.T("Няма отчети по фирми.", "No reports per company yet."));
        Fill(_reportCard, _byReport, stats.ByReport, Lang.T("Няма започнати отчети.", "No reports started."));

        FillIdle(stats.Idle);

        base.Refresh();
    }

    /// <summary>
    /// Как се казва видът отчет. Името идва от схемата на НИСО; ако схемата не
    /// се намери — отчетът е правен с друга версия на програмата — редът пак
    /// се брои, само че под общо название.
    /// </summary>
    private string ReportName(DocumentSummary document)
    {
        var report = _service.Schemas
            .FirstOrDefault(s => s.Key == document.SchemaKey)?
            .FindReport(document.Report);

        return report is null ? string.Empty : ReportTitles.Short(report);
    }

    private static void Fill(Card card, BarList bars, IReadOnlyList<MetricSlice> items, string empty)
    {
        bars.EmptyText = empty;
        bars.SetItems(items);

        card.Height = BarList.HeightFor(items.Count) + 2 * Theme.GapSmall;
    }

    private void FillIdle(IReadOnlyList<IdleDocument> idle)
    {
        _idle.Controls.Clear();

        if (idle.Count == 0)
        {
            _idle.Controls.Add(Empty(Lang.T(
                $"Нищо не стои недовършено повече от {IdleAfterDays} дни.",
                $"Nothing has been left unfinished for more than {IdleAfterDays} days.")));

            return;
        }

        foreach (var item in idle) _idle.Controls.Add(IdleRow(item));
    }

    private Control IdleRow(IdleDocument item)
    {
        var card = new ClickableCard
        {
            Width = ContentWidth,
            Height = Theme.Scaled(58),
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(Theme.Gap, 0, Theme.Gap, 0)
        };

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = item.Document.Title,
            Font = Theme.Base,
            ForeColor = Theme.Ink,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        });

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Right,
            Width = Theme.Scaled(300),
            Text = $"{item.Document.StatusLabel}   ·   {Days(item.Days)}",
            Font = Theme.Small,
            ForeColor = Theme.Warn,
            TextAlign = ContentAlignment.MiddleRight
        });

        card.Click += (_, _) => GoToWork?.Invoke(this, EventArgs.Empty);
        card.MakeChildrenTransparent();

        return card;
    }

    private static string Days(int days) => Lang.T($"стои от {days} дни", $"{days} days idle");

    private Control Empty(string text)
    {
        var card = new Card
        {
            Width = ContentWidth,
            Height = Theme.Scaled(68),
            Fill = Theme.SurfaceAlt,
            Outline = Theme.BorderSoft,
            Padding = new Padding(Theme.Gap, 0, Theme.Gap, 0)
        };

        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Theme.InkFaint,
            TextAlign = ContentAlignment.MiddleLeft
        });

        return card;
    }
}

using System.Globalization;
using RiosvImports.App.Controls;
using RiosvImports.Core;
using RiosvImports.Core.Balance;
using RiosvImports.Core.Excel;
using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;
using RiosvImports.Data;

namespace RiosvImports.App.Shell;

/// <summary>
/// Балансът: какво остава на площадката, месец по месец.
///
/// Отчетната книга отговаря на „какво се случи“ — ред по ред, с дата и с
/// количество. Не отговаря на „колко има сега“, а точно това пита човекът,
/// който трябва да реши дали да приеме следващата партида. Досега сметката
/// се правеше в Excel, отстрани, по копирани редове от четири раздела.
///
/// Тук тя е на екрана: кодовете по редове, месеците по колони. Четирите
/// раздела се гледат поотделно — тогава числата са такива, каквито са
/// записани, — или кръстосани, и тогава образуваното се събира, а
/// предаденото се вади.
///
/// Кои отчети влизат, решава <see cref="BalanceSource"/>; какво излиза от
/// тях — <see cref="BalanceMath"/>. Тази страница само пита и рисува.
/// </summary>
internal sealed class BalancePage : ContentPage
{
    /// <summary>Числата се показват по български: запетая за десетичен знак.</summary>
    private static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");

    /// <summary>Колоната с кода — единствената, която не е месец.</summary>
    private const string CodeColumn = "__ewc";

    private const string OpeningColumn = "__opening";
    private const string TotalColumn = "__total";

    private readonly ImportService _service;
    private readonly Workspace _workspace;
    private readonly CompanyScope _scope;
    private readonly BalanceSource _source;

    /// <summary>
    /// Следващото пълнене да отметне САМО фирмата от лентата.
    ///
    /// Балансът е единственият екран, който може да събира няколко фирми — и
    /// това си остава. Но той тръгва от избраната в лентата: смяната ѝ значи
    /// „сега работя по тази“, а не „добави я към предишната“. Оттам нататък
    /// човек може да вдигне и други отметки и те се пазят, докато не смени
    /// фирмата пак.
    /// </summary>
    private bool _onlyScope = true;

    private readonly DataGridView _grid = new();
    private readonly Panel _content = new();

    /// <summary>
    /// Свързаните колони и помнените ширини. Таблицата се сглобява наново при
    /// всеки изглед — без това всяка нагласена ширина би изчезнала при
    /// първото щракване.
    /// </summary>
    private readonly ColumnLinks _links;
    private readonly BalanceDashboard _dashboard = new();
    private readonly BalanceOptions _options;

    /// <summary>Таблица или табло — едни и същи числа, два начина да се гледат.</summary>
    private readonly SegmentedControl _tab = new();

    /// <summary>
    /// Откъде идват числата: от проверените отчети или от склада.
    ///
    /// Двата отговарят на различни въпроси — „какво е отчетено“ и „какво стои
    /// сега“ — и затова са превключвател, а не отметка: единият не е частен
    /// случай на другия. Всичко останало (таблица, табло, изгледи, изнасяне)
    /// е едно и също за двата.
    /// </summary>
    private readonly SegmentedControl _origin = new();

    /// <summary>Подзаглавието — казва кое се брои, според източника.</summary>
    private readonly Label _subtitle = new();

    /// <summary>Изборът на чернова — има смисъл само при отчетите.</summary>
    private Control? _draftHost;

    /// <summary>Дали числата идват от склада — първият избор и подразбирането.</summary>
    private bool Store => _origin.SelectedIndex == 0;

    /// <summary>Под какво име се помни източникът между пусканията.</summary>
    private const string OriginKey = "balance.origin";

    /// <summary>Под какво име се помнят ширините и свързаните колони.</summary>
    private const string ColumnsKey = "balance.columns";

    private readonly SegmentedControl _view = new();
    private readonly SegmentedControl _mode = new();
    private readonly ComboBox _year = new();
    private readonly CheckedListBox _companies = new();
    private readonly ComboBox _draft = new();
    private readonly ComboBox _from = new();
    private readonly ComboBox _to = new();
    private readonly Label _summary = new();

    /// <summary>Копчето, което маха ситото по код. Вижда се само когато има какво да махне.</summary>
    private readonly AppButton _clearCode = Theme.GhostButton(string.Empty, Theme.Glyph.Delete);

    /// <summary>Ситото по код — идва от щракване по таблото, не от писане.</summary>
    private string _code = string.Empty;

    /// <summary>
    /// Копчето, което връща обхвата към подразбиращия се.
    ///
    /// Прави се тук, а не в лентата: ситата се сглобяват преди нея и още при
    /// пълненето им искат да го скрият или покажат.
    /// </summary>
    private readonly AppButton _clearFilters = Theme.GhostButton(string.Empty, Theme.Glyph.Delete);

    /// <summary>
    /// Номерата на фирмите по реда на списъка с отметки. null е редът
    /// „(без фирма)“ — заварените отчети нямат такава.
    /// </summary>
    private List<long?> _companyIds = [];

    /// <summary>Номерата на черновите по реда на падащия списък; първата е „(без)“.</summary>
    private List<long?> _draftIds = [null];

    /// <summary>Годините по реда на падащия списък.</summary>
    private List<int> _years = [];

    /// <summary>Пълненето на списъците вдига същите събития, които и човекът вдига.</summary>
    private bool _filling;

    /// <summary>Номенклатурата по код — чете се веднъж от схемата.</summary>
    private Dictionary<string, string>? _names;

    /// <summary>Заглавието на екрана — носи и това, което се събира в него.</summary>
    private readonly Label _pageTitle = new();

    /// <summary>
    /// Фирмата идва отвън — от лентата отляво. Подаден null значи „направи си
    /// свой обхват“: така страницата се сглобява и сама, което ползва тестът.
    /// </summary>
    public BalancePage(ImportService service, Workspace workspace, CompanyScope? scope = null) : base(1600)
    {
        _service = service;
        _workspace = workspace;
        _scope = scope ?? new CompanyScope(workspace);
        _source = new BalanceSource(workspace.Documents);

        _scope.Changed += (_, _) =>
        {
            _onlyScope = true;
            Refresh();
        };

        _dashboard.CodePicked += (_, code) => PickCode(code);
        _dashboard.MonthPicked += (_, month) => PickMonth(month);
        _dashboard.SheetPicked += (_, sheet) => PickSheet(sheet);

        _links = new ColumnLinks(_grid,
            () => workspace.Settings.Get(ColumnsKey, string.Empty),
            value => workspace.Settings.Set(ColumnsKey, value));

        _options = new BalanceOptions(workspace.Settings);
        _options.Changed += (_, _) => Rebuild();
        _options.ManageRequested += (_, _) => ManageOpenings();

        // Закачените отгоре и отдясно се редят наопаки: добавеното последно
        // застава най-отвън. Затова съдържанието влиза първо, панелът след
        // него, а лентите — накрая.
        Controls.Add(BuildContent());
        Controls.Add(_options);
        Controls.Add(BuildFilters());
        Controls.Add(BuildToolbar());
        Controls.Add(BuildTitle());

        Refresh();
    }

    /// <summary>Таблицата и таблото стоят на едно място и се сменят.</summary>
    private Control BuildContent()
    {
        _content.Dock = DockStyle.Fill;
        _content.BackColor = Theme.Canvas;

        _dashboard.Dock = DockStyle.Fill;
        _dashboard.Visible = false;

        var grid = BuildGrid();
        grid.Dock = DockStyle.Fill;

        _content.Controls.Add(_dashboard);
        _content.Controls.Add(grid);

        return _content;
    }

    // ------------------------------------------------------------- устройство

    private Control BuildTitle()
    {
        var host = new Panel { Dock = DockStyle.Top, Height = Theme.Scaled(76), BackColor = Theme.Canvas };

        // Закачените отгоре се редят наопаки: последният добавен застава най-
        // горе. Затова подзаглавието влиза първо, а заглавието — след него.
        _subtitle.Dock = DockStyle.Top;
        _subtitle.Height = Theme.Scaled(26);
        _subtitle.Font = Theme.Base;
        _subtitle.ForeColor = Theme.InkMuted;
        _subtitle.AutoEllipsis = true;

        host.Controls.Add(_subtitle);

        var row = new Panel { Dock = DockStyle.Top, Height = Theme.Scaled(44), BackColor = Theme.Canvas };

        _pageTitle.Dock = DockStyle.Fill;
        _pageTitle.Font = Theme.Display;
        _pageTitle.ForeColor = Theme.Ink;
        _pageTitle.Text = Lang.T("Баланс", "Balance");

        // Източникът стои при заглавието, а не в лентата с изгледите: той
        // мени какво значат всички числа на страницата, а лентата е пълна.
        // Складът е пръв и по подразбиране: по него се работи всеки ден, а
        // отчетите се гледат, когато се сверява подаденото.
        _origin.SetItems([Lang.T("По склада", "From the store"), Lang.T("По отчети", "From reports")]);
        _origin.Dock = DockStyle.Right;
        _origin.Width = Theme.Scaled(300);
        _origin.Height = Theme.Scaled(38);
        _origin.SelectedIndex = _workspace.Settings.Get(OriginKey, "store") == "reports" ? 1 : 0;
        _origin.SelectedIndexChanged += (_, _) => SwitchOrigin();

        new ToolTip().SetToolTip(_origin, Lang.T(
            "„По склада“ — движенията от склада: какво стои сега, без да се чака отчет.\n" +
            "„По отчети“ — само проверените отчетни книги: какво е отчетено.",
            "“From the store” — the store movements: what stands now, without waiting for a report.\n" +
            "“From reports” — only the checked books: what has been reported."));

        // Пълнещият влиза пръв.
        row.Controls.Add(_pageTitle);
        row.Controls.Add(_origin);

        host.Controls.Add(row);

        ShowOrigin();

        return host;
    }

    /// <summary>
    /// Какво се гледа: кой раздел, коя година и в какъв вид.
    ///
    /// Разделите са превключвател, а не падащ списък: те са пет и се сменят
    /// през цялото време, а падащият списък би скрил, че кръстосаният изглед
    /// изобщо съществува.
    /// </summary>
    private Control BuildToolbar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(56),
            BackColor = Theme.Canvas,
            Padding = new Padding(0, 0, 0, Theme.Gap)
        };

        _tab.SetItems([Lang.T("Таблица", "Table"), Lang.T("Табло", "Dashboard")]);
        _tab.Dock = DockStyle.Left;
        _tab.Width = Math.Max(Theme.Scaled(180), _tab.Needed);
        _tab.Height = Theme.Scaled(40);
        _tab.SelectedIndexChanged += (_, _) => SwitchTab();

        _view.SetItems([
            .. BalanceSheets.All.Select(BalanceSheets.Title),
            Lang.T("Баланс", "Balance")
        ]);

        // Кръстосаният изглед е този, заради който страницата съществува.
        _view.SelectedIndex = BalanceSheets.All.Count;
        _view.Dock = DockStyle.Left;
        _view.Width = Math.Max(Theme.Scaled(440), _view.Needed);
        _view.Height = Theme.Scaled(40);
        _view.SelectedIndexChanged += (_, _) => Rebuild();

        _mode.SetItems([Lang.T("Движение", "Movement"), Lang.T("Салдо", "Running")]);
        _mode.Dock = DockStyle.Left;
        _mode.Width = Math.Max(Theme.Scaled(180), _mode.Needed);
        _mode.Height = Theme.Scaled(40);
        _mode.Margin = new Padding(Theme.Gap, 0, 0, 0);
        _mode.SelectedIndexChanged += (_, _) => Rebuild();

        _year.Dock = DockStyle.Left;
        _year.Width = Theme.Scaled(110);
        _year.Font = Theme.Base;
        _year.DropDownStyle = ComboBoxStyle.DropDownList;
        _year.SelectedIndexChanged += (_, _) => Rebuild();
        Theme.StyleInput(_year);

        // Копчетата вдясно стоят в поток, а не закачени едно по едно: така
        // между тях има разстояние и надписите им могат да растат с езика.
        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Canvas,

            // Отляво: групата расте наляво и без отстояние се долепя до
            // годината, а двете нямат нищо общо.
            Padding = new Padding(Theme.GapLarge, 0, 0, 0)
        };

        // Изнасянето е главното действие на страницата: гледането свършва с
        // файл, който отива при друг човек.
        var export = Theme.PrimaryButton(Lang.T("Изнеси в Excel", "Export to Excel"));
        export.Glyph = Theme.Glyph.Excel;
        export.Height = Theme.Scaled(40);
        export.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        export.Click += (_, _) => Export();

        // Шаблонът и внасянето на начални количества се правят веднъж и после
        // не се пипат — мястото им е в панела отдясно, при другите настройки,
        // а не в лентата, където всеки пиксел е нужен на изгледите.
        tools.Controls.Add(export.FitWidth());
        tools.Controls.Add(Action(Lang.T("Обнови", "Refresh"), Theme.Glyph.Refresh, Refresh));
        tools.Controls.Add(Action(Lang.T("Периоди", "Periods"), Theme.Glyph.Documents, ShowPeriods));

        _clearFilters.Text = Lang.T("Изчисти ситата", "Clear filters");
        _clearFilters.Height = Theme.Scaled(40);
        _clearFilters.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        _clearFilters.Visible = false;
        _clearFilters.Click += (_, _) => ClearFilters();

        tools.Controls.Add(_clearFilters.FitWidth());

        // Два реда, а не един — както в „Склад“: три превключвателя, годината и
        // четири копчета не се побират на лаптоп 1366 пиксела, и копчетата
        // („Изнеси в Excel“ сред тях) изчезваха зад десния ръб. Горе стои КАКВО
        // се гледа, долу — годината и какво се прави с него.
        var views = new Panel { Dock = DockStyle.Top, Height = Theme.Scaled(48), BackColor = Theme.Canvas };

        views.Controls.Add(_mode);
        views.Controls.Add(Spacer());
        views.Controls.Add(_view);
        views.Controls.Add(Spacer());
        views.Controls.Add(_tab);

        var scope = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };

        scope.Controls.Add(tools);
        scope.Controls.Add(_year);

        // Пълнещият влиза пръв (виж АРХИТЕКТУРА.md).
        bar.Controls.Add(scope);
        bar.Controls.Add(views);
        bar.Height = Theme.Scaled(56 + 48);

        return bar;
    }

    /// <summary>
    /// Сменя таблицата с таблото.
    ///
    /// Изборът на раздел и на вид число важи само за таблицата и се скрива с
    /// нея: на таблото те не значат нищо, а изключеното копче, което си стои
    /// на екрана, е покана да бъде натиснато.
    /// </summary>
    private void SwitchTab()
    {
        var dashboard = _tab.SelectedIndex == 1;

        _view.Visible = !dashboard;
        _mode.Visible = !dashboard;

        foreach (Control child in _content.Controls)
            child.Visible = ReferenceEquals(child, _dashboard) == dashboard;

        _options.ShowFor(dashboard);

        Rebuild();
    }

    /// <summary>
    /// Сменя източника: отчетите или складът. Помни се между пусканията —
    /// който работи по склада, работи по него всеки ден.
    /// </summary>
    private void SwitchOrigin()
    {
        _workspace.Settings.Set(OriginKey, Store ? "store" : "reports");

        ShowOrigin();
        Rebuild();
    }

    /// <summary>Подзаглавието и черновата следват източника.</summary>
    private void ShowOrigin()
    {
        _subtitle.Text = Store
            ? Lang.T(
                "Кодовете по редове, месеците по колони. Движенията от склада — текущото състояние, без да се чака отчет.",
                "Codes down the rows, months across the columns. The store movements — the current state, without waiting for a report.")
            : Lang.T(
                "Кодовете по редове, месеците по колони. Влизат само проверените отчети.",
                "Codes down the rows, months across the columns. Only checked reports count.");

        // Черновата е допълнение към отчетите; складът я няма — там всичко
        // записано вече се брои.
        if (_draftHost is not null) _draftHost.Visible = !Store;
    }

    /// <summary>
    /// По кои фирми и с коя чернова.
    ///
    /// Фирмите са списък с отметки, а не падащ списък: балансът се води по
    /// фирма, но счетоводител с две дружества на един склад иска и сборния
    /// изглед, а той се получава само ако могат да се отметнат няколко.
    /// </summary>
    private Control BuildFilters()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(116),
            BackColor = Theme.Canvas,
            Padding = new Padding(0, 0, 0, Theme.Gap)
        };

        _companies.Dock = DockStyle.Left;
        _companies.Width = Theme.Scaled(320);
        _companies.Font = Theme.Base;
        _companies.BorderStyle = BorderStyle.FixedSingle;
        _companies.CheckOnClick = true;
        _companies.IntegralHeight = false;
        Theme.StyleInput(_companies);

        // ItemCheck идва ПРЕДИ отметката да е сменена. Прочетено веднага,
        // ситото би останало с едно движение назад — затова се отлага.
        //
        // Отлага се обаче само това, което идва от човек. Слагането на
        // отметките при пълнене вдига същото събитие, а тогава прозорец още
        // няма и BeginInvoke хвърля „window handle has been created“ —
        // програмата пада при отваряне, но само при заведена фирма, тоест
        // навсякъде освен на празна база.
        _companies.ItemCheck += (_, _) =>
        {
            if (_filling || !IsHandleCreated) return;

            BeginInvoke(Rebuild);
        };

        _draft.Dock = DockStyle.Left;
        _draft.Width = Theme.Scaled(380);
        _draft.Font = Theme.Base;
        _draft.DropDownStyle = ComboBoxStyle.DropDownList;
        _draft.SelectedIndexChanged += (_, _) => Rebuild();
        Theme.StyleInput(_draft);

        _summary.Dock = DockStyle.Fill;
        _summary.Font = Theme.Base;
        _summary.ForeColor = Theme.InkMuted;
        _summary.Padding = new Padding(Theme.Gap, 0, 0, 0);
        _summary.TextAlign = ContentAlignment.TopLeft;

        bar.Controls.Add(_summary);
        bar.Controls.Add(BuildPeriod());
        bar.Controls.Add(Spacer());
        _draftHost = Labelled(_draft, Lang.T("Включи и една чернова", "Include one draft"), 340);
        _draftHost.Visible = !Store;
        bar.Controls.Add(_draftHost);
        bar.Controls.Add(Spacer());
        bar.Controls.Add(BuildCompanies());

        return bar;
    }

    /// <summary>
    /// Списъкът с фирми и двете думи под него.
    ///
    /// „Нито една“ изглежда безполезно — празен избор показва празна таблица.
    /// Полезно е обаче като начало: при дванайсет фирми да се свалят единайсет
    /// отметки, за да остане една, е единайсет движения; тук са две.
    /// </summary>
    private Control BuildCompanies()
    {
        var host = new Panel { Dock = DockStyle.Left, Width = Theme.Scaled(300), BackColor = Theme.Canvas };

        var picks = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Small.Height + 10,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Canvas
        };

        picks.Controls.Add(Pick(Lang.T("всички", "all"), true));
        picks.Controls.Add(Pick(Lang.T("нито една", "none"), false));

        _companies.Dock = DockStyle.Fill;

        host.Controls.Add(_companies);
        host.Controls.Add(picks);
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Small.Height + 6,
            Text = Lang.T("Фирми", "Companies"),
            Font = Theme.SmallBold,
            ForeColor = Theme.InkFaint
        });

        return host;
    }

    private Control Pick(string text, bool ticked)
    {
        var link = new Label
        {
            AutoSize = true,
            Text = text,
            Font = Theme.Small,
            ForeColor = Theme.Accent,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 2, Theme.Gap, 0),
            BackColor = Color.Transparent
        };

        link.Click += (_, _) => TickAll(ticked);

        return link;
    }

    /// <summary>Вдига или сваля всички отметки наведнъж.</summary>
    private void TickAll(bool ticked)
    {
        _filling = true;

        try
        {
            for (var i = 0; i < _companies.Items.Count; i++) _companies.SetItemChecked(i, ticked);
        }
        finally
        {
            _filling = false;
        }

        Rebuild();
    }

    /// <summary>
    /// Периодът: от кой до кой месец.
    ///
    /// Цялата година е подразбиращото се, защото балансът е годишен. Но
    /// въпросът „какво стана през второто тримесечие“ се задава често, а
    /// отговорът му дотогава се получаваше със затваряне на едно око върху
    /// осем колони.
    ///
    /// Началното количество на периода е салдото ПРЕДИ първия му месец, не
    /// онова от януари — иначе всяко тримесечие би тръгвало от нула.
    /// </summary>
    private Control BuildPeriod()
    {
        var host = new Panel { Dock = DockStyle.Left, Width = Theme.Scaled(250), BackColor = Theme.Canvas };

        var line = new Panel { Dock = DockStyle.Top, Height = Theme.Base.Height + 12, BackColor = Theme.Canvas };

        foreach (var box in new[] { _from, _to })
        {
            box.Dock = DockStyle.Left;
            box.Width = Theme.Scaled(112);
            box.Font = Theme.Base;
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            Theme.StyleInput(box);

            for (var month = 1; month <= BalanceMath.Months; month++)
                box.Items.Add(BalanceSheets.Month(month));

            box.SelectedIndexChanged += (_, _) => PeriodChanged();
        }

        _from.SelectedIndex = 0;
        _to.SelectedIndex = BalanceMath.Months - 1;

        // Закачените отляво се редят наопаки: последният добавен застава вляво.
        line.Controls.Add(_to);
        line.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Theme.Scaled(6), BackColor = Theme.Canvas });
        line.Controls.Add(_from);

        // Ситото по код се появява само когато има такова: празно копче на
        // екрана е покана да бъде натиснато, а тук няма какво.
        _clearCode.Dock = DockStyle.Top;
        _clearCode.Height = Theme.Base.Height + 14;
        _clearCode.Visible = false;
        _clearCode.Click += (_, _) => PickCode(string.Empty);

        host.Controls.Add(_clearCode);
        host.Controls.Add(line);
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Small.Height + 6,
            Text = Lang.T("Период", "Period"),
            Font = Theme.SmallBold,
            ForeColor = Theme.InkFaint
        });

        return host;
    }

    /// <summary>Първият месец не може да е след последния — списъкът се държи наред сам.</summary>
    private void PeriodChanged()
    {
        if (_filling) return;

        if (_to.SelectedIndex < _from.SelectedIndex)
        {
            _to.SelectedIndex = _from.SelectedIndex;
            return;
        }

        Rebuild();
    }

    // ------------------------------------------------------------- обхват

    /// <summary>
    /// Показва кои отчети влизат и какъв период покрива всеки.
    ///
    /// Балансът е сбор от няколко книги и мълчи, ако две от тях покриват един
    /// и същ месец. Числото тогава изглежда наред — затова проверката е
    /// отделна и се вика нарочно.
    /// </summary>
    private void ShowPeriods()
    {
        var scope = Current();

        if (scope.Chosen.Count == 0)
        {
            MessageBox.Show(this,
                Lang.T("В баланса не влиза нито един отчет — няма периоди за проверка.",
                       "No report goes into the balance — there are no periods to check."),
                Lang.T("Няма отчети", "No reports"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var companies = _workspace.Companies.List();

        string Name(long? id) => id is null
            ? Lang.T("(без фирма)", "(no company)")
            : companies.FirstOrDefault(c => c.Id == id)?.Name ?? id.Value.ToString(CultureInfo.InvariantCulture);

        using var dialog = new BalanceReportsDialog(
            _source.Periods(scope.Chosen, Name), scope.Year, scope.From, scope.To);

        var open = 0L;
        dialog.OpenRequested += (_, id) => open = id;

        dialog.ShowDialog(this);

        if (open != 0) Open(open);
    }

    /// <summary>Отваря отчет и чете наново: в него може да е пипано точно това, което гледаме.</summary>
    private void Open(long documentId)
    {
        try
        {
            using var editor = new Editor.EditorForm(_service, _workspace, documentId);
            editor.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                Lang.T("Отчетът не можа да се отвори", "The report could not be opened"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        Refresh();
    }

    /// <summary>
    /// Връща обхвата към подразбиращия се: всички фирми, цялата година, без
    /// чернова и без сито по код.
    ///
    /// Ситата се трупат едно по едно — отметка тук, щракване там — и в един
    /// момент числото на екрана отговаря на въпрос, който никой не помни, че е
    /// задал. Копчето се вижда само когато има какво да изчисти.
    /// </summary>
    private void ClearFilters()
    {
        _filling = true;

        try
        {
            for (var i = 0; i < _companies.Items.Count; i++) _companies.SetItemChecked(i, true);

            _from.SelectedIndex = 0;
            _to.SelectedIndex = BalanceMath.Months - 1;
            _draft.SelectedIndex = 0;

            _code = string.Empty;
            _clearCode.Visible = false;
        }
        finally
        {
            _filling = false;
        }

        Rebuild();
    }

    /// <summary>Има ли изобщо какво да се изчиства.</summary>
    private bool Filtered(Scope scope) =>
        !scope.WholeYear ||
        scope.Code.Length > 0 ||
        scope.Draft is not null ||
        scope.Companies.Count != _companies.Items.Count;

    // ------------------------------------------------------- щракване по таблото

    /// <summary>
    /// Щракнат код: таблицата се свежда до него.
    ///
    /// Таблото отговаря на „кое“; таблицата — на „кога и колко“. Затова
    /// щракването по код води и до другия раздел: въпросът вече е зададен.
    /// </summary>
    private void PickCode(string code)
    {
        _code = code;

        _clearCode.Text = code.Length == 0
            ? string.Empty
            : Lang.T($"само {code} ×", $"only {code} ×");

        _clearCode.Visible = code.Length > 0;

        if (code.Length > 0) UseTab(dashboard: false);
        else Rebuild();
    }

    /// <summary>Щракнат месец: периодът става този месец, а повторното щракване го връща.</summary>
    private void PickMonth(int month)
    {
        var single = _from.SelectedIndex == month - 1 && _to.SelectedIndex == month - 1;

        _filling = true;

        _from.SelectedIndex = single ? 0 : month - 1;
        _to.SelectedIndex = single ? BalanceMath.Months - 1 : month - 1;

        _filling = false;

        Rebuild();
    }

    /// <summary>Щракнато едро число: води към раздела, от който идва.</summary>
    private void PickSheet(BalanceSheet? sheet)
    {
        _view.SelectedIndex = sheet is null
            ? BalanceSheets.All.Count
            : BalanceSheets.All.ToList().IndexOf(sheet.Value);

        UseTab(dashboard: false);
    }

    /// <summary>Поле с надпис над него — двете се местят като едно.</summary>
    private static Control Labelled(Control input, string caption, int width)
    {
        var host = new Panel { Dock = DockStyle.Left, Width = Theme.Scaled(width), BackColor = Theme.Canvas };

        input.Dock = input is CheckedListBox ? DockStyle.Fill : DockStyle.Top;

        host.Controls.Add(input);
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Small.Height + 6,
            Text = caption,
            Font = Theme.SmallBold,
            ForeColor = Theme.InkFaint
        });

        return host;
    }

    private static Control Spacer() => new Panel
    {
        Dock = DockStyle.Left,
        Width = Theme.Gap,
        BackColor = Theme.Canvas
    };

    private static AppButton Action(string text, string glyph, Action handler)
    {
        var button = Theme.GhostButton(text, glyph);
        button.Height = Theme.Scaled(40);
        button.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        button.Click += (_, _) => handler();

        return button.FitWidth();
    }

    private Control BuildGrid()
    {
        var frame = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            Fill = Theme.Canvas,
            Outline = Theme.Border
        };

        _grid.Dock = DockStyle.Fill;
        _grid.Font = Theme.Base;
        _grid.BackgroundColor = Theme.Canvas;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.MultiSelect = true;
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.AllowUserToOrderColumns = false;

        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.InkStrong;
        _grid.ColumnHeadersDefaultCellStyle.Font = Theme.Bold;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 6, 6, 6);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        _grid.DefaultCellStyle.BackColor = Theme.Canvas;
        _grid.DefaultCellStyle.ForeColor = Theme.Ink;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentLight;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        _grid.DefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
        _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.Surface;
        _grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.AccentLight;
        _grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Theme.Ink;
        _grid.GridColor = Theme.Border;

        // Височините се мерят по шрифта, а не се оставят по подразбиране: при
        // мащаб 125 % заглавният ред реже надписите наполовина, а редовете
        // залепват един за друг.
        var line = Math.Max(1, _grid.Font.Height);

        _grid.RowTemplate.Height = line + 14;
        _grid.ColumnHeadersHeight = line + 22;

        frame.Controls.Add(_grid);

        return frame;
    }

    // ------------------------------------------------------------- пълнене

    /// <summary>Чете наново от базата: фирми, години, чернови — и после таблицата.</summary>
    public new void Refresh()
    {
        _filling = true;

        try
        {
            var all = _workspace.Documents.List();

            FillCompanies(all);
            FillYears(all);
            FillDrafts(all);
        }
        finally
        {
            _filling = false;
        }

        Rebuild();
        base.Refresh();
    }

    /// <summary>
    /// Пълни списъка с фирми.
    ///
    /// Заварените отчети нямат фирма — стъпката, която въведе фирмите, не е
    /// гадала чии са. Ред „(без фирма)“ се показва само когато такива има:
    /// избор, който винаги е празен, е по-лош от липсващ.
    ///
    /// Отметките преживяват презареждането, включително свалените: човек
    /// сваля фирма нарочно и не бива да я намери вдигната, само защото е
    /// сменил страницата. Ново заведена фирма обаче влиза отметната — иначе
    /// изглежда, че отчетите ѝ ги няма.
    /// </summary>
    private void FillCompanies(IReadOnlyList<DocumentSummary> all)
    {
        var companies = _workspace.Companies.List();

        var known = _companyIds;
        var chosen = Chosen();

        var orphans = all.Any(d => d.Report == BalanceSheets.Report && d.CompanyId is null);

        _companyIds = [.. companies.Select(c => (long?)c.Id)];
        if (orphans) _companyIds.Add(null);

        // Смяна на фирмата от лентата: отмята се само тя. Иначе отметките
        // преживяват презареждането — човек ги е нагласил нарочно.
        bool Ticked(long? id) => _onlyScope
            ? id == _scope.Current
            : !known.Contains(id) || chosen.Contains(id);

        _companies.BeginUpdate();
        _companies.Items.Clear();

        foreach (var company in companies)
            _companies.Items.Add(company.Display, Ticked(company.Id));

        if (orphans) _companies.Items.Add(Lang.T("(без фирма)", "(no company)"), Ticked(null));

        _companies.EndUpdate();

        _onlyScope = false;
    }

    private void FillYears(IReadOnlyList<DocumentSummary> all)
    {
        var chosen = _years.ElementAtOrDefault(_year.SelectedIndex);

        // Годините на двата източника заедно: превключването не бива да
        // оставя избраната година без място в списъка.
        var stock = _workspace.Companies.List().SelectMany(c => _workspace.Ledger.Read(c.Id));

        _years =
        [
            .. _source.Years(all, DateTime.Now.Year)
                .Union(StoreBalance.Years(stock, DateTime.Now.Year))
                .Distinct()
                .OrderByDescending(y => y)
        ];

        _year.BeginUpdate();
        _year.Items.Clear();

        foreach (var year in _years) _year.Items.Add(year.ToString(CultureInfo.InvariantCulture));

        _year.EndUpdate();

        var back = _years.IndexOf(chosen);
        _year.SelectedIndex = _years.Count == 0 ? -1 : Math.Max(0, back);
    }

    /// <summary>
    /// Черновите, между които се избира временното допълнение.
    ///
    /// Списъкът е на всички фирми, не само на отметнатите: човек сменя
    /// отметките, за да види как изглежда сборът, а изборът на чернова не
    /// бива да се губи при всяко движение.
    /// </summary>
    private void FillDrafts(IReadOnlyList<DocumentSummary> all)
    {
        var chosen = _draftIds.ElementAtOrDefault(_draft.SelectedIndex);
        var drafts = BalanceSource.Drafts(all);

        _draftIds = [null, .. drafts.Select(d => (long?)d.Id)];

        _draft.BeginUpdate();
        _draft.Items.Clear();
        _draft.Items.Add(Lang.T("(без незавършени)", "(no unfinished ones)"));

        foreach (var draft in drafts) _draft.Items.Add(draft.Title + "  ·  " + draft.StatusLabel);

        _draft.EndUpdate();

        var back = _draftIds.IndexOf(chosen);
        _draft.SelectedIndex = back < 0 ? 0 : back;
    }

    /// <summary>
    /// Заглавието на „Баланс“.
    ///
    /// Единственият екран, който може да събира няколко фирми — затова и
    /// единственият, чието заглавие не идва наготово от обхвата: то казва какво
    /// наистина е сборено. „Баланс — Метал ООД“ при една отметка, „Баланс — 3
    /// фирми“ при повече. Иначе сборът на три дружества би стоял под името на
    /// едното.
    /// </summary>
    private void Retitle(Scope scope)
    {
        var name = Lang.T("Баланс", "Balance");

        if (scope.Companies.Count == 1)
        {
            var chosen = scope.Companies[0];

            var company = chosen is null
                ? Lang.T("без фирма", "no company")
                : _workspace.Companies.Find(chosen.Value)?.Name ?? string.Empty;

            _pageTitle.Text = company.Length > 0 ? $"{name} — {company}" : name;

            return;
        }

        _pageTitle.Text = scope.Companies.Count > 1
            ? Lang.T($"{name} — {scope.Companies.Count} фирми", $"{name} — {scope.Companies.Count} companies")
            : name;
    }

    /// <summary>Отметнатите фирми. Празно значи, че нито една не е отметната.</summary>
    private List<long?> Chosen() =>
    [
        .. _companies.CheckedIndices
            .Cast<int>()
            .Where(i => i >= 0 && i < _companyIds.Count)
            .Select(i => _companyIds[i])
    ];

    // ------------------------------------------------------------- обхват

    /// <summary>
    /// Какво точно се гледа в момента.
    ///
    /// Едно и също за екрана и за изнасянето — иначе изнесеният файл рано или
    /// късно ще съдържа нещо, което на екрана не се вижда.
    /// </summary>
    private sealed record Scope(
        int Year,
        List<long?> Companies,
        long? Draft,
        IReadOnlyList<DocumentSummary> Chosen,
        int From,
        int To,
        string Code,
        bool Store = false,
        IReadOnlyList<Move>? Moves = null)
    {
        /// <summary>Движенията от склада на отметнатите фирми — празно при отчетите.</summary>
        public IReadOnlyList<Move> Stock => Moves ?? [];

        /// <summary>Дали изобщо има от какво да се смята.</summary>
        public bool HasData => Store ? Stock.Count > 0 : Chosen.Count > 0;

        /// <summary>Цялата година ли се гледа.</summary>
        public bool WholeYear => From == 1 && To == BalanceMath.Months;
    }

    private Scope Current()
    {
        // Списъкът с години винаги съдържа поне текущата, затова тук не може
        // да няма избрана: нова книга още няма нито ред, а балансът ѝ трябва
        // отсега.
        var year = _years.ElementAtOrDefault(_year.SelectedIndex);

        var companies = Chosen();
        var draft = _draftIds.ElementAtOrDefault(_draft.SelectedIndex);

        // Нито една отметка значи непосочен обхват, а не „всички“. За четенето
        // празният списък значи всички — това е разумното му подразбиране, —
        // затова тук изборът се спира преди него.
        var store = Store;

        var chosen = companies.Count == 0 || store
            ? []
            : BalanceSource.Eligible(_workspace.Documents.List(), companies, draft);

        // Складът се води по фирма и по площадка; балансът събира всички
        // площадки на отметнатите фирми, както и отчетът ги събира.
        IReadOnlyList<Move> moves = store
            ? [.. companies.Where(c => c is not null).SelectMany(c => _workspace.Ledger.Read(c!.Value))]
            : [];

        return new Scope(year, companies, store ? null : draft, chosen,
            _from.SelectedIndex + 1, _to.SelectedIndex + 1, _code, store, moves);
    }

    /// <summary>
    /// Началните количества за обхвата.
    ///
    /// Отметнато само „(без фирма)“ няма свои: началните количества се водят
    /// по фирма. Празният списък обаче значи „всички“ за хранилището, затова
    /// отговорът е тук, а не там.
    /// </summary>
    private Dictionary<string, decimal> Openings(Scope scope)
    {
        // По склада началото не се въвежда: то е завареното и всичко, записано
        // преди годината.
        if (scope.Store) return StoreBalance.Openings(scope.Stock, scope.Year);

        long[] ids = [.. scope.Companies.Where(c => c is not null).Select(c => c!.Value)];

        return _options.FromPreviousYear
            ? PreviousClosing(scope, ids)
            : Manual(scope.Year, ids);
    }

    private Dictionary<string, decimal> Manual(int year, long[] ids) =>
        ids.Length == 0
            ? new Dictionary<string, decimal>(StringComparer.Ordinal)
            : _workspace.Openings.Totals(year, ids);

    /// <summary>
    /// Салдото в края на предходната година, взето за начално на тази.
    ///
    /// Това е, което човек без въведени начални количества всъщност иска:
    /// каквото е останало на площадката на 31 декември, стои там и на 1
    /// януари. Програмата вече го знае — няма причина да се преписва през
    /// Excel.
    ///
    /// Предходната година се смята като приключена: незавършена книга в нея не
    /// участва, дори да е избрана за тази. А нейните собствени начални
    /// количества се вземат от въведените — веригата е една стъпка назад, не
    /// безкрайна.
    /// </summary>
    private Dictionary<string, decimal> PreviousClosing(Scope scope, long[] ids)
    {
        var year = scope.Year - 1;

        var chosen = scope.Companies.Count == 0
            ? []
            : BalanceSource.Eligible(_workspace.Documents.List(), scope.Companies, draft: null);

        var table = BalanceMath.Build(_source.Read(chosen, year), Manual(year, ids), sheet: null);

        return table.Lines
            .Where(line => line.Closing != 0)
            .ToDictionary(line => line.Ewc, line => line.Closing, StringComparer.Ordinal);
    }

    /// <summary>Редовете на баланса — от отчетите или от склада.</summary>
    private IReadOnlyList<BalanceEntry> Entries(Scope scope) =>
        scope.Store
            ? StoreBalance.Entries(scope.Stock, scope.Year)
            : _source.Read(scope.Chosen, scope.Year);

    /// <summary>
    /// Един изглед, пресметнат от вече прочетените движения.
    ///
    /// Периодът и ситото по код се прилагат ТУК, а не при рисуването: така
    /// таблицата, таблото и изнесеният файл гледат едно и също, вместо всеки
    /// да свежда сам.
    /// </summary>
    private BalanceTable Table(IReadOnlyList<BalanceEntry> entries, Scope scope, BalanceSheet? sheet)
    {
        var table = BalanceMath.Build(entries, sheet is null ? Openings(scope) : null, sheet);

        return BalanceMath.Only(BalanceMath.Window(table, scope.From, scope.To), scope.Code);
    }

    /// <summary>Имената на отметнатите фирми — за надписа и за изнесения файл.</summary>
    private List<string> CompanyNames(Scope scope)
    {
        var companies = _workspace.Companies.List();

        return
        [
            .. scope.Companies.Select(id => id is null
                ? Lang.T("(без фирма)", "(no company)")
                : companies.FirstOrDefault(c => c.Id == id)?.Display ?? id.Value.ToString(CultureInfo.InvariantCulture))
        ];
    }

    // ------------------------------------------------------------- таблица

    /// <summary>Смята наново и рисува. Не пипа базата извън четенето.</summary>
    private void Rebuild()
    {
        if (_filling || IsDisposed) return;

        var scope = Current();
        var entries = Entries(scope);

        Retitle(scope);

        _clearFilters.Visible = Filtered(scope);

        if (_tab.SelectedIndex == 1)
        {
            Draw(scope, entries);
            return;
        }

        var sheet = _view.SelectedIndex < BalanceSheets.All.Count
            ? BalanceSheets.All[_view.SelectedIndex]
            : (BalanceSheet?)null;

        var table = Table(entries, scope, sheet);

        Fill(table, sheet, scope, new BalanceMarking(table, Highlight(scope, sheet), _mode.SelectedIndex == 1));
        Describe(table, scope, sheet);
    }

    /// <summary>
    /// Какво да се оцветява и кои клетки идват от незавършената книга.
    ///
    /// Докоснатите клетки се питат от самата чернова, вместо таблицата да се
    /// смята два пъти и да се сравнява: отговорът е същият и струва една
    /// обиколка по редовете ѝ.
    /// </summary>
    private BalanceHighlight Highlight(Scope scope, BalanceSheet? sheet)
    {
        var draft = scope.Store ? null : scope.Chosen.FirstOrDefault(d => d.Id == scope.Draft);

        var touched = draft is null
            ? new HashSet<(string, int)>()
            : BalanceMath.Touched(_source.Read([draft], scope.Year), sheet);

        return new BalanceHighlight { Marks = _options.Marks, Draft = touched };
    }

    /// <summary>Пълни таблото със същите числа, които рисува и таблицата.</summary>
    private void Draw(Scope scope, IReadOnlyList<BalanceEntry> entries)
    {
        var cross = Table(entries, scope, sheet: null);

        var sheets = BalanceSheets.All.ToDictionary(
            sheet => sheet,
            sheet => Table(entries, scope, sheet));

        var stats = BalanceStats.Build(
            scope.Year, scope.Store ? (scope.HasData ? 1 : 0) : scope.Chosen.Count, cross, sheets, Names());

        // Кои плочки се виждат се пита при всяко пресмятане, а не само при
        // промяна в панела: така таблото няма второ копие на избора, което да
        // остарее.
        _dashboard.ShowTiles(_options.Tiles);

        _dashboard.SetStats(
            stats,
            scope.Store ? [StoreSource()] : [.. scope.Chosen.Select(d => d.Title)],
            cross.Lines.ToDictionary(line => line.Ewc, line => line.Movement.ToArray(), StringComparer.Ordinal));

        Describe(cross, scope, sheet: null);
    }

    /// <summary>
    /// Избира изглед и вид на числата — ползва се от тестовете.
    ///
    /// Показалецът е този на превключвателя: разделите по реда на пътя през
    /// площадката, а последният е кръстосаният изглед.
    /// </summary>
    internal void UseView(int view, bool running)
    {
        _view.SelectedIndex = view;
        _mode.SelectedIndex = running ? 1 : 0;

        Rebuild();
    }

    /// <summary>
    /// F6 свива и отваря панела отдясно.
    ///
    /// Същият клавиш, с който се свива и страничният панел в редактора: две
    /// различни панели, но един и същ въпрос — „махни това за малко“.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (keys == Keys.F6)
        {
            _options.Collapsed = !_options.Collapsed;
            return true;
        }

        return base.ProcessCmdKey(ref message, keys);
    }

    /// <summary>Свързаните колони — ползва се от тестовете.</summary>
    internal ColumnLinks Links => _links;

    /// <summary>Сменя ширината на колона, както влачене по ръба — ползва се от тестовете.</summary>
    internal void UseWidth(string column, int width)
    {
        if (_grid.Columns.Contains(column)) _grid.Columns[column].Width = width;
    }

    /// <summary>Сменя източника — отчети или склад — ползва се от тестовете.</summary>
    internal void UseStore(bool store)
    {
        _origin.SelectedIndex = store ? 0 : 1;
        SwitchOrigin();
    }

    /// <summary>Какво пише под лентата — ползва се от тестовете.</summary>
    internal string SummaryText => _summary.Text;

    /// <summary>Сменя таблицата с таблото — ползва се от тестовете.</summary>
    internal void UseTab(bool dashboard)
    {
        _tab.SelectedIndex = dashboard ? 1 : 0;
        SwitchTab();
    }

    /// <summary>Задава период по месеци — ползва се от тестовете.</summary>
    internal void UsePeriod(int from, int to)
    {
        _filling = true;

        _from.SelectedIndex = Math.Clamp(from, 1, BalanceMath.Months) - 1;
        _to.SelectedIndex = Math.Clamp(to, from, BalanceMath.Months) - 1;

        _filling = false;

        Rebuild();
    }

    /// <summary>Свежда до един код, както го прави щракването по таблото — за тестовете.</summary>
    internal void UseCode(string code) => PickCode(code);

    /// <summary>Кодът, до който е сведено — ползва се от тестовете.</summary>
    internal string Code => _code;

    /// <summary>Избира незавършена книга по началото на заглавието ѝ — за тестовете.</summary>
    internal bool UseDraft(string title)
    {
        var index = _draft.Items.Cast<object>().ToList()
            .FindIndex(item => (item?.ToString() ?? string.Empty).StartsWith(title, StringComparison.Ordinal));

        if (index < 0) return false;

        _draft.SelectedIndex = index;

        return true;
    }

    /// <summary>Панелът с настройките — ползва се от тестовете.</summary>
    internal BalanceOptions Options => _options;

    /// <summary>Заглавието на екрана — ползва се от тестовете.</summary>
    internal string TitleText => _pageTitle.Text;

    /// <summary>Таблото — ползва се от тестовете.</summary>
    internal BalanceDashboard Dashboard => _dashboard;

    /// <summary>Фонът на клетка от месец — ползва се от тестовете.</summary>
    internal Color CellColour(int row, int month) =>
        row >= 0 && row < _grid.Rows.Count && _grid.Columns.Contains("m" + month)
            ? _grid.Rows[row].Cells["m" + month].Style.BackColor
            : Color.Empty;

    /// <summary>Отмята или сваля фирма по ред в списъка — ползва се от тестовете.</summary>
    internal void TickCompany(int index, bool ticked)
    {
        if (index < 0 || index >= _companies.Items.Count) return;

        _companies.SetItemChecked(index, ticked);
        Rebuild();
    }

    /// <summary>Вдига или сваля всички отметки — ползва се от тестовете.</summary>
    internal void UseAllCompanies(bool ticked) => TickAll(ticked);

    /// <summary>Колко фирми са отметнати — ползва се от тестовете.</summary>
    internal int TickedCompanies => Chosen().Count;

    /// <summary>Редовете в списъка с фирми — ползва се от тестовете.</summary>
    internal List<string> CompanyChoices() =>
        [.. _companies.Items.Cast<object>().Select(item => item.ToString() ?? string.Empty)];

    /// <summary>Заглавията на колоните — ползва се от тестовете.</summary>
    internal List<string> ColumnHeaders() =>
        [.. _grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText)];

    /// <summary>Редовете такива, каквито се четат на екрана — ползва се от тестовете.</summary>
    internal List<string> RowTexts() =>
    [
        .. _grid.Rows.Cast<DataGridViewRow>().Select(row => string.Join(" | ",
            row.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? string.Empty)))
    ];

    private void Describe(BalanceTable table, Scope scope, BalanceSheet? sheet)
    {
        var (year, draft, chosen) = (scope.Year, scope.Draft, scope.Chosen);
        var included = draft is not null && chosen.Any(d => d.Id == draft);

        if (scope.Companies.Count == 0)
        {
            _summary.Text = Lang.T(
                "Не е отметната нито една фирма." + Environment.NewLine +
                "Балансът се води по фирма; отметнете една или няколко, за да се съберат заедно.",

                "No company is ticked." + Environment.NewLine +
                "The balance is kept per company; tick one or several to add them up.");

            _summary.ForeColor = Theme.InkMuted;
            return;
        }

        if (scope.Store && !scope.HasData)
        {
            _summary.Text = Lang.T(
                $"В склада няма движения по избраните фирми." + Environment.NewLine +
                "Балансът по склада тръгва с първото приемане или със завареното.",

                $"There are no store movements for the chosen companies." + Environment.NewLine +
                "The store balance starts with the first receipt or with the opening stock.");

            _summary.ForeColor = Theme.InkMuted;
            return;
        }

        if (!scope.Store && chosen.Count == 0)
        {
            _summary.Text = Lang.T(
                $"За {year} г. няма проверена отчетна книга за оползотворяване по избраните фирми." +
                Environment.NewLine +
                "Отчетът влиза в баланса, щом мине проверката — или ако се избере тук като незавършен.",

                $"There is no checked recovery and disposal book for {year} for the chosen companies." +
                Environment.NewLine +
                "A report joins the balance once it passes the check — or if it is picked here as unfinished.");

            _summary.ForeColor = Theme.InkMuted;
            return;
        }

        var what = sheet is null
            ? Lang.T("образуван минус предаден", "generated minus submitted")
            : BalanceSheets.Title(sheet.Value);

        var when = scope.WholeYear
            ? Lang.T($"{year} г.", $"{year}")
            : Lang.T($"{BalanceSheets.Month(scope.From)}–{BalanceSheets.Month(scope.To)} {year} г.",
                     $"{BalanceSheets.Month(scope.From)}–{BalanceSheets.Month(scope.To)} {year}");

        var lines = new List<string>
        {
            scope.Store
                ? Lang.T($"{when} · {what} · по склада, към днес · {table.Lines.Count} кода",
                         $"{when} · {what} · from the store, as of today · {table.Lines.Count} codes")
                : Lang.T($"{when} · {what} · {chosen.Count} отчета · {table.Lines.Count} кода",
                         $"{when} · {what} · {chosen.Count} reports · {table.Lines.Count} codes")
        };

        if (scope.Code.Length > 0)
        {
            lines.Add(Lang.T($"Показва се само код {scope.Code} — махнете ситото, за да се върнат останалите.",
                             $"Only code {scope.Code} is shown — clear the filter to bring the rest back."));
        }

        if (sheet is null && table.Totals.Opening != 0)
        {
            // При стеснен период „начално“ е салдото, с което периодът тръгва,
            // а не онова от 1 януари — надписът го казва, за да не се сверява
            // с друго число.
            lines.Add(scope.WholeYear
                ? scope.Store
                    ? Lang.T($"Начално салдо по склада — заварено и записаното преди {year} г.: {Amount(table.Totals.Opening)} т.",
                             $"Opening balance from the store — opening stock and what was written before {year}: {Amount(table.Totals.Opening)} t.")
                    : _options.FromPreviousYear
                    ? Lang.T($"Начално салдо, пренесено от {year - 1} г.: {Amount(table.Totals.Opening)} т.",
                             $"Opening balance carried over from {year - 1}: {Amount(table.Totals.Opening)} t.")
                    : Lang.T($"Начално салдо (въведено за годината): {Amount(table.Totals.Opening)} т.",
                             $"Opening balance (entered for the year): {Amount(table.Totals.Opening)} t.")
                : Lang.T($"В началото на периода: {Amount(table.Totals.Opening)} т.",
                         $"At the start of the period: {Amount(table.Totals.Opening)} t."));
        }

        var closing = table.Totals.Running[scope.To - 1];

        if (sheet is null)
        {
            lines.Add(scope.WholeYear
                ? Lang.T($"В края на годината остават {Amount(closing)} т.",
                         $"{Amount(closing)} t remain at the end of the year.")
                : Lang.T($"В края на периода остават {Amount(closing)} т.",
                         $"{Amount(closing)} t remain at the end of the period."));
        }
        else
        {
            lines.Add(scope.WholeYear
                ? Lang.T($"Общо за годината: {Amount(table.Totals.Total)} т.",
                         $"{Amount(table.Totals.Total)} t for the year in total.")
                : Lang.T($"Общо за периода: {Amount(table.Totals.Total)} т.",
                         $"{Amount(table.Totals.Total)} t for the period in total."));
        }

        if (included)
        {
            lines.Add(Lang.T(
                "Включена е и една незавършена книга — числата ще се променят, докато тя се затвори.",
                "One unfinished book is included — these numbers will change until it is closed."));
        }

        _summary.Text = string.Join(Environment.NewLine, lines);
        _summary.ForeColor = included ? Theme.Warn : Theme.InkMuted;
    }

    private void Fill(BalanceTable table, BalanceSheet? sheet, Scope scope, BalanceMarking marking)
    {
        var running = _mode.SelectedIndex == 1;
        var cross = sheet is null;

        _grid.SuspendLayout();
        _grid.Rows.Clear();
        _grid.Columns.Clear();
        _links.Begin();

        AddColumn(CodeColumn, Lang.T("Код на отпадъка", "Waste code"), 300, left: true);

        // Началното количество е налично количество и има смисъл само в
        // кръстосания изглед; един раздел поотделно е движение, не наличност.
        if (cross) AddColumn(OpeningColumn, Lang.T("Начално", "Opening"), 86);

        // Тесни нарочно: с отворен панел отдясно четиринайсет широки колони
        // изяждат колоната с кода и от него остава „1601…“.
        //
        // Показват се само месеците от периода: колона, в която по устройство
        // няма как да има число, само стеснява останалите.
        for (var month = scope.From; month <= scope.To; month++)
            AddColumn("m" + month, BalanceSheets.Month(month), 64);

        AddColumn(TotalColumn,
            running ? Lang.T("Край", "Closing") : Lang.T("Общо", "Total"), 92);

        foreach (var line in table.Lines) AddRow(line, cross, running, total: false, scope, marking);

        // Сборният ред не се оцветява: той е сбор на редове, а не наблюдение,
        // и оцветен изглежда като поредното число, което иска внимание.
        //
        // При сито по един код го няма изобщо: сборът на един ред е самият ред,
        // а показан отделно изглежда като второ число.
        if (table.Lines.Count > 0 && scope.Code.Length == 0)
            AddRow(table.Totals, cross, running, total: true, scope, marking: null);

        _links.Apply();

        _grid.ResumeLayout();

        // Без това първата клетка стои избрана и изглежда като нещо, което
        // човекът е посочил. Тук нищо не се избира — таблицата само показва.
        // След ResumeLayout, защото подредбата връща текущата клетка обратно.
        _grid.CurrentCell = null;
        _grid.ClearSelection();
    }

    private void AddColumn(string name, string header, int width, bool left = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = Theme.Scaled(width),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.True
        };

        if (left)
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.FillWeight = 240;

            // Долна граница: колоната се разтяга по остатъка, а остатъкът при
            // отворен панел и дванайсет месеца е няколко пиксела. Кодът обаче е
            // единственото, по което се разпознава редът.
            column.MinimumWidth = Theme.Scaled(240);
        }

        _grid.Columns.Add(column);
    }

    private void AddRow(
        BalanceLine line, bool cross, bool running, bool total, Scope scope, BalanceMarking? marking)
    {
        var index = _grid.Rows.Add();
        var row = _grid.Rows[index];

        row.Cells[CodeColumn].Value = total
            ? Lang.T("Общо", "Total")
            : Describe(line.Ewc);

        if (cross) row.Cells[OpeningColumn].Value = Amounts.Cell(line.Opening);

        for (var month = scope.From; month <= scope.To; month++)
        {
            var value = running ? line.Running[month - 1] : line.Movement[month - 1];
            var cell = row.Cells["m" + month];

            cell.Value = Amounts.Cell(value);
            Paint(cell, marking, line, month, value);
        }

        // В края на периода, не в края на годината: извън прозореца движения
        // няма, затова салдото на последния му месец е и краят му.
        var end = running ? line.Running[scope.To - 1] : line.Total;
        var last = row.Cells[TotalColumn];

        last.Value = Amounts.Cell(end);

        if (marking is not null) Paint(last, marking.Total(line, out var heat), heat, end);
        else if (end < 0) last.Style.ForeColor = Theme.Error;

        row.DefaultCellStyle.Font = total ? Theme.Bold : Theme.Base;

        // Залежалият отпадък е свойство на целия ред: цялата година е един и
        // същ отговор, а не отделно наблюдение по месеци.
        if (marking?.Idle(line) == true) row.DefaultCellStyle.ForeColor = Theme.InkFaint;

        if (!total) return;

        row.DefaultCellStyle.BackColor = Theme.SurfaceAlt;
        row.DefaultCellStyle.SelectionBackColor = Theme.AccentLight;
    }

    private void Paint(DataGridViewCell cell, BalanceMarking? marking, BalanceLine line, int month, decimal value)
    {
        if (marking is null)
        {
            // Без оцветяване отрицателното пак се вижда: то не е украса, а
            // единственото число в таблицата, което значи грешка.
            if (value < 0) cell.Style.ForeColor = Theme.Error;
            return;
        }

        Paint(cell, marking.Tint(line, month, out var heat), heat, value);
    }

    /// <summary>
    /// Слага цвят на клетка.
    ///
    /// Фонът се взема от палитрата, за да следва темата; топлината се смесва с
    /// платното, а не се слага плътно — иначе числото под нея изчезва.
    /// </summary>
    private static void Paint(DataGridViewCell cell, BalanceTint tint, double heat, decimal value)
    {
        switch (tint)
        {
            case BalanceTint.Negative:
                cell.Style.BackColor = Theme.ErrorSoft;
                cell.Style.ForeColor = Theme.Error;
                break;

            case BalanceTint.Draft:
                cell.Style.BackColor = Theme.WarnSoft;
                break;

            case BalanceTint.Peak:
                cell.Style.BackColor = Theme.AccentSoft;
                break;

            case BalanceTint.Heat:
                var warm = Blend(Theme.Canvas, Theme.Accent, heat * 0.55);

                cell.Style.BackColor = warm;

                // Върху тъмно мастилото на темата изчезва. Плътността се мери
                // и текстът минава на светло — иначе най-важните клетки в
                // таблицата са и единствените нечетими.
                cell.Style.ForeColor = Readable(warm);
                cell.Style.SelectionForeColor = Readable(warm);
                break;

            default:
                if (value < 0) cell.Style.ForeColor = Theme.Error;
                break;
        }
    }

    /// <summary>
    /// Мастило, което се чете върху този фон.
    ///
    /// Мери се възприеманата яркост, а не средното от трите канала: окото
    /// вижда зеленото далеч по-светло от синьото и средното лъже точно при
    /// тъмносиньото, с което са боядисани най-големите клетки.
    /// </summary>
    private static Color Readable(Color background)
    {
        var light = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;

        return light < 0.55 ? Theme.OnAccent : Theme.Ink;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        var mix = Math.Clamp(amount, 0, 1);

        int Channel(int a, int b) => (int)Math.Round(a + (b - a) * mix);

        return Color.FromArgb(
            Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    /// <summary>Номенклатурата по код — строи се при първото питане.</summary>
    private Dictionary<string, string> Names() => _names ??= ReadNames();

    /// <summary>Кодът заедно с номенклатурата — „161004“ не значи нищо само.</summary>
    private string Describe(string ewc)
    {
        _names ??= ReadNames();

        return _names.TryGetValue(ewc, out var name) && name.Length > 0
            ? ewc + " — " + name
            : ewc;
    }

    /// <summary>
    /// Имената на кодовете, прочетени от схемата.
    ///
    /// Веднъж: списъкът е над осемстотин реда и е един и същ за цялата
    /// програма — схемата се чете при стартиране и не се мени.
    /// </summary>
    private Dictionary<string, string> ReadNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var option in EwcOptions())
            names[option.Value] = option.Description ?? string.Empty;

        return names;
    }

    private IReadOnlyList<EnumOption> EwcOptions() =>
        _service.Schemas
            .FirstOrDefault(s => s.Key == "book")?
            .FindReport(BalanceSheets.Report)?
            .SelfAndDescendants()
            .FirstOrDefault(n => n.ElementName == BalanceSheets.Node(BalanceSheet.Received))?
            .Attributes.FirstOrDefault(a => a.Name == "ewc")?
            .Domain.Options ?? [];

    /// <summary>Количество за надписа под ситото — виж <see cref="Amounts"/>.</summary>
    private static string Amount(decimal value) => Amounts.Text(value);

    // ------------------------------------------------------------- изнасяне

    /// <summary>
    /// Изнася баланса в Excel.
    ///
    /// Изнася се ЦЕЛИЯТ обхват, а не показаният изглед: всичките пет раздела и
    /// в двата вида — движение и салдо. Кое копче е било натиснато в момента
    /// на изнасянето не е свойство на данните, а файлът се отваря след месец
    /// от човек, който не е бил пред екрана.
    ///
    /// Отметките обаче важат изцяло: изнася се това, което се вижда — една
    /// фирма или няколко, събрани заедно.
    /// </summary>
    private void Export()
    {
        var scope = Current();

        if (scope.Companies.Count == 0)
        {
            MessageBox.Show(this,
                Lang.T("Отметнете поне една фирма — балансът се води по фирма.",
                       "Tick at least one company — the balance is kept per company."),
                Lang.T("Няма избрана фирма", "No company chosen"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!scope.HasData)
        {
            MessageBox.Show(this,
                Lang.T($"За {scope.Year} г. няма какво да се изнесе по избраните фирми.",
                       $"There is nothing to export for {scope.Year} for the chosen companies."),
                Lang.T("Няма данни", "No data"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var save = new SaveFileDialog
        {
            Filter = Lang.T("Работна книга на Excel (*.xlsx)|*.xlsx", "Excel workbook (*.xlsx)|*.xlsx"),
            FileName = FileName(scope),
            InitialDirectory = _workspace.Settings.Get(SettingsStore.Keys.LastFolder, string.Empty)
        };

        if (save.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            BalanceWorkbook.Save(save.FileName, Build(scope));

            _workspace.Settings.Set(SettingsStore.Keys.LastFolder, Path.GetDirectoryName(save.FileName)!);

            if (MessageBox.Show(this,
                    Lang.T("Балансът е изнесен. Да се отвори ли сега?",
                           "The balance is exported. Open it now?"),
                    Lang.T("Готово", "Done"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(save.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                Lang.T("Балансът не беше изнесен", "The balance was not exported"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Как се назовава източникът „склад“ — на таблото и в изнесения файл.</summary>
    private static string StoreSource() =>
        Lang.T($"Склад — движенията към {DateTime.Today.ToString(ValueConverter.BulgarianDateFormat)}",
               $"Store — the movements as of {DateTime.Today.ToString(ValueConverter.BulgarianDateFormat)}");

    /// <summary>Сглобява изнасяното — отделено от прозореца, за да се проверява.</summary>
    private BalanceExport Build(Scope scope)
    {
        // Редовете се четат веднъж и се раздават на петте изгледа: те се
        // различават по знак и по сито, не по източник.
        var entries = Entries(scope);

        var draft = scope.Store ? null : scope.Chosen.FirstOrDefault(d => d.Id == scope.Draft);

        return new BalanceExport
        {
            Year = scope.Year,
            Companies = CompanyNames(scope),
            Reports = scope.Store ? [StoreSource()] : [.. scope.Chosen.Select(d => $"{d.Title}  ·  {d.StatusLabel}")],
            Names = Names(),
            Draft = draft?.Title,

            From = scope.From,
            To = scope.To,
            Code = scope.Code,

            Views =
            [
                .. BalanceSheets.All.Select(sheet => new BalanceExportView(
                    BalanceSheets.Title(sheet), Table(entries, scope, sheet), Opening: false,
                    Colours(scope, sheet))),

                new(Lang.T("Баланс", "Balance"), Table(entries, scope, null), Opening: true,
                    Colours(scope, null))
            ]
        };
    }

    /// <summary>
    /// Оцветяването, което да замине с изнесеното — или нищо.
    ///
    /// Отметката в панела решава: файл с цветове се чете по-бързо, но и се
    /// разпечатва по-скъпо, а някой ще го прати нататък като таблица за
    /// смятане, не за гледане.
    /// </summary>
    private BalanceHighlight? Colours(Scope scope, BalanceSheet? sheet) =>
        _options.ExportMarks ? Highlight(scope, sheet) : null;

    /// <summary>Изнесеното наготово — ползва се от тестовете.</summary>
    internal BalanceExport BuildExport() => Build(Current());

    /// <summary>
    /// Как се казва файлът.
    ///
    /// Името носи обхвата: три файла в една папка се различават по фирмата и
    /// по годината, а не по „(1)“ и „(2)“.
    /// </summary>
    private string FileName(Scope scope)
    {
        var names = CompanyNames(scope);

        var who = names.Count == 1
            ? names[0]
            : Lang.T($"{names.Count} фирми", $"{names.Count} companies");

        var when = scope.WholeYear
            ? scope.Year.ToString(CultureInfo.InvariantCulture)
            : $"{scope.Year} {BalanceSheets.Month(scope.From)}-{BalanceSheets.Month(scope.To)}";

        var name = Lang.T($"Баланс {when} — {who}", $"Balance {when} — {who}");

        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, ' ');

        return name.Trim() + ".xlsx";
    }

    // --------------------------------------------------- начални количества

    /// <summary>
    /// Показва въведените начални количества — само тях, без движенията.
    ///
    /// В баланса началното количество стои в една колона до дванайсет други и
    /// се губи; тук се вижда самò. Оттук се и поправя: количествата се внасят
    /// от файл, а после никой не помни какво е имало в него.
    /// </summary>
    private void ManageOpenings()
    {
        var companies = _workspace.Companies.List();

        if (companies.Count == 0)
        {
            MessageBox.Show(this,
                Lang.T("Първо заведете фирма в Настройки — началните количества се водят по фирма.",
                       "Enter a company in Settings first — opening quantities are kept per company."),
                Lang.T("Няма фирми", "No companies"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var before = _workspace.Openings.All();

        using var dialog = new OpeningsDialog(before, companies, Names(), Moved);

        var template = false;
        var import = false;

        dialog.TemplateRequested += (_, _) => template = true;
        dialog.ImportRequested += (_, _) => import = true;

        var answer = dialog.ShowDialog(this);

        if (answer == DialogResult.OK) Apply(before, dialog.Result);

        // Двете копчета за файл водят навън от прозореца: диалог върху диалог
        // с обща тема е повече, отколкото случаят иска.
        if (template) SaveTemplate();
        if (import) ImportOpenings();

        if (answer == DialogResult.OK || import) Refresh();
    }

    /// <summary>
    /// Движението по кодове за една фирма и година — справочната колона в
    /// прозореца.
    ///
    /// Гледат се само проверените отчети, както навсякъде в баланса: въпросът
    /// е „мръднало ли е нещо по този код“, а недовършена книга не отговаря на
    /// него.
    /// </summary>
    private IReadOnlyDictionary<string, decimal>? Moved(long company, int year)
    {
        var chosen = BalanceSource.Eligible(_workspace.Documents.List(), [company]);

        // Няма отчет за тази фирма и година: тогава „нищо не е мръднало“ не е
        // наблюдение, а липса на данни, и не бива да изглежда като подозрение.
        if (chosen.Count == 0) return null;

        var table = BalanceMath.Build(_source.Read(chosen, year));

        return table.Lines.ToDictionary(line => line.Ewc, line => line.Total, StringComparer.Ordinal);
    }

    /// <summary>Записва разликата: махнатите редове се трият, останалите се записват.</summary>
    private void Apply(IReadOnlyList<Opening> before, IReadOnlyList<Opening> after)
    {
        var kept = after.Select(o => (o.CompanyId, o.Year, o.Ewc)).ToHashSet();

        foreach (var gone in before.Where(o => !kept.Contains((o.CompanyId, o.Year, o.Ewc))))
            _workspace.Openings.Remove(gone.CompanyId, gone.Year, gone.Ewc);

        foreach (var opening in after)
            _workspace.Openings.Put(opening.CompanyId, opening.Year, opening.Ewc, opening.Amount);
    }

    /// <summary>
    /// Записва шаблона за начални количества.
    ///
    /// Вече въведеното влиза във файла: така той е и шаблон, и извадка — човек
    /// поправя две числа и го връща, вместо да преписва сто реда наново.
    /// </summary>
    private void SaveTemplate()
    {
        var companies = _workspace.Companies.List();

        if (companies.Count == 0)
        {
            MessageBox.Show(this,
                Lang.T("Първо заведете фирма в Настройки — началните количества се водят по фирма.",
                       "Enter a company in Settings first — opening quantities are kept per company."),
                Lang.T("Няма фирми", "No companies"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var year = _years.ElementAtOrDefault(_year.SelectedIndex);
        if (year == 0) year = DateTime.Now.Year;

        using var save = new SaveFileDialog
        {
            Filter = Lang.T("Работна книга на Excel (*.xlsx)|*.xlsx", "Excel workbook (*.xlsx)|*.xlsx"),
            FileName = Lang.T($"Начални количества {year}.xlsx", $"Opening quantities {year}.xlsx"),
            InitialDirectory = _workspace.Settings.Get(SettingsStore.Keys.LastFolder, string.Empty)
        };

        if (save.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var byId = companies.ToDictionary(c => c.Id, c => c.Eik);

            var existing = _workspace.Openings.All()
                .Where(o => byId.ContainsKey(o.CompanyId))
                .Select(o => new OpeningRow(0, byId[o.CompanyId], o.Ewc, o.Year, o.Amount))
                .ToList();

            OpeningWorkbook.Save(
                save.FileName,
                year,
                [.. companies.Select(c => new OpeningCompany(c.Eik, c.Name))],
                [.. EwcOptions().Select(o => o.Value)],
                existing);

            _workspace.Settings.Set(SettingsStore.Keys.LastFolder, Path.GetDirectoryName(save.FileName)!);

            if (MessageBox.Show(this,
                    Lang.T("Шаблонът е готов. Да се отвори ли сега?",
                           "The template is ready. Open it now?"),
                    Lang.T("Готово", "Done"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(save.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                Lang.T("Шаблонът не беше създаден", "The template was not created"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Внася попълнения файл.
    ///
    /// Редовете с непозната фирма не спират останалите: при сто реда един
    /// сбъркан ЕИК не бива да отмени деветдесет и деветте верни. Какво е
    /// пропуснато, се казва накрая.
    /// </summary>
    private void ImportOpenings()
    {
        using var open = new OpenFileDialog
        {
            Filter = Lang.T("Работна книга на Excel (*.xlsx)|*.xlsx", "Excel workbook (*.xlsx)|*.xlsx"),
            InitialDirectory = _workspace.Settings.Get(SettingsStore.Keys.LastFolder, string.Empty)
        };

        if (open.ShowDialog(this) != DialogResult.OK) return;

        OpeningFile file;

        try
        {
            file = OpeningWorkbook.Read(open.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                Lang.T("Файлът не беше прочетен", "The file was not read"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var problems = new List<string>(file.Problems);
        var openings = new List<Opening>();

        foreach (var row in file.Rows)
        {
            if (_workspace.Companies.FindByEik(row.Eik) is not { } company)
            {
                problems.Add(Lang.T(
                    $"ред {row.ExcelRow}: няма заведена фирма с ЕИК {row.Eik}.",
                    $"row {row.ExcelRow}: no company with UIC {row.Eik} is entered."));

                continue;
            }

            openings.Add(new Opening(company.Id, row.Year, row.Ewc, row.Amount));
        }

        var written = _workspace.Openings.Replace(openings);

        _workspace.Settings.Set(SettingsStore.Keys.LastFolder, Path.GetDirectoryName(open.FileName)!);

        Refresh();

        var text = Lang.T($"Внесени са {written} начални количества.",
                          $"{written} opening quantities were imported.");

        if (problems.Count > 0)
        {
            text += Environment.NewLine + Environment.NewLine +
                    Lang.T($"Пропуснати редове: {problems.Count}", $"Rows skipped: {problems.Count}") +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, problems.Take(10)) +
                    (problems.Count > 10 ? Environment.NewLine + "…" : string.Empty);
        }

        MessageBox.Show(this, text,
            Lang.T("Начални количества", "Opening quantities"),
            MessageBoxButtons.OK,
            problems.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }
}

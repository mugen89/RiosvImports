using System.Globalization;
using RiosvImports.App.Controls;
using RiosvImports.Core;
using RiosvImports.Core.Excel;
using RiosvImports.Core.Help;
using RiosvImports.Core.Balance;
using RiosvImports.Core.Import;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;
using RiosvImports.Data;

namespace RiosvImports.App.Editor;

/// <summary>
/// Работният екран за един отчет.
///
/// Тук отпада обиколката през Excel: същите таблици се попълват на място, а
/// проверката минава през същия път, по който минава и готовият файл. Затова
/// казаното тук и казаното при генерирането не могат да се разминат — това е
/// една и съща проверка, извикана по-рано.
///
/// Записът е в местната база и става сам: работа се губи от изключен ток, не
/// от забравен бутон.
/// </summary>
internal sealed class EditorForm : Dialog
{
    private readonly ImportService _service;
    private readonly Workspace _workspace;
    private readonly SchemaDefinition _schema;
    private readonly ReportNode _report;
    private readonly long _documentId;

    private readonly List<SheetGrid> _grids = [];
    private readonly Dictionary<string, Control> _headerFields = new(StringComparer.Ordinal);

    /// <summary>Заглавни стойности без избор — попълват се сами и не се показват.</summary>
    private readonly Dictionary<string, string> _fixedHeader = new(StringComparer.Ordinal);

    private readonly TabStrip _sheets = new();

    /// <summary>„Провери“ и „Създай файл“ — стоят върху реда с разделите.</summary>
    private readonly FlowLayoutPanel _actions = new();

    /// <summary>Страничният панел с помощните полета. Скрит е по подразбиране.</summary>
    private readonly TransferPanel _side;

    private readonly IssueList _issues = new();
    private readonly Label _summary = new();
    private readonly ToolStripStatusLabel _status = new();
    private readonly System.Windows.Forms.Timer _autosave = new() { Interval = 20_000 };

    private ImportResult? _lastResult;

    /// <summary>Проблемите в реда, в който стоят в списъка — за прескачането.</summary>
    private List<Issue> _issueSource = [];

    public EditorForm(ImportService service, Workspace workspace, long documentId)
    {
        _service = service;
        _workspace = workspace;
        _documentId = documentId;

        var summary = workspace.Documents.Find(documentId)
            ?? throw new InvalidOperationException(Lang.T("Отчетът не е намерен.", "The report was not found."));

        _schema = service.GetSchema(summary.SchemaKey);
        _report = _schema.FindReport(summary.Report)
            ?? throw new InvalidOperationException(Lang.T("Видът отчет липсва в схемата.", "That report type is missing from the schema."));

        Text = summary.Title + " — " + ReportTitles.Short(_report);
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(Theme.Scaled(1100), Theme.Scaled(720));
        KeyPreview = true;

        _side = new TransferPanel(_schema, workspace.Settings, 320, Limits);

        BuildLayout();
        LoadDocument();

        // Накрая: каквото не е оцветено нарочно, получава цвят от темата.
        Theme.Apply(this);

        _autosave.Tick += (_, _) => Save(quiet: true);
        _autosave.Start();
    }

    /// <summary>Таблиците в реда, в който стоят разделите — ползва се от тестовете.</summary>
    internal IReadOnlyList<SheetGrid> Grids => _grids;

    /// <summary>Страничният панел — ползва се от тестовете.</summary>
    internal TransferPanel Side => _side;

    /// <summary>Отваря страничния панел за сегашния раздел — ползва се от тестовете.</summary>
    internal void OpenSidePanel() { _sideOpen = true; _side.Visible = true; SyncSidePanel(); }

    /// <summary>Застава на раздел по име на възел — ползва се от тестовете.</summary>
    internal void SelectSheet(string node)
    {
        var index = _grids.FindIndex(g => g.Plan.Node.ElementName == node);
        if (index < 0) return;

        _sheets.SelectedIndex = index;
        _side.ShowFor(_report.ElementName, node);
    }

    // ------------------------------------------------------------- устройство

    /// <summary>
    /// Кратките списъци на фирмата, за която е този отчет.
    ///
    /// Питат се наново при всяка нужда, а не се пазят в поле: „Списъци“ е
    /// отделен екран и може да е пипан, докато отчетът стои отворен.
    /// </summary>
    private ValueLimits Limits() =>
        _workspace.Lists.LimitsFor(_workspace.Documents.Find(_documentId)?.CompanyId, _schema.Key);

    private void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = Theme.Scaled(6),

            // Мереното при 100 %. Тук разделителят още е 150 на 100 пиксела и
            // по-голям минимум хвърля; в мащаба се вдига, щом прозорецът има
            // истински размери (виж Shown по-долу).
            Panel2MinSize = 120
        };

        _sheets.Dock = DockStyle.Fill;
        _sheets.Font = Theme.Base;
        _sheets.Padding = new Point(14, 6);

        BuildActions();

        split.Panel1.Controls.Add(_sheets);
        split.Panel1.Controls.Add(_side);
        split.Panel1.Controls.Add(_actions);
        _actions.BringToFront();

        // Панелът се сменя заедно с раздела: помощните полета важат за
        // раздела, в който човек стои.
        _sheets.SelectedIndexChanged += (_, _) => SyncSidePanel();

        split.Panel2.Controls.Add(BuildIssues());

        Controls.Add(split);
        Controls.Add(BuildHeader());
        Controls.Add(BuildStatusBar());

        split.Panel1.Resize += (_, _) => PlaceActions();
        _actions.SizeChanged += (_, _) => PlaceActions();
        _side.VisibleChanged += (_, _) => PlaceActions();

        _tabArea = split.Panel1;

        // Разделителят се мести чак когато прозорецът има истински размери.
        Shown += (_, _) =>
        {
            // В мащаба на екрана и винаги в позволеното: на лаптоп при 150 %
            // долната граница (полето за грешките) е по-висока, и неизчислено
            // разстояние хвърляше, вместо прозорецът да се отвори.
            var least = Theme.Scaled(120);

            if (split.Height - split.Panel1MinSize - split.SplitterWidth > least)
                split.Panel2MinSize = least;

            var high = split.Height - split.Panel2MinSize - split.SplitterWidth;

            if (high > split.Panel1MinSize)
                split.SplitterDistance = Math.Clamp(
                    split.Height - Theme.Scaled(220), Math.Min(Theme.Scaled(300), high), high);
            PlaceActions();
        };
    }

    /// <summary>Панелът, в който стоят разделите — по него се мери редът им.</summary>
    private Control? _tabArea;

    /// <summary>
    /// Двете действия на екрана. Стоят на реда с разделите, най-вдясно.
    ///
    /// Горе, до заглавната лента, те ѝ отнемаха ширината: на широк екран
    /// оставаше празно поле по средата, а на тесен полетата се смачкваха и
    /// най-левият бутон изчезваше наполовина. Тук редът е техен — разделите
    /// се стесняват, докато останат отляво, и заглавната лента получава
    /// целия прозорец.
    ///
    /// Бутон „Запиши“ няма: записът е автоматичен — на всеки двайсет секунди
    /// и при затваряне. Бутон, който не решава нищо, само кара човек да се
    /// пита какво става, ако го забрави.
    /// </summary>
    private void BuildActions()
    {
        _actions.FlowDirection = FlowDirection.RightToLeft;
        _actions.WrapContents = false;
        _actions.AutoSize = true;
        _actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _actions.BackColor = Theme.Canvas;

        var height = Math.Max(28, Theme.Base.Height + 12);

        var generate = Theme.PrimaryButton(Lang.T("Създай файл…", "Create file…"));
        generate.Glyph = Theme.Glyph.Shield;
        generate.Height = height;
        generate.Click += (_, _) => Generate();
        generate.FitWidth(16);

        var check = Theme.SecondaryButton(Lang.T("Провери  (F5)", "Check  (F5)"));
        check.Glyph = Theme.Glyph.Check;
        check.Height = height;
        check.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        check.Click += (_, _) => Check(announce: true);
        check.FitWidth(14);

        // Отделно от „Провери“ нарочно: ред с дата извън периода понякога е
        // сгрешен, а понякога е точно това, което човекът е искал. Проверка,
        // която спира заради него, би карала хората да я заобикалят.
        var dates = Theme.GhostButton(Lang.T("Дати", "Dates"), Theme.Glyph.Warning);
        dates.Height = height;
        dates.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        dates.AccessibleName = Lang.T("Дати извън периода на отчета", "Dates outside the report's period");
        dates.Click += (_, _) => CheckDates();
        dates.FitWidth(12);

        new ToolTip().SetToolTip(dates, dates.AccessibleName);

        var helpers = Theme.GhostButton(string.Empty, Theme.Glyph.Panel);
        helpers.Size = new Size(height + 6, height);
        helpers.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        helpers.AccessibleName = Lang.T("Помощни полета  (F6)", "Helper fields  (F6)");
        helpers.Click += (_, _) => ToggleSidePanel();

        new ToolTip().SetToolTip(helpers, helpers.AccessibleName);

        // Обратната посока на подреждане слага първия добавен най-вдясно.
        _actions.Controls.Add(generate);
        _actions.Controls.Add(check);
        _actions.Controls.Add(dates);
        _actions.Controls.Add(helpers);
    }

    /// <summary>Дали страничният панел е отворен. Пази се отделно от Visible,
    /// който при непоказан прозорец връща false, независимо какво е зададено.</summary>
    private bool _sideOpen;

    /// <summary>Отваря и затваря страничния панел.</summary>
    private void ToggleSidePanel()
    {
        _sideOpen = !_sideOpen;
        _side.Visible = _sideOpen;

        if (_sideOpen) SyncSidePanel();
    }

    /// <summary>Настройва панела за раздела, в който стоим.</summary>
    private void SyncSidePanel()
    {
        // Докато разделите още нямат прозорец, SelectedIndex е -1, а избран е
        // първият: панелът трябва да пита за него, а не да остане празен.
        var index = _sheets.SelectedIndex >= 0 ? _sheets.SelectedIndex : 0;
        var node = index < _grids.Count ? _grids[index].Plan.Node.ElementName : null;

        _side.ShowFor(_report.ElementName, node);
    }

    /// <summary>
    /// Слага бутоните в десния край на реда с разделите и стеснява разделите
    /// толкова, че да не стигнат до тях.
    ///
    /// Постоянните 230 пиксела на раздел преливат още на 1366: шест раздела
    /// не се побират, Windows слага стрелки за прелистване и част от имената
    /// изчезват. По-тясно с многоточие се чете по-добре от скрито.
    /// </summary>
    private void PlaceActions()
    {
        if (_tabArea is null) return;

        // Отвореният страничен панел изяжда от реда с разделите — иначе
        // копчетата отиват под него.
        var width = _tabArea.ClientSize.Width - (_sideOpen ? _side.Width : 0);

        // Мери се по панела, а не по самите раздели: при разтягане на
        // прозореца събитието идва, преди залепеното вътре да е преместено, и
        // разделите още помнят старата ширина.
        var reserve = _actions.Width + Theme.Gap + Theme.GapLarge;

        if (_sheets.TabCount > 0)
        {
            // Долната граница е ниска нарочно: раздел с отрязано име се вижда
            // и се натиска, а раздел, изтикан под копчетата, го няма изобщо.
            var wanted = new Size(
                Math.Clamp((width - reserve) / _sheets.TabCount, 70, 230),
                Math.Max(30, Theme.Base.Height + 16));

            if (_sheets.ItemSize != wanted) _sheets.ItemSize = wanted;
        }

        var band = _sheets.ItemSize.Height + 4;

        _actions.Location = new Point(
            Math.Max(0, width - _actions.Width - Theme.Gap),
            Math.Max(0, (band - _actions.Height) / 2));
    }

    /// <summary>
    /// Заглавната лента: една ивица от край до край.
    ///
    /// Дотук ѝ отнемаха ширината пет бутона отдясно, а на по-малък екран
    /// двете се срещаха по средата и полетата оставаха наполовина. Сега
    /// действията са долу, на реда с разделите, а тук остават само полетата —
    /// с иконите на упътването пред тях.
    ///
    /// Височините се мерят по шрифта, а не по кръгли числа: при мащаб 125 %
    /// и 150 % буквите растат, а числата в кода — не, и надписите се режат.
    /// </summary>
    private Control BuildHeader()
    {
        var labelHeight = Theme.Base.Height + 6;
        var inputHeight = Theme.Big.Height + 12;
        var fieldHeight = labelHeight + inputHeight;
        var titleHeight = Theme.Heading.Height + 8;

        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = titleHeight + fieldHeight + 18,
            BackColor = Theme.Surface,
            Padding = new Padding(16, 8, 16, 10)
        };

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = titleHeight,
            Font = Theme.Heading,
            Text = ReportTitles.Short(_report),
            ForeColor = Theme.Accent,
            AutoEllipsis = true
        };

        var appendix = ReportTitles.Appendix(_report);
        if (appendix is not null) title.Text += "   " + appendix;

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Surface
        };

        foreach (var column in _schema.RootAttributes)
        {
            // Поле с една-единствена допустима стойност не е избор, а
            // церемония: попълва се само и не се показва. „Тип на импорта“
            // днес е точно такова — схемата позволява само REPLACE, а APPEND
            // стои закоментиран. Ако НИСО го върне, полето се появява само,
            // без тук да се пипа нищо.
            if (column.Domain.IsEnum && column.Domain.Options.Count == 1)
            {
                _fixedHeader[column.Name] = column.Domain.Options[0].Value;
                continue;
            }

            fields.Controls.Add(BuildHeaderField(column, labelHeight, fieldHeight));
        }

        // Упътването и клавишите се отварят рядко, а надписите им ядяха реда.
        // Като икони те стоят пред полетата и не мърдат с превъртането —
        // затова са отделен панел отляво, а не първи в самата редица.
        var icons = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            // Отместването отгоре ги изравнява с полетата, а не с надписите им.
            Padding = new Padding(0, labelHeight, Theme.Gap, 0)
        };

        icons.Controls.Add(IconButton(
            Theme.Glyph.Info, Lang.T("Как се попълва  (F1)", "How to fill in  (F1)"), ShowHelp, inputHeight));

        icons.Controls.Add(IconButton(
            Theme.Glyph.Keyboard, Lang.T("Клавиши  (Ctrl+F1)", "Shortcuts  (Ctrl+F1)"), ShowShortcuts, inputHeight));

        icons.Controls.Add(new Panel
        {
            Width = 1,
            Height = inputHeight,
            Margin = new Padding(Theme.GapSmall, 0, 0, 0),
            BackColor = Theme.Border
        });

        // Редът на добавяне решава подредбата: пълнещото се добавя първо,
        // залепените отстрани — след него, а най-горното — накрая.
        host.Controls.Add(fields);
        host.Controls.Add(icons);
        host.Controls.Add(title);

        return host;
    }

    /// <summary>
    /// Бутон само с икона. Какво прави и с кой клавиш се вика, стои в
    /// подсказката и в името за екранния четец — на реда остава само знакът.
    /// </summary>
    private static Controls.AppButton IconButton(string glyph, string tip, Action handler, int size)
    {
        var button = Theme.GhostButton(string.Empty, glyph);
        button.Size = new Size(size + 6, size);
        button.Margin = new Padding(0, 0, Theme.GapSmall, 0);
        button.AccessibleName = tip;
        button.Click += (_, _) => handler();

        new ToolTip().SetToolTip(button, tip);

        return button;
    }

    private Control BuildHeaderField(ColumnDef column, int labelHeight, int boxHeight)
    {
        var caption = ReportTitles.ColumnShort(column) + (column.Required ? " *" : string.Empty);

        var line = Theme.Base.Height;
        var captionWidth = TextRenderer.MeasureText(caption, Theme.Base).Width + 14;

        // Полето се мери по стойността, която ще стои в него, а не само по
        // името си. Датата е точно десет знака, годината — четири; широки
        // колкото „Начален период на данните“, те държаха празно място, което
        // на по-тесен екран липсваше на съседите. Ширините са в редове, а не
        // в пиксели, за да растат заедно с шрифта при мащаб 150 %.
        var width = column.Domain.Kind switch
        {
            ValueKind.Date => Math.Max(
                captionWidth, TextRenderer.MeasureText("00.00.0000", Theme.Big).Width + 40),

            ValueKind.Integer or ValueKind.Decimal => Math.Max(
                captionWidth, TextRenderer.MeasureText("000000", Theme.Big).Width + 26),

            _ => Math.Clamp(captionWidth + 2, line * 10, line * 16)
        };

        var box = new Panel
        {
            Width = width,
            Height = boxHeight,
            Margin = new Padding(0, 0, Theme.Gap, 0),
            BackColor = Theme.Surface
        };

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = labelHeight,
            Text = caption,
            ForeColor = Theme.InkMuted,
            AutoEllipsis = true
        };

        Control input;

        // ЕИК-ът не се набира — избира се измежду фирмите от настройките.
        // Така един счетоводител с няколко задължени лица не бърка деветте
        // цифри, а и отчетът се връзва към фирма и може да се пресява по нея.
        if (column.Name == "eik" && _workspace.Companies.List() is { Count: > 0 } companies)
        {
            var picker = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            picker.Items.AddRange([.. companies.Select(c => (object)c.Display)]);

            picker.SelectedIndexChanged += (_, _) =>
            {
                var chosen = companies[picker.SelectedIndex];
                _workspace.Documents.SetCompany(_documentId, chosen.Id);

                // Кратките списъци се водят по фирма. Смяната ѝ трябва да се
                // види веднага в менютата — иначе човек попълва с чуждия
                // списък пред очите си и разбира чак утре.
                var limits = Limits();
                foreach (var grid in _grids) grid.ApplyLimits(limits);

                AnnounceBlocked();
            };

            input = picker;
        }
        else if (column.Domain.Kind == ValueKind.Date)
        {
            // Календар само тук, в заглавната част. В самите редове той пречи:
            // там датите се поставят от Excel по двайсет наведнъж и всяка
            // клетка с падащ календар е спънка, а не удобство.
            input = new DateTimePicker
            {
                Dock = DockStyle.Top,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = ValueConverter.BulgarianDateFormat,
                // Отметката позволява полето да остане празно — периодът невинаги
                // се попълва, а сложената по подразбиране днешна дата се пропуска.
                ShowCheckBox = !column.Required,
                Checked = column.Required
            };
        }
        else if (column.Domain.IsEnum && column.Domain.Options.Count <= 40)
        {
            var combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange([.. column.Domain.Options.Select(o => (object)o.Display)]);
            input = combo;
        }
        else
        {
            // Числото стои в средата: полето е широко точно колкото него и
            // залепено вляво изглежда като недовършено.
            input = new TextBox
            {
                Dock = DockStyle.Top,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = column.Domain.Kind is ValueKind.Integer or ValueKind.Decimal
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Left
            };
        }

        input.Font = Theme.Big;
        input.Tag = column;
        Theme.StyleInput(input);

        var tip = new ToolTip();
        tip.SetToolTip(input, ReportTitles.Column(column));

        _headerFields[column.Name] = input;

        box.Controls.Add(input);
        box.Controls.Add(label);

        return box;
    }

    private Control BuildIssues()
    {
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 8, 18, 8) };

        _summary.Dock = DockStyle.Top;
        _summary.Height = Theme.Scaled(26);
        _summary.Font = Theme.Bold;
        _summary.Text = Lang.T("Още не е правена проверка.", "No check has been run yet.");

        _issues.Dock = DockStyle.Fill;
        _issues.DoubleClick += (_, _) => JumpToIssue();

        host.Controls.Add(_issues);
        host.Controls.Add(_summary);

        return host;
    }

    private Control BuildStatusBar()
    {
        var strip = new StatusStrip { SizingGrip = false, BackColor = Theme.Surface };
        _status.Text = Lang.T("Готово.", "Done.");
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        strip.Items.Add(_status);

        return strip;
    }

    /// <summary>Упътването се съставя при отваряне — схемата вече е заредена.</summary>
    private void ShowHelp()
    {
        using var dialog = new HelpDialog(ReportGuide_Builder.For(_schema, _report));
        dialog.ShowDialog(this);
    }

    private void ShowShortcuts()
    {
        using var dialog = new ShortcutsDialog();
        dialog.ShowDialog(this);
    }

    // ------------------------------------------------------------- данни

    private void LoadDocument()
    {
        var header = _workspace.Documents.ReadHeader(_documentId);

        foreach (var (name, control) in _headerFields)
        {
            if (!header.TryGetValue(name, out var value)) continue;

            switch (control)
            {
                case ComboBox combo when ((ColumnDef)control.Tag!).Name == "eik":
                {
                    // Показва се фирмата, чийто ЕИК е записан в отчета.
                    var match = _workspace.Companies.List()
                        .FirstOrDefault(c => c.Eik == value);

                    if (match is not null) combo.SelectedItem = match.Display;
                    break;
                }

                case ComboBox combo:
                {
                    var column = (ColumnDef)control.Tag!;
                    var option = column.Domain.Options.FirstOrDefault(o =>
                        string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));

                    combo.SelectedItem = option?.Display ?? value;
                    break;
                }

                case DateTimePicker picker when ValueConverter.TryNormalizeDate(value, out var text) &&
                                                DateTime.TryParseExact(
                                                    text, ValueConverter.BulgarianDateFormat,
                                                    CultureInfo.InvariantCulture, DateTimeStyles.None,
                                                    out var date):
                    picker.Value = date;
                    picker.Checked = true;
                    break;

                case DateTimePicker picker:
                    // Записана, но неразбираема стойност: по-добре празно поле,
                    // отколкото тиха подмяна с днешна дата.
                    if (picker.ShowCheckBox) picker.Checked = false;
                    break;

                default:
                    control.Text = value;
                    break;
            }
        }

        var layout = WorkbookLayout.Build(_schema, [_report]);

        // Веднъж за всички листове: питането минава през целия списък с отчети,
        // а листовете на един отчет са до дванайсет.
        var limits = Limits();

        foreach (var plan in TabOrder.Arrange(_report, layout.Sheets))
        {
            var rows = _workspace.Documents.ReadRows(_documentId, plan.Node.ElementName)
                .Select(r => r.Values)
                .ToList();

            var grid = new SheetGrid(plan, rows, ExtraColumnsFor(plan.Node.ElementName), limits)
            {
                ConfirmRowDeletes = _workspace.Settings.GetBool(SettingsStore.Keys.ConfirmOnDelete, true)
            };

            // „Описание на дейност“ повтаря избраната дейност R/D — пише се
            // само и когато редът се попълва на ръка, не само при пренасяне.
            foreach (var rule in TransferRule.ForReport(_report.ElementName))
                if (rule.ToNode == plan.Node.ElementName && rule.Derived.Count > 0)
                    grid.DescriptionLink = rule.Derived[0];

            var index = _grids.Count;

            grid.Changed += (_, _) =>
            {
                _status.Text = Lang.T("Има незаписани промени.", "There are unsaved changes.");

                // Броят попълнени редове стои върху раздела, за да се вижда
                // къде има работа, без да се влиза във всеки.
                _sheets.Counts[index] = grid.FilledRows;
                _sheets.Invalidate();
            };

            grid.RowAction += (_, e) => Transfer(grid, e.RowIndex, e.Column);

            // Свързаният ред не си мени количеството, а изтриването му минава
            // през редовете от другата страна на връзката.
            if (Involved(plan.Node.ElementName))
            {
                grid.LockedColumns = ["amount_ton"];
                grid.BeforeDelete = rows => AllowDelete(grid, rows);
            }

            _grids.Add(grid);
            _sheets.Counts[index] = grid.FilledRows;

            var page = new TabPage(TabTitle(plan)) { BackColor = Theme.Canvas, Padding = new Padding(6) };
            page.Controls.Add(grid);
            _sheets.TabPages.Add(page);
        }

        SyncSidePanel();
    }

    /// <summary>Името на служебната колона с отметката „вече пренесен“.</summary>
    private const string MarkColumn = "__moved";

    /// <summary>Името на служебната колона с копчето за едно към едно.</summary>
    private const string ActionColumn = "__move";

    /// <summary>Името на служебната колона с копчето, което дели.</summary>
    private const string SplitColumn = "__split";

    /// <summary>
    /// Служебните колони на един раздел: копчето за пренасяне и отметката,
    /// че редът вече е пренесен. Появяват се само там, откъдето тръгва
    /// правило — останалите таблици остават както са.
    /// </summary>
    private IReadOnlyList<LocalColumn>? ExtraColumnsFor(string node)
    {
        // Разделът, в който се появяват създадените редове, също носи скрита
        // колона: по нея се намират братята на един ред, когато той се пипне.
        var isTarget = TransferRule.ForReport(_report.ElementName).Any(r => r.ToNode == node);

        if (TransferRule.From(_report.ElementName, node) is not { } rule)
        {
            return isTarget
                ? [new LocalColumn(SheetGrid.LinkColumn, string.Empty, LocalColumnKind.Text, 0) { Hidden = true }]
                : null;
        }

        var target = rule.Target(_schema);
        var name = target is null ? rule.ToNode : ReportTitles.Table(target);

        // Копчетата стоят преди отметката: натискат се, отметката се чете.
        // Обратно — както беше — окото минава през резултата, за да стигне до
        // действието.
        var columns = new List<LocalColumn>
        {
            new(ActionColumn, rule.Action, LocalColumnKind.Button, 64,
                Lang.T($"{rule.ActionTip} Създава един ред в „{name}“ със същото количество.",
                       $"{rule.ActionTip} Creates one row in “{name}” with the same amount."))
        };

        if (rule.CanSplit)
        {
            columns.Add(new LocalColumn(SplitColumn, rule.SplitAction!, LocalColumnKind.Button, 64,
                Lang.T($"{rule.SplitTip} Дели количеството между няколко нови кода в „{name}“.",
                       $"{rule.SplitTip} Splits the amount between several new codes in “{name}”.")));
        }

        columns.Add(new LocalColumn(MarkColumn, rule.Mark, LocalColumnKind.Text, 90,
            Lang.T($"Отбелязва редовете, които вече са пренесени в „{name}“.",
                   $"Marks the rows already transferred into “{name}”.")));

        columns.Add(new LocalColumn(SheetGrid.SentColumn, string.Empty, LocalColumnKind.Text, 0)
        {
            Hidden = true
        });

        if (isTarget)
            columns.Add(new LocalColumn(SheetGrid.LinkColumn, string.Empty, LocalColumnKind.Text, 0)
            {
                Hidden = true
            });

        return columns;
    }

    /// <summary>
    /// Името на раздела. Идва от <see cref="ReportTitles.Table"/> — същото,
    /// което съобщенията при проверка слагат в „мястото“. Ако двете се
    /// разминат, съобщението сочи раздел, който на екрана го няма.
    /// </summary>
    private static string TabTitle(SheetPlan plan)
    {
        var text = ReportTitles.Table(plan.Node);

        return text.Length <= 34 ? text : text[..32].TrimEnd() + "…";
    }

    private Dictionary<string, string> CollectHeader()
    {
        // Скритите стойности влизат първи, за да могат видимите да ги
        // застъпят, ако някога полето стане избираемо.
        var values = new Dictionary<string, string>(_fixedHeader, StringComparer.Ordinal);

        foreach (var (name, control) in _headerFields)
        {
            var column = (ColumnDef)control.Tag!;

            if (control is DateTimePicker picker)
            {
                // Незачекнатият календар значи „не е попълнено“, а не „днес“.
                if (picker.ShowCheckBox && !picker.Checked) continue;

                values[name] = picker.Value.ToString(
                    ValueConverter.BulgarianDateFormat, CultureInfo.InvariantCulture);
                continue;
            }

            var text = control.Text.Trim();

            if (text.Length == 0) continue;

            if (column.Name == "eik")
            {
                // „Име — ЕИК“ отива в XML-а само с ЕИК-а.
                var separator = text.LastIndexOf(" — ", StringComparison.Ordinal);
                values[name] = separator > 0 ? text[(separator + 3)..] : text;
                continue;
            }

            // Изброените се записват с кода, не с описанието до него.
            var dash = text.IndexOf(" — ", StringComparison.Ordinal);
            if (column.Domain.IsEnum && dash > 0) text = text[..dash];

            values[name] = text;
        }

        return values;
    }

    private void Save(bool quiet)
    {
        try
        {
            _workspace.Documents.SaveHeader(_documentId, CollectHeader());

            foreach (var grid in _grids)
            {
                _workspace.Documents.SaveRows(_documentId, grid.Plan.Node.ElementName, grid.Snapshot());
                grid.MarkSaved();
            }

            _status.Text = quiet
                ? Lang.T($"Записано автоматично в {DateTime.Now:HH:mm}.", $"Saved automatically at {DateTime.Now:HH:mm}.")
                : Lang.T($"Записано в {DateTime.Now:HH:mm}.", $"Saved at {DateTime.Now:HH:mm}.");
        }
        catch (Exception ex) when (quiet)
        {
            // Автоматичният запис мълчи: съобщение на всеки двайсет секунди е
            // по-лошо от липсващ запис, а ръчният бутон ще покаже същата грешка.
            _status.Text = Lang.T("Автоматичният запис не успя: ", "The automatic save failed: ") + ex.Message;
        }
    }

    // ------------------------------------------------------------- пренасяне

    /// <summary>
    /// Създава ред в съседния раздел по данните на този.
    ///
    /// Смисълът е в броя: при петдесет получени партиди третирането се
    /// различава по два реда — код на отпадъка след третирането и дата —
    /// а всичко останало се преписва. Преписването на ръка не е бавно, то е
    /// неточно: работният лист се сбърква на трийсетия ред и грешката излиза
    /// чак при проверката, ако изобщо излезе.
    ///
    /// Затова: копчето взема количеството и връзките от реда, взема
    /// различаващото се от панела и оставя следа, че редът е пренесен.
    /// </summary>
    private void Transfer(SheetGrid source, int rowIndex, string column)
    {
        if (TransferRule.From(_report.ElementName, source.Plan.Node.ElementName) is not { } rule) return;
        if (rule.Target(_schema) is not { } target) return;
        if (GridFor(rule.ToNode) is not { } destination) return;

        var row = source.RowValues(rowIndex);

        if (row.Count == 0)
        {
            _status.Text = Lang.T("Редът е празен — няма какво да се пренесе.", "The row is empty — there is nothing to transfer.");
            return;
        }

        // Пренасянето е едно към едно: цялото количество отива в новия ред.
        // Без него редът в отсрещния раздел е невалиден, а мълчаливата нула
        // е по-лоша от питането.
        if (!row.TryGetValue("amount_ton", out var amount) || amount.Trim().Length == 0)
        {
            MessageBox.Show(this,
                Lang.T("Този ред няма попълнено количество. Попълнете го и опитайте пак.",
                       "This row has no amount filled in. Fill it in and try again."),
                Lang.T("Няма количество", "No amount"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Заключеният код се взема от реда — ако редът е без код, няма какво
        // да се вземе, а мълчаливо празно поле пада чак при проверката.
        if (SameCode(rule) && rule.SameAsSource is { } reused &&
            (!row.TryGetValue(reused, out var carriedCode) || carriedCode.Trim().Length == 0))
        {
            MessageBox.Show(this,
                Lang.T("Отметката „Използвай същия код“ е вдигната, но този ред е без код. Попълнете го и опитайте пак.",
                       "“Use the same code” is ticked, but this row has no code. Fill it in and try again."),
                Lang.T("Няма код", "No code"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Веднъж пренесен ред не се пренася пак: втора група със същото
        // количество удвоява отчета, без нищо да го подскаже. Копчето вместо
        // това отваря вече създаденото — редовете се променят, но си остават
        // едни и същи редове.
        if (ExistingGroup(source, rowIndex, destination) is { } already)
        {
            Reopen(source, rowIndex, column, rule, target, destination, already);
            return;
        }

        var defaults = _side.Rule?.ToNode == rule.ToNode
            ? _side.Values()
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var missing = _side.Rule?.ToNode == rule.ToNode ? _side.Missing() : [.. rule.Defaults];

        var made = column == SplitColumn
            ? AskSplit(rule, target, defaults, row, amount)
            : Single(rule, target, defaults, row, missing);

        if (made is null || made.Count == 0) return;

        // Групата се именува веднъж: по това име редовете се намират един друг
        // и се разбира, че петдесетте тона отсреща са същите петдесет.
        var link = Guid.NewGuid().ToString("N")[..8];

        foreach (var values in made)
        {
            Carry(rule, target, row, values);
            destination.LinkRow(destination.AppendRow(values), link);
        }

        source.SetLocalValue(rowIndex, MarkColumn, Lang.T("да", "yes"));
        source.SentRow(rowIndex, link);

        RefreshCounts();

        _status.Text = made.Count == 1
            ? Lang.T(
                $"Нов ред в „{ReportTitles.Table(target)}“ — {amount} т.",
                $"A new row in “{ReportTitles.Table(target)}” — {amount} t.")
            : Lang.T(
                $"{made.Count} нови реда в „{ReportTitles.Table(target)}“ — общо {amount} т.",
                $"{made.Count} new rows in “{ReportTitles.Table(target)}” — {amount} t in total.");
    }

    /// <summary>Един ред: или наготово от панела, или през прозорчето.</summary>
    private List<Dictionary<string, string>>? Single(
        TransferRule rule,
        ReportNode target,
        Dictionary<string, string> defaults,
        IReadOnlyDictionary<string, string> row,
        IReadOnlyList<string> missing)
    {
        // Отметката „винаги“ важи само когато има какво да се използва:
        // мълчаливо създаден непълен ред е точно грешката, която тази работа
        // трябва да предотврати.
        var values = _side.AlwaysUse && missing.Count == 0
            ? defaults
            : Ask(rule, target, defaults, row);

        return values is null ? null : [values];
    }

    /// <summary>
    /// Много редове от един: количеството се дели между няколко кода.
    ///
    /// С избран шаблон и вдигната отметка делението минава без прозорче —
    /// същото разкомплектоване се прави всеки месец и единственото, което се
    /// мени, е количеството.
    /// </summary>
    private List<Dictionary<string, string>>? AskSplit(
        TransferRule rule,
        ReportNode target,
        Dictionary<string, string> defaults,
        IReadOnlyDictionary<string, string> row,
        string amount)
    {
        if (!SplitMath.TryRead(amount, out var total) || total <= 0)
        {
            MessageBox.Show(this,
                Lang.T($"Количеството „{amount}“ не се разчита като число.",
                       $"The amount “{amount}” does not read as a number."),
                Lang.T("Няма количество", "No amount"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        // Шаблонът носи и кодовете, и дяловете — от панела тук трябва само
        // датата, а тя е винаги налице. Затова делението не пита за полетата,
        // които важат за пренасянето едно към едно.
        if (_side.AlwaysUse && _side.Rule?.ToNode == rule.ToNode &&
            _side.Template is { Parts.Count: > 0 } template)
        {
            return FromTemplate(template, defaults, total);
        }

        using var dialog = new SplitDialog(
            rule, _schema, target, _workspace.Settings, total, defaults, limits: Limits());

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Rows : null;
    }

    /// <summary>Прилага шаблон без питане — сборът излиза точно по построение.</summary>
    private static List<Dictionary<string, string>> FromTemplate(
        SplitTemplate template,
        IReadOnlyDictionary<string, string> defaults,
        decimal total)
    {
        var shares = SplitMath.Shares(total, [.. template.Parts.Select(p => p.Percent)]);
        var rows = new List<Dictionary<string, string>>();

        for (var i = 0; i < template.Parts.Count; i++)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ewc"] = template.Parts[i].Ewc,
                ["amount_ton"] = SplitMath.Amount(shares[i])
            };

            if (defaults.TryGetValue("date_record", out var when) && when.Length > 0)
                values["date_record"] = when;

            rows.Add(values);
        }

        return rows;
    }

    /// <summary>
    /// Пренася полетата, които се повтарят, и попълва изведените.
    ///
    /// Изброените стойности се сверяват със списъка на отсрещната колона:
    /// „Категория НУБА“ при третиран и при образуван са различни типове в
    /// схемата и съвпадението на имената не значи съвпадение на стойностите.
    /// Пренесена наслуки стойност минава за попълнена и пада чак при проверката.
    /// </summary>
    private void Carry(
        TransferRule rule,
        ReportNode target,
        IReadOnlyDictionary<string, string> row,
        Dictionary<string, string> values)
    {
        // С вдигната отметка заключеното поле се държи като пренасяно: не се
        // избира в панела, а идва от реда заедно с количеството.
        var fields = SameCode(rule) && rule.SameAsSource is { } reused
            ? rule.Carried.Append(reused)
            : rule.Carried;

        foreach (var name in fields)
        {
            // Каквото прозорчето вече е решило, стои: при делене количеството
            // на реда е дял от изходното, а не самото то.
            if (values.ContainsKey(name)) continue;
            if (!row.TryGetValue(name, out var carried) || carried.Length == 0) continue;

            var column = target.Attributes.FirstOrDefault(a => a.Name == name);

            if (column is { Domain.IsEnum: true } &&
                !column.Domain.Options.Any(o => string.Equals(o.Value, carried, StringComparison.OrdinalIgnoreCase)))
                continue;

            values[name] = carried;
        }

        // „Описание на отпадъка“ е името на кода по ЕКО, „Описание на дейност“
        // — текстът на дейността. И двете се пишат сами.
        foreach (var (from, to) in rule.Derived)
        {
            if (values.ContainsKey(to)) continue;
            if (!values.TryGetValue(from, out var code) || code.Length == 0) continue;

            var description = target.Attributes.FirstOrDefault(a => a.Name == from)?.Domain.Options
                .FirstOrDefault(o => string.Equals(o.Value, code, StringComparison.OrdinalIgnoreCase))?
                .Description;

            if (!string.IsNullOrWhiteSpace(description)) values[to] = description;
        }
    }

    /// <summary>
    /// Дали кодът се взема от изходния ред.
    ///
    /// Пита се и за кой раздел важи отметката: панелът обслужва един раздел, а
    /// копчетата стоят на два.
    /// </summary>
    private bool SameCode(TransferRule rule) =>
        rule.CanReuseSource && _side.SameCode && _side.Rule?.ToNode == rule.ToNode;

    /// <summary>Кодът на реда, както се чете — за заключеното поле в прозорчето.</summary>
    private string SourceValue(TransferRule rule, IReadOnlyDictionary<string, string> row)
    {
        if (rule.SameAsSource is not { } name) return string.Empty;
        if (!row.TryGetValue(name, out var stored) || stored.Length == 0) return string.Empty;

        var column = _schema.FindReport(rule.Report)?
            .SelfAndDescendants()
            .FirstOrDefault(n => n.ElementName == rule.FromNode)?
            .Attributes.FirstOrDefault(a => a.Name == name);

        return column?.Domain.Options
            .FirstOrDefault(o => string.Equals(o.Value, stored, StringComparison.OrdinalIgnoreCase))?
            .Display ?? stored;
    }

    /// <summary>Пита за един ред и връща избраното, или null при отказ.</summary>
    private Dictionary<string, string>? Ask(
        TransferRule rule,
        ReportNode target,
        IReadOnlyDictionary<string, string> defaults,
        IReadOnlyDictionary<string, string> row)
    {
        using var dialog = new TransferDialog(rule, _schema, target, defaults, Describe(rule, row),
            SameCode(rule) ? SourceValue(rule, row) : null, Limits());

        if (dialog.ShowDialog(this) != DialogResult.OK) return null;

        // Избраното в прозорчето става новото предварително: вторият ред не
        // бива да пита отначало за същото.
        _side.Remember(dialog.Values);
        if (dialog.AlwaysFromNow) _side.AlwaysUse = true;

        return dialog.Values;
    }

    /// <summary>Какво се пренася — показва се в прозорчето, за да се види кой ред е.</summary>
    private string Describe(TransferRule rule, IReadOnlyDictionary<string, string> row)
    {
        var source = _schema.FindReport(rule.Report)?
            .SelfAndDescendants()
            .FirstOrDefault(n => n.ElementName == rule.FromNode);

        var parts = new List<string>();

        foreach (var name in rule.Carried)
        {
            if (!row.TryGetValue(name, out var value) || value.Length == 0) continue;

            var column = source?.Attributes.FirstOrDefault(a => a.Name == name);
            parts.Add((column is null ? name : ReportTitles.ColumnShort(column)) + ": " + value);
        }

        return Lang.T("От реда се взема — ", "Taken from the row — ") + string.Join(" · ", parts);
    }

    /// <summary>Дали разделът участва във връзка — като източник или като цел.</summary>
    private bool Involved(string node) =>
        TransferRule.ForReport(_report.ElementName).Any(r => r.FromNode == node || r.ToNode == node);

    /// <summary>
    /// Какво става, когато се трие свързан ред.
    ///
    /// Един ред от група не се трие сам: петдесетте тона са разпределени между
    /// три реда и махането на единия оставя четиридесет и седем, които вече не
    /// отговарят на нищо. Затова изборът е между двете неща, които запазват
    /// сметката цяла — ново разпределение или махане на цялата група.
    ///
    /// Връща false, когато сам се е разпоредил: тогава повикващият не пипа
    /// нищо повече.
    /// </summary>
    private bool AllowDelete(SheetGrid grid, IReadOnlyList<int> rows)
    {
        // Ред, който е породил група: махането му осиротява редовете отсреща.
        foreach (var index in rows)
        {
            if (grid.SentBy(index) is not { Count: > 0 } sent) continue;

            return DeleteWithChildren(grid, rows, sent);
        }

        // Ред от група.
        foreach (var index in rows)
        {
            var link = grid.LinkOf(index);
            if (link.Length == 0) continue;

            return DeleteFromGroup(grid, link);
        }

        return true;
    }

    private bool DeleteWithChildren(SheetGrid grid, IReadOnlyList<int> rows, IReadOnlyList<string> sent)
    {
        var children = 0;
        var where = string.Empty;

        foreach (var other in _grids)
        {
            foreach (var link in sent)
            {
                var found = other.RowsOf(link).Count;
                if (found == 0) continue;

                children += found;
                where = ReportTitles.Table(other.Plan.Node);
            }
        }

        if (children == 0) return true;

        var answer = MessageBox.Show(this,
            Lang.T(
                $"От този ред са създадени {children} реда в „{where}“. " +
                "Да се изтрият ли заедно с него?",
                $"{children} rows in “{where}” were created from this row. " +
                "Delete them together with it?"),
            Lang.T("Свързани редове", "Linked rows"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes) return false;

        foreach (var other in _grids)
            foreach (var link in sent)
                other.RemoveRows(other.RowsOf(link));

        grid.RemoveRows(rows);
        RefreshCounts();

        return false;
    }

    private bool DeleteFromGroup(SheetGrid grid, string link)
    {
        var group = grid.RowsOf(link);
        var rule = TransferRule.ForReport(_report.ElementName)
            .FirstOrDefault(r => r.ToNode == grid.Plan.Node.ElementName);

        var source = rule is null ? null : GridFor(rule.FromNode);
        var parent = source?.RowThatSent(link) ?? -1;

        // Един-единствен ред от групата се трие направо — няма какво да се
        // преразпределя между един.
        if (group.Count <= 1)
        {
            grid.RemoveRows(group);
            Detach(source, parent, link);
            RefreshCounts();
            return false;
        }

        var choice = GroupDialog.Ask(this, group.Count, ReportTitles.Table(grid.Plan.Node));

        if (choice == GroupChoice.Cancel) return false;

        if (choice == GroupChoice.DeleteAll)
        {
            grid.RemoveRows(group);
            Detach(source, parent, link);
            RefreshCounts();
            return false;
        }

        Redistribute(grid, group, link, rule, source, parent);
        return false;
    }

    /// <summary>Маха следата от групата у реда, който я е породил.</summary>
    private static void Detach(SheetGrid? source, int parent, string link)
    {
        if (source is null || parent < 0) return;

        source.Unsend(parent, link);

        if (source.SentBy(parent).Count == 0)
            source.SetLocalValue(parent, MarkColumn, string.Empty);
    }

    /// <summary>
    /// Отваря групата наново: същото прозорче, тръгнало от сегашните редове.
    /// Каквото излезе, замества старото изцяло.
    /// </summary>
    private void Redistribute(
        SheetGrid grid,
        IReadOnlyList<int> group,
        string link,
        TransferRule? rule,
        SheetGrid? source,
        int parent)
    {
        if (rule is null || rule.Target(_schema) is not { } target) return;

        var seed = SeedOf(grid, group);
        var total = seed.Sum(part => part.Amount);

        using var dialog = new SplitDialog(
            rule, _schema, target, _workspace.Settings, total, DefaultsOf(grid, group), seed, Limits());

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var row = source is not null && parent >= 0
            ? source.RowValues(parent)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        Replace(grid, group, link, rule, target, row, dialog.Rows);

        _status.Text = Lang.T(
            $"Разпределението е сменено — {dialog.Rows.Count} реда, общо {SplitMath.Amount(total)} т.",
            $"The split is changed — {dialog.Rows.Count} rows, {SplitMath.Amount(total)} t in total.");
    }

    /// <summary>
    /// Групата, която този ред вече е създал отсреща, или null.
    ///
    /// Проверява се и че редовете още стоят там: изтрита група не бива да
    /// пречи на ново пренасяне.
    /// </summary>
    internal static string? ExistingGroup(SheetGrid source, int rowIndex, SheetGrid destination) =>
        source.SentBy(rowIndex).FirstOrDefault(id => destination.RowsOf(id).Count > 0);

    /// <summary>
    /// Отваря вече създадената група вместо да прави втора.
    ///
    /// Кое прозорче — решава натиснатото копче: „Дели“ отваря разпределението,
    /// другото — единичния ред. Така от един ред може да се стигне и до
    /// няколко, и обратно, без нито веднъж да се получат две групи за едно и
    /// също количество.
    /// </summary>
    private void Reopen(
        SheetGrid source,
        int rowIndex,
        string column,
        TransferRule rule,
        ReportNode target,
        SheetGrid destination,
        string link)
    {
        var group = destination.RowsOf(link);
        var row = source.RowValues(rowIndex);

        List<Dictionary<string, string>>? made;

        if (column == SplitColumn && rule.CanSplit)
        {
            if (!SplitMath.TryRead(row.GetValueOrDefault("amount_ton"), out var total) || total <= 0) return;

            using var dialog = new SplitDialog(
                rule, _schema, target, _workspace.Settings, total,
                DefaultsOf(destination, group), SeedOf(destination, group), Limits());

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            made = dialog.Rows;
        }
        else
        {
            var defaults = group.Count > 0 ? destination.RowValues(group[0]) : _side.Values();

            made = Ask(rule, target, defaults, row) is { } one ? [one] : null;
        }

        if (made is null || made.Count == 0) return;

        Replace(destination, group, link, rule, target, row, made);

        _status.Text = Lang.T(
            $"Пренесеното е променено — {made.Count} реда в „{ReportTitles.Table(target)}“.",
            $"The transfer is changed — {made.Count} rows in “{ReportTitles.Table(target)}”.");
    }

    /// <summary>Сегашното разпределение на групата — за начало на прозорчето.</summary>
    private static List<(string Ewc, decimal Amount)> SeedOf(SheetGrid grid, IReadOnlyList<int> group)
    {
        var seed = new List<(string, decimal)>();

        foreach (var index in group)
        {
            var values = grid.RowValues(index);
            SplitMath.TryRead(values.GetValueOrDefault("amount_ton"), out var amount);

            seed.Add((values.GetValueOrDefault("ewc", string.Empty), amount));
        }

        return seed;
    }

    /// <summary>Общото за групата — засега само датата.</summary>
    private static Dictionary<string, string> DefaultsOf(SheetGrid grid, IReadOnlyList<int> group)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);

        if (group.Count > 0 && grid.RowValues(group[0]).TryGetValue("date_record", out var when))
            defaults["date_record"] = when;

        return defaults;
    }

    /// <summary>Заменя цялата група с новото, без да мени името ѝ.</summary>
    private void Replace(
        SheetGrid grid,
        IReadOnlyList<int> group,
        string link,
        TransferRule rule,
        ReportNode target,
        IReadOnlyDictionary<string, string> row,
        IReadOnlyList<Dictionary<string, string>> made)
    {
        grid.RemoveRows(group);

        foreach (var values in made)
        {
            Carry(rule, target, row, values);
            grid.LinkRow(grid.AppendRow(values), link);
        }

        RefreshCounts();
    }

    private SheetGrid? GridFor(string node) =>
        _grids.FirstOrDefault(g => g.Plan.Node.ElementName == node);

    /// <summary>Пресмята наново значетата по разделите.</summary>
    private void RefreshCounts()
    {
        for (var i = 0; i < _grids.Count; i++) _sheets.Counts[i] = _grids[i].FilledRows;

        _sheets.Invalidate();
    }

    // ------------------------------------------------------------- проверка

    /// <summary>
    /// Сглобява същото устройство, което идва и от Excel файл, и го подава на
    /// същия процесор. Така единствената разлика между двата пътя е откъде са
    /// дошли редовете.
    /// </summary>
    private ParsedWorkbook BuildWorkbook()
    {
        var config = CollectHeader().ToDictionary(
            p => p.Key, p => CellValue.FromText(p.Value), StringComparer.OrdinalIgnoreCase);

        var sheets = new List<SheetData>();

        foreach (var grid in _grids)
        {
            var rows = new List<RawRow>();
            var snapshot = grid.Snapshot();

            for (var i = 0; i < snapshot.Count; i++)
            {
                rows.Add(new RawRow
                {
                    // Номерът съвпада с реда, който потребителят вижда отляво.
                    ExcelRow = i + 1,
                    Cells = snapshot[i].ToDictionary(
                        p => p.Key, p => CellValue.FromText(p.Value), StringComparer.OrdinalIgnoreCase)
                });
            }

            sheets.Add(new SheetData { Plan = grid.Plan, Rows = rows, UnknownColumns = [] });
        }

        return new ParsedWorkbook
        {
            Schema = _schema,
            Config = config,
            Sheets = sheets,
            Warnings = []
        };
    }

    /// <summary>Колко реда в целия отчет са извън краткия списък на фирмата.</summary>
    private int BlockedRows() => _grids.Sum(grid => grid.BlockedRows);

    /// <summary>
    /// Казва какво е станало след смяна на фирмата.
    ///
    /// Смяната не пипа нито един ред — това е нарочно. Но новата фирма има свои
    /// списъци и някои от вече попълнените стойности излизат извън тях. Те се
    /// отбелязват и спират проверката; изтриването им щеше да реши задачата,
    /// без да пита, а сгрешена е фирмата толкова често, колкото и редът.
    /// </summary>
    private void AnnounceBlocked()
    {
        var blocked = BlockedRows();

        if (blocked == 0)
        {
            _status.Text = Lang.T("Фирмата е сменена.", "The company has been changed.");
            return;
        }

        // Отчет, който вече е бил проверен, престава да бъде: списъците на
        // новата фирма са друга мярка и старата проверка не важи за нея.
        _workspace.Documents.SetStatus(_documentId, DocumentStatus.Draft);

        _summary.ForeColor = Theme.Error;
        _summary.Text = Lang.T(
            $"{blocked} реда са извън краткия списък на тази фирма. Отбелязани са в червено.",
            $"{blocked} rows are outside this company's short list. They are marked in red.");

        MessageBox.Show(this,
            Lang.T($"{blocked} реда имат стойност извън краткия списък на новата фирма.\r\n\r\n" +
                   "Нищо не е изтрито — редовете са отбелязани в червено. Докато не бъдат " +
                   "поправени, отчетът не може да мине проверка и да стане файл; записва се и " +
                   "се затваря както обикновено.\r\n\r\n" +
                   "„Само отбелязаните“ над таблицата показва точно тях.",
                   $"{blocked} rows have a value outside the new company's short list.\r\n\r\n" +
                   "Nothing was deleted — the rows are marked in red. Until they are fixed the " +
                   "report cannot pass the check or become a file; it still saves and closes as " +
                   "usual.\r\n\r\n" +
                   "“Only the marked” above the table shows exactly those."),
            Lang.T("Смяна на фирмата", "Changing the company"),
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>За теста: колко реда чакат поправка и минава ли проверката.</summary>
    internal int BlockedRowCount => BlockedRows();

    internal bool CheckForTest() => Check(announce: false);

    private bool Check(bool announce)
    {
        Save(quiet: true);

        // Спира преди проверката, а не в нея: редът е добър по схема и НИСО би
        // го приел — спира го решението на самата фирма кое ползва. Затова и
        // съобщението е друго, и статусът пада обратно на чернова.
        if (BlockedRows() is > 0 and var blocked)
        {
            _workspace.Documents.SetStatus(_documentId, DocumentStatus.Draft);

            _summary.ForeColor = Theme.Error;
            _summary.Text = Lang.T(
                $"{blocked} реда са извън краткия списък на фирмата. Поправете ги и проверете пак.",
                $"{blocked} rows are outside the company's short list. Fix them and check again.");

            if (announce)
                MessageBox.Show(this,
                    Lang.T($"{blocked} реда са отбелязани в червено: стойността им е извън краткия " +
                           "списък на фирмата.\r\n\r\n" +
                           "Поправете ги — или махнете списъка от „Списъци“, ако стойността е " +
                           "правилна.",
                           $"{blocked} rows are marked in red: their value is outside the company's " +
                           "short list.\r\n\r\n" +
                           "Fix them — or remove the list under “Lists”, if the value is right."),
                    Lang.T("Проверка", "Check"), MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return false;
        }

        try
        {
            _lastResult = _service.Process(BuildWorkbook());
        }
        catch (Exception ex)
        {
            _summary.ForeColor = Theme.Error;
            _summary.Text = Lang.T("Проверката не можа да завърши: ", "The check could not finish: ") + ex.Message;
            return false;
        }

        ShowIssues(_lastResult);

        var clean = !_lastResult.HasErrors && _lastResult.CanExport;

        if (clean)
            _workspace.Documents.SetStatus(_documentId, DocumentStatus.Checked);

        if (announce && clean)
            _status.Text = Lang.T("Проверката мина без грешки. Отчетът е готов за създаване на файл.", "The check passed with no errors. The report is ready to become a file.");

        return clean;
    }

    /// <summary>
    /// Кои редове имат дата извън периода на отчета.
    ///
    /// Отделна проверка, която не спира нищо. Отчетът обявява период в
    /// заглавните си стойности, а датите в редовете ги пише човек — и двете
    /// могат да се разминат по две причини: сбъркана дата или нарочно включен
    /// ред отпреди или отсле периода. Програмата не може да ги различи, затова
    /// само ги показва.
    ///
    /// Ползата е в баланса: отчет, чиито редове се разливат извън периода му,
    /// се застъпва със съседния и количествата се броят два пъти.
    /// </summary>
    private void CheckDates()
    {
        Save(quiet: true);

        var header = CollectHeader();

        var from = BalanceCoverage.Date(header.GetValueOrDefault("period_start"));
        var to = BalanceCoverage.Date(header.GetValueOrDefault("period_end"));

        if (from is null || to is null)
        {
            MessageBox.Show(this,
                Lang.T("Отчетът още не казва какъв период покрива. Попълнете „Начален период“ и „Краен период“ горе.",
                       "The report does not say which period it covers yet. Fill in the start and end of the period above."),
                Lang.T("Няма период", "No period"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (from > to)
        {
            MessageBox.Show(this,
                Lang.T("Периодът свършва преди да е започнал.", "The period ends before it begins."),
                Lang.T("Обърнат период", "Backwards period"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var found = new List<Issue>();

        foreach (var grid in _grids)
        {
            var columns = grid.Plan.Columns.Where(c => c.Domain.Kind == ValueKind.Date).ToList();
            if (columns.Count == 0) continue;

            for (var index = 0; index < grid.DataRowCount; index++)
            {
                var values = grid.RowValues(index);
                if (values.Count == 0) continue;

                foreach (var column in columns)
                {
                    if (BalanceCoverage.Date(values.GetValueOrDefault(column.Name)) is not { } date) continue;
                    if (date >= from && date <= to) continue;

                    var where = date < from
                        ? Lang.T("преди началото", "before the start")
                        : Lang.T("след края", "after the end");

                    found.Add(new Issue(IssueSeverity.Warning, grid.Plan.SheetName, index + 1,
                        Lang.T($"„{ReportTitles.ColumnShort(column)}“: {date:dd.MM.yyyy} е {where} на периода ({from:dd.MM.yyyy} – {to:dd.MM.yyyy}).",
                               $"“{ReportTitles.ColumnShort(column)}”: {date:dd.MM.yyyy} is {where} of the period ({from:dd.MM.yyyy} – {to:dd.MM.yyyy})."))
                    {
                        SheetTitle = ReportTitles.Table(grid.Plan.Node)
                    });
                }
            }
        }

        _issues.Fill(found.Select(IssueEntry.From));
        _issueSource = found;

        if (found.Count == 0)
        {
            _summary.ForeColor = Theme.Ok;
            _summary.Text = Lang.T(
                $"Всички дати са в периода {from:dd.MM.yyyy} – {to:dd.MM.yyyy}.",
                $"Every date is inside the period {from:dd.MM.yyyy} – {to:dd.MM.yyyy}.");
        }
        else
        {
            _summary.ForeColor = Theme.Warn;
            _summary.Text = Lang.T(
                $"{found.Count} дати са извън периода. Това не е грешка само по себе си — проверете дали е нарочно.",
                $"{found.Count} dates fall outside the period. That is not an error by itself — check whether it is deliberate.");
        }

        _status.Text = Lang.T("Проверката за дати е отделна и не спира създаването на файл.",
                              "The date check is separate and does not block creating the file.");
    }

    private void ShowIssues(ImportResult result)
    {
        var entries = result.AllIssues
            .OrderBy(i => i.Severity)
            .Select(IssueEntry.From)
            .Concat(result.SchemaErrors.Select(IssueEntry.Schema))
            .ToList();

        _issues.Fill(entries);
        _issueSource = result.AllIssues.OrderBy(i => i.Severity).ToList();

        var errors = result.AllIssues.Count(i => i.Severity == IssueSeverity.Error) + result.SchemaErrors.Count;

        if (errors == 0 && entries.Count == 0)
        {
            _summary.ForeColor = Theme.Ok;
            _summary.Text = Lang.T($"Няма намерени проблеми. Редове за подаване: {result.TotalValidRows}.", $"No problems found. Rows to submit: {result.TotalValidRows}.");
        }
        else if (errors == 0)
        {
            _summary.ForeColor = Theme.Warn;
            _summary.Text = Lang.T(
                $"{entries.Count} бележки, нито една спираща. " +
                $"Редове за подаване: {result.TotalValidRows}.",
                $"{entries.Count} notes, none of them blocking. " +
                $"Rows to submit: {result.TotalValidRows}.");
        }
        else
        {
            _summary.ForeColor = Theme.Error;
            _summary.Text = Lang.T($"{errors} неща трябва да се поправят, преди да се създаде файл.", $"{errors} things must be fixed before a file can be created.");
        }
    }

    /// <summary>Двоен клик върху проблем отвежда до реда, за който се отнася.</summary>
    /// <summary>
    /// Двоен клик върху проблем отвежда до таблицата, за която се отнася.
    ///
    /// Съответствието се прави по позиция: списъкът се пълни в същия ред, в
    /// който се пази и източникът, така че редът в списъка сочи същия проблем.
    /// </summary>
    private void JumpToIssue()
    {
        var index = _issues.SelectedIndex;
        if (index < 0 || index >= _issueSource.Count) return;

        var issue = _issueSource[index];

        for (var i = 0; i < _grids.Count; i++)
        {
            if (_grids[i].Plan.SheetName != issue.Sheet) continue;

            _sheets.SelectedIndex = i;
            _grids[i].Focus();
            break;
        }
    }

    // ------------------------------------------------------------- създаване

    private void Generate()
    {
        // Отбелязаните редове се съобщават от самата проверка: тя знае колко са
        // и къде да се гледат, а долният списък в този случай е празен.
        if (BlockedRows() > 0)
        {
            Check(announce: true);
            return;
        }

        if (!Check(announce: false))
        {
            MessageBox.Show(this,
                Lang.T("Има неща за поправяне. Списъкът е долу — двоен клик върху ред отваря мястото.", "There are things to fix. The list is below — double-click a line to open the spot."),
                Lang.T("Още не е готово", "Not ready yet"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!AppServices.Account.CanSign)
        {
            MessageBox.Show(this,
                AppServices.Account.Describe() + "\r\n\r\n" +
                Lang.T("Създаването на файл за НИСО изисква активен абонамент.", "Creating a NWIS file requires an active subscription."),
                Lang.T("Абонамент", "Subscription"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = Lang.T("XML файл (*.xml)|*.xml", "XML file (*.xml)|*.xml"),
            FileName = SuggestFileName(),
            InitialDirectory = _workspace.Settings.Get(SettingsStore.Keys.LastFolder, string.Empty)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _workspace.Settings.Set(SettingsStore.Keys.LastFolder, Path.GetDirectoryName(dialog.FileName)!);

        var choice = AskForSignature();

        try
        {
            var files = _service.Save(
                _lastResult!, dialog.FileName, Text, choice?.Certificate, choice?.Profile);

            _workspace.Documents.SetStatus(
                _documentId,
                files.Signed ? DocumentStatus.Signed : DocumentStatus.Checked,
                files.XmlPath);

            var message = files.Signed
                ? Lang.T("Файлът е създаден и подписан.", "The file is created and signed.")
                : Lang.T("Файлът е създаден, но НЕ е подписан — НИСО ще го отхвърли.", "The file is created but NOT signed — NWIS will reject it.");

            MessageBox.Show(this, message + "\r\n\r\n" + files.XmlPath,
                Lang.T("Готово", "Done"), MessageBoxButtons.OK,
                files.Signed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Lang.T("Файлът не беше създаден", "The file was not created"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string SuggestFileName()
    {
        var header = CollectHeader();
        var year = header.GetValueOrDefault("year", DateTime.Now.Year.ToString());

        return $"{_report.ElementName}_{year}.xml";
    }

    private SigningChoice? AskForSignature()
    {
        var answer = MessageBox.Show(this,
            Lang.T(
                "Схемата на НИСО изисква електронен подпис — неподписан файл се отхвърля " +
                "още при импорта.\r\n\r\nДа бъде ли подписан файлът сега с вашия КЕП?",
                "The NWIS schema requires an electronic signature — an unsigned file is " +
                "rejected at import.\r\n\r\nSign the file now with your certificate?"),
            Lang.T("Електронен подпис", "Electronic signature"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes) return null;

        using var dialog = new SignDialog();
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Choice : null;
    }

    // ------------------------------------------------------------- клавиши

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                Save(quiet: false);
                return true;

            case Keys.F5:
                Check(announce: true);
                return true;

            case Keys.F1:
                ShowHelp();
                return true;

            case Keys.Control | Keys.F1:
                ShowShortcuts();
                return true;

            case Keys.F6:
                ToggleSidePanel();
                return true;

            case Keys.Control | Keys.Tab:
                _sheets.SelectedIndex = (_sheets.SelectedIndex + 1) % _sheets.TabCount;
                return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    /// <summary>
    /// При затваряне се записва мълчаливо.
    ///
    /// Въпросът „да запиша ли“ пита за нещо, което програмата вече е решила:
    /// записът е автоматичен на всеки двайсет секунди и няма как да се
    /// избере „не“. Оставаше само едно излишно натискане на изход.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _autosave.Stop();

        if (_grids.Any(g => g.IsDirty)) Save(quiet: true);

        base.OnFormClosing(e);
    }
}

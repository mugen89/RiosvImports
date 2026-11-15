using RiosvImports.App.Controls;
using RiosvImports.Core;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Data;

namespace RiosvImports.App.Shell;

/// <summary>
/// Съставянето на един кратък списък.
///
/// Редът на питане следва как човек мисли за това: първо за кои фирми, после
/// докъде стига, после кое поле, и чак накрая — кои стойности. Фирмите са
/// най-отгоре и се отмятат по няколко: една и съща площадка се води на две
/// дружества и списъкът им е един и същ. Записват се обаче поотделно — оттам
/// нататък всяко се пипа само за себе си.
///
/// Обхватът е три нива и по-точното печели над по-широкото. „Всички отчети“ е
/// нарочно първото и по подразбиране: двайсетте ЕКО кода на една фирма са
/// едни и същи навсякъде, а въвеждането им за всеки от четиридесет и двата
/// вида поотделно е точно работата, която този екран трябва да спести.
///
/// При промяна на вече записан списък се пипат само стойностите. Обхватът е
/// ключът на записа — сменен, той прави друг списък, а не същия; тогава
/// по-честно е старият да се изтрие.
/// </summary>
internal sealed class ValueListDialog : Dialog
{
    private readonly ImportService _service;
    private readonly IReadOnlyList<Company> _companies;
    private readonly ValueList? _editing;
    private readonly long? _preferred;

    private readonly CheckedListBox _firms = new();
    private readonly SegmentedControl _kind = new();
    private readonly ComboBox _report = new();
    private readonly CheckBox _oneSheet = new();
    private readonly ComboBox _sheet = new();
    private readonly ComboBox _field = new();

    private readonly SearchBox _search = new(Lang.T("Търсете по код или по име", "Search by code or by name"));
    private readonly CheckedListBox _values = new();
    private readonly Label _chosenCount = new();

    /// <summary>Отметнатото се пази тук, а не в списъка: търсенето го пресява.</summary>
    private readonly HashSet<string> _chosen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Стойностите, които в момента се виждат — редът им е редът в списъка.</summary>
    private List<EnumOption> _shown = [];

    /// <summary>Пълненето на списъка вдига ItemCheck; тогава то не е избор на човека.</summary>
    private bool _filling;

    /// <summary>
    /// <paramref name="preferred"/> е фирмата от лентата отляво: тя влиза
    /// отметната. Списъкът остава с отметки, защото един и същи кратък списък
    /// обикновено важи за няколко дружества и въвеждането му по веднъж за
    /// всяко е точно работата, която този екран спестява.
    /// </summary>
    public ValueListDialog(
        ImportService service,
        IReadOnlyList<Company> companies,
        ValueList? editing = null,
        long? preferred = null)
    {
        _service = service;
        _companies = companies;
        _editing = editing;
        _preferred = preferred;

        Text = editing is null
            ? Lang.T("Нов кратък списък", "New short list")
            : Lang.T("Кратък списък", "Short list");

        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(Theme.Scaled(980), Theme.Scaled(700));
        KeyPreview = true;

        Controls.Add(BuildBody());
        Controls.Add(BuildFooter());
        Controls.Add(BuildHeading());

        FillFirms();
        FillKinds();
        Preload();

        Theme.Apply(this);
    }

    // ------------------------------------------------------------- резултат

    public IReadOnlyList<long> Companies { get; private set; } = [];
    public string SchemaKey { get; private set; } = string.Empty;

    /// <summary>Празно значи „всички отчети от този вид“.</summary>
    public string Report { get; private set; } = string.Empty;

    /// <summary>Празно значи „целият отчет“.</summary>
    public string Node { get; private set; } = string.Empty;

    public string Column { get; private set; } = string.Empty;
    public IReadOnlyList<string> Allowed { get; private set; } = [];

    // ------------------------------------------------------------- устройство

    private SchemaDefinition CurrentSchema => _service.Schemas[Math.Max(_kind.SelectedIndex, 0)];

    private Control BuildHeading()
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(78),
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.GapLarge, Theme.Gap, Theme.GapLarge, 0)
        };

        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(26),
            Text = _editing is null
                ? Lang.T("За избраните фирми се записва по един списък — оттам нататък се пипат поотделно.",
                         "One list is saved per chosen company — from then on each is edited on its own.")
                : Lang.T("Обхватът не се мени. Отметнете кои стойности да се предлагат.",
                         "The scope does not change. Tick which values should be offered."),
            ForeColor = Theme.InkMuted
        });

        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(38),
            Text = Text,
            Font = Theme.Title,
            ForeColor = Theme.Ink
        });

        return host;
    }

    private Control BuildBody()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.GapLarge, 0, Theme.GapLarge, 0)
        };

        host.Controls.Add(BuildValues());
        host.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Theme.GapLarge, BackColor = Theme.Canvas });
        host.Controls.Add(BuildScope());

        return host;
    }

    /// <summary>Лявата колона: за кого, докъде и кое поле.</summary>
    private Control BuildScope()
    {
        var host = new Panel { Dock = DockStyle.Left, Width = Theme.Scaled(420), BackColor = Theme.Canvas };

        _field.DropDownStyle = ComboBoxStyle.DropDownList;
        _field.Dock = DockStyle.Top;
        _field.Font = Theme.Big;
        _field.SelectedIndexChanged += (_, _) => FillValues(keep: true);
        Theme.StyleInput(_field);

        _sheet.DropDownStyle = ComboBoxStyle.DropDownList;
        _sheet.Dock = DockStyle.Top;
        _sheet.Enabled = false;
        _sheet.SelectedIndexChanged += (_, _) => FillFields();
        Theme.StyleInput(_sheet);

        // Изключение, не подразбиране: едно и също поле почти винаги значи
        // едно и също във всички таблици на отчета. Затова нивото „лист“ стои
        // зад отметка — който не му знае смисъла, не го и вижда.
        _oneSheet.Dock = DockStyle.Top;
        _oneSheet.Height = Theme.Base.Height + 12;
        _oneSheet.Text = Lang.T("Само за един лист", "For one sheet only");
        _oneSheet.ForeColor = Theme.Ink;
        _oneSheet.BackColor = Color.Transparent;
        _oneSheet.Enabled = false;
        _oneSheet.CheckedChanged += (_, _) =>
        {
            _sheet.Enabled = _oneSheet.Checked && _report.SelectedIndex > 0;
            FillFields();
        };

        _report.DropDownStyle = ComboBoxStyle.DropDownList;
        _report.Dock = DockStyle.Top;
        _report.Font = Theme.Big;
        _report.SelectedIndexChanged += (_, _) =>
        {
            // „Всички отчети“ и „само за един лист“ се изключват взаимно:
            // имената на листовете важат в рамките на един отчет.
            if (_report.SelectedIndex == 0) _oneSheet.Checked = false;

            _oneSheet.Enabled = _report.SelectedIndex > 0;
            _sheet.Enabled = _oneSheet.Checked && _report.SelectedIndex > 0;

            FillSheets();
            FillFields();
        };
        Theme.StyleInput(_report);

        _kind.Dock = DockStyle.Top;
        _kind.SelectedIndexChanged += (_, _) => { FillReports(); FillSheets(); FillFields(); };

        _firms.Dock = DockStyle.Top;
        _firms.Height = Theme.Scaled(132);
        _firms.BorderStyle = BorderStyle.FixedSingle;
        _firms.CheckOnClick = true;
        _firms.IntegralHeight = false;
        Theme.StyleInput(_firms);

        // Закачените отгоре се редят наопаки на реда на добавяне: последният
        // добавен застава най-горе.
        Add(host, _field, Lang.T("Поле", "Field"));
        Add(host, _sheet, Lang.T("Лист", "Sheet"));
        host.Controls.Add(_oneSheet);
        Add(host, _report, Lang.T("Отчет", "Report"));
        Add(host, _kind, Lang.T("Вид отчетност", "Kind of reporting"));
        Add(host, _firms, Lang.T("Фирми", "Companies"));

        return host;
    }

    /// <summary>Полето с надписа над него.</summary>
    private static void Add(Control host, Control input, string caption)
    {
        host.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Gap, BackColor = Theme.Canvas });
        host.Controls.Add(input);
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Small.Height + 6,
            Font = Theme.Small,
            ForeColor = Theme.InkMuted,
            Text = caption
        });
    }

    /// <summary>Дясната колона: самите стойности.</summary>
    private Control BuildValues()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };

        var frame = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            Fill = Theme.Canvas,
            Outline = Theme.Border
        };

        _values.Dock = DockStyle.Fill;
        _values.BorderStyle = BorderStyle.None;
        _values.CheckOnClick = true;
        _values.IntegralHeight = false;
        Theme.StyleInput(_values);
        _values.ItemCheck += (_, e) =>
        {
            if (_filling || e.Index < 0 || e.Index >= _shown.Count) return;

            var code = _shown[e.Index].Value;

            if (e.NewValue == CheckState.Checked) _chosen.Add(code);
            else _chosen.Remove(code);

            ShowCount();
        };

        frame.Controls.Add(_values);

        _search.Dock = DockStyle.Top;
        _search.TextChanged += (_, _) => FillValues(keep: true);
        _search.ForwardArrowsTo(_values);

        _chosenCount.Dock = DockStyle.Bottom;
        _chosenCount.Height = Theme.Base.Height + 10;
        _chosenCount.Font = Theme.Small;
        _chosenCount.ForeColor = Theme.InkMuted;

        host.Controls.Add(frame);
        host.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Gap, BackColor = Theme.Canvas });
        host.Controls.Add(_search);
        host.Controls.Add(BuildValueTools());
        host.Controls.Add(_chosenCount);

        return host;
    }

    private Control BuildValueTools()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(44),
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Theme.Canvas
        };

        var all = Theme.GhostButton(Lang.T("Отметни показаните", "Tick what is shown"));
        all.Width = Theme.Scaled(190);
        all.Click += (_, _) => SetShown(true);

        var none = Theme.GhostButton(Lang.T("Изчисти", "Clear"));
        none.Width = Theme.Scaled(120);
        none.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        none.Click += (_, _) => SetShown(false);

        bar.Controls.Add(all);
        bar.Controls.Add(none);

        return bar;
    }

    private Control BuildFooter()
    {
        var host = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Scaled(76),
            Padding = new Padding(Theme.GapLarge, Theme.Gap, Theme.GapLarge, Theme.Gap),
            BackColor = Theme.Canvas
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            Width = Theme.Scaled(300),
            WrapContents = false,
            BackColor = Theme.Canvas
        };

        var ok = Theme.PrimaryButton(Lang.T("Запиши", "Save"));
        ok.Glyph = Theme.Glyph.Save;
        ok.Width = Theme.Scaled(160);
        ok.Click += (_, _) => Accept();

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Width = Theme.Scaled(110);
        cancel.Height = Theme.Scaled(40);
        cancel.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        host.Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;

        return host;
    }

    // ------------------------------------------------------------- пълнене

    private void FillFirms()
    {
        foreach (var company in _companies)
            _firms.Items.Add(company.Display, company.Id == _preferred);
    }

    private void FillKinds()
    {
        _kind.SetItems(_service.Schemas.Select(ShortKind));

        FillReports();
        FillSheets();
        FillFields();
    }

    private static string ShortKind(SchemaDefinition schema) => schema.Key switch
    {
        "year" => Lang.T("Годишни", "Annual"),
        "quarter" => Lang.T("Тримесечни", "Quarterly"),
        "book" => Lang.T("Отчетни книги", "Log books"),
        _ => schema.Title
    };

    private void FillReports()
    {
        _report.BeginUpdate();
        _report.Items.Clear();
        _report.Items.Add(Lang.T("Всички отчети от този вид", "All reports of this kind"));

        foreach (var report in CurrentSchema.Reports) _report.Items.Add(ReportTitles.Short(report));

        _report.SelectedIndex = 0;
        _report.EndUpdate();
    }

    private void FillSheets()
    {
        _sheet.BeginUpdate();
        _sheet.Items.Clear();

        foreach (var node in Sheets()) _sheet.Items.Add(ReportTitles.Table(node));

        if (_sheet.Items.Count > 0) _sheet.SelectedIndex = 0;
        _sheet.EndUpdate();
    }

    private void FillFields()
    {
        var previous = SelectedField?.Name;
        var fields = FieldsInScope().ToList();

        _field.BeginUpdate();
        _field.Items.Clear();

        foreach (var column in fields)
            _field.Items.Add(ReportTitles.ColumnShort(column) + $"  ({column.Domain.Options.Count})");

        if (fields.Count > 0)
        {
            // Същото поле остава избрано при смяна на обхвата: човек стеснява
            // обхвата тъкмо защото полето му вече е ясно.
            var same = fields.FindIndex(c => c.Name == previous);
            _field.SelectedIndex = Math.Max(same, 0);
        }

        _field.EndUpdate();

        FillValues(keep: true);
    }

    private void FillValues(bool keep)
    {
        if (!keep) _chosen.Clear();

        var column = SelectedField;
        var query = _search.Text.Trim();

        _shown = column is null
            ? []
            : [.. column.Domain.Options.Where(o =>
                query.Length == 0 ||
                o.Value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (o.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false))];

        _filling = true;
        _values.BeginUpdate();
        _values.Items.Clear();

        foreach (var option in _shown) _values.Items.Add(option.Display, _chosen.Contains(option.Value));

        _values.EndUpdate();
        _filling = false;

        ShowCount();
    }

    private void SetShown(bool on)
    {
        foreach (var option in _shown)
        {
            if (on) _chosen.Add(option.Value);
            else _chosen.Remove(option.Value);
        }

        FillValues(keep: true);
    }

    private void ShowCount()
    {
        if (IsDisposed) return;

        var total = SelectedField?.Domain.Options.Count ?? 0;

        _chosenCount.Text = _chosen.Count == 0
            ? Lang.T($"Няма отметнати — от {total} стойности.", $"Nothing ticked — out of {total} values.")
            : Lang.T($"Отметнати {_chosen.Count} от {total}.", $"{_chosen.Count} of {total} ticked.");
    }

    /// <summary>Пълни прозореца от вече записан списък и заключва обхвата.</summary>
    private void Preload()
    {
        if (_editing is not { } entry) return;

        var kind = _service.Schemas.ToList().FindIndex(s => s.Key == entry.SchemaKey);
        _kind.SelectedIndex = Math.Max(kind, 0);

        var index = CurrentSchema.Reports.ToList().FindIndex(r => r.ElementName == entry.Report);
        _report.SelectedIndex = index >= 0 ? index + 1 : 0;

        if (entry.Node.Length > 0)
        {
            _oneSheet.Checked = true;

            var sheet = Sheets().FindIndex(n => n.ElementName == entry.Node);
            if (sheet >= 0) _sheet.SelectedIndex = sheet;
        }

        FillFields();

        var field = FieldsInScope().ToList().FindIndex(c => c.Name == entry.Column);
        if (field >= 0) _field.SelectedIndex = field;

        var company = _companies.ToList().FindIndex(c => c.Id == entry.CompanyId);
        if (company >= 0) _firms.SetItemChecked(company, true);

        _chosen.Clear();
        foreach (var value in entry.Allowed) _chosen.Add(value);

        FillValues(keep: true);

        _firms.Enabled = false;
        _kind.Enabled = false;
        _report.Enabled = false;
        _oneSheet.Enabled = false;
        _sheet.Enabled = false;
        _field.Enabled = false;
    }

    // ------------------------------------------------------------- избраното

    private ReportNode? SelectedReport =>
        _report.SelectedIndex > 0 ? CurrentSchema.Reports[_report.SelectedIndex - 1] : null;

    private List<ReportNode> Sheets() =>
        SelectedReport is { } report
            ? [.. report.SelfAndDescendants().Where(n => n.HasData)]
            : [];

    private ReportNode? SelectedSheet
    {
        get
        {
            if (!_oneSheet.Checked) return null;

            var sheets = Sheets();

            return _sheet.SelectedIndex >= 0 && _sheet.SelectedIndex < sheets.Count
                ? sheets[_sheet.SelectedIndex]
                : null;
        }
    }

    private ColumnDef? SelectedField
    {
        get
        {
            var fields = FieldsInScope().ToList();

            return _field.SelectedIndex >= 0 && _field.SelectedIndex < fields.Count
                ? fields[_field.SelectedIndex]
                : null;
        }
    }

    /// <summary>
    /// Полетата с меню, които обхватът покрива.
    ///
    /// Едно и също име се среща в няколко таблици — „ЕКО код“ го има в шест
    /// от отчетната книга. Взима се първото: зад тях стои един и същ прост тип
    /// от схемата, а записът се води по име на поле.
    /// </summary>
    private IEnumerable<ColumnDef> FieldsInScope()
    {
        var nodes = SelectedSheet is { } sheet
            ? [sheet]
            : SelectedReport is { } report
                ? report.SelfAndDescendants()
                : CurrentSchema.Reports.SelectMany(r => r.SelfAndDescendants());

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            foreach (var column in node.Attributes)
            {
                if (column.Domain.IsEnum && !column.IsLink && seen.Add(column.Name))
                    yield return column;
            }
        }
    }

    // ------------------------------------------------------------- записване

    private void Accept()
    {
        var firms = Enumerable.Range(0, _firms.Items.Count)
            .Where(_firms.GetItemChecked)
            .Select(i => _companies[i].Id)
            .ToList();

        if (firms.Count == 0)
        {
            Complain(Lang.T("Изберете поне една фирма.", "Choose at least one company."));
            return;
        }

        if (SelectedField is not { } column)
        {
            Complain(Lang.T("Изберете поле.", "Choose a field."));
            return;
        }

        // Празен списък не се записва: той не ограничава нищо, а оставен на
        // екрана изглежда като свършена работа. Който иска да махне
        // ограничението, маха самия списък.
        if (_chosen.Count == 0)
        {
            Complain(Lang.T("Отметнете поне една стойност.", "Tick at least one value."));
            return;
        }

        Companies = firms;
        SchemaKey = CurrentSchema.Key;
        Report = SelectedReport?.ElementName ?? string.Empty;
        Node = SelectedSheet?.ElementName ?? string.Empty;
        Column = column.Name;

        // Записва се в реда на схемата, а не в реда на отмятане: така два
        // еднакви списъка изглеждат еднакво и в базата.
        Allowed = [.. column.Domain.Options.Where(o => _chosen.Contains(o.Value)).Select(o => o.Value)];

        DialogResult = DialogResult.OK;
    }

    private void Complain(string text) =>
        MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
}

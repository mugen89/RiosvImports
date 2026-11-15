using System.ComponentModel;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;
using RiosvImports.Data;

namespace RiosvImports.App.Editor;

/// <summary>
/// Страничният панел с предварително зададените стойности.
///
/// Скрит е по подразбиране и се отваря с F6. Причината да не е още един ред
/// над таблицата е проста: редовете горе са четири и на лаптоп за таблицата
/// остава ивица. Отстрани панелът взема ширина, каквато на широк екран и
/// без това стои празна, а на тесен се затваря и не пречи.
///
/// Съдържанието зависи от избрания раздел: където няма какво да се помага,
/// панелът го казва, вместо да показва чужди полета.
/// </summary>
internal sealed class TransferPanel : Panel
{
    private readonly SchemaDefinition _schema;
    private readonly SettingsStore _settings;
    private readonly Panel _body = new();
    private readonly Label _title = new();
    private readonly Label _empty = new();
    private readonly CheckBox _always = new();

    /// <summary>„Същият код“ — важи само за правилата, които го предлагат.</summary>
    private readonly CheckBox _sameCode = new();

    /// <summary>Наименованото разделяне — важи само за правилата, които делят.</summary>
    private readonly Panel _templateBox = new();
    private readonly ComboBox _template = new();

    private List<SplitTemplate> _saved = [];
    private TransferFields? _fields;

    /// <summary>Запомненото за всяко правило — панелът се пресъздава при смяна на раздел.</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _remembered = new(StringComparer.Ordinal);

    /// <summary>
    /// Кратките списъци се питат при всяко сглобяване, а не се дават наготово:
    /// панелът живее колкото целия отчет, а фирмата му може да се смени по
    /// средата — и „Списъци“ е отделен екран, който може да е пипан дотогава.
    /// </summary>
    private readonly Func<ValueLimits> _limits;

    public TransferPanel(
        SchemaDefinition schema,
        SettingsStore settings,
        int width,
        Func<ValueLimits>? limits = null)
    {
        _schema = schema;
        _settings = settings;
        _limits = limits ?? (() => ValueLimits.None);

        Dock = DockStyle.Right;
        Width = width;
        Visible = false;
        BackColor = Theme.Surface;
        Padding = new Padding(Theme.Gap, Theme.GapSmall, Theme.Gap, Theme.Gap);

        _title.Dock = DockStyle.Top;
        _title.Height = Theme.Heading.Height + 10;
        _title.Font = Theme.Heading;
        _title.ForeColor = Theme.Accent;
        _title.AutoEllipsis = true;

        _empty.Dock = DockStyle.Top;
        _empty.Height = Theme.Base.Height * 3;
        _empty.ForeColor = Theme.InkMuted;
        _empty.Text = Lang.T(
            "За този раздел няма помощни полета.",
            "There are no helper fields for this tab.");

        _always.Dock = DockStyle.Bottom;
        _always.Height = Theme.Base.Height + 14;
        _always.Text = Lang.T("Винаги използвай тези стойности", "Always use these values");
        _always.ForeColor = Theme.Ink;
        _always.BackColor = Color.Transparent;
        _always.Visible = false;

        // Кодът след третирането най-често е същият, с който отпадъкът е
        // дошъл. Отметката го заключва: полето спира да се пита и стойността
        // идва от самия ред. Така не се преписва от списък с осемстотин реда
        // това, което вече пише отсреща.
        _sameCode.Dock = DockStyle.Bottom;
        _sameCode.Height = Theme.Base.Height + 14;
        _sameCode.Text = Lang.T("Използвай същия код", "Use the same code");
        _sameCode.ForeColor = Theme.Ink;
        _sameCode.BackColor = Color.Transparent;
        _sameCode.Visible = false;
        _sameCode.CheckedChanged += (_, _) => ApplyLock();

        // Кратко нарочно: всеки ред от този надпис е ред по-малко за полетата
        // отгоре, а панелът е тесен и на лаптоп висок колкото половин екран.
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Small.Height * 2 + 4,
            Font = Theme.Small,
            ForeColor = Theme.InkFaint,
            Text = Lang.T(
                "Без отметката всеки ред пита.",
                "Without the tick each row asks.")
        };

        // Шаблонът стои над полетата: изборът му решава всичко останало.
        _templateBox.Dock = DockStyle.Top;
        _templateBox.Height = Theme.Base.Height * 2 + Theme.Big.Height + 20;
        _templateBox.BackColor = Theme.Surface;
        _templateBox.Visible = false;

        var templateLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Base.Height + 3,
            Text = Lang.T("Шаблон за разделяне", "Splitting template"),
            ForeColor = Theme.InkMuted,
            BackColor = Color.Transparent
        };

        _template.Dock = DockStyle.Top;
        _template.DropDownStyle = ComboBoxStyle.DropDownList;
        _template.Font = Theme.Big;
        Theme.StyleInput(_template);

        var templateHint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Small.Height * 2 + 2,
            Font = Theme.Small,
            ForeColor = Theme.InkFaint,
            BackColor = Color.Transparent,
            Text = Lang.T(
                "Шаблоните се записват в прозорчето за разделяне.",
                "Templates are saved in the splitting window.")
        };

        _templateBox.Controls.Add(_template);
        _templateBox.Controls.Add(templateHint);
        _templateBox.Controls.Add(templateLabel);

        _body.Dock = DockStyle.Fill;
        _body.BackColor = Theme.Surface;
        _body.AutoScroll = true;

        // Закачените отдолу се редят наопаки: последният добавен застава най-
        // ниско. Затова редът е обратен на видимия — надписът е на дъното, над
        // него двете отметки.
        Controls.Add(_body);
        Controls.Add(_templateBox);
        Controls.Add(_sameCode);
        Controls.Add(_always);
        Controls.Add(hint);
        Controls.Add(_empty);
        Controls.Add(_title);
    }

    /// <summary>
    /// Дали редът да се пренася без питане.
    ///
    /// Гледа се правилото, а не дали отметката се вижда: Visible на дъщерна
    /// контрола връща false, докато прозорецът още не е показан, и панелът
    /// би отричал каквото сам показва.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AlwaysUse
    {
        get => Rule is not null && _always.Checked;
        set => _always.Checked = value;
    }

    /// <summary>
    /// Дали кодът се взема от изходния ред.
    ///
    /// Пита се правилото, не видимостта на отметката — по същата причина,
    /// поради която и <see cref="AlwaysUse"/> го прави: Visible на дъщерна
    /// контрола лъже, докато прозорецът още не е показан.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SameCode
    {
        get => Rule?.SameAsSource is not null && _sameCode.Checked;
        set => _sameCode.Checked = value;
    }

    /// <summary>Правилото, което панелът обслужва в момента.</summary>
    public TransferRule? Rule { get; private set; }

    /// <summary>Избраният шаблон за разделяне, ако има такъв.</summary>
    public SplitTemplate? Template
    {
        get
        {
            var index = _template.SelectedIndex - 1;
            return index >= 0 && index < _saved.Count ? _saved[index] : null;
        }
    }

    /// <summary>Избира шаблон по име — ползва се от тестовете.</summary>
    internal bool UseTemplate(string name)
    {
        var at = _saved.FindIndex(t => t.Name == name);
        if (at < 0) return false;

        _template.SelectedIndex = at + 1;
        return true;
    }

    /// <summary>Стойностите, попълнени за сегашното правило.</summary>
    public Dictionary<string, string> Values() =>
        _fields?.Values() ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Кои задължителни полета липсват — празен списък значи готово.</summary>
    public IReadOnlyList<string> Missing() => _fields?.Missing() ?? [];

    /// <summary>
    /// Приема стойностите, избрани в прозорчето, за нови предварителни.
    ///
    /// Първият ред минава през прозорчето и там човек избира кода и датата.
    /// Ако вторият ред пита пак отначало, изборът се е загубил без причина.
    /// </summary>
    public void Remember(IReadOnlyDictionary<string, string> values) => _fields?.SetValues(values);

    /// <summary>
    /// Пренарежда панела за избрания раздел. Подава се възелът на раздела;
    /// null изчиства панела.
    /// </summary>
    public void ShowFor(string report, string? node)
    {
        Save();

        var rule = node is null ? null : TransferRule.From(report, node);

        Rule = rule;
        _body.Controls.Clear();
        _fields = null;

        if (rule is null || rule.Target(_schema) is not { } target)
        {
            _title.Text = Lang.T("Помощни полета", "Helper fields");
            _empty.Visible = true;
            _always.Visible = false;
            _sameCode.Visible = false;
            _templateBox.Visible = false;
            return;
        }

        _title.Text = rule.Title(_schema);
        _empty.Visible = false;
        _always.Visible = true;
        _sameCode.Visible = rule.CanReuseSource;

        _templateBox.Visible = rule.CanSplit;
        if (rule.CanSplit) FillTemplates(rule.ToNode);

        var fields = new TransferFields(rule, target, Width - Padding.Horizontal - Theme.Gap, _limits())
        {
            Dock = DockStyle.Top
        };

        if (_remembered.TryGetValue(rule.ToNode, out var saved)) fields.SetValues(saved);

        _body.Controls.Add(fields);
        _fields = fields;

        ApplyLock();
    }

    /// <summary>
    /// Прилага отметката върху полето, което тя заключва.
    ///
    /// В панела не се знае кой ред ще бъде натиснат, затова вместо стойност
    /// стои откъде ще дойде. Самият код се вижда в прозорчето при реда.
    /// </summary>
    private void ApplyLock()
    {
        if (_fields is null || Rule?.SameAsSource is not { } name) return;

        if (_sameCode.Checked) _fields.Lock(name, Lang.T("(от реда)", "(from the row)"));
        else _fields.Unlock(name);
    }

    /// <summary>
    /// Чете шаблоните наново при всяко показване: те се записват в другото
    /// прозорче и списъкът тук трябва да ги знае веднага.
    /// </summary>
    private void FillTemplates(string node)
    {
        var chosen = _template.SelectedIndex > 0 ? _template.Text : null;

        _saved = SplitTemplates.Load(_settings, node);

        _template.Items.Clear();
        _template.Items.Add(Lang.T("(питай за всеки ред)", "(ask for every row)"));

        foreach (var template in _saved) _template.Items.Add(template.Name);

        var back = chosen is null ? -1 : _saved.FindIndex(t => t.Name == chosen);
        _template.SelectedIndex = back >= 0 ? back + 1 : 0;
    }

    /// <summary>Запомня попълненото, преди панелът да се пренареди.</summary>
    private void Save()
    {
        if (Rule is null || _fields is null) return;

        _remembered[Rule.ToNode] = _fields.Values();
    }
}

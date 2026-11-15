using System.Globalization;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;
using RiosvImports.Data;

namespace RiosvImports.App.Editor;

/// <summary>
/// Прозорчето, което разделя един ред на няколко.
///
/// От третирането рядко излиза един отпадък: петдесет тона стар кабел се
/// разпадат на мед, пластмаса и остатък. Всеки дял е нов ред с нов код по
/// ЕКО, а сборът им трябва да е точно петдесет — не 49,999 и не 50,001,
/// защото НИСО брои до грам.
///
/// Затова тук количеството и процентът вървят заедно: пише се което е под
/// ръка, вижда се и другото. А когато едно и също разкомплектоване се прави
/// всеки месец, дяловете се записват като шаблон и после се прилагат наведнъж.
/// </summary>
internal sealed class SplitDialog : Dialog
{
    private readonly ColumnDef _code;

    /// <summary>Кодовете, които менюто предлага — след кратките списъци.</summary>
    private readonly IReadOnlyList<EnumOption> _offered;
    private readonly ColumnDef _amount;
    private readonly ColumnDef _when;
    private readonly decimal _total;
    private readonly SettingsStore _settings;
    private readonly string _node;

    private readonly Panel _rows = new();
    private readonly NumericUpDown _count = new();
    private readonly ComboBox _templates = new();
    private readonly Label _sum = new();
    private readonly DateTimePicker _date = new();
    private readonly List<Line> _lines = [];

    private List<SplitTemplate> _saved;
    private Controls.AppButton? _create;
    private bool _syncing;

    public SplitDialog(
        TransferRule rule,
        SchemaDefinition schema,
        ReportNode target,
        SettingsStore settings,
        decimal total,
        IReadOnlyDictionary<string, string> defaults,
        IReadOnlyList<(string Ewc, decimal Amount)>? seed = null,
        ValueLimits? limits = null)
    {
        _settings = settings;
        _node = rule.ToNode;
        _total = total;
        _saved = SplitTemplates.Load(settings, rule.ToNode);

        _code = target.Attributes.First(a => a.Name == "ewc");

        // Само предлаганото се свива. Разчитането на набран код по-долу
        // продължава да пита схемата: тук се разделя вече свършена работа и
        // код отпреди списъка не бива да се изчиства мълчаливо.
        _offered = (limits ?? ValueLimits.None)
            .Offer(target.Root.ElementName, target.ElementName, _code);
        _amount = target.Attributes.First(a => a.Name == "amount_ton");
        _when = target.Attributes.First(a => a.Name == "date_record");

        Text = rule.Title(schema);
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        KeyPreview = true;
        ClientSize = new Size(Width_, Theme.Scaled(520));

        // Редът на добавяне решава подредбата: пълнещото се добавя първо,
        // залепените отстрани — след него.
        Controls.Add(BuildRows());
        Controls.Add(BuildFoot());
        Controls.Add(BuildHead(rule, defaults));

        // Преразпределяне на вече създадена група: прозорчето тръгва от нея.
        if (seed is { Count: > 0 })
        {
            _count.Value = Math.Clamp(seed.Count, _count.Minimum, _count.Maximum);
            Rebuild((int)_count.Value);

            _syncing = true;

            for (var i = 0; i < _lines.Count && i < seed.Count; i++)
            {
                _lines[i].Code.Text = Display(seed[i].Ewc);
                _lines[i].Amount.Text = SplitMath.Amount(seed[i].Amount);
                _lines[i].Percent.Text = SplitMath.Share(SplitMath.Percent(seed[i].Amount, total));
            }

            _syncing = false;
            RefreshSum();
        }
        else
        {
            Rebuild((int)_count.Value);
        }

        Theme.Apply(this);
    }

    // Мерено при 100 % — през мащаба, както и височината до него.
    private static int Width_ => Theme.Scaled(800);
    private const int Margin_ = 20;

    /// <summary>Сменя броя редове — ползва се от тестовете.</summary>
    internal void SetCount(int value) =>
        _count.Value = Math.Clamp(value, _count.Minimum, _count.Maximum);

    /// <summary>Избира шаблон по име — ползва се от тестовете.</summary>
    internal void UseTemplate(string name)
    {
        var at = _saved.FindIndex(t => t.Name == name);
        if (at >= 0) _templates.SelectedIndex = at + 1;
    }

    /// <summary>Готовите редове — попълва се чак при „Създай“.</summary>
    public List<Dictionary<string, string>> Rows { get; private set; } = [];

    // ------------------------------------------------------------- устройство

    private Control BuildHead(TransferRule rule, IReadOnlyDictionary<string, string> defaults)
    {
        var line = Theme.Base.Height;

        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = line * 2 + 56,
            BackColor = Theme.Surface,
            Padding = new Padding(Margin_, 12, Margin_, 10)
        };

        var top = 8;

        // Шаблонът е първи: изборът му запълва всичко отдолу наведнъж.
        var templateLabel = Caption(Lang.T("Шаблон", "Template"), Margin_, top);

        _templates.Location = new Point(Margin_, top + line + 4);
        _templates.Size = new Size(Theme.Scaled(230), line + 10);
        _templates.DropDownStyle = ComboBoxStyle.DropDownList;
        _templates.Font = Theme.Base;
        Theme.StyleInput(_templates);
        _templates.SelectedIndexChanged += (_, _) => ApplyTemplate();

        var save = Theme.SecondaryButton(Lang.T("Запази…", "Save…"));
        save.Location = new Point(_templates.Right + Theme.GapSmall, _templates.Top);
        save.Height = _templates.Height;
        save.FitWidth(8);
        save.Click += (_, _) => SaveTemplate();

        var drop = Theme.GhostButton(Lang.T("Изтрий", "Delete"), null);
        drop.Location = new Point(save.Right + 4, _templates.Top);
        drop.Height = _templates.Height;
        drop.FitWidth(4);
        drop.Click += (_, _) => DeleteTemplate();

        // Датата е обща за всички нови редове.
        var dateLabel = Caption(rule.Caption(_when), drop.Right + Theme.Gap, top);

        _date.Location = new Point(drop.Right + Theme.Gap, _templates.Top);
        _date.Size = new Size(Theme.Scaled(130), _templates.Height);
        _date.Format = DateTimePickerFormat.Custom;
        _date.CustomFormat = ValueConverter.BulgarianDateFormat;
        _date.Font = Theme.Base;
        Theme.StyleInput(_date);

        if (defaults.TryGetValue(_when.Name, out var text) &&
            ValueConverter.TryNormalizeDate(text, out var normalized) &&
            DateTime.TryParseExact(normalized, ValueConverter.BulgarianDateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            _date.Value = date;
        }

        var countLabel = Caption(Lang.T("Брой", "How many"), _date.Right + Theme.Gap, top);

        _count.Location = new Point(_date.Right + Theme.Gap, _templates.Top);
        _count.Size = new Size(Theme.Scaled(54), _templates.Height);
        _count.Minimum = 1;
        _count.Maximum = 20;
        _count.Value = 2;
        _count.Font = Theme.Base;
        _count.TextAlign = HorizontalAlignment.Center;
        Theme.StyleInput(_count);
        _count.ValueChanged += (_, _) => Rebuild((int)_count.Value);

        // Стрелките на кутийката са две точки на екрана и се уцелват трудно.
        // Един ред повече или по-малко е най-честото движение тук.
        var less = Step("−", -1, _count.Right + 4, _templates.Top, _templates.Height);
        var more = Step("+", 1, less.Right + 4, _templates.Top, _templates.Height);

        host.Controls.Add(templateLabel);
        host.Controls.Add(_templates);
        host.Controls.Add(save);
        host.Controls.Add(drop);
        host.Controls.Add(dateLabel);
        host.Controls.Add(_date);
        host.Controls.Add(countLabel);
        host.Controls.Add(_count);
        host.Controls.Add(less);
        host.Controls.Add(more);

        FillTemplates();

        return host;
    }

    /// <summary>Копче, което мърда броя редове с един.</summary>
    private Controls.AppButton Step(string glyph, int by, int x, int y, int height)
    {
        var button = Theme.SecondaryButton(glyph);
        button.Location = new Point(x, y);
        button.Size = new Size(height, height);
        button.Font = Theme.Bold;
        button.Click += (_, _) =>
            _count.Value = Math.Clamp(_count.Value + by, _count.Minimum, _count.Maximum);

        return button;
    }

    private static Label Caption(string text, int x, int y) => new()
    {
        Location = new Point(x, y),
        AutoSize = true,
        Text = text,
        ForeColor = Theme.InkMuted,
        BackColor = Color.Transparent
    };

    private Control BuildRows()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas,
            Padding = new Padding(Margin_, 8, Margin_, 8)
        };

        var head = new Panel { Dock = DockStyle.Top, Height = Theme.Base.Height + 8, BackColor = Theme.Canvas };
        head.Controls.Add(Caption(Lang.T("Код на новия отпадък", "Code of the new waste"), 34, 2));
        head.Controls.Add(Caption(Lang.T("Тона", "Tonnes"), 34 + CodeWidth + 12, 2));
        head.Controls.Add(Caption("%", 34 + CodeWidth + 12 + NumberWidth + 12, 2));

        _rows.Dock = DockStyle.Fill;
        _rows.BackColor = Theme.Canvas;
        _rows.AutoScroll = true;

        host.Controls.Add(_rows);
        host.Controls.Add(head);

        return host;
    }

    private const int CodeWidth = 420;
    private const int NumberWidth = 100;

    private Control BuildFoot()
    {
        var host = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Base.Height + 58,
            BackColor = Theme.Surface,
            Padding = new Padding(Margin_, 10, Margin_, 10)
        };

        _sum.Dock = DockStyle.Fill;
        _sum.Font = Theme.Bold;
        _sum.TextAlign = ContentAlignment.MiddleLeft;
        _sum.AutoEllipsis = true;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = Theme.Scaled(340),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Surface
        };

        var create = Theme.PrimaryButton(Lang.T("Създай редовете", "Create the rows"));
        create.Height = Theme.Base.Height + 16;
        create.FitWidth(16);
        create.Click += (_, _) => Accept();
        _create = create;

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Height = create.Height;
        cancel.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        cancel.FitWidth(10);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        var balance = Theme.GhostButton(Lang.T("Изравни", "Balance"), null);
        balance.Height = create.Height;
        balance.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        balance.FitWidth(8);
        balance.Click += (_, _) => Balance();

        new ToolTip().SetToolTip(balance, Lang.T(
            "Слага разликата в последния ред, за да стане сборът точно колкото е влязло.",
            "Puts the difference in the last row so the sum matches what came in."));

        buttons.Controls.Add(create);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(balance);

        host.Controls.Add(_sum);
        host.Controls.Add(buttons);

        AcceptButton = create;
        CancelButton = cancel;

        return host;
    }

    // ------------------------------------------------------------- редове

    /// <summary>Един дял: код, количество и процент, вързани един за друг.</summary>
    private sealed class Line
    {
        public required ComboBox Code { get; init; }
        public required TextBox Amount { get; init; }
        public required TextBox Percent { get; init; }
        public required Label Number { get; init; }
    }

    /// <summary>
    /// Догражда или маха редове, докато станат колкото трябва.
    ///
    /// Не ги прави наново. Всяко падащо меню носи осемстотин кода и при пълно
    /// пресъздаване „+“ се бавеше секунда при три реда и три секунди при осем
    /// — толкова, че натискането изглеждаше пропуснато и се натискаше пак.
    /// Тук се пипа само разликата, а вече попълненото си остава на място,
    /// защото никой не го е бутал.
    /// </summary>
    private void Rebuild(int wanted)
    {
        if (wanted == _lines.Count) { RefreshSum(); return; }

        _rows.SuspendLayout();

        while (_lines.Count > wanted) Drop();
        while (_lines.Count < wanted) Grow();

        _rows.ResumeLayout(true);

        RefreshSum();
    }

    /// <summary>Маха последния ред и освобождава контролите му.</summary>
    private void Drop()
    {
        var line = _lines[^1];
        _lines.RemoveAt(_lines.Count - 1);

        foreach (var control in (Control[])[line.Number, line.Code, line.Amount, line.Percent])
        {
            _rows.Controls.Remove(control);
            control.Dispose();
        }
    }

    /// <summary>Добавя един ред в края.</summary>
    private void Grow()
    {
        var index = _lines.Count;
        var height = Theme.Big.Height + 14;
        var top = 4 + index * (height + 6);

        var number = new Label
        {
            Location = new Point(0, top + 4),
            Size = new Size(Theme.Scaled(28), height),
            Text = (index + 1).ToString(),
            ForeColor = Theme.InkFaint,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent
        };

        var code = new ComboBox
        {
            Location = new Point(34, top),
            Size = new Size(CodeWidth, height),
            DropDownStyle = ComboBoxStyle.DropDown,
            DropDownWidth = CodeWidth + 120,
            MaxDropDownItems = 14,
            Font = Theme.Base
        };

        Codes.Fill(code, _offered);
        Theme.StyleInput(code);

        var amount = new TextBox
        {
            Location = new Point(code.Right + 12, top),
            Size = new Size(NumberWidth, height),
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Right,
            Font = Theme.Base
        };

        var percent = new TextBox
        {
            Location = new Point(amount.Right + 12, top),
            Size = new Size(NumberWidth, height),
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Right,
            Font = Theme.Base
        };

        Theme.StyleInput(amount);
        Theme.StyleInput(percent);

        var line = new Line { Code = code, Amount = amount, Percent = percent, Number = number };

        // Стойността влиза и с писане, и от шаблон, не само с избор от менюто.
        code.TextChanged += (_, _) => RefreshSum();
        amount.TextChanged += (_, _) => OnAmountTyped(line);
        percent.TextChanged += (_, _) => OnPercentTyped(line);

        code.KeyDown += OnRowKey;
        amount.KeyDown += OnRowKey;
        percent.KeyDown += OnRowKey;

        _lines.Add(line);
        _rows.Controls.Add(number);
        _rows.Controls.Add(code);
        _rows.Controls.Add(amount);
        _rows.Controls.Add(percent);
    }

    /// <summary>
    /// Стрелките нагоре и надолу местят между редовете, а не в самото поле.
    ///
    /// Данните се пишат отгоре надолу по една колона — код, код, код, после
    /// количествата — и посягането към мишката на всеки ред е повече работа
    /// от самото писане.
    ///
    /// В отвореното падащо меню стрелките си остават негови: там те избират.
    /// Затворено обаче менюто ги ползва, за да сменя кода незабелязано —
    /// едно случайно завъртане на колелцето и в реда стои друг отпадък. Тук
    /// това също отпада.
    /// </summary>
    private void OnRowKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Up or Keys.Down)) return;
        if (sender is ComboBox { DroppedDown: true }) return;

        var from = _lines.FindIndex(l =>
            ReferenceEquals(l.Code, sender) ||
            ReferenceEquals(l.Amount, sender) ||
            ReferenceEquals(l.Percent, sender));

        if (from < 0) return;

        var to = from + (e.KeyCode == Keys.Down ? 1 : -1);

        e.Handled = true;
        e.SuppressKeyPress = true;

        if (to < 0 || to >= _lines.Count) return;

        Control target =
            ReferenceEquals(_lines[from].Code, sender) ? _lines[to].Code :
            ReferenceEquals(_lines[from].Amount, sender) ? _lines[to].Amount :
            _lines[to].Percent;

        target.Focus();

        if (target is TextBox box) box.SelectAll();
        if (target is ComboBox combo) combo.SelectAll();
    }

    private void OnAmountTyped(Line line)
    {
        if (_syncing) return;

        _syncing = true;

        line.Percent.Text = SplitMath.TryRead(line.Amount.Text, out var value)
            ? SplitMath.Share(SplitMath.Percent(value, _total))
            : string.Empty;

        _syncing = false;
        RefreshSum();
    }

    private void OnPercentTyped(Line line)
    {
        if (_syncing) return;

        _syncing = true;

        line.Amount.Text = SplitMath.TryRead(line.Percent.Text, out var value)
            ? SplitMath.Amount(SplitMath.Round(_total * value / 100m))
            : string.Empty;

        _syncing = false;
        RefreshSum();
    }

    /// <summary>Слага разликата в последния ред.</summary>
    private void Balance()
    {
        if (_lines.Count == 0) return;

        decimal used = 0;

        for (var i = 0; i < _lines.Count - 1; i++)
            if (SplitMath.TryRead(_lines[i].Amount.Text, out var value)) used += value;

        var last = _lines[^1];
        var rest = SplitMath.Round(_total - used);

        _syncing = true;
        last.Amount.Text = SplitMath.Amount(rest);
        last.Percent.Text = SplitMath.Share(SplitMath.Percent(rest, _total));
        _syncing = false;

        RefreshSum();
    }

    private decimal Sum()
    {
        decimal sum = 0;

        foreach (var line in _lines)
            if (SplitMath.TryRead(line.Amount.Text, out var value)) sum += value;

        return sum;
    }

    /// <summary>
    /// Долният ред казва докъде е стигнало делението.
    ///
    /// Тук няма изскачащи прозорци: те спират работата по средата, за да
    /// съобщят нещо, което и без това се вижда. Каквото не е наред, пише на
    /// този ред, а копчето „Създай редовете“ стои изключено, докато е така.
    /// </summary>
    private void RefreshSum()
    {
        var sum = Sum();
        var left = SplitMath.Round(_total - sum);
        var blank = _lines.Count(l => Stored(l.Code.Text).Length == 0);

        var text = Lang.T(
            $"{SplitMath.Amount(sum)} от {SplitMath.Amount(_total)} т",
            $"{SplitMath.Amount(sum)} of {SplitMath.Amount(_total)} t");

        if (left != 0)
            text += Lang.T(
                $"  ·  остават {SplitMath.Amount(left)} т ({SplitMath.Share(SplitMath.Percent(left, _total))} %)",
                $"  ·  {SplitMath.Amount(left)} t left ({SplitMath.Share(SplitMath.Percent(left, _total))} %)");

        if (blank > 0)
            text += Lang.T(
                $"  ·  {blank} без код",
                $"  ·  {blank} without a code");

        var ready = left == 0 && blank == 0;

        _sum.ForeColor = ready ? Theme.Ok : Theme.InkMuted;
        _sum.Text = text;

        if (_create is not null) _create.Enabled = ready;
    }

    // ------------------------------------------------------------- шаблони

    private void FillTemplates()
    {
        _syncing = true;

        _templates.Items.Clear();
        _templates.Items.Add(Lang.T("(без шаблон)", "(no template)"));

        foreach (var template in _saved) _templates.Items.Add(template.Name);

        _templates.SelectedIndex = 0;
        _syncing = false;
    }

    private void ApplyTemplate()
    {
        if (_syncing) return;

        var index = _templates.SelectedIndex - 1;
        if (index < 0 || index >= _saved.Count) return;

        var template = _saved[index];

        _count.Value = Math.Clamp(template.Parts.Count, (int)_count.Minimum, (int)_count.Maximum);
        Rebuild((int)_count.Value);

        var shares = SplitMath.Shares(_total, [.. template.Parts.Select(p => p.Percent)]);

        _syncing = true;

        for (var i = 0; i < _lines.Count && i < template.Parts.Count; i++)
        {
            _lines[i].Code.Text = Display(template.Parts[i].Ewc);
            _lines[i].Amount.Text = SplitMath.Amount(shares[i]);
            _lines[i].Percent.Text = SplitMath.Share(SplitMath.Percent(shares[i], _total));
        }

        _syncing = false;
        RefreshSum();
    }

    private void SaveTemplate()
    {
        var parts = new List<SplitPart>();

        foreach (var line in _lines)
        {
            var code = Stored(line.Code.Text);
            if (code.Length == 0) continue;

            SplitMath.TryRead(line.Amount.Text, out var amount);
            parts.Add(new SplitPart(code, SplitMath.Percent(amount, _total)));
        }

        if (parts.Count == 0)
        {
            _sum.ForeColor = Theme.InkMuted;
            _sum.Text = Lang.T(
                "Шаблонът се записва, след като има избрани кодове.",
                "A template is saved once there are codes chosen.");
            return;
        }

        var name = NameDialog.Ask(this,
            Lang.T("Име на шаблона", "Template name"),
            _templates.SelectedIndex > 0 ? _templates.Text : string.Empty);

        if (string.IsNullOrWhiteSpace(name)) return;

        _saved = SplitTemplates.Put(_settings, _node, new SplitTemplate(name.Trim(), parts));

        FillTemplates();
        _templates.SelectedIndex = _saved.FindIndex(t => t.Name == name.Trim()) + 1;
    }

    private void DeleteTemplate()
    {
        var index = _templates.SelectedIndex - 1;
        if (index < 0 || index >= _saved.Count) return;

        var name = _saved[index].Name;

        var answer = MessageBox.Show(this,
            Lang.T($"Да се изтрие ли шаблонът „{name}“?", $"Delete the template “{name}”?"),
            Lang.T("Изтриване", "Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes) return;

        _saved = SplitTemplates.Remove(_settings, _node, name);
        FillTemplates();
    }

    // ------------------------------------------------------------- готово

    private string Display(string stored) =>
        _code.Domain.Options
            .FirstOrDefault(o => string.Equals(o.Value, stored, StringComparison.OrdinalIgnoreCase))?
            .Display ?? stored;

    private string Stored(string shown)
    {
        var text = shown.Trim();
        if (text.Length == 0) return string.Empty;

        var dash = text.IndexOf(" — ", StringComparison.Ordinal);
        var code = dash > 0 ? text[..dash] : text;

        return _code.Domain.Options.Any(o => string.Equals(o.Value, code, StringComparison.OrdinalIgnoreCase))
            ? code
            : string.Empty;
    }

    private void Accept()
    {
        var rows = new List<Dictionary<string, string>>();
        var missing = 0;

        foreach (var line in _lines)
        {
            var code = Stored(line.Code.Text);
            var hasAmount = SplitMath.TryRead(line.Amount.Text, out var amount);

            if (code.Length == 0 || !hasAmount || amount <= 0) { missing++; continue; }

            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [_code.Name] = code,
                [_amount.Name] = SplitMath.Amount(amount),
                [_when.Name] = _date.Value.ToString(
                    ValueConverter.BulgarianDateFormat, CultureInfo.InvariantCulture)
            });
        }

        // Копчето е изключено, докато нещо не е наред, така че дотук се стига
        // само с готово деление. Проверката остава като предпазител, но мълчи:
        // тя няма какво да съобщи, което долният ред вече да не казва.
        if (missing > 0 || Sum() != _total) { RefreshSum(); return; }

        Rows = rows;
        DialogResult = DialogResult.OK;
    }
}

using System.Globalization;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;

namespace RiosvImports.App.Editor;

/// <summary>
/// Полетата, които човек попълва за прехвърлянето — веднъж в страничния
/// панел и после при нужда в прозорчето на всеки ред.
///
/// Едно и също устройство и на двете места: иначе списъкът с дейности в
/// панела и списъкът в прозорчето рано или късно се разминават и човек
/// избира различно нещо на едно и също име.
/// </summary>
internal sealed class TransferFields : Panel
{
    private readonly Dictionary<string, Control> _editors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ColumnDef> _columns = new(StringComparer.Ordinal);

    /// <summary>Надписите под всяко изброено поле — показват номенклатурата.</summary>
    private readonly Dictionary<string, Label> _hints = new(StringComparer.Ordinal);

    /// <summary>
    /// Полетата, чиято стойност идва отвън — от изходния ред, не оттук.
    ///
    /// Заключеното поле не е просто изключено: то не дава стойност и не се
    /// иска при проверката за липсващо. Иначе панелът би се оплаквал от
    /// празнота, която сам е наложил.
    /// </summary>
    private readonly HashSet<string> _locked = new(StringComparer.Ordinal);

    private readonly TransferRule _rule;
    private readonly ReportNode _target;

    /// <summary>Кратките списъци на фирмата — стесняват само предлаганото.</summary>
    private readonly ValueLimits _limits;

    public TransferFields(TransferRule rule, ReportNode target, int width, ValueLimits? limits = null)
    {
        _rule = rule;
        _target = target;
        _limits = limits ?? ValueLimits.None;

        Width = width;
        Height = 0;
        BackColor = Theme.Surface;

        var top = 0;

        foreach (var name in rule.Defaults)
        {
            var column = target.Attributes.FirstOrDefault(a => a.Name == name);
            if (column is null) continue;

            _columns[name] = column;

            var box = BuildField(column, width);
            box.Location = new Point(0, top);
            top += box.Height + Gap;

            Controls.Add(box);
        }

        Height = Math.Max(0, top - Gap);
    }

    /// <summary>
    /// Разстоянието между полетата. По-малко от обичайното: панелът е висок
    /// колкото половин екран, а всяко спестено отстояние е ред от
    /// номенклатурата, който се вижда без превъртане.
    /// </summary>
    private const int Gap = 4;

    /// <summary>Вдига се при всяка промяна — панелът пази стойностите.</summary>
    public event EventHandler? Edited;

    // ------------------------------------------------------------- полета

    private Panel BuildField(ColumnDef column, int width)
    {
        var line = Theme.Base.Height;

        var box = new Panel { Width = width, BackColor = Theme.Surface };

        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = line + 3,
            Text = _rule.Caption(column) + (column.Required ? " *" : string.Empty),
            ForeColor = Theme.InkMuted,
            AutoEllipsis = true
        };

        var input = BuildInput(column, width);
        input.Dock = DockStyle.Top;
        _editors[column.Name] = input;

        var tip = new ToolTip();
        tip.SetToolTip(input, ReportTitles.Column(column));

        // Номенклатурата стои под полето, а не като второ поле за попълване:
        // тя не е избор, а името на избраното. Кодът се вижда в самото поле,
        // но „161105“ не значи нищо, докато не се прочете какво е.
        Label? hint = null;

        if (column.Domain.IsEnum)
        {
            hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = line * 2 + 2,
                Font = Theme.Small,
                ForeColor = Theme.InkFaint,
                BackColor = Color.Transparent
            };

            _hints[column.Name] = hint;
        }

        box.Controls.Add(input);
        if (hint is not null) box.Controls.Add(hint);
        box.Controls.Add(label);

        box.Height = label.Height + input.Height + (hint?.Height ?? 0);

        return box;
    }

    private Control BuildInput(ColumnDef column, int width)
    {
        Control input;

        if (column.Domain.Kind == ValueKind.Date)
        {
            var picker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = ValueConverter.BulgarianDateFormat
            };

            picker.ValueChanged += (_, _) => Changed();
            input = picker;
        }
        else if (column.Domain.IsEnum)
        {
            // Всички допустими кодове стоят в списъка, включително дългите:
            // тук се избира какъв отпадък излиза след третирането, а това е
            // избор от номенклатурата, не свободен текст.
            //
            // При над четиридесет стойности списъкът се и пише: превъртането
            // на осемстотин реда е по-бавно от набирането на „1610“, а
            // допълването по въведеното свежда списъка до няколко реда.
            var options = _limits.Offer(_target.Root.ElementName, _target.ElementName, column);
            var long_ = options.Count > 40;

            var combo = new ComboBox
            {
                DropDownStyle = long_ ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
                MaxDropDownItems = 14,

                // Затвореният списък е широк колкото панела, отвореният —
                // колкото името на отпадъка. Иначе се избира по номер наслуки.
                DropDownWidth = Math.Max(width, 520)
            };

            combo.SelectedIndexChanged += (_, _) => Changed();

            if (!long_) combo.Items.AddRange(Codes.Items(options));

            if (long_)
            {
                Codes.Fill(combo, options);
                combo.TextChanged += (_, _) => Changed();

                // Недописаното не се пази: половин код изглежда като избор, а
                // не е и се хваща чак при проверката.
                combo.Leave += (_, _) => Snap(combo, column);
            }

            input = combo;
        }
        else
        {
            var text = new TextBox { BorderStyle = BorderStyle.FixedSingle };
            text.TextChanged += (_, _) => Changed();
            input = text;
        }

        input.Font = Theme.Big;
        input.Tag = column;
        input.Width = width;
        Theme.StyleInput(input);

        return input;
    }

    /// <summary>Свежда набраното до стойност от списъка или го изчиства.</summary>
    private static void Snap(ComboBox combo, ColumnDef column)
    {
        var text = combo.Text.Trim();
        if (text.Length == 0) return;

        var match = column.Domain.Options.FirstOrDefault(o =>
            string.Equals(o.Display, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.Value, text, StringComparison.OrdinalIgnoreCase));

        combo.Text = match?.Display ?? string.Empty;
    }

    // ------------------------------------------------------------- заключване

    /// <summary>
    /// Заключва поле: стойността му се взема от изходния ред.
    ///
    /// В полето остава какво ще се случи — в панела текстът е общ („от
    /// реда“), в прозорчето е самият код на реда. И двете са по-полезни от
    /// празно сиво поле.
    /// </summary>
    public void Lock(string name, string shown)
    {
        if (!_editors.TryGetValue(name, out var input)) return;

        _locked.Add(name);

        input.Enabled = false;
        input.Text = shown;

        RefreshHints();
    }

    /// <summary>Връща полето за попълване и изчиства наложеното.</summary>
    public void Unlock(string name)
    {
        if (!_locked.Remove(name)) return;
        if (!_editors.TryGetValue(name, out var input)) return;

        input.Enabled = true;
        input.Text = string.Empty;

        RefreshHints();
    }

    /// <summary>Дали полето изобщо е в панела — заключването пита преди да пише.</summary>
    public bool Has(string name) => _editors.ContainsKey(name);

    private void Changed()
    {
        RefreshHints();
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Показва номенклатурата на избраното под самото поле.</summary>
    private void RefreshHints()
    {
        foreach (var (name, label) in _hints) label.Text = Description(name) ?? string.Empty;
    }

    // ------------------------------------------------------------- стойности

    /// <summary>Записваната стойност на едно поле — кодът, не описанието.</summary>
    public string Value(string name)
    {
        // Заключеното поле не дава стойност: в него стои какво ще се случи, а
        // не какво да се запише. Пренасянето взема истинската стойност от реда.
        if (_locked.Contains(name)) return string.Empty;

        if (!_editors.TryGetValue(name, out var input)) return string.Empty;
        if (!_columns.TryGetValue(name, out var column)) return string.Empty;

        if (input is DateTimePicker picker)
            return picker.Value.ToString(ValueConverter.BulgarianDateFormat, CultureInfo.InvariantCulture);

        var text = input.Text.Trim();
        if (text.Length == 0) return string.Empty;

        // „R04 — Рециклиране…“ се записва като „R04“.
        var dash = text.IndexOf(" — ", StringComparison.Ordinal);
        return column.Domain.IsEnum && dash > 0 ? text[..dash] : text;
    }

    /// <summary>Описанието зад избраната стойност, ако полето е изброено.</summary>
    public string? Description(string name)
    {
        if (!_columns.TryGetValue(name, out var column) || !column.Domain.IsEnum) return null;

        var code = Value(name);
        if (code.Length == 0) return null;

        return column.Domain.Options
            .FirstOrDefault(o => string.Equals(o.Value, code, StringComparison.OrdinalIgnoreCase))?
            .Description;
    }

    /// <summary>Всички попълнени стойности, включително изведените описания.</summary>
    public Dictionary<string, string> Values()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in _columns.Keys)
            if (Value(name) is { Length: > 0 } value)
                values[name] = value;

        foreach (var (from, to) in _rule.Derived)
            if (Description(from) is { Length: > 0 } text)
                values[to] = text;

        return values;
    }

    /// <summary>Кои задължителни полета още не са попълнени.</summary>
    public IReadOnlyList<string> Missing() =>
    [
        .. _columns.Values
            .Where(c => !_locked.Contains(c.Name) && Needed(c) && Value(c.Name).Length == 0)
            .Select(_rule.Caption)
    ];

    /// <summary>
    /// Дали полето трябва да е попълнено.
    ///
    /// Освен изрично задължителните такова е и всяко, от което се извежда
    /// задължително: „Код на отпадъка“ в „Образуван“ е незадължителен по
    /// схема, но описанието, което се пише от него, е задължително. Ред без
    /// код минава за създаден и пада чак при проверката.
    /// </summary>
    private bool Needed(ColumnDef column) =>
        column.Required ||
        _rule.Derived.Any(d =>
            d.From == column.Name &&
            _target.Attributes.FirstOrDefault(a => a.Name == d.To)?.Required == true);

    /// <summary>Слага стойности отвън — прозорчето тръгва от тези в панела.</summary>
    public void SetValues(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (name, value) in values)
        {
            // Заключеното поле показва откъде идва стойността му; запомненото
            // отпреди заключването не бива да го замества.
            if (_locked.Contains(name)) continue;

            if (!_editors.TryGetValue(name, out var input)) continue;
            if (!_columns.TryGetValue(name, out var column)) continue;
            if (value.Length == 0) continue;

            switch (input)
            {
                case DateTimePicker picker
                    when ValueConverter.TryNormalizeDate(value, out var text) &&
                         DateTime.TryParseExact(text, ValueConverter.BulgarianDateFormat,
                             CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                    picker.Value = date;
                    break;

                case ComboBox combo:
                    combo.SelectedItem = Display(column, value);

                    // Списъкът с осемстотин реда приема и текст: ако кодът не
                    // съвпадне с ред от него, поне се вижда какво стои.
                    if (combo.SelectedItem is null) combo.Text = Display(column, value);
                    break;

                default:
                    input.Text = Display(column, value);
                    break;
            }
        }

        RefreshHints();
    }

    private static string Display(ColumnDef column, string stored)
    {
        if (!column.Domain.IsEnum) return stored;

        return column.Domain.Options
            .FirstOrDefault(o => string.Equals(o.Value, stored, StringComparison.OrdinalIgnoreCase))?
            .Display ?? stored;
    }
}

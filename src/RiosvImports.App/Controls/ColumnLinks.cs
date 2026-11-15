using System.Globalization;
using RiosvImports.Core.Localization;

namespace RiosvImports.App.Controls;

/// <summary>
/// Свързани колони: разшириш ли една, свързаните я следват.
///
/// Балансът има дванайсет месеца и човек иска всичките по-широки — или
/// по-тесни, за да се видят на лаптоп. Колона по колона това са дванайсет
/// влачения. Тук се свързват веднъж: Ctrl+щракване по заглавие добавя или маха
/// колоната, Shift+щракване взима поредицата до последно щракнатата, а
/// обикновено щракване развързва. Влачиш ли ръба на коя да е свързана колона,
/// останалите застават на същата ширина.
///
/// <b>Ширините се помнят.</b> Таблицата на баланса се сглобява наново при всяка
/// смяна на изгледа, годината или ситото — без памет всяка нагласена ширина би
/// изчезнала при първото щракване. Затова и ширините, и връзките се пазят по
/// ИМЕ на колоната и се връщат след всяко сглобяване (<see cref="Apply"/>), а
/// между пусканията — в настройките.
///
/// Колоната, която пълни остатъка от ширината, не се свързва и не се помни:
/// нейната ширина е следствие от прозореца, не решение на човека.
/// </summary>
internal sealed class ColumnLinks
{
    private readonly DataGridView _grid;
    private readonly Func<string> _load;
    private readonly Action<string> _save;

    private readonly Dictionary<string, int> _widths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _linked = new(StringComparer.Ordinal);

    /// <summary>Докато колоните се сглобяват или се нареждат оттук — не се слуша.</summary>
    private bool _quiet;

    /// <summary>Ширина, сменена от влачене между натискането и пускането на бутона.</summary>
    private bool _dragged;

    /// <summary>От коя колона тръгва Shift+щракване.</summary>
    private string? _anchor;

    public ColumnLinks(DataGridView grid, Func<string> load, Action<string> save)
    {
        _grid = grid;
        _load = load;
        _save = save;

        Read(_load());

        _grid.ShowCellToolTips = true;
        _grid.ColumnWidthChanged += (_, e) => Changed(e.Column);
        _grid.MouseDown += (_, _) => _dragged = false;
        _grid.ColumnHeaderMouseClick += (_, e) => Clicked(e);
    }

    /// <summary>Свързаните колони — по име.</summary>
    public IReadOnlyCollection<string> Linked => _linked;

    /// <summary>Колоните ще се сглобяват наново — ширините им още не са решение на човека.</summary>
    public void Begin() => _quiet = true;

    /// <summary>
    /// Връща помнените ширини и знаците на свързаните — след всяко сглобяване.
    /// </summary>
    public void Apply()
    {
        _quiet = true;

        try
        {
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                if (!Adjustable(column)) continue;

                if (_widths.TryGetValue(column.Name, out var width))
                    column.Width = Math.Max(column.MinimumWidth, width);

                column.HeaderCell.ToolTipText = Lang.T(
                    "Ctrl+щракване свързва колоната, Shift+щракване — поредица, щракване развързва. " +
                    "Разшириш ли свързана колона, другите я следват.",
                    "Ctrl+click links the column, Shift+click a range, a plain click unlinks. " +
                    "Widen a linked column and the others follow.");
            }
        }
        finally
        {
            _quiet = false;
        }

        Paint();
    }

    /// <summary>Свързва точно тези колони — ползва се и от тестовете.</summary>
    public void Link(IEnumerable<string> names)
    {
        _linked.Clear();

        foreach (var name in names)
            if (_grid.Columns.Contains(name) && Adjustable(_grid.Columns[name]))
                _linked.Add(name);

        Paint();
        Save();
    }

    /// <summary>Развързва всичко. Ширините остават.</summary>
    public void Unlink()
    {
        if (_linked.Count == 0) return;

        _linked.Clear();
        Paint();
        Save();
    }

    // ------------------------------------------------------------- събития

    private void Clicked(DataGridViewCellMouseEventArgs e)
    {
        // Влачене по ръба също завършва с щракване по заглавието. То не е
        // избор, а разширяване — и не бива да развързва току-що нагласеното.
        if (_dragged)
        {
            _dragged = false;
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count) return;

        var column = _grid.Columns[e.ColumnIndex];
        if (!Adjustable(column)) return;

        var keys = Control.ModifierKeys;

        if (keys.HasFlag(Keys.Shift) && _anchor is not null && _grid.Columns.Contains(_anchor))
        {
            var from = Math.Min(_grid.Columns[_anchor].DisplayIndex, column.DisplayIndex);
            var to = Math.Max(_grid.Columns[_anchor].DisplayIndex, column.DisplayIndex);

            foreach (DataGridViewColumn each in _grid.Columns)
                if (each.DisplayIndex >= from && each.DisplayIndex <= to && Adjustable(each))
                    _linked.Add(each.Name);
        }
        else if (keys.HasFlag(Keys.Control))
        {
            if (!_linked.Remove(column.Name)) _linked.Add(column.Name);

            _anchor = column.Name;
        }
        else
        {
            _linked.Clear();
            _anchor = column.Name;
        }

        Paint();
        Save();
    }

    /// <summary>Една ширина е сменена — помни се и се разнася по свързаните.</summary>
    private void Changed(DataGridViewColumn column)
    {
        if (_quiet || !Adjustable(column)) return;

        _dragged = true;
        _widths[column.Name] = column.Width;

        if (_linked.Contains(column.Name)) Follow(column.Name, column.Width);

        Save();
    }

    /// <summary>Нарежда свързаните колони на една ширина.</summary>
    private void Follow(string leader, int width)
    {
        _quiet = true;

        try
        {
            foreach (var name in _linked)
            {
                if (name == leader || !_grid.Columns.Contains(name)) continue;

                var column = _grid.Columns[name];
                column.Width = Math.Max(column.MinimumWidth, width);
                _widths[name] = column.Width;
            }
        }
        finally
        {
            _quiet = false;
        }
    }

    /// <summary>Свързаните се виждат: заглавието им е в цвета на избраното.</summary>
    private void Paint()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            var linked = _linked.Contains(column.Name);

            column.HeaderCell.Style.BackColor = linked ? Theme.AccentLight : Color.Empty;
            column.HeaderCell.Style.ForeColor = linked ? Theme.Accent : Color.Empty;
        }
    }

    private static bool Adjustable(DataGridViewColumn column) =>
        column.AutoSizeMode != DataGridViewAutoSizeColumnMode.Fill &&
        column.Resizable != DataGridViewTriState.False;

    // ------------------------------------------------------------- памет

    /// <summary>„m1=80;m2=80|m1,m2“ — ширините, после свързаните.</summary>
    private void Save()
    {
        var widths = string.Join(";", _widths.Select(w => $"{w.Key}={w.Value.ToString(CultureInfo.InvariantCulture)}"));
        _save(widths + "|" + string.Join(",", _linked));
    }

    private void Read(string text)
    {
        var parts = (text ?? string.Empty).Split('|');

        foreach (var pair in parts[0].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = pair.Split('=');

            if (bits.Length == 2 && int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
                width is > 10 and < 2000)
                _widths[bits[0]] = width;
        }

        if (parts.Length > 1)
            foreach (var name in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                _linked.Add(name);
    }

    // ------------------------------------------------------------- за теста

    /// <summary>Ширината на колона по име — 0, ако я няма.</summary>
    internal int WidthOf(string name) => _grid.Columns.Contains(name) ? _grid.Columns[name].Width : 0;
}

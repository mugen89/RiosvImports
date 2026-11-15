using RiosvImports.App.Editor;
using RiosvImports.Core.Localization;

namespace RiosvImports.App.Controls;

/// <summary>
/// Пресяване по колона за таблиците в работните прозорчета.
///
/// Същото като в редактора на отчета: десен бутон по заглавието отваря
/// списъка със стойностите, които наистина стоят в колоната, и се отмятат
/// тези, които да се виждат. Там то живее в <c>SheetGrid</c> и е вплетено в
/// неговите редове; тук таблиците се пълнят от прозорчетата и се пресяват след
/// всяко пълнене — затова пресяването е отделно и се вика с <see cref="Apply"/>.
///
/// <b>Скритият ред си остава ред.</b> Пресяването пипа само какво се вижда,
/// не какво се взима: скрита партида, от която вече се взима, продължава да се
/// взима. Прозорчетата затова прескачат скритите редове, когато превръщат
/// избора в количества — иначе щракване по видимото би изваждало невидимото.
/// </summary>
internal sealed class GridFilter
{
    private readonly DataGridView _grid;
    private readonly Dictionary<string, HashSet<string>> _filters = new(StringComparer.Ordinal);

    public GridFilter(DataGridView grid)
    {
        _grid = grid;
    }

    /// <summary>Пресяването е сменено от човека — прозорчето опреснява своето.</summary>
    public event Action? Changed;

    /// <summary>Дали нещо е пресято.</summary>
    public bool Active => _filters.Count > 0 || Outside.Length > 0;

    /// <summary>
    /// Още едно условие отвън — търсенето над таблицата.
    ///
    /// Минава през същото <see cref="Apply"/>, а не скрива редове само̀: две
    /// неща, които пипат видимостта поотделно, се разминават при първото
    /// пълнене — едното показва това, което другото е скрило.
    /// </summary>
    public Func<DataGridViewRow, bool>? Also { get; set; }

    /// <summary>
    /// Какво казва условието отвън за себе си — „търсене „метал““ — или празно,
    /// когато то не отсява нищо. По него се знае дали има скрито.
    /// </summary>
    public Func<string>? AlsoNote { get; set; }

    private string Outside => AlsoNote?.Invoke() ?? string.Empty;

    /// <summary>Дали редът минава през всички пресявания.</summary>
    public bool Allows(DataGridViewRow row) =>
        _filters.All(f => f.Value.Contains(Text(row, f.Key))) && (Also?.Invoke(row) ?? true);

    /// <summary>Отваря списъка със стойностите на една колона.</summary>
    public void Open(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _grid.Columns.Count) return;

        var column = _grid.Columns[columnIndex];

        var values = _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => Text(r, column.Name))
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(v => v, StringComparer.CurrentCulture)
            .ToList();

        // Стойност, скрита от друго пресяване, пак трябва да може да се върне.
        if (_filters.TryGetValue(column.Name, out var allowed))
            values = [.. values.Union(allowed).OrderBy(v => v, StringComparer.CurrentCulture)];

        if (values.Count == 0) return;

        using var dialog = new ColumnFilter(Header(column), values, allowed);

        if (dialog.ShowDialog(_grid.FindForm()) != DialogResult.OK) return;

        Set(column.Name, dialog.ShowsEverything ? null : dialog.Chosen);
    }

    /// <summary>Слага или маха пресяване по колона. <c>null</c> го маха.</summary>
    public void Set(string column, IEnumerable<string>? allowed)
    {
        if (allowed is null) _filters.Remove(column);
        else _filters[column] = [.. allowed];

        Apply();
        Changed?.Invoke();
    }

    /// <summary>Маха всичко пресято.</summary>
    public void Clear()
    {
        if (_filters.Count == 0) return;

        _filters.Clear();

        Apply();
        Changed?.Invoke();
    }

    /// <summary>
    /// Скрива редовете, които не минават. Вика се и след всяко пълнене на
    /// таблицата: редовете са нови, а пресяването е същото.
    /// </summary>
    public void Apply()
    {
        var shown = _grid.Rows.Cast<DataGridViewRow>()
            .Select(r => r.IsNewRow || Allows(r))
            .ToList();

        // Първо се показва това, което трябва да се види: курсорът може да
        // отиде само на видим ред, а първият оставащ може още да е скрит от
        // предишното пресяване — тогава Windows Forms хвърля.
        for (var i = 0; i < shown.Count; i++)
            if (shown[i] && !_grid.Rows[i].Visible)
                _grid.Rows[i].Visible = true;

        // Ред с текущата клетка не се скрива — Windows Forms хвърля. Курсорът
        // се мести на първия оставащ ред, а изборът се пази: по курсора висят
        // партидите вдясно, а изборът е какво се взима.
        if (_grid.CurrentCell is { RowIndex: >= 0 } current && !shown[current.RowIndex])
        {
            var chosen = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).ToList();
            var first = shown.IndexOf(true);
            var column = _grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);

            _grid.CurrentCell = first >= 0 && column is not null
                ? _grid.Rows[first].Cells[column.Index]
                : null;

            _grid.ClearSelection();

            foreach (var index in chosen) _grid.Rows[index].Selected = true;
        }

        for (var i = 0; i < shown.Count; i++)
            if (_grid.Rows[i].Visible != shown[i])
                _grid.Rows[i].Visible = shown[i];

        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderCell.Style.ForeColor =
                _filters.ContainsKey(column.Name) ? Theme.Accent : Theme.InkStrong;
    }

    /// <summary>Колко реда се виждат — за реда под таблицата.</summary>
    public int Visible => _grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible && !r.IsNewRow);

    /// <summary>„Показани 3 от 20 · пресято по Клиент“, или празно без пресяване.</summary>
    public string Note()
    {
        if (!Active) return string.Empty;

        var by = string.Join(", ", _filters.Keys
            .Where(_grid.Columns.Contains)
            .Select(k => Header(_grid.Columns[k])));

        var parts = new List<string> { Lang.T($"Показани {Visible} от {_grid.Rows.Count}",
                                              $"{Visible} of {_grid.Rows.Count} shown") };

        if (by.Length > 0) parts.Add(Lang.T($"пресято по {by}", $"filtered by {by}"));
        if (Outside.Length > 0) parts.Add(Outside);

        return string.Join(" · ", parts);
    }

    private static string Text(DataGridViewRow row, string column) =>
        row.Cells[column].Value?.ToString() ?? string.Empty;

    private static string Header(DataGridViewColumn column) =>
        column.HeaderText.Replace(Environment.NewLine, " ").Replace("\n", " ").Trim();
}

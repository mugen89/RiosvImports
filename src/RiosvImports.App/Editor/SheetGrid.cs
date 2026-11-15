using System.Text;
using RiosvImports.Core.Excel;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;

namespace RiosvImports.App.Editor;

/// <summary>
/// Таблицата, в която се попълва един възел от отчета.
///
/// Целта е две различни ръце да работят удобно на едно и също място:
/// човек, който не борави свободно с компютър, вижда български надписи,
/// подсказки под ръка и веднага оцветена грешка; човек, който върши това
/// всеки месец, не пипа мишката изобщо.
///
/// Затова всичко, което Excel прави с клавиатурата, работи и тук: Tab и Enter
/// се движат предвидимо, поставянето от Excel пълни цяла област наведнъж,
/// Ctrl+D повтаря стойността отгоре.
/// </summary>
internal sealed class SheetGrid : UserControl
{
    /// <summary>Над този брой стойности падащото меню става неудобно и се сменя с търсачка.</summary>
    private const int PickerThreshold = 40;

    /// <summary>
    /// Служебната колона с поредния номер. Не е поле от схемата и не влиза в
    /// XML-а — затова е с име, което схемата не може да заеме.
    /// </summary>
    private const string IndexColumn = "__no";

    private readonly DataGridView _grid = new();
    private readonly Label _hint = new();

    /// <summary>Копчето „Само отбелязаните“. Показва се само когато има какво.</summary>
    private Controls.AppButton? _blockedFilter;

    private readonly NumericUpDown _rowCount = new();
    private readonly SheetPlan _plan;
    private readonly IReadOnlyList<LocalColumn> _extras;

    /// <summary>
    /// Кратките списъци на фирмата. Стесняват какво се ПРЕДЛАГА в менютата и
    /// нищо друго: разчитането на поставена клетка и проверката преди подаване
    /// продължават да питат схемата.
    /// </summary>
    private ValueLimits _limits;

    /// <summary>Ред „нов запис“ не се брои за данни, докато не се пипне.</summary>
    private bool _dirty;

    /// <summary>
    /// Да пита ли, преди да изтрие редове с данни. Идва от настройките;
    /// по подразбиране пита, за да не мълчи, ако някой забрави да я подаде.
    ///
    /// Поле, а не свойство: проверката на Windows Forms иска всяко свойство за
    /// запис в наследник на Control да казва как се сериализира в дизайнера, а
    /// тази контрола не минава през дизайнер.
    /// </summary>
    internal bool ConfirmRowDeletes = true;

    public SheetGrid(
        SheetPlan plan,
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<LocalColumn>? extras = null,
        ValueLimits? limits = null)
    {
        _plan = plan;
        _extras = extras ?? [];
        _limits = limits ?? ValueLimits.None;

        Dock = DockStyle.Fill;
        BackColor = Theme.Canvas;

        BuildGrid();

        // Редовете влизат в сглобяването на колоните, а не само след него:
        // късото меню трябва да съдържа и стойностите, които вече стоят в
        // таблицата, иначе клетка с падащ списък показва празно всичко, което
        // не е в менюто ѝ — а това е чужда или по-стара, но свършена работа.
        BuildColumns(rows);

        // Заглавният ред се мери чак когато колоните и ширините им са налице.
        ApplyRowMetrics();

        LoadRows(rows);

        _hint.Dock = DockStyle.Bottom;
        // Подсказката е до два реда — мери се по шрифта, за да не се реже
        // вторият при по-едър мащаб.
        _hint.Height = Theme.Base.Height * 2 + 14;
        _hint.Padding = new Padding(10, 6, 10, 6);
        _hint.Font = Theme.Base;
        _hint.ForeColor = Theme.InkMuted;
        _hint.BackColor = Theme.Surface;
        _hint.Text = Lang.T("Изберете клетка, за да видите какво се попълва в нея.", "Select a cell to see what goes in it.");

        Controls.Add(_grid);
        Controls.Add(BuildTools());
        Controls.Add(_hint);
    }

    /// <summary>
    /// Лентата над таблицата: добавяне на много редове наведнъж и номериране.
    ///
    /// Отчет с осемдесет реда се попълва от Excel, а не ред по ред. Затова
    /// първото действие е „направи ми толкова празни реда“, а второто —
    /// „сложи поредните номера“, защото номерът на работния лист е един и същ
    /// вид работа във всичките четиридесет и два отчета.
    /// </summary>
    private Control BuildTools()
    {
        // Лентата се пренася на втори ред, вместо да реже.
        //
        // На широк екран всичко стои на един ред, както и досега. На по-тесен
        // — или при мащаб 125 % — последните две-три копчета излизаха извън
        // панела и просто ги нямаше: няма стрелка, няма подсказка, действието
        // изчезва. Втори ред отнема трийсетина пиксела и връща всичко обратно.
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Theme.Surface,
            Padding = new Padding(8, 6, 8, 6)
        };

        _rowCount.Minimum = 1;
        _rowCount.Maximum = 5000;
        _rowCount.Value = 10;
        _rowCount.Width = Theme.Scaled(68);
        _rowCount.Font = Theme.Base;
        _rowCount.TextAlign = HorizontalAlignment.Center;
        _rowCount.Margin = new Padding(0, 4, 6, 0);
        Theme.StyleInput(_rowCount);

        bar.Controls.Add(new Label
        {
            Text = Lang.T("Редове:", "Rows:"),
            Font = Theme.Base,
            ForeColor = Theme.InkMuted,
            Width = TextRenderer.MeasureText(Lang.T("Редове:", "Rows:"), Theme.Base).Width + 10,
            Height = Theme.Base.Height + 12,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 0, 0)
        });

        bar.Controls.Add(_rowCount);
        bar.Controls.Add(Tool(Lang.T("Задай общо", "Set total"), Theme.Glyph.Check, () => SetRowCount((int)_rowCount.Value),
            Lang.T("Прави броя редове точно толкова. Излишните ПРАЗНИ се махат; ред с данни не се пипа.", "Makes the row count exactly this. Spare EMPTY rows go; a row with data is left alone.")));
        bar.Controls.Add(Tool(Lang.T("Добави още", "Add more"), Theme.Glyph.Add, () => AddRows((int)_rowCount.Value),
            Lang.T("Добавя толкова празни реда в края.", "Adds that many empty rows at the end.")));
        bar.Controls.Add(Separator());
        bar.Controls.Add(Tool(Lang.T("Изтрий редовете", "Delete rows"), Theme.Glyph.Delete, DeleteSelectedRows,
            Lang.T("Ctrl+Delete — маха избраните редове изцяло, със стойностите в тях. " +
                   "Достатъчна е една избрана клетка от реда.",
                   "Ctrl+Delete — removes the selected rows entirely, values and all. " +
                   "One selected cell from the row is enough.")));
        bar.Controls.Add(Tool(Lang.T("Махни празните", "Remove empty"), Theme.Glyph.Delete, ClearEmptyRows,
            Lang.T("Премахва всички редове без нито една стойност, включително по средата.", "Removes every row with no value at all, including ones in the middle.")));
        bar.Controls.Add(Tool(Lang.T("Номерирай", "Number"), Theme.Glyph.Rename, NumberColumn,
            Lang.T("Ctrl+Shift+N — попълва избраната колона с 1, 2, 3…", "Ctrl+Shift+N — fills the selected column with 1, 2, 3…")));
        bar.Controls.Add(Tool(Lang.T("Повтори отгоре", "Repeat above"), Theme.Glyph.Copy, () => FillDown(false),
            Lang.T("Ctrl+D — слага стойността от реда отгоре.", "Ctrl+D — takes the value from the row above.")));
        bar.Controls.Add(Separator());
        bar.Controls.Add(Tool(Lang.T("Побери текста", "Fit the text"), null, () => FitColumnsToContent(),
            Lang.T("Разширява колоните, докато най-дългата стойност се вижда цяла.", "Widens the columns until the longest value shows in full.")));
        bar.Controls.Add(Tool(Lang.T("Пресей", "Filter"), Theme.Glyph.Filter,
            () => OpenFilter(_grid.CurrentCell?.ColumnIndex ?? -1),
            Lang.T("Показва само избрани стойности от колоната на избраната клетка. " +
                   "Същото прави и десен бутон върху заглавието на колоната.",
                   "Shows only chosen values from the column of the selected cell. " +
                   "A right-click on the column heading does the same.")));
        bar.Controls.Add(Tool(Lang.T("Махни пресяването", "Clear the filter"), null, ClearFilters,
            Lang.T("Връща всички редове на екрана.", "Brings every row back on screen.")));

        // Отбелязаните редове са тези, чиято стойност е извън краткия списък на
        // фирмата. Копчето стои тук, а не в съобщението: поправянето им е
        // работа на няколко минути и през това време екранът трябва да показва
        // само тях.
        _blockedFilter = Tool(Lang.T("Само отбелязаните", "Only the marked"), Theme.Glyph.Warning,
            ToggleOnlyBlocked,
            Lang.T("Показва само редовете със стойност извън краткия списък на фирмата.",
                   "Shows only the rows with a value outside the company's short list."));

        _blockedFilter.Visible = false;
        bar.Controls.Add(_blockedFilter);

        return bar;
    }

    /// <summary>
    /// Дорисува бледа отвесна черта по десния кант на всяка клетка.
    ///
    /// Мрежата на Windows Forms знае само „всички черти еднакви“. Тук
    /// водоравните трябва да са по-силни от отвесните: редът е това, което се
    /// чете, а колоната само се разграничава.
    /// </summary>
    private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        // Заглавията на редовете нямат нужда от черта отдясно — там вече има
        // кант към самата таблица.
        if (e.ColumnIndex < 0 || e.RowIndex < -1 || e.Graphics is null) return;

        e.Paint(e.ClipBounds, e.PaintParts);

        using var pen = new Pen(Theme.BorderSoft);
        e.Graphics.DrawLine(pen,
            e.CellBounds.Right - 1, e.CellBounds.Top,
            e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);

        e.Handled = true;
    }

    /// <summary>
    /// Разширява всяка колона колкото ѝ трябва, за да се вижда най-дългата
    /// стойност в нея.
    ///
    /// Има таван: една колона със свободен текст от петстотин знака иначе
    /// изтиква всички останали извън екрана и лекът става по-лош от болестта.
    /// След това таблицата се превърта настрани, което е приемливо — целта е
    /// да се прочете, а не всичко да се побере наведнъж.
    /// </summary>
    internal void FitColumnsToContent(int maximum = 420)
    {
        _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);

        foreach (DataGridViewColumn column in _grid.Columns)
            if (column.Width > maximum) column.Width = maximum;

        _hint.Text = Lang.T(
            "Колоните са разширени по съдържанието. " +
            "Ръбът между заглавията ги преоразмерява на ръка.",
            "The columns are widened to fit their contents. " +
            "Drag the edge between headings to size them by hand.");
    }

    private static Control Separator() => new Panel
    {
        Width = 1,
        Height = Theme.Base.Height + 4,
        Margin = new Padding(Theme.GapSmall, 8, 2, 0),
        BackColor = Theme.Border
    };

    private static Controls.AppButton Tool(string text, string? glyph, Action handler, string tip)
    {
        var button = Theme.GhostButton(text, glyph);
        button.Height = Math.Max(32, Theme.Base.Height + 14);
        button.Margin = new Padding(6, 3, 0, 3);
        button.Click += (_, _) => handler();
        button.FitWidth();

        new ToolTip().SetToolTip(button, tip);

        return button;
    }

    /// <summary>Добавя празни редове в края и застава на първия от тях.</summary>
    internal void AddRows(int count)
    {
        // Нов ред е празен и почти винаги пада извън пресяването. По-добре
        // филтърът да си отиде с обяснение, отколкото копчето да изглежда
        // счупено, защото добавените редове не се появяват.
        var wasFiltered = HasFilters;
        _filters.Clear();

        // Празният ред не е отбелязан и „само отбелязаните“ би го скрило
        // веднага след добавянето.
        if (_onlyBlocked) ToggleOnlyBlocked();

        var first = _grid.Rows.Count - 1;

        for (var i = 0; i < count; i++) _grid.Rows.Insert(Math.Max(0, _grid.Rows.Count - 1), 1);

        if (wasFiltered) ApplyFilters();

        RenumberIndex();
        RenumberRowHeaders();
        MarkDirty();

        if (first >= 0 && first < _grid.Rows.Count)
            _grid.CurrentCell = _grid.Rows[first].Cells[Math.Max(0, _grid.CurrentCell?.ColumnIndex ?? 0)];
    }

    /// <summary>
    /// Определението на колона по номер, или null за служебната колона с
    /// поредния номер. Всяко място, което пипа клетки по номер, минава оттук.
    /// </summary>
    private ColumnDef? ColumnAt(int index) =>
        index >= 0 && index < _grid.Columns.Count
            ? _grid.Columns[index].Tag as ColumnDef
            : null;

    /// <summary>
    /// Служебна колона, в която човек пише сам (<see cref="LocalColumn.Editable"/>).
    ///
    /// Жестовете за клетки питат тук, защото иначе минават само през колоните на
    /// схемата: залепеното от Excel щеше мълчаливо да прескача точно нея.
    /// </summary>
    private bool Writable(int index) =>
        index >= 0 && index < _grid.Columns.Count &&
        _extras.Any(e => e.Editable && e.Name == _grid.Columns[index].Name);

    /// <summary>Скритата колона с групата, към която редът принадлежи.</summary>
    internal const string LinkColumn = "__link";

    /// <summary>Скритата колона с групите, които редът е породил.</summary>
    internal const string SentColumn = "__sent";

    /// <summary>
    /// Колони, които не се пипат в свързан ред.
    ///
    /// Петдесет тона, разпаднали се на 35, 12,5 и 2,5, спират да са петдесет
    /// в мига, в който някой поправи една от трите бройки. Отчетът остава
    /// правдоподобен и минава проверката — просто вече не отговаря на нищо.
    /// Затова количеството е заключено от двете страни на връзката.
    /// </summary>
    internal IReadOnlyList<string> LockedColumns = [];

    /// <summary>
    /// Пита, преди редове да бъдат изтрити. Връща false, за да спре
    /// изтриването — повикващият вече се е разпоредил сам.
    /// </summary>
    internal Func<IReadOnlyList<int>, bool>? BeforeDelete;

    /// <summary>Групата, към която принадлежи редът, или празно.</summary>
    internal string LinkOf(int rowIndex) => LocalValue(rowIndex, LinkColumn);

    /// <summary>Групите, породени от този ред.</summary>
    internal IReadOnlyList<string> SentBy(int rowIndex) =>
        LocalValue(rowIndex, SentColumn).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Редовете от една група, в реда, в който стоят.</summary>
    internal IReadOnlyList<int> RowsOf(string link)
    {
        var found = new List<int>();
        if (link.Length == 0) return found;

        for (var i = 0; i < DataRowCount; i++)
            if (LinkOf(i) == link) found.Add(i);

        return found;
    }

    /// <summary>Редът, породил тази група, или -1.</summary>
    internal int RowThatSent(string link)
    {
        for (var i = 0; i < DataRowCount; i++)
            if (SentBy(i).Contains(link)) return i;

        return -1;
    }

    /// <summary>Слага реда в група и го оцветява.</summary>
    internal void LinkRow(int rowIndex, string link)
    {
        SetLocalValue(rowIndex, LinkColumn, link);
        Tint(rowIndex);
    }

    /// <summary>Отбелязва, че редът е породил група.</summary>
    internal void SentRow(int rowIndex, string link)
    {
        var all = SentBy(rowIndex).Append(link).Distinct();
        SetLocalValue(rowIndex, SentColumn, string.Join(' ', all));
    }

    /// <summary>Маха реда от групата му и връща цвета.</summary>
    internal void Unlink(int rowIndex)
    {
        SetLocalValue(rowIndex, LinkColumn, string.Empty);
        Tint(rowIndex);
    }

    /// <summary>Маха една група от списъка на породените.</summary>
    internal void Unsend(int rowIndex, string link)
    {
        var left = SentBy(rowIndex).Where(l => l != link);
        SetLocalValue(rowIndex, SentColumn, string.Join(' ', left));
    }

    /// <summary>
    /// Създаденият ред се различава по цвят.
    ///
    /// Не по икона и не по колона: цветът се вижда с крайчеца на окото, а
    /// точно това е нужно — да личи кои редове идват отнякъде и не се пипат
    /// на ръка.
    /// </summary>
    private void Tint(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;

        var linked = LinkOf(rowIndex).Length > 0;

        _grid.Rows[rowIndex].DefaultCellStyle.BackColor =
            linked ? Theme.OkSoft : Color.Empty;
    }

    /// <summary>Трие редове по същия път, по който минава и потребителят.</summary>
    internal void DeleteForTest(IReadOnlyList<int> rows)
    {
        if (BeforeDelete is not null && !BeforeDelete(rows)) return;

        RemoveRows(rows);
    }

    /// <summary>Дали клетката е заключена заради връзка — ползва се от тестовете.</summary>
    internal bool IsLockedCell(int rowIndex, string column) =>
        _grid.Columns.Contains(column) && IsLocked(rowIndex, _grid.Columns[column].Index);

    /// <summary>Дали клетката е заключена заради връзка.</summary>
    private bool IsLocked(int rowIndex, int columnIndex)
    {
        if (LockedColumns.Count == 0) return false;
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count || _grid.Rows[rowIndex].IsNewRow) return false;
        if (columnIndex < 0 || columnIndex >= _grid.Columns.Count) return false;

        if (!LockedColumns.Contains(_grid.Columns[columnIndex].Name)) return false;

        return LinkOf(rowIndex).Length > 0 || SentBy(rowIndex).Count > 0;
    }

    private void RefuseEdit()
    {
        _hint.Text = Lang.T(
            "Количеството е заключено: този ред е свързан с редове в друг раздел. " +
            "Изтрийте връзката или променете разпределението, за да го смените.",
            "The amount is locked: this row is tied to rows in another tab. " +
            "Remove the link or change the split to alter it.");
    }

    /// <summary>
    /// Позволените стойности по колона. Липсваща колона значи „всичко“.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _filters = new(StringComparer.Ordinal);

    /// <summary>
    /// Показват ли се само отбелязаните редове.
    ///
    /// Отделно от <see cref="_filters"/>, а не поредна колона в тях: то не пита
    /// „коя стойност“, а „кои редове са за поправяне“ — и трябва да преживее
    /// махането на пресяването по колони.
    /// </summary>
    private bool _onlyBlocked;

    /// <summary>Стойността, с която се показва празна клетка.</summary>
    private static string Blank => Lang.T("(празно)", "(blank)");

    /// <summary>Има ли пресяване в момента.</summary>
    internal bool HasFilters => _filters.Count > 0 || _onlyBlocked;

    /// <summary>
    /// Редовете със стойност извън краткия списък на фирмата.
    ///
    /// Броят се редове, не клетки: човекът поправя ред, а не клетка — един ред
    /// с два сгрешени кода си остава един ред работа.
    /// </summary>
    internal int BlockedRows =>
        _grid.Rows.Cast<DataGridViewRow>().Count(row => !row.IsNewRow && IsBlockedRow(row));

    /// <summary>Показват ли се само отбелязаните.</summary>
    internal bool OnlyBlocked => _onlyBlocked;

    /// <summary>Включва и изключва показването само на отбелязаните.</summary>
    internal void ToggleOnlyBlocked()
    {
        _onlyBlocked = !_onlyBlocked;

        if (_blockedFilter is not null)
            _blockedFilter.Text = _onlyBlocked
                ? Lang.T("Покажи всички", "Show all")
                : Lang.T("Само отбелязаните", "Only the marked");

        ApplyFilters();
    }

    /// <summary>
    /// Отбелязан ли е редът — има ли клетка със стойност извън списъка.
    ///
    /// Пита се самият списък, а не цветът на клетката: цветът се слага при
    /// проверката и е следствие, а не източник.
    /// </summary>
    private bool IsBlockedRow(DataGridViewRow row)
    {
        if (row.IsNewRow) return false;

        foreach (var column in _plan.Columns)
        {
            if (!column.Domain.IsEnum) continue;

            var text = Stored(column, row.Cells[column.Name].Value?.ToString());

            if (!_limits.Allows(_plan.Report, _plan.Node, column, text)) return true;
        }

        return false;
    }

    /// <summary>
    /// Показва копчето за отбелязаните, ако има какво да покаже.
    ///
    /// Копче, което е винаги там и почти винаги не прави нищо, се научава като
    /// безполезно и после не се забелязва точно когато трябва.
    /// </summary>
    private void ShowBlockedFilter()
    {
        if (_blockedFilter is null) return;

        var any = BlockedRows > 0;

        if (!any && _onlyBlocked) ToggleOnlyBlocked();

        _blockedFilter.Visible = any;
    }

    /// <summary>Колко реда с данни се виждат сега.</summary>
    internal int VisibleRowCount =>
        _grid.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow && r.Visible);

    /// <summary>Текстът, по който се пресява — това, което се вижда в клетката.</summary>
    private string CellText(DataGridViewRow row, string column)
    {
        if (!_grid.Columns.Contains(column)) return Blank;

        var text = row.Cells[column].Value?.ToString()?.Trim();
        return string.IsNullOrEmpty(text) ? Blank : text;
    }

    /// <summary>
    /// Отваря пресяването за една колона.
    ///
    /// Списъкът е от стойностите, които наистина стоят в нея: в отчет с
    /// осемдесет реда се срещат пет кода, а номенклатурата е осемстотин.
    /// </summary>
    internal void OpenFilter(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _grid.Columns.Count) return;

        var view = _grid.Columns[columnIndex];

        // По копче няма какво да се пресява, а по „№“ — няма смисъл.
        if (view is DataGridViewButtonColumn || view.Name == IndexColumn) return;

        var values = _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => CellText(r, view.Name))
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(v => v, StringComparer.CurrentCulture)
            .ToList();

        if (values.Count == 0) return;

        _filters.TryGetValue(view.Name, out var allowed);

        using var dialog = new ColumnFilter(
            view.HeaderText.Replace(Environment.NewLine, " ").Trim(), values, allowed);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (dialog.ShowsEverything) _filters.Remove(view.Name);
        else _filters[view.Name] = dialog.Chosen;

        ApplyFilters();
    }

    /// <summary>Слага пресяване направо — ползва се от тестовете.</summary>
    internal void FilterForTest(string column, IEnumerable<string> allowed)
    {
        _filters[column] = [.. allowed];
        ApplyFilters();
    }

    /// <summary>Маха всички пресявания.</summary>
    internal void ClearFilters()
    {
        if (_filters.Count == 0) return;

        _filters.Clear();
        ApplyFilters();
    }

    /// <summary>
    /// Скрива редовете, които не минават през пресяването.
    ///
    /// Скрит ред си остава ред: пресяването пипа само какво се вижда, а не
    /// какво се записва. Затова и отчетът излиза цял, независимо какво е
    /// показано на екрана.
    /// </summary>
    private void ApplyFilters()
    {
        // Ред с текущата клетка не може да се скрие — Windows Forms хвърля.
        _grid.CurrentCell = null;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;

            row.Visible = _filters.All(f => f.Value.Contains(CellText(row, f.Key)))
                          && (!_onlyBlocked || IsBlockedRow(row));
        }

        foreach (DataGridViewColumn column in _grid.Columns)
            column.HeaderCell.Style.ForeColor =
                _filters.ContainsKey(column.Name) ? Theme.Accent : Theme.InkStrong;

        RenumberRowHeaders();

        if (_onlyBlocked)
        {
            _hint.Text = Lang.T(
                $"Показани са само отбелязаните — {VisibleRowCount} от {DataRowCount} реда. " +
                "Поправете ги и натиснете копчето пак.",
                $"Only the marked are shown — {VisibleRowCount} of {DataRowCount} rows. " +
                "Fix them and press the button again.");
            return;
        }

        if (_filters.Count == 0)
        {
            _hint.Text = Lang.T(
                "Пресяването е махнато — виждат се всички редове.",
                "The filter is off — every row is shown.");
            return;
        }

        var by = string.Join(", ", _filters.Keys.Select(NameOf));

        _hint.Text = Lang.T(
            $"Показани са {VisibleRowCount} от {DataRowCount} реда. Пресято по: {by}. " +
            "Скритите редове влизат в отчета както преди.",
            $"{VisibleRowCount} of {DataRowCount} rows shown. Filtered by: {by}. " +
            "Hidden rows still go into the report.");
    }

    private string NameOf(string column) =>
        _grid.Columns.Contains(column)
            ? _grid.Columns[column].HeaderText.Replace(Environment.NewLine, " ").Trim()
            : column;

    /// <summary>Дали редът няма нито една попълнена клетка.</summary>
    private bool IsEmptyRow(DataGridViewRow row) =>
        !row.IsNewRow && row.Cells.Cast<DataGridViewCell>()
            .Where(c => ColumnAt(c.ColumnIndex) is not null)
            .All(c => string.IsNullOrWhiteSpace(c.Value?.ToString()));

    /// <summary>Колко реда с данни има в момента, без реда за нов запис.</summary>
    internal int DataRowCount => _grid.Rows.Count - (_grid.AllowUserToAddRows ? 1 : 0);

    /// <summary>
    /// Свежда броя редове до искания и връща колко реда са останали над него,
    /// защото съдържат данни.
    ///
    /// Съобщението към потребителя е работа на повикващия — така правилото
    /// може да се провери с тест, без да изскача прозорец.
    /// </summary>
    internal int TrimTo(int wanted)
    {
        for (var i = _grid.Rows.Count - 1; i >= 0 && DataRowCount > wanted; i--)
        {
            var row = _grid.Rows[i];
            if (row.IsNewRow || !IsEmptyRow(row)) continue;

            _grid.Rows.RemoveAt(i);
        }

        var missing = wanted - DataRowCount;
        if (missing > 0) AddRows(missing);

        RenumberIndex();
        RenumberRowHeaders();
        MarkDirty();

        return Math.Max(0, DataRowCount - wanted);
    }

    /// <summary>
    /// Редовете, от които има поне една избрана клетка.
    ///
    /// Нарочно се тръгва от клетките, а не от заглавията на редовете: мрежата
    /// е в режим „избор на клетки“ и човек рядко щраква точно върху тясното
    /// заглавие отляво. Една избрана клетка стига, за да се сочи редът ѝ.
    /// </summary>
    internal IReadOnlyList<int> SelectedDataRows() =>
        _grid.SelectedCells.Cast<DataGridViewCell>()
            .Select(c => c.RowIndex)
            .Where(i => i >= 0 && i < _grid.Rows.Count && !_grid.Rows[i].IsNewRow)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

    /// <summary>Избира по една клетка от редовете в обхвата — ползва се от тестовете.</summary>
    internal bool IsWritableForTest(string column) =>
        _grid.Columns.Contains(column) && !_grid.Columns[column].ReadOnly;

    /// <summary>Какво подсказва клетката на тази колона, докато се пише.</summary>
    internal IReadOnlyList<string> HintsForTest(string column) => Suggest?.Invoke(column) ?? [];

    /// <summary>
    /// Залепя текст в клетка, както Ctrl+V. Клипбордът е общ за целия Windows и
    /// тест през него би зависел от това какво прави човекът на машината.
    /// </summary>
    internal void PasteForTest(string text, int rowIndex, string column)
    {
        _grid.CurrentCell = _grid.Rows[rowIndex].Cells[column];
        PasteText(text);
    }

    internal void SelectCellsForTest(int firstRow, int lastRow)
    {
        _grid.ClearSelection();

        for (var row = firstRow; row <= lastRow && row < DataRowCount; row++)
            _grid.Rows[row].Cells[1].Selected = true;
    }

    /// <summary>
    /// Маха посочените редове изцяло — със стойностите в тях, за разлика от
    /// Delete, който само изчиства клетките.
    ///
    /// Върви отзад напред: номерата на редовете се местят при всяко махане и
    /// при обхождане отпред нататък всяко следващо махане би било с един ред
    /// по-надолу от искания.
    ///
    /// Питането е работа на повикващия — както при <see cref="TrimTo"/>. Така
    /// правилото се проверява с тест, без да изскача прозорец.
    /// </summary>
    internal int RemoveRows(IEnumerable<int> rowIndexes)
    {
        var removed = 0;

        foreach (var index in rowIndexes.Distinct().OrderByDescending(i => i))
        {
            if (index < 0 || index >= _grid.Rows.Count || _grid.Rows[index].IsNewRow) continue;

            _grid.Rows.RemoveAt(index);
            removed++;
        }

        if (removed == 0) return 0;

        RenumberIndex();
        RenumberRowHeaders();
        MarkDirty();

        return removed;
    }

    /// <summary>
    /// Маха избраните редове, като преди това пита, ако сред тях има данни.
    ///
    /// За празни редове не се пита дори при включена настройка: питане за
    /// нищо приучва човека да натиска „Да“, без да чете — и точно затова
    /// после не прочита въпроса, който има значение.
    /// </summary>
    private void DeleteSelectedRows()
    {
        var rows = SelectedDataRows();
        if (rows.Count == 0) return;

        // Свързаните редове се решават другаде: там се знае какво стои от
        // другата страна на връзката.
        if (BeforeDelete is not null && !BeforeDelete(rows)) return;

        var withData = rows.Count(i => !IsEmptyRow(_grid.Rows[i]));

        if (ConfirmRowDeletes && withData > 0)
        {
            var what = rows.Count == 1
                ? Lang.T("Да се изтрие ли избраният ред?", "Delete the selected row?")
                : Lang.T($"Да се изтрият ли {rows.Count} избрани реда?", $"Delete the {rows.Count} selected rows?");

            var detail = withData == rows.Count
                ? rows.Count == 1 ? Lang.T("Той съдържа данни.", "It contains data.") : Lang.T("Всичките съдържат данни.", "All of them contain data.")
                : Lang.T($"{withData} от тях съдържат данни.", $"{withData} of them contain data.");

            var answer = MessageBox.Show(this, $"{what}\r\n\r\n{detail}",
                Lang.T("Изтриване на редове", "Deleting rows"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes) return;
        }

        var removed = RemoveRows(rows);

        // Таблицата не бива да остане без нито един ред за писане.
        if (DataRowCount == 0) AddRows(1);

        _hint.Text = removed == 1 ? Lang.T("Редът е изтрит.", "The row is deleted.") : Lang.T($"{removed} реда са изтрити.", $"{removed} rows deleted.");
    }

    /// <summary>Маха всички празни редове — и тези по средата, не само в края.</summary>
    internal int RemoveEmptyRows()
    {
        var removed = 0;

        for (var i = _grid.Rows.Count - 1; i >= 0; i--)
        {
            if (_grid.Rows[i].IsNewRow || !IsEmptyRow(_grid.Rows[i])) continue;

            _grid.Rows.RemoveAt(i);
            removed++;
        }

        RenumberIndex();
        RenumberRowHeaders();
        if (removed > 0) MarkDirty();

        return removed;
    }

    /// <summary>Слага стойност в клетка по име на колона — ползва се и от тестовете.</summary>
    internal void PutValue(int rowIndex, string columnName, string value)
    {
        while (DataRowCount <= rowIndex) AddRows(1);

        _grid.Rows[rowIndex].Cells[columnName].Value = value;
    }

    /// <summary>Подрежда по колона по име — ползва се и от тестовете.</summary>
    internal void SortByColumn(string columnName)
    {
        if (_grid.Columns[columnName] is { } view) SortBy(view.Index);
    }

    /// <summary>Стойностите на една колона в текущата подредба, без реда за нов запис.</summary>
    internal List<string> ColumnValues(string columnName) =>
        _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.Cells[columnName].Value?.ToString() ?? string.Empty)
            .ToList();

    /// <summary>
    /// Дали таблицата поема това съчетание — ползва се от тестовете.
    ///
    /// Изпълнява самото действие, затова се вика само върху празна таблица без
    /// избрана клетка: тогава всяко от действията излиза веднага, без да пипне
    /// нищо и без да отвори прозорец.
    /// </summary>
    internal bool HandlesKey(Keys keys)
    {
        var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        return ProcessCmdKey(ref message, keys);
    }

    /// <summary>Ширината на колона по име — ползва се от тестовете.</summary>
    internal int ColumnWidth(string columnName) => _grid.Columns[columnName]?.Width ?? 0;

    /// <summary>
    /// Каквото се вижда в клетката, а не каквото се пази в нея — за копчетата
    /// двете са различни. Ползва се от тестовете.
    /// </summary>
    internal string ShownAt(int rowIndex, string columnName) =>
        rowIndex >= 0 && rowIndex < _grid.Rows.Count && _grid.Columns.Contains(columnName)
            ? _grid.Rows[rowIndex].Cells[columnName].FormattedValue?.ToString() ?? string.Empty
            : string.Empty;

    /// <summary>Чете клетка по име на колона.</summary>
    internal string ValueAt(int rowIndex, string columnName) =>
        _grid.Rows[rowIndex].Cells[columnName].Value?.ToString() ?? string.Empty;

    /// <summary>
    /// Слага една стойност в част от колона и връща колко клетки са пипнати.
    ///
    /// Това е същият път, по който минава и поставянето върху избрана област,
    /// но без клипборда — така правилото се проверява с тест.
    /// </summary>
    internal int FillColumn(string columnName, string value, int firstRow, int lastRow)
    {
        var view = _grid.Columns[columnName];
        if (view?.Tag is not ColumnDef column) return 0;

        var text = Display(column, Normalize(column, value));
        var touched = 0;

        for (var row = firstRow; row <= lastRow && row < DataRowCount; row++)
        {
            if (IsLocked(row, view.Index)) { RefuseEdit(); continue; }

            _grid.Rows[row].Cells[view.Index].Value = text;
            Validate(row, view.Index);
            touched++;
        }

        if (touched > 0) MarkDirty();

        return touched;
    }

    private void SetRowCount(int wanted)
    {
        var blocked = TrimTo(wanted);
        if (blocked == 0) return;

        MessageBox.Show(this,
            Lang.T(
                $"Останаха {DataRowCount} реда вместо {wanted}: {blocked} от тях " +
                "съдържат данни и не бяха премахнати.\r\n\r\n" +
                "Изчистете ги ръчно, ако наистина не са нужни.",
                $"{DataRowCount} rows are left instead of {wanted}: {blocked} of them " +
                "contain data and were not removed.\r\n\r\n" +
                "Clear them by hand if they really are not needed."),
            Lang.T("Редове с данни", "Rows with data"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearEmptyRows()
    {
        var removed = RemoveEmptyRows();

        _hint.Text = removed == 0
            ? Lang.T("Нямаше празни редове.", "There were no empty rows.")
            : Lang.T($"Премахнати {removed} празни реда.", $"Removed {removed} empty rows.");
    }

    /// <summary>
    /// Попълва избраната колона с поредни числа. Ако са избрани няколко
    /// клетки, се номерират само те; иначе — цялата колона отгоре надолу.
    /// </summary>
    private void NumberColumn()
    {
        var column = _grid.CurrentCell?.ColumnIndex ?? -1;
        if (column < 0 || ColumnAt(column) is null) return;

        var rows = _grid.SelectedCells.Cast<DataGridViewCell>()
            .Where(c => c.ColumnIndex == column && !_grid.Rows[c.RowIndex].IsNewRow)
            .Select(c => c.RowIndex)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (rows.Count < 2)
        {
            rows = _grid.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => r.Index)
                .ToList();
        }

        for (var i = 0; i < rows.Count; i++)
        {
            _grid.Rows[rows[i]].Cells[column].Value = (i + 1).ToString();
            Validate(rows[i], column);
        }

        MarkDirty();
    }

    public SheetPlan Plan => _plan;

    /// <summary>Има ли незаписани промени.</summary>
    public bool IsDirty => _dirty;

    /// <summary>Колко реда носят стойност — показва се в заглавието на подраздела.</summary>
    public int FilledRows => Snapshot().Count;

    public event EventHandler? Changed;

    /// <summary>Натиснато копче в служебна колона: кой ред и коя колона.</summary>
    public event EventHandler<RowActionEventArgs>? RowAction;

    /// <summary>
    /// Двойка колони, в която втората е описанието на първата.
    ///
    /// „Описание на дейност“ повтаря дума по дума текста на избраната дейност
    /// R/D. Схемата държи двете разделени, но съдържанието им е едно и също,
    /// затова второто се пише само.
    /// </summary>
    internal (string From, string To)? DescriptionLink;

    /// <summary>Ред и колона на натиснатото копче.</summary>
    internal sealed class RowActionEventArgs(int rowIndex, string column) : EventArgs
    {
        public int RowIndex { get; } = rowIndex;
        public string Column { get; } = column;
    }

    /// <summary>
    /// Слага надписа върху копчето.
    ///
    /// UseColumnTextForButtonValue стига за редовете с данни, но не и за реда
    /// „нов запис“ — там копчето излиза празно синьо петно. В празна таблица
    /// това е единственият ред и копчето изглежда счупено, каквото и да пише
    /// на колоната.
    /// </summary>
    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count) return;

        var name = _grid.Columns[e.ColumnIndex].Name;
        var extra = _extras.FirstOrDefault(x => x.Kind == LocalColumnKind.Button && x.Name == name);

        if (extra is null) return;

        e.Value = extra.Header;
        e.FormattingApplied = true;

        // Цветът се слага и тук, не само на колоната: редуването на редовете
        // се налага над стила на колоната и всеки втори бутон избледнява.
        e.CellStyle.BackColor = Theme.Accent;
        e.CellStyle.ForeColor = Theme.OnAccent;
        e.CellStyle.SelectionBackColor = Theme.Accent;
        e.CellStyle.SelectionForeColor = Theme.OnAccent;
    }

    private void RaiseRowAction(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0) return;
        if (_grid.Rows[rowIndex].IsNewRow) return;

        var name = _grid.Columns[columnIndex].Name;
        if (!_extras.Any(e => e.Kind == LocalColumnKind.Button && e.Name == name)) return;

        RowAction?.Invoke(this, new RowActionEventArgs(rowIndex, name));
    }

    /// <summary>
    /// Попълва описанието, щом се избере дейност.
    ///
    /// Не пипа описание, писано на ръка: замества се само празното и това,
    /// което съвпада с описанието на някоя дейност — тоест е било сложено
    /// оттук. Иначе една смяна на кода би изтрила чужд текст.
    /// </summary>
    private void ApplyDescriptionLink(int rowIndex, int columnIndex)
    {
        if (DescriptionLink is not { } link) return;
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count || _grid.Rows[rowIndex].IsNewRow) return;
        if (ColumnAt(columnIndex) is not { } column || column.Name != link.From) return;
        if (_grid.Columns[link.To] is not { } target) return;

        var code = Stored(column, _grid.Rows[rowIndex].Cells[columnIndex].Value?.ToString());

        var description = column.Domain.Options
            .FirstOrDefault(o => string.Equals(o.Value, code, StringComparison.OrdinalIgnoreCase))?
            .Description;

        if (string.IsNullOrWhiteSpace(description)) return;

        var current = _grid.Rows[rowIndex].Cells[target.Index].Value?.ToString()?.Trim() ?? string.Empty;

        var mine = current.Length == 0 ||
                   column.Domain.Options.Any(o => string.Equals(o.Description, current, StringComparison.Ordinal));

        if (!mine) return;

        _grid.Rows[rowIndex].Cells[target.Index].Value = description;
        Validate(rowIndex, target.Index);
    }

    /// <summary>Натиска копчето в реда по име на колона — ползва се от тестовете.</summary>
    internal void PressRowAction(int rowIndex, string column)
    {
        if (_grid.Columns[column] is { } view) RaiseRowAction(rowIndex, view.Index);
    }

    /// <summary>Стойностите на един ред, както се пазят — по имена от схемата.</summary>
    internal Dictionary<string, string> RowValues(int rowIndex)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return values;

        var row = _grid.Rows[rowIndex];

        foreach (var column in _plan.Columns)
        {
            var text = Stored(column, row.Cells[column.Name].Value?.ToString());
            if (!string.IsNullOrWhiteSpace(text)) values[column.Name] = text;
        }

        return values;
    }

    /// <summary>Чете служебна колона.</summary>
    internal string LocalValue(int rowIndex, string name) =>
        rowIndex >= 0 && rowIndex < _grid.Rows.Count && _grid.Columns.Contains(name)
            ? _grid.Rows[rowIndex].Cells[name].Value?.ToString() ?? string.Empty
            : string.Empty;

    /// <summary>Пише в служебна колона.</summary>
    internal void SetLocalValue(int rowIndex, string name, string value)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count || !_grid.Columns.Contains(name)) return;

        _grid.Rows[rowIndex].Cells[name].Value = value;
        MarkDirty();
    }

    /// <summary>
    /// Добавя ред в края и връща номера му. Стойностите идват както се пазят
    /// — кодове, не описания — и се превеждат за показване.
    /// </summary>
    internal int AppendRow(IReadOnlyDictionary<string, string> values)
    {
        var index = _grid.Rows.Add();

        // Редът за нов запис винаги стои последен: току-що добавеният е този
        // преди него.
        if (_grid.AllowUserToAddRows && index == _grid.Rows.Count - 1) index = Math.Max(0, index - 1);

        foreach (var column in _plan.Columns)
        {
            if (!values.TryGetValue(column.Name, out var value) || value.Length == 0) continue;

            _grid.Rows[index].Cells[column.Name].Value = Display(column, Normalize(column, value));
        }

        RenumberIndex();
        RenumberRowHeaders();
        ValidateRow(index);
        MarkDirty();

        return index;
    }

    /// <summary>
    /// Добавя много редове наведнъж и връща колко са влезли.
    ///
    /// <see cref="AppendRow"/> пренномерира и проверява при ВСЕКИ ред, а
    /// пренномерирането минава през цялата таблица: при един ред това не се
    /// забелязва, при петстотин става квадратично и програмата спира да
    /// отговаря. Внасянето от чужд дневник обаче идва точно на петстотин реда —
    /// истински юлски износ носи 553 приемания.
    ///
    /// Затова тук редовете се сипват наведнъж, а подреждането и проверката
    /// стават веднъж накрая, и то само за новите: старите вече са проверени.
    ///
    /// Приемат се и служебните колони („__“): при внасяне те носят неща като
    /// прякото предаване, а <see cref="AppendRow"/> ги подминава, защото гледа
    /// само колоните от плана.
    /// </summary>
    internal int AppendRows(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        var first = -1;
        var added = 0;

        _grid.SuspendLayout();

        try
        {
            foreach (var values in rows)
            {
                var index = _grid.Rows.Add();

                if (_grid.AllowUserToAddRows && index == _grid.Rows.Count - 1)
                    index = Math.Max(0, index - 1);

                if (first < 0) first = index;

                foreach (var column in _plan.Columns)
                {
                    if (!values.TryGetValue(column.Name, out var value) || value.Length == 0) continue;

                    _grid.Rows[index].Cells[column.Name].Value = Display(column, Normalize(column, value));
                }

                foreach (var (name, value) in values)
                {
                    if (!name.StartsWith("__", StringComparison.Ordinal)) continue;
                    if (value.Length == 0 || !_grid.Columns.Contains(name)) continue;

                    _grid.Rows[index].Cells[name].Value = value;
                }

                added++;
            }
        }
        finally
        {
            _grid.ResumeLayout();
        }

        if (added == 0) return 0;

        RenumberIndex();
        RenumberRowHeaders();

        for (var index = first; index < _grid.Rows.Count; index++) ValidateRow(index);

        MarkDirty();

        return added;
    }

    // ------------------------------------------------------------- устройство

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.Font = Theme.Base;
        _grid.BackgroundColor = Theme.Canvas;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersWidth = Theme.Scaled(46);

        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        // Водоравните черти са от мрежата, отвесните се рисуват отделно и
        // по-бледо. Еднакво силни във всички посоки, те правят екрана таблица
        // от база данни; съвсем без отвесни пък при четиринайсет колони окото
        // губи къде свършва едната и започва другата.
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.CellPainting += OnCellPainting;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.MultiSelect = true;
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        _grid.ShowCellToolTips = true;

        // Tab се движи вътре в таблицата, а не към следващия бутон — иначе
        // попълването на ред налага мишка.
        _grid.StandardTab = false;

        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.InkStrong;
        _grid.ColumnHeadersDefaultCellStyle.Font = Theme.Bold;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

        // Клетките също получават цвят от темата: иначе при тъмен набор
        // остават бели и текстът в тях изчезва.
        _grid.DefaultCellStyle.BackColor = Theme.Canvas;
        _grid.DefaultCellStyle.ForeColor = Theme.Ink;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentLight;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        _grid.RowHeadersDefaultCellStyle.BackColor = Theme.Surface;
        _grid.RowHeadersDefaultCellStyle.ForeColor = Theme.InkMuted;
        _grid.RowHeadersDefaultCellStyle.SelectionBackColor = Theme.AccentLight;
        _grid.RowHeadersDefaultCellStyle.SelectionForeColor = Theme.Accent;
        _grid.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
        // Редуването е през цял ред и е достатъчно видимо, за да води окото
        // по двайсет колони. По-бледото от това не върши работа на широк екран.
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.Surface;
        _grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.AccentLight;
        _grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Theme.Ink;
        _grid.GridColor = Theme.Border;

        _grid.CellBeginEdit += (_, e) =>
        {
            if (!IsLocked(e.RowIndex, e.ColumnIndex)) return;

            e.Cancel = true;
            RefuseEdit();
        };

        _grid.CellEndEdit += (_, e) => { NormalizeCell(e.RowIndex, e.ColumnIndex); MarkDirty(); };
        _grid.CellEnter += (_, e) => ShowHint(e.ColumnIndex);
        _grid.RowsAdded += (_, _) => RenumberRowHeaders();
        _grid.RowsRemoved += (_, _) => { RenumberRowHeaders(); MarkDirty(); };
        _grid.UserDeletingRow += (_, e) =>
        {
            if (BeforeDelete is not null && !BeforeDelete([e.Row.Index])) { e.Cancel = true; return; }

            MarkDirty();
        };
        _grid.EditingControlShowing += OnEditingControlShowing;
        _grid.DataError += OnDataError;
        _grid.ColumnHeaderMouseClick += (_, e) =>
        {
            // Ляв бутон подрежда, десен пресява — двете най-често се искат
            // една след друга върху една и съща колона.
            if (e.Button == MouseButtons.Right) OpenFilter(e.ColumnIndex);
            else SortBy(e.ColumnIndex);
        };

        // Избраното от списъка се записва веднага, а не при напускане на реда:
        // иначе преместването встрани изглежда като че ли изборът се е загубил.
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellDoubleClick += (_, e) => OpenPicker(e.RowIndex, e.ColumnIndex);
        _grid.CellContentClick += (_, e) => RaiseRowAction(e.RowIndex, e.ColumnIndex);
        _grid.CellFormatting += OnCellFormatting;
        _grid.CellValueChanged += (_, e) => ApplyDescriptionLink(e.RowIndex, e.ColumnIndex);

        // Смяната на екран с друг мащаб минава през шрифта на таблицата.
        // Новите редове се раждат с височината на образеца; наново се мери
        // само когато шрифтът или мащабът на екрана се смени.
        _grid.FontChanged += (_, _) => ApplyRowMetrics();
        _grid.ColumnWidthChanged += (_, _) => ApplyHeaderHeight();
        _grid.DpiChangedAfterParent += (_, _) => ApplyRowMetrics();

        ApplyRowMetrics();
    }

    /// <summary>
    /// Мери редовете по шрифта, а не по кръгло число.
    ///
    /// Постоянните трийсет пиксела стигат на екран без увеличение и на нищо
    /// друго: при мащаб 125 % и 150 % Windows уголемява шрифта, редът остава
    /// същият и опашките на „р“, „у“ и „щ“ се отрязват. Оттам и оплакването,
    /// че на малък монитор текстът в таблицата е окълцан.
    ///
    /// Заглавието остава на два реда: имената на полетата в схемата са дълги
    /// и на един ред задължително се режат.
    /// </summary>
    private void ApplyRowMetrics()
    {
        // Мери се по мащаба на екрана, на който стои таблицата. Font.Height
        // мери по главния екран: при два монитора с различен мащаб това е
        // грешният — прозорецът се рисува по своя.
        var line = (int)Math.Ceiling(_grid.Font.GetHeight(_grid.DeviceDpi));
        var height = line + 16;

        if (_grid.RowTemplate.Height != height) _grid.RowTemplate.Height = height;

        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Height != height) row.Height = height;

        ApplyHeaderHeight();
    }

    /// <summary>
    /// Височината на заглавния ред следва ширините на колоните.
    ///
    /// Пресмята се наново при всяка смяна на ширина: „Побери текста“ и
    /// дърпането на ръба стесняват колоната, заглавието се пренася на ред
    /// повече и последният ред остава отрязан наполовина — точно там, където
    /// стои звездичката за задължително поле.
    /// </summary>
    private void ApplyHeaderHeight()
    {
        var headers = Math.Max(1, _grid.Font.Height) * HeaderLines() + 22;

        if (_grid.ColumnHeadersHeight != headers) _grid.ColumnHeadersHeight = headers;
    }

    /// <summary>
    /// На колко реда се пренася най-дългото заглавие.
    ///
    /// Два реда стигаха, докато не се появи „Номер на работен лист *“ — от
    /// него се виждаха две думи и половин звездичка, а точно звездичката
    /// казва, че полето е задължително. Повече от три реда обаче изяждат
    /// таблицата, затова там свършва.
    /// </summary>
    private int HeaderLines()
    {
        var line = Math.Max(1, _grid.Font.Height);
        var lines = 2;

        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (column.HeaderText.Length == 0) continue;

            var room = new Size(Math.Max(20, column.Width - 16), line * 6);

            var height = TextRenderer.MeasureText(
                column.HeaderText, Theme.Bold, room, TextFormatFlags.WordBreak).Height;

            lines = Math.Max(lines, (int)Math.Ceiling(height / (double)line));
        }

        return Math.Min(lines, 3);
    }

    private void BuildColumns(IReadOnlyList<Dictionary<string, string>> rows)
    {
        // Поредният номер стои като колона, а не само в заглавието на реда:
        // при подредба по друга колона номерът пътува с реда и с един клик
        // връща първоначалния ред.
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = IndexColumn,
            HeaderText = "№",
            ReadOnly = true,
            Width = Theme.Scaled(54),
            SortMode = DataGridViewColumnSortMode.Programmatic,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                ForeColor = Theme.InkFaint,
                Alignment = DataGridViewContentAlignment.MiddleRight
            }
        });

        foreach (var column in _plan.Columns)
        {
            var domain = column.Domain;
            var options = Offer(column, rows);

            // Броят се мери по късия списък, не по схемата. Точно това е
            // печалбата: свити до двайсет, ЕКО кодовете слизат под прага и
            // полето от търсачка става обикновено падащо меню.
            DataGridViewColumn view = domain.IsEnum && options.Count <= PickerThreshold
                ? new DataGridViewComboBoxColumn
                {
                    DataSource = options.Select(o => o.Display).ToList(),
                    FlatStyle = FlatStyle.Flat,
                    // Стрелка само в клетката, върху която стои показалецът.
                    //
                    // Видима във всеки ред тя запълва екрана с еднакви иконки
                    // и таблицата заприличва на изглед към база данни. Че има
                    // избор, се разбира веднага щом клетката се активира, а
                    // подсказката отдолу го казва и с думи.
                    DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                    DisplayStyleForCurrentCellOnly = true
                }
                : new DataGridViewTextBoxColumn();

            view.Name = column.Name;
            view.HeaderText = Header(column);
            view.Tag = column;
            view.SortMode = DataGridViewColumnSortMode.Programmatic;
            view.MinimumWidth = Theme.Scaled(90);
            view.AutoSizeMode = DataGridViewAutoSizeColumnMode.NotSet;
            view.Width = WidthFor(column, view.HeaderText);
            view.ToolTipText = Describe(column, options.Count);

            if (column.Domain.Kind is ValueKind.Integer or ValueKind.Decimal)
                view.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            // Служебните колони за връзка се различават по цвят на текста, а
            // не по фон: собствен фон къса редуването и редът престава да се
            // чете като едно цяло.
            if (column.IsLink)
                view.DefaultCellStyle.ForeColor = Theme.InkMuted;

            _grid.Columns.Add(view);
        }

        BuildExtraColumns();
    }

    /// <summary>
    /// Колоните, които ги няма в схемата: отметката „вече пренесен“ и копчето,
    /// което го прави.
    ///
    /// Стоят най-вдясно, след всичко от отчета, за да не се вклиняват между
    /// полета, които се четат заедно. В XML не влизат — износът минава само
    /// по колоните от плана и просто не ги вижда.
    /// </summary>
    private void BuildExtraColumns()
    {
        foreach (var extra in _extras)
        {
            DataGridViewColumn view = extra.Kind == LocalColumnKind.Button
                ? new DataGridViewButtonColumn
                {
                    Text = extra.Header,
                    UseColumnTextForButtonValue = true,
                    FlatStyle = FlatStyle.Flat
                }
                : new DataGridViewTextBoxColumn { ReadOnly = !extra.Editable };

            view.Name = extra.Name;
            view.Visible = !extra.Hidden;
            view.HeaderText = extra.Kind == LocalColumnKind.Button ? string.Empty : extra.Header;
            view.SortMode = DataGridViewColumnSortMode.NotSortable;
            view.MinimumWidth = Theme.Scaled(60);
            view.Width = extra.Width;
            view.ToolTipText = extra.Tip ?? string.Empty;
            view.DefaultCellStyle.Alignment = extra.Editable
                ? DataGridViewContentAlignment.MiddleLeft
                : DataGridViewContentAlignment.MiddleCenter;

            if (extra.Kind == LocalColumnKind.Button)
            {
                // Плътен цвят: копче, което не личи, се търси с мишката, а
                // тук се натиска петдесет пъти подред.
                view.DefaultCellStyle.BackColor = Theme.Accent;
                view.DefaultCellStyle.ForeColor = Theme.OnAccent;
                view.DefaultCellStyle.SelectionBackColor = Theme.Accent;
                view.DefaultCellStyle.SelectionForeColor = Theme.OnAccent;
                view.DefaultCellStyle.Font = Theme.Bold;
                view.DefaultCellStyle.Padding = new Padding(2, 3, 2, 3);
            }
            else if (!extra.Editable)
            {
                view.DefaultCellStyle.ForeColor = Theme.Ok;
                view.DefaultCellStyle.Font = Theme.Bold;
            }

            _grid.Columns.Add(view);
        }
    }

    private static string Header(ColumnDef column)
    {
        var text = ReportTitles.ColumnShort(column);
        return column.Required ? text + " *" : text;
    }

    /// <summary>
    /// <paramref name="offered"/> е дължината на менюто след кратките списъци.
    /// Пише се и пълният брой, когато двете се различават: липсващ код трябва
    /// да се обяснява с „така сме го нагласили“, а не с „програмата не го знае“.
    /// </summary>
    private static string Describe(ColumnDef column, int? offered = null)
    {
        var text = new StringBuilder(ReportTitles.Column(column));
        var domain = column.Domain;

        text.Append(column.Required ? Lang.T("\nЗадължително.", "\nRequired.") : Lang.T("\nНезадължително.", "\nOptional."));

        text.Append(domain.Kind switch
        {
            ValueKind.Date => Lang.T("\nДата във вид 31.12.2025 г.", "\nA date like 31.12.2025."),
            ValueKind.Integer => Lang.T("\nЦяло число.", "\nA whole number."),
            ValueKind.Decimal => Lang.T("\nЧисло с десетична запетая.", "\nA number with a decimal comma."),
            ValueKind.Enum => Lang.T($"\nИзбор от {domain.Options.Count} стойности — двоен клик или F4.", $"\nA choice of {domain.Options.Count} values — double-click or F4."),
            _ => string.Empty
        });

        if (domain.MaxLength is { } max) text.Append(Lang.T($"\nДо {max} знака.", $"\nUp to {max} characters."));
        if (domain.MinInclusive is { } min) text.Append(Lang.T($"\nНе по-малко от {min}.", $"\nNot less than {min}."));

        return text.ToString();
    }

    /// <summary>
    /// Ширина на колоната.
    ///
    /// Взима се по-голямото от две неща: колкото трябва на съдържанието и
    /// колкото трябва на заглавието, разположено на два реда. Досега се
    /// гледаше само съдържанието и заглавия като „Основание за притежание“ се
    /// режеха с многоточие — а точно те казват какво се попълва.
    /// </summary>
    private static int WidthFor(ColumnDef column, string header)
    {
        // Мери се текстът, който ще стои вътре, а не кръгло число.
        //
        // Оттам идваха отрязаните дати: сто и десет пиксела стигат за
        // „31.12.2025“ при мащаб 100 % и за нищо повече — при 125 % буквите
        // порастват, числото в кода не, и последната цифра изчезва. Същото
        // важи за всяка колона; датата само се вижда най-често.
        var sample = column.Domain.Kind switch
        {
            ValueKind.Date => "00.00.0000",
            ValueKind.Integer => "0000000",
            ValueKind.Decimal => "0000000,000",
            ValueKind.Enum => LongestOption(column),
            _ => column.Domain.MaxLength is > 60 ? new string('0', 30) : new string('0', 20)
        };

        // Отстоянията на клетката, чертата между колоните и малко въздух:
        // стойност, която опира в ръба, се чете като отрязана дори когато не е.
        var content = TextRenderer.MeasureText(sample, Theme.Base).Width + 34;

        // Заглавието се пренася на два реда, затова му стига половината
        // ширина плюс отстоянията.
        var needed = TextRenderer.MeasureText(header, Theme.Bold).Width / 2 + 30;

        return Math.Clamp(Math.Max(content, needed), 90, 320);
    }

    /// <summary>За теста: какво предлага менюто на тази колона.</summary>
    internal IReadOnlyList<EnumOption> OfferedForTest(string column) =>
        _plan.Columns.FirstOrDefault(c => c.Name == column) is { } def
            ? Offer(def, Snapshot())
            : [];

    /// <summary>За теста: падащо меню в клетката ли е колоната, или търсачка.</summary>
    internal bool IsMenuColumn(string column) =>
        _grid.Columns.Contains(column) && _grid.Columns[column] is DataGridViewComboBoxColumn;

    /// <summary>
    /// Сменени кратки списъци — най-често защото в заглавната част е избрана
    /// друга фирма.
    ///
    /// Колоните се сглобяват наново, а не само им се сменя източникът: при
    /// двайсет вместо осемстотин стойности полето сменя вида си от търсачка на
    /// падащо меню, а видът се решава при сглобяването. Редовете минават през
    /// снимка и се връщат — стойност, попълнена преди смяната, остава.
    /// </summary>
    internal void ApplyLimits(ValueLimits limits)
    {
        var rows = Snapshot();
        var dirty = _dirty;

        // Къде стоеше показалецът. Изчистването на колоните го обезсилва, а
        // таблица без текуща клетка не отговаря на клавиши и на двоен клик,
        // докато някой не щракне в нея — изглежда като заяла.
        var column = _grid.CurrentCell?.OwningColumn?.Name;
        var row = _grid.CurrentCell?.RowIndex ?? -1;

        // Редакцията в клетката преживява изчистването на колоните и хвърля
        // после, върху колона, която вече не съществува.
        _grid.CancelEdit();
        _grid.CurrentCell = null;

        _limits = limits;

        _grid.Rows.Clear();
        _grid.Columns.Clear();

        BuildColumns(rows);
        ApplyRowMetrics();
        LoadRows(rows);

        Restore(row, column);

        // Пресглобяването не е промяна, направена от човека.
        _dirty = dirty;
    }

    /// <summary>Връща показалеца там, където беше преди пресглобяването.</summary>
    private void Restore(int row, string? column)
    {
        if (column is null || !_grid.Columns.Contains(column)) return;
        if (row < 0 || row >= _grid.Rows.Count) return;
        if (!_grid.Rows[row].Visible || !_grid.Columns[column].Visible) return;

        try
        {
            _grid.CurrentCell = _grid.Rows[row].Cells[column];
        }
        catch (InvalidOperationException)
        {
            // Клетка, която не може да стане текуща (скрита или заключена
            // колона). Показалецът просто остава невъзстановен.
        }
    }

    /// <summary>
    /// Какво да се предложи в менюто на тази колона.
    ///
    /// Вече попълненото се подава като „в употреба“ и остава в списъка дори
    /// да е извън него — виж <see cref="ValueLimits.Offer"/>.
    /// </summary>
    private IReadOnlyList<EnumOption> Offer(
        ColumnDef column,
        IReadOnlyList<Dictionary<string, string>> rows) =>
        _limits.Offer(
            _plan.Report,
            _plan.Node,
            column,
            rows.Select(row => row.TryGetValue(column.Name, out var value) ? value : string.Empty));

    /// <summary>
    /// Показателна стойност за широчината на изброена колона: кодът и
    /// началото на номенклатурата. Цялото име невинаги се побира — за него
    /// има подсказка и „Побери текста“.
    /// </summary>
    private static string LongestOption(ColumnDef column)
    {
        var first = column.Domain.Options.Count > 0 ? column.Domain.Options[0] : null;
        if (first is null) return new string('0', 12);

        var display = first.Display;

        return display.Length <= 28 ? display : display[..28];
    }

    // ------------------------------------------------------------- данни

    private void LoadRows(IReadOnlyList<Dictionary<string, string>> rows)
    {
        _grid.Rows.Clear();

        foreach (var row in rows)
        {
            var index = _grid.Rows.Add();

            foreach (var column in _plan.Columns)
                if (row.TryGetValue(column.Name, out var value) && value.Length > 0)
                    _grid.Rows[index].Cells[column.Name].Value = Display(column, value);

            foreach (var extra in _extras)
                if (extra.Kind == LocalColumnKind.Text &&
                    row.TryGetValue(extra.Name, out var local) && local.Length > 0)
                    _grid.Rows[index].Cells[extra.Name].Value = local;
        }

        for (var i = 0; i < DataRowCount; i++) Tint(i);

        RenumberIndex();
        RenumberRowHeaders();
        ValidateAll();
        ShowBlockedFilter();
        _dirty = false;
    }

    /// <summary>Текущото съдържание, без празните редове.</summary>
    public List<Dictionary<string, string>> Snapshot()
    {
        var rows = new List<Dictionary<string, string>>();

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var column in _plan.Columns)
            {
                var text = Stored(column, row.Cells[column.Name].Value?.ToString());
                if (!string.IsNullOrWhiteSpace(text)) values[column.Name] = text;
            }

            // Празен ред си остава празен: отметката „пренесен“ не е данни и
            // не може сама да задържи ред в отчета.
            if (values.Count == 0) continue;

            foreach (var extra in _extras)
            {
                if (extra.Kind != LocalColumnKind.Text) continue;

                var local = row.Cells[extra.Name].Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(local)) values[extra.Name] = local;
            }

            rows.Add(values);
        }

        return rows;
    }

    public void MarkSaved() => _dirty = false;

    /// <summary>
    /// Изброените стойности се показват с описание, а се пазят само с кода.
    ///
    /// Важи и за дългите списъци. „161004“ не значи нищо дори на човек, който
    /// пише отчета всеки месец — номенклатурата е името на отпадъка и без
    /// нея редът се проверява с отделен списък в ръка. Колоната е достатъчно
    /// широка за кода и началото на името, а цялото се вижда в подсказката.
    /// </summary>
    private static string Display(ColumnDef column, string stored)
    {
        if (!column.Domain.IsEnum) return stored;

        var option = column.Domain.Options.FirstOrDefault(o =>
            string.Equals(o.Value, stored, StringComparison.OrdinalIgnoreCase));

        return option?.Display ?? stored;
    }

    private static string Stored(ColumnDef column, string? shown)
    {
        var text = shown?.Trim() ?? string.Empty;
        if (text.Length == 0 || !column.Domain.IsEnum) return text;

        // „04 — Оползотворяване“ се записва като „04“.
        var dash = text.IndexOf(" — ", StringComparison.Ordinal);
        return dash > 0 ? text[..dash] : text;
    }

    /// <summary>
    /// Привежда стойността към вида, в който я очакваме.
    ///
    /// Засега това важи само за датите: разделителят не е уточнен и хората
    /// лепят „01/06/2026“, „1-6-26“ или голото серийно число на Excel. Каквото
    /// се разпознае, се записва като 01.06.2026 — така колоната изглежда
    /// еднакво и грешката се вижда веднага, вместо чак при проверката.
    ///
    /// Неразпознатото се оставя както е въведено: потребителят трябва да види
    /// какво е написал, а не мълчаливо поправена стойност.
    /// </summary>
    private static string Normalize(ColumnDef column, string? text)
    {
        var raw = text?.Trim() ?? string.Empty;

        if (column.Domain.Kind != ValueKind.Date || raw.Length == 0) return raw;

        return ValueConverter.TryNormalizeDate(raw, out var normalized) ? normalized : raw;
    }

    private void NormalizeCell(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0) return;
        if (_grid.Rows[rowIndex].IsNewRow) return;
        if (_grid.Columns[columnIndex].Tag is not ColumnDef column) return;

        var cell = _grid.Rows[rowIndex].Cells[columnIndex];
        var normalized = Normalize(column, cell.Value?.ToString());

        if (!string.Equals(normalized, cell.Value?.ToString(), StringComparison.Ordinal))
            cell.Value = normalized;

        Validate(rowIndex, columnIndex);
    }

    // ------------------------------------------------------------- проверка

    private void ValidateAll()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            foreach (DataGridViewCell cell in row.Cells) Validate(cell.RowIndex, cell.ColumnIndex);
        }
    }

    /// <summary>
    /// Оцветява клетката веднага, вместо да чака проверката при генериране.
    /// Ползва същия преобразувател, който после ще прави и XML-а — така
    /// подсказката тук и грешката там не могат да си противоречат.
    /// </summary>
    private void Validate(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0) return;
        if (_grid.Rows[rowIndex].IsNewRow) return;
        if (_grid.Columns[columnIndex].Tag is not ColumnDef column) return;

        var cell = _grid.Rows[rowIndex].Cells[columnIndex];
        var text = Stored(column, cell.Value?.ToString());

        if (text.Length == 0)
        {
            // Празен задължителен ред се съобщава при проверката на целия отчет,
            // не докато човекът още пише в него.
            PaintCell(cell, ok: true, null);
            return;
        }

        var result = ValueConverter.Convert(CellValue.FromText(text), column);

        if (!result.Ok)
        {
            PaintCell(cell, ok: false, result.Error);
            return;
        }

        // Стойността е добра по схема, но извън краткия списък на фирмата.
        // Не се чисти и не се маха — само се вижда, че е за поглеждане.
        var allowed = _limits.Allows(_plan.Report, _plan.Node, column, text);

        PaintCell(cell, allowed, allowed ? null : Lang.T(
            "Стойността е извън краткия списък на тази фирма.",
            "The value is outside this company's short list."));
    }

    private static void PaintCell(DataGridViewCell cell, bool ok, string? error)
    {
        cell.Style.BackColor = ok ? Color.Empty : Theme.ErrorSoft;
        cell.Style.ForeColor = ok ? Color.Empty : Theme.Error;
        cell.ErrorText = ok ? string.Empty : error ?? string.Empty;
    }

    /// <summary>
    /// Подрежда по избраната колона. Празните редове винаги остават най-долу.
    ///
    /// Те не участват в сравнението нарочно: иначе при подредба по дата
    /// осемдесетте празни реда, оставени „за всеки случай“, се качват най-горе
    /// и изтикват данните извън екрана.
    /// </summary>
    private void SortBy(int columnIndex)
    {
        if (columnIndex < 0 || _grid.IsCurrentCellInEditMode) return;

        var view = _grid.Columns[columnIndex];

        // Копчето и отметката нямат подредба. Стрелката за посока обаче се
        // слага безусловно, а Windows Forms хвърля, щом колоната е обявена за
        // неподреждаема — щракване върху такова заглавие сваляше програмата.
        if (view.SortMode == DataGridViewColumnSortMode.NotSortable) return;

        var ascending = view.HeaderCell.SortGlyphDirection != SortOrder.Ascending;

        foreach (DataGridViewColumn other in _grid.Columns)
            other.HeaderCell.SortGlyphDirection = SortOrder.None;

        _grid.Sort(new RowOrder(columnIndex, ascending, ColumnAt(columnIndex), IsEmptyRow));

        view.HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;

        RenumberRowHeaders();
        MarkDirty();

        var by = view.HeaderText.Replace(Environment.NewLine, " ");

        _hint.Text = Lang.T(
            $"Подредено по „{by}“. Празните редове остават най-долу; " +
            "„№“ връща първоначалния ред.",
            $"Sorted by “{by}”. Empty rows stay at the bottom; " +
            "“№” brings back the original order.");
    }

    /// <summary>
    /// Сравнява два реда по една колона, съобразно вида ѝ: числата като
    /// числа, датите като дати, останалото по азбучен ред според българската
    /// подредба.
    ///
    /// Текстово сравнение навсякъде би наредило 10 преди 9, а 01.02 преди
    /// 31.01 — тъкмо там, където подредбата трябва да помогне.
    /// </summary>
    private sealed class RowOrder(
        int column, bool ascending, ColumnDef? definition, Func<DataGridViewRow, bool> isEmpty)
        : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not DataGridViewRow left || y is not DataGridViewRow right) return 0;

            // Празните винаги най-долу, независимо от посоката.
            var leftEmpty = isEmpty(left);
            var rightEmpty = isEmpty(right);

            if (leftEmpty != rightEmpty) return leftEmpty ? 1 : -1;
            if (leftEmpty) return 0;

            var a = left.Cells[column].Value?.ToString() ?? string.Empty;
            var b = right.Cells[column].Value?.ToString() ?? string.Empty;

            var result = CompareValues(a, b);

            return ascending ? result : -result;
        }

        private int CompareValues(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0)
                return a.Length == b.Length ? 0 : a.Length == 0 ? 1 : -1;

            switch (definition?.Domain.Kind)
            {
                case ValueKind.Integer or ValueKind.Decimal:
                {
                    if (Number(a) is { } na && Number(b) is { } nb) return na.CompareTo(nb);
                    break;
                }

                case ValueKind.Date:
                {
                    if (Date(a) is { } da && Date(b) is { } db) return da.CompareTo(db);
                    break;
                }

                case null:
                    // Служебната колона „№“ е винаги цяло число.
                    if (Number(a) is { } ia && Number(b) is { } ib) return ia.CompareTo(ib);
                    break;
            }

            return string.Compare(a, b, StringComparison.CurrentCulture);
        }

        private static decimal? Number(string text) =>
            decimal.TryParse(text.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        private static DateTime? Date(string text) =>
            ValueConverter.TryNormalizeDate(text, out var normalized) &&
            DateTime.TryParseExact(normalized, ValueConverter.BulgarianDateFormat,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var value)
                ? value
                : null;
    }

    /// <summary>
    /// Попълва колоната „№“ в текущата подредба.
    ///
    /// Вика се при зареждане, добавяне и махане на редове, но НЕ след
    /// подреждане: там номерът трябва да пътува с реда си, иначе подредбата
    /// по „№“ не би могла да върне първоначалния ред.
    /// </summary>
    private void RenumberIndex()
    {
        var number = 1;

        foreach (DataGridViewRow row in _grid.Rows)
            if (!row.IsNewRow) row.Cells[IndexColumn].Value = number++;
    }

    private void RenumberRowHeaders()
    {
        foreach (DataGridViewRow row in _grid.Rows)
            row.HeaderCell.Value = row.IsNewRow ? "＋" : (row.Index + 1).ToString();
    }

    private void ShowHint(int columnIndex)
    {
        if (columnIndex < 0 || _grid.Columns[columnIndex].Tag is not ColumnDef column) return;

        _hint.Text = Describe(column).Replace("\n", "   •   ");
    }

    private void MarkDirty()
    {
        _dirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------- клавиши

    /// <summary>
    /// Подсказки за свободно поле — по името на колоната.
    ///
    /// Свободното поле си остава свободно: подсказката допълва, докато се
    /// пише, но не отхвърля нищо. Празно значи „няма подсказки за тази
    /// колона“ — тогава полето е обикновено поле.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string, IReadOnlyList<string>>? Suggest { get; set; }

    private void OnEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is TextBox box)
        {
            // Полето за писане е едно за всички колони с текст: подсказките
            // на едната не бива да останат и в съседната.
            var name = _grid.CurrentCell?.OwningColumn?.Name ?? string.Empty;
            var hints = Suggest?.Invoke(name) ?? [];

            if (hints.Count == 0)
            {
                box.AutoCompleteMode = AutoCompleteMode.None;
                return;
            }

            var source = new AutoCompleteStringCollection();
            source.AddRange([.. hints]);

            box.AutoCompleteCustomSource = source;
            box.AutoCompleteSource = AutoCompleteSource.CustomSource;
            box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;

            return;
        }

        if (e.Control is not ComboBox combo) return;

        // Списъкът е затворен нарочно.
        //
        // При отворен за писане списък всичко, което не съвпада точно с някоя
        // стойност, се отхвърля при излизане от клетката — и изборът просто
        // изчезва пред очите на човека, без съобщение. Затворен списък не
        // позволява да се стигне дотам.
        //
        // Търсенето не се губи: набирането на първите знаци подскача до
        // стойността, а F4 отваря търсачката с целия списък.
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.IntegralHeight = false;
        combo.MaxDropDownItems = 14;
    }

    /// <summary>
    /// Стойност извън списъка не бива да срива работата.
    ///
    /// Такава се появява при поставяне от Excel с чужд правопис. Мълчаливото
    /// изчистване е по-лошо от видимата грешка, затова стойността се записва
    /// като текст и клетката се оцветява при проверката.
    /// </summary>
    private void OnDataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        e.Cancel = false;

        if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            _hint.Text = Lang.T("Стойността не е от списъка. Изберете от менюто или натиснете F4.", "That value is not on the list. Pick from the menu or press F4.");
    }

    /// <summary>
    /// Съчетанията се прихващат тук, а не в KeyDown.
    ///
    /// Мрежата на Windows Forms и редакторът вътре в клетката поглъщат част от
    /// клавишите, преди KeyDown изобщо да се вдигне — Ctrl+V е точно такъв
    /// случай. ProcessCmdKey минава преди тях.
    ///
    /// Докато в клетката се пише, съчетанията се пускат надолу: там Ctrl+V
    /// трябва да значи „постави в тази клетка“, а не „постави област“.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        var editing = _grid.IsCurrentCellInEditMode;

        switch (keyData)
        {
            case Keys.Control | Keys.V when !editing:
                PasteBlock();
                return true;

            case Keys.Control | Keys.D:
                FillDown(acrossSelection: false);
                return true;

            case Keys.Control | Keys.Shift | Keys.D:
                FillDown(acrossSelection: true);
                return true;

            case Keys.Control | Keys.Enter when !editing:
                DuplicateRow();
                return true;

            case Keys.Control | Keys.Shift | Keys.N:
                NumberColumn();
                return true;

            case Keys.Delete when !editing:
                ClearSelection();
                return true;

            // Delete изчиства клетките, Ctrl+Delete маха самия ред. Ctrl+„-“ е
            // същото действие: така се трие ред в Excel и пръстите го помнят.
            case Keys.Control | Keys.Delete when !editing:
            case Keys.Control | Keys.OemMinus when !editing:
                DeleteSelectedRows();
                return true;

            case Keys.F4:
                OpenPicker(_grid.CurrentCell?.RowIndex ?? -1, _grid.CurrentCell?.ColumnIndex ?? -1);
                return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    /// <summary>
    /// Ctrl+D повтаря стойността от реда отгоре; с Shift — стойността от първия
    /// избран ред надолу по цялата селекция. Това е най-често повтаряното
    /// действие при попълване на отчет.
    /// </summary>
    private void FillDown(bool acrossSelection)
    {
        var cells = _grid.SelectedCells.Cast<DataGridViewCell>()
            .Where(c => !_grid.Rows[c.RowIndex].IsNewRow &&
                        (ColumnAt(c.ColumnIndex) is not null || Writable(c.ColumnIndex)))
            .OrderBy(c => c.RowIndex).ThenBy(c => c.ColumnIndex)
            .ToList();

        if (cells.Count == 0) return;

        foreach (var group in cells.GroupBy(c => c.ColumnIndex))
        {
            var ordered = group.OrderBy(c => c.RowIndex).ToList();

            var source = acrossSelection || ordered.Count > 1
                ? ordered[0].Value
                : ordered[0].RowIndex > 0 ? _grid.Rows[ordered[0].RowIndex - 1].Cells[group.Key].Value : null;

            var start = acrossSelection || ordered.Count > 1 ? 1 : 0;

            for (var i = start; i < ordered.Count; i++)
            {
                ordered[i].Value = source;
                Validate(ordered[i].RowIndex, ordered[i].ColumnIndex);
            }
        }

        MarkDirty();
    }

    /// <summary>Прави копие на текущия ред веднага под него.</summary>
    private void DuplicateRow()
    {
        var current = _grid.CurrentCell?.RowIndex ?? -1;
        if (current < 0 || _grid.Rows[current].IsNewRow) return;

        _grid.Rows.Insert(current + 1, 1);

        foreach (DataGridViewColumn column in _grid.Columns)
            _grid.Rows[current + 1].Cells[column.Index].Value = _grid.Rows[current].Cells[column.Index].Value;

        RenumberRowHeaders();
        ValidateRow(current + 1);
        _grid.CurrentCell = _grid.Rows[current + 1].Cells[_grid.CurrentCell!.ColumnIndex];
        MarkDirty();
    }

    private void ClearSelection()
    {
        foreach (DataGridViewCell cell in _grid.SelectedCells)
        {
            if (_grid.Rows[cell.RowIndex].IsNewRow) continue;
            if (ColumnAt(cell.ColumnIndex) is null && !Writable(cell.ColumnIndex)) continue;

            cell.Value = null;
            Validate(cell.RowIndex, cell.ColumnIndex);
        }

        MarkDirty();
    }

    /// <summary>
    /// Поставя област от Excel: редовете са разделени с нов ред, клетките с
    /// табулация. Липсващите редове се създават, за да не се налага първо да
    /// се броят.
    /// </summary>
    private void PasteBlock()
    {
        var text = ReadClipboard();
        if (string.IsNullOrEmpty(text)) return;

        PasteText(text);
    }

    private void PasteText(string text)
    {
        var start = _grid.CurrentCell;
        if (start is null) return;

        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        // Една стойност върху много избрани клетки означава „сложи я на
        // всички“. Така се попълва цяла колона с един и същ получател или вид
        // дейност — точно каквото прави и Excel, и каквото се очаква, когато
        // стойността идва от падащ списък.
        if (lines.Length == 1 && !lines[0].Contains('\t') && _grid.SelectedCells.Count > 1)
        {
            FillSelection(lines[0]);
            return;
        }

        for (var r = 0; r < lines.Length; r++)
        {
            var rowIndex = start.RowIndex + r;

            while (rowIndex >= _grid.Rows.Count - 1) _grid.Rows.Add();

            var cells = lines[r].Split('\t');

            for (var c = 0; c < cells.Length; c++)
            {
                var columnIndex = start.ColumnIndex + c;
                if (columnIndex >= _grid.Columns.Count) break;

                if (ColumnAt(columnIndex) is not { } column)
                {
                    if (Writable(columnIndex)) _grid.Rows[rowIndex].Cells[columnIndex].Value = cells[c].Trim();

                    continue;
                }

                if (IsLocked(rowIndex, columnIndex)) { RefuseEdit(); continue; }

                _grid.Rows[rowIndex].Cells[columnIndex].Value = Display(column, Normalize(column, cells[c]));
                Validate(rowIndex, columnIndex);
            }
        }

        RenumberRowHeaders();
        MarkDirty();
    }

    /// <summary>
    /// Чете клипборда, без да сваля програмата.
    ///
    /// Клипбордът е обща стойност за целия Windows и докато друга програма го
    /// държи отворен, всяко четене хвърля. Случва се на съвсем обикновен
    /// потребител: натиска Ctrl+V точно докато Excel още пише в него.
    ///
    /// Затова се опитва няколко пъти и после се отказва тихо — със съобщение
    /// в лентата отдолу, а не с прозорец за неочаквана грешка.
    /// </summary>
    private string? ReadClipboard()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(60);
            }
        }

        _hint.Text = Lang.T("Клипбордът е зает от друга програма. Опитайте пак след миг.", "The clipboard is busy in another program. Try again in a moment.");
        return null;
    }

    /// <summary>
    /// Слага една и съща стойност във всички избрани клетки.
    ///
    /// Стойността се тълкува спрямо колоната, в която влиза: в колона с
    /// падащ списък се записва показваният вид, за да я приеме клетката, а в
    /// колона за дата се привежда към дд.мм.гггг. Затова една и съща
    /// поставена дума може да изглежда различно в две различни колони — и
    /// това е правилното поведение.
    /// </summary>
    private void FillSelection(string value)
    {
        foreach (DataGridViewCell cell in _grid.SelectedCells)
        {
            if (_grid.Rows[cell.RowIndex].IsNewRow) continue;

            if (_grid.Columns[cell.ColumnIndex].Tag is not ColumnDef column)
            {
                if (Writable(cell.ColumnIndex)) cell.Value = value.Trim();

                continue;
            }

            if (IsLocked(cell.RowIndex, cell.ColumnIndex)) { RefuseEdit(); continue; }

            cell.Value = Display(column, Normalize(column, value));
            Validate(cell.RowIndex, cell.ColumnIndex);
        }

        MarkDirty();
    }

    /// <summary>
    /// Превръща реда „нов запис“ в истински ред и връща новия му номер.
    ///
    /// Windows Forms прави това само когато човек започне да пише в клетка.
    /// Стойност, сложена отвън — от търсачката или от пренасянето — не минава
    /// през редактора и затова редът трябва да се добави тук.
    /// </summary>
    internal int Materialize(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count || !_grid.Rows[rowIndex].IsNewRow)
            return rowIndex;

        // Както в AddRows: вмъква се ПРЕД реда „нов запис“, за да остане той
        // най-долу — Rows.Add() на места се държи иначе.
        _grid.Rows.Insert(rowIndex, 1);

        RenumberIndex();
        RenumberRowHeaders();

        return rowIndex;
    }

    private void ValidateRow(int rowIndex)
    {
        for (var c = 0; c < _grid.Columns.Count; c++) Validate(rowIndex, c);
    }

    /// <summary>
    /// Търсачка за дългите списъци — кодовете по ЕКО са над 800 и падащо меню
    /// с толкова стойности е по-бавно от това да се напише част от името.
    /// </summary>
    private void OpenPicker(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0) return;
        if (_grid.Columns[columnIndex].Tag is not ColumnDef column) return;

        // Търсачката важи за всички изброени типове, не само за дългите:
        // и при трийсет стойности е по-лесно да се напише дума, отколкото да
        // се превърта списък.
        if (!column.Domain.IsEnum) return;

        var current = Stored(column, _grid.Rows[rowIndex].Cells[columnIndex].Value?.ToString());

        using var dialog = new OptionPicker(ReportTitles.Column(column), Offer(column, Snapshot()), current);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Selected is null) return;

        // Ред „нов запис“ не е ред, докато нещо не влезе в него. Досега
        // търсачката просто не се отваряше върху него — и на празен отчет
        // първата клетка, която човек натиска, не отговаряше на нищо, докато
        // не попълни друга колона на ръка.
        if (_grid.Rows[rowIndex].IsNewRow) rowIndex = Materialize(rowIndex);

        // Клетката с падащ списък приема само стойност от своя списък, затова
        // тук влиза показваният вид, а не голият код.
        _grid.Rows[rowIndex].Cells[columnIndex].Value = Display(column, dialog.Selected.Value);
        Validate(rowIndex, columnIndex);
        MarkDirty();
    }
}

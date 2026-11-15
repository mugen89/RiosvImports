using RiosvImports.Core.Balance;
using RiosvImports.Core.Localization;
using RiosvImports.Data;

namespace RiosvImports.App.Shell;

/// <summary>
/// Панелът отдясно: какво да се вижда и как да изглежда.
///
/// Отделно от лентата отгоре, защото това са различни неща. Горе стои какво се
/// гледа — година, раздел, вид на числата; тук стои как се гледа. Второто се
/// нагласява веднъж и после не се пипа месеци наред, а на лаптоп всяка лента
/// отгоре е три реда по-малко таблица.
///
/// Затова панелът и се сгъва: щом настройките са направени, той става ивица от
/// трийсетина пиксела и връща ширината на данните. Изборът се помни в базата —
/// човек, който е скрил половината плочки, не бива да ги намери обратно утре.
/// </summary>
internal sealed class BalanceOptions : Panel
{
    /// <summary>Ширината, когато е отворен и когато е сгънат.</summary>
    // Мерени при 100 % — при 150 % отметките в панела иначе се режат.
    private static int OpenWidth => Theme.Scaled(300);
    private static int ShutWidth => Theme.Scaled(34);

    private readonly SettingsStore _settings;

    private readonly Panel _body = new();
    private readonly Label _title = new();
    private readonly Button _toggle = new();

    private readonly RadioButton _manual = new();
    private readonly RadioButton _previous = new();

    private readonly Dictionary<BalanceMark, CheckBox> _marks = [];
    private readonly CheckBox _exportMarks = new();

    private readonly Panel _highlights = new();
    private readonly Panel _openings = new();
    private readonly Panel _tilesBox = new();
    private readonly CheckedListBox _tiles = new();

    /// <summary>Пълненето вдига същите събития, които вдига и човекът.</summary>
    private bool _filling;

    public BalanceOptions(SettingsStore settings)
    {
        _settings = settings;

        Dock = DockStyle.Right;
        BackColor = Theme.Surface;
        Width = OpenWidth;

        BuildHeader();

        _body.Dock = DockStyle.Fill;
        _body.BackColor = Theme.Surface;
        _body.AutoScroll = true;
        _body.Padding = new Padding(Theme.Gap, 0, Theme.GapSmall, Theme.Gap);

        // Закачените отгоре се редят наопаки: последният добавен застава най-горе.
        _body.Controls.Add(BuildTiles());
        _body.Controls.Add(BuildHighlights());
        _body.Controls.Add(BuildOpenings());

        Controls.Add(_body);

        // Пълнещото се закачва последно, за да вземе остатъка ПОД заглавието.
        // Добавено след него, то се закачваше първо, заемаше всичко, а
        // заглавието лягаше върху горните му 40 пиксела — върху „Начални
        // количества към 1 януари“ и първия избор под него. При всеки размер.
        _body.BringToFront();

        Load();
    }

    /// <summary>Вдига се при всяка промяна, която мени показаното.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Иска се прегледът на въведените начални количества.
    ///
    /// Само това: шаблонът и внасянето се искат от самия преглед, откакто той
    /// съществува. Панелът не води до файл, а до списък.
    /// </summary>
    public event EventHandler? ManageRequested;

    // ------------------------------------------------------------- четимо

    /// <summary>Кои оцветявания са включени.</summary>
    public BalanceMark Marks =>
        _marks.Where(pair => pair.Value.Checked)
              .Aggregate(BalanceMark.None, (all, pair) => all | pair.Key);

    /// <summary>Дали оцветяването да влезе и в изнесения файл.</summary>
    public bool ExportMarks => _exportMarks.Checked;

    /// <summary>Дали началните количества да се вземат от края на предходната година.</summary>
    public bool FromPreviousYear => _previous.Checked;

    /// <summary>Ключовете на включените плочки.</summary>
    public IReadOnlySet<string> Tiles =>
        new HashSet<string>(
            _tiles.CheckedIndices.Cast<int>()
                .Where(i => i >= 0 && i < BalanceTiles.All.Count)
                .Select(i => BalanceTiles.Key(BalanceTiles.All[i])),
            StringComparer.Ordinal);

    // ------------------------------------------------------------- устройство

    private void BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Base.Height + 22,
            BackColor = Theme.Surface,
            Padding = new Padding(Theme.GapSmall, Theme.GapSmall, 0, 0)
        };

        _toggle.Dock = DockStyle.Right;
        _toggle.Width = ShutWidth - 6;
        _toggle.FlatStyle = FlatStyle.Flat;
        _toggle.FlatAppearance.BorderSize = 0;
        _toggle.BackColor = Theme.Surface;
        _toggle.ForeColor = Theme.InkMuted;
        _toggle.Font = Theme.Bold;
        _toggle.TabStop = false;
        _toggle.Click += (_, _) => Collapsed = !Collapsed;

        _title.Dock = DockStyle.Fill;
        _title.Text = Lang.T("Настройки на изгледа", "View settings");
        _title.Font = Theme.SmallBold;
        _title.ForeColor = Theme.InkFaint;
        _title.BackColor = Color.Transparent;
        _title.TextAlign = ContentAlignment.MiddleLeft;

        header.Controls.Add(_title);
        header.Controls.Add(_toggle);

        Controls.Add(header);
    }

    private Control BuildOpenings()
    {
        _openings.Dock = DockStyle.Top;
        _openings.BackColor = Theme.Surface;
        _openings.AutoSize = true;
        _openings.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var stack = Stack();

        // Заявеното от човека, който още няма въведени начални количества:
        // краят на миналата година е точно това, което трябва да стои в
        // началото на тази, и програмата вече го знае.
        //
        // Надписите нарочно НЕ споменават Excel. „От Excel“ в тази програма
        // значи друго — отчет, внесен от файл — и първият, който прочете
        // избора, го разбра точно така: като сито по произход на отчетите.
        // Тук става дума единствено за началното салдо към 1 януари.
        _previous.Text = Lang.T("Пренесени от предходната година", "Carried over from the previous year");
        _manual.Text = Lang.T("Въведени за годината", "Entered for the year");

        foreach (var radio in new[] { _manual, _previous })
        {
            radio.AutoSize = false;
            radio.Width = OpenWidth - Theme.Gap * 3;
            radio.Height = Theme.Base.Height + 10;
            radio.ForeColor = Theme.Ink;
            radio.BackColor = Color.Transparent;
            radio.CheckedChanged += (_, _) => Raise();

            stack.Controls.Add(radio);
        }

        stack.Controls.Add(Note(Lang.T(
            "Колко има на площадката към 1 януари. Пренесеното се смята от проверените отчети за миналата година; въведеното се попълва с шаблона отдолу.",
            "How much is on site as of 1 January. What is carried over is worked out from last year's checked reports; what is entered is filled in with the template below.")));

        // Едно копче вместо две: прозорецът зад него е и списъкът с въведеното,
        // и мястото, откъдето се тегли шаблон и се внася файл. Досега списъкът
        // го нямаше никъде — въведеното се виждаше само разбъркано между
        // движенията в баланса.
        stack.Controls.Add(Small(Lang.T("Начални количества…", "Opening quantities…"),
            () => ManageRequested?.Invoke(this, EventArgs.Empty)));

        _openings.Controls.Add(stack);
        _openings.Controls.Add(Section(Lang.T("Начални количества към 1 януари",
                                              "Opening quantities as of 1 January")));

        return _openings;
    }

    private Control BuildHighlights()
    {
        _highlights.Dock = DockStyle.Top;
        _highlights.BackColor = Theme.Surface;
        _highlights.AutoSize = true;
        _highlights.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var stack = Stack();

        (BalanceMark Mark, string Bg, string En)[] options =
        [
            (BalanceMark.Draft, "Клетки от незавършената книга", "Cells from the unfinished book"),
            (BalanceMark.Negative, "Отрицателни стойности", "Negative values"),
            (BalanceMark.Peak, "Най-голямото движение на реда", "The row's largest movement"),
            (BalanceMark.Heat, "Топлинна карта по количество", "Heat map by quantity"),
            (BalanceMark.Idle, "Редове без движение", "Rows with no movement")
        ];

        foreach (var (mark, bg, en) in options)
        {
            var box = new CheckBox
            {
                Text = Lang.T(bg, en),
                AutoSize = false,
                Width = OpenWidth - Theme.Gap * 3,
                Height = Theme.Base.Height + 10,
                ForeColor = Theme.Ink,
                BackColor = Color.Transparent
            };

            box.CheckedChanged += (_, _) => Raise();

            _marks[mark] = box;
            stack.Controls.Add(box);
        }

        _exportMarks.Text = Lang.T("Пренеси оцветяването в Excel", "Carry the colouring into Excel");
        _exportMarks.AutoSize = false;
        _exportMarks.Width = OpenWidth - Theme.Gap * 3;
        _exportMarks.Height = Theme.Base.Height * 2 + 6;
        _exportMarks.ForeColor = Theme.Ink;
        _exportMarks.BackColor = Color.Transparent;
        _exportMarks.CheckedChanged += (_, _) => Raise();

        stack.Controls.Add(Note(Lang.T(
            "Оцветяването е само за гледане — числата не се менят от него.",
            "The colouring is for reading only — it changes no numbers.")));

        stack.Controls.Add(_exportMarks);

        _highlights.Controls.Add(stack);
        _highlights.Controls.Add(Section(Lang.T("Оцветяване", "Colouring")));

        return _highlights;
    }

    private Control BuildTiles()
    {
        _tilesBox.Dock = DockStyle.Top;
        _tilesBox.BackColor = Theme.Surface;
        _tilesBox.Visible = false;
        _tilesBox.Height = Theme.Base.Height * 13;

        _tiles.Dock = DockStyle.Fill;
        _tiles.Font = Theme.Base;
        _tiles.BorderStyle = BorderStyle.FixedSingle;
        _tiles.CheckOnClick = true;
        _tiles.IntegralHeight = false;
        Theme.StyleInput(_tiles);

        foreach (var tile in BalanceTiles.All) _tiles.Items.Add(BalanceTiles.Title(tile), true);

        // ItemCheck идва ПРЕДИ отметката да е сменена, а при пълнене прозорец
        // още няма — виж същата бележка в списъка с фирми.
        _tiles.ItemCheck += (_, _) =>
        {
            if (_filling || !IsHandleCreated) return;

            BeginInvoke(Raise);
        };

        _tilesBox.Controls.Add(_tiles);
        _tilesBox.Controls.Add(Section(Lang.T("Плочки на таблото", "Dashboard tiles")));

        return _tilesBox;
    }

    private static FlowLayoutPanel Stack() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        BackColor = Theme.Surface,
        Margin = new Padding(0),
        Padding = new Padding(0, 0, 0, Theme.GapSmall)
    };

    private static Label Section(string text) => new()
    {
        Dock = DockStyle.Top,
        Height = Theme.SmallBold.Height + 16,
        Text = text,
        Font = Theme.SmallBold,
        ForeColor = Theme.Accent,
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.BottomLeft
    };

    private static Label Note(string text) => new()
    {
        AutoSize = false,
        Width = OpenWidth - Theme.Gap * 3,
        Height = Theme.Small.Height * 4 + 8,
        Text = text,
        Font = Theme.Small,
        ForeColor = Theme.InkFaint,
        BackColor = Color.Transparent
    };

    private static Control Small(string text, Action handler)
    {
        var button = Theme.GhostButton(text, Theme.Glyph.Excel);
        button.Height = Theme.Base.Height + 16;
        button.Width = OpenWidth - Theme.Gap * 3;
        button.Margin = new Padding(0, 2, 0, 0);
        button.Click += (_, _) => handler();

        return button;
    }

    // ------------------------------------------------------------- поведение

    /// <summary>Кой раздел обслужва панелът: таблицата или таблото.</summary>
    public void ShowFor(bool dashboard)
    {
        _highlights.Visible = !dashboard;
        _tilesBox.Visible = dashboard;
    }

    /// <summary>Сгънат ли е панелът.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Collapsed
    {
        get => Width == ShutWidth;
        set
        {
            Width = value ? ShutWidth : OpenWidth;

            _body.Visible = !value;
            _title.Visible = !value;
            _toggle.Text = value ? "‹" : "›";

            if (!_filling) _settings.Set(SettingsStore.Keys.BalancePanel, value);
        }
    }

    private void Raise()
    {
        if (_filling || IsDisposed) return;

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------- памет

    /// <summary>
    /// Чете запомненото. Настройка, която липсва, взема стойността, с която
    /// екранът е най-полезен при първо отваряне — не най-празната.
    /// </summary>
    private void Load()
    {
        _filling = true;

        try
        {
            var marks = (BalanceMark)Number(SettingsStore.Keys.BalanceMarks,
                (int)(BalanceMark.Draft | BalanceMark.Negative));

            foreach (var (mark, box) in _marks) box.Checked = (marks & mark) != 0;

            _exportMarks.Checked = _settings.GetBool(SettingsStore.Keys.BalanceExportMarks, true);

            var previous = _settings.Get(SettingsStore.Keys.BalanceOpening) == "previous";
            _previous.Checked = previous;
            _manual.Checked = !previous;

            var saved = _settings.Get(SettingsStore.Keys.BalanceTiles);

            if (saved is not null)
            {
                var keys = new HashSet<string>(
                    saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.Ordinal);

                for (var i = 0; i < BalanceTiles.All.Count; i++)
                    _tiles.SetItemChecked(i, keys.Contains(BalanceTiles.Key(BalanceTiles.All[i])));
            }

            Collapsed = _settings.GetBool(SettingsStore.Keys.BalancePanel, false);
        }
        finally
        {
            _filling = false;
        }
    }

    private void Save()
    {
        _settings.Set(SettingsStore.Keys.BalanceMarks, ((int)Marks).ToString());
        _settings.Set(SettingsStore.Keys.BalanceExportMarks, _exportMarks.Checked);
        _settings.Set(SettingsStore.Keys.BalanceOpening, _previous.Checked ? "previous" : "manual");
        _settings.Set(SettingsStore.Keys.BalanceTiles, string.Join(",", Tiles));
    }

    private int Number(string key, int fallback) =>
        int.TryParse(_settings.Get(key), out var value) ? value : fallback;

    // ------------------------------------------------------------- тестове

    /// <summary>Включва или изключва оцветяване — ползва се от тестовете.</summary>
    internal void UseMark(BalanceMark mark, bool on)
    {
        if (_marks.TryGetValue(mark, out var box)) box.Checked = on;
    }

    /// <summary>Включва или изключва плочка — ползва се от тестовете.</summary>
    internal void UseTile(BalanceTile tile, bool on)
    {
        var index = BalanceTiles.All.ToList().IndexOf(tile);
        if (index < 0) return;

        _tiles.SetItemChecked(index, on);
        Raise();
    }

    /// <summary>Откъде да идват началните количества — ползва се от тестовете.</summary>
    internal void UsePreviousYear(bool on)
    {
        _previous.Checked = on;
        _manual.Checked = !on;
    }

    /// <summary>Дали оцветяването да влиза в изнесеното — ползва се от тестовете.</summary>
    internal void UseExportMarks(bool on) => _exportMarks.Checked = on;
}

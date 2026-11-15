using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace RiosvImports.App;

/// <summary>
/// Общият вид на програмата — цветове, шрифтове, отстояния.
///
/// Windows Forms няма понятие за тема, затова тук стоят стойностите, а в
/// <see cref="Controls.AppButton"/> и съседите му — рисуването. Правилото е
/// едно: никъде другаде в програмата не се пише цвят или размер на шрифт.
/// Ако някъде трябва нов нюанс, той се появява първо тук.
///
/// Изборът е сдържан нарочно. Отчет към държавна система се попълва под
/// напрежение и срещу срок; тук се цени спокойна подредба и ясна йерархия, а
/// не украса.
/// </summary>
internal static class Theme
{
    // ------------------------------------------------------------- цветове

    /// <summary>Наборът, който важи в момента.</summary>
    public static Palette Current { get; private set; } = Palette.Light;

    /// <summary>Вдига се при смяна на темата, за да се пресъздадат екраните.</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Сменя темата. Нарисуваните на ръка контроли четат цветовете при всяко
    /// рисуване и се обновяват сами; тези, на които цветът е зададен веднъж
    /// при сглобяване, се пресъздават от <see cref="Changed"/>.
    /// </summary>
    public static void Use(string? key)
    {
        var next = Palette.ByKey(key);
        if (ReferenceEquals(next, Current)) return;

        Current = next;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Color Accent => Current.Accent;
    public static Color AccentHover => Current.AccentHover;
    public static Color AccentPressed => Current.AccentPressed;
    public static Color AccentLight => Current.AccentLight;
    public static Color AccentSoft => Current.AccentSoft;

    public static Color Ink => Current.Ink;
    public static Color InkStrong => Current.InkStrong;
    public static Color InkMuted => Current.InkMuted;
    public static Color InkFaint => Current.InkFaint;

    public static Color Border => Current.Border;
    public static Color BorderSoft => Current.BorderSoft;
    public static Color Surface => Current.Surface;
    public static Color SurfaceAlt => Current.SurfaceAlt;
    public static Color Canvas => Current.Canvas;

    public static Color Ok => Current.Ok;
    public static Color OkSoft => Current.OkSoft;
    public static Color Warn => Current.Warn;
    public static Color WarnSoft => Current.WarnSoft;
    public static Color Error => Current.Error;
    public static Color ErrorSoft => Current.ErrorSoft;

    public static Color DropIdle => Current.SurfaceAlt;
    public static Color DropActive => Current.AccentLight;

    /// <summary>
    /// Цветът на текста ВЪРХУ плътния основен цвят.
    ///
    /// В светлите набори основният цвят е тъмен и бялото върху него се чете.
    /// В тъмния той е светло синьо — там бялото дава едва два и половина към
    /// едно и надписът се разтваря във фона. Затова всеки набор го казва сам,
    /// вместо да се приема, че върху основния цвят винаги е бяло.
    /// </summary>
    public static Color OnAccent => Current.OnAccent;

    /// <summary>
    /// Цветът на поредната редица в диаграма.
    ///
    /// Шест на брой: седмият сектор в кръг вече не се различава от съседа си,
    /// а легендата под него изяжда плочката. Метод, а не готов списък —
    /// цветовете идват от темата и се четат при рисуване.
    /// </summary>
    public static Color Series(int index)
    {
        Color[] wheel = [Accent, Ok, Warn, Error, AccentHover, InkMuted];

        return wheel[((index % wheel.Length) + wheel.Length) % wheel.Length];
    }

    // ------------------------------------------------------------- отстояния

    public const int Radius = 8;
    public const int RadiusSmall = 6;
    public const int GapSmall = 8;
    public const int Gap = 16;
    public const int GapLarge = 28;
    public const int PageMargin = 36;

    /// <summary>Най-широката колона за четене. По-широко от това уморява.</summary>
    public const int ContentWidth = 980;

    /// <summary>
    /// Колко пъти е по-едър този екран от мащаб 100 %.
    ///
    /// Шрифтовете са в ТОЧКИ и растат сами при 125 % и 150 %; кръглите числа в
    /// кода са в ПИКСЕЛИ и не растат. Оттам идва разпадналият се екран на
    /// лаптоп: буквите стават един път и половина по-високи, кутиите остават
    /// същите и всичко се реже едновременно.
    ///
    /// Мери се по същия шрифт при 96 точки на инч — това е „сто процента“ — и
    /// по него при сегашния. Редакторът се научи на това отдавна
    /// (`Font.GetHeight(DeviceDpi)`); прозорчетата на склада са писани после и
    /// повториха грешката.
    ///
    /// <see cref="Emulated"/> се умножава отгоре: при подражаван мащаб буквите
    /// са вече едри при 96 точки и отношението по-горе излиза едно.
    /// </summary>
    public static double Zoom => Emulated * Base.Height / Math.Ceiling(Base.GetHeight(96f));

    /// <summary>
    /// Мащаб „на ужким“ — лаптопът на голям монитор.
    ///
    /// Програмата се пише на монитор при 100 %, а се проверява на лаптоп при
    /// 125 % и 150 %, и разминаването между двете се вижда чак на снимка от
    /// проверяващия — защото на монитора при 100 % то просто не съществува.
    /// „--zoom 1.5“ (или променливата RIOSV_ZOOM) уголемява шрифтовете и
    /// <see cref="Zoom"/> точно толкова, колкото би ги уголемил Windows. Каквото
    /// се реже на лаптопа, се реже и тук, на голям монитор.
    ///
    /// Чете се веднъж, преди първия шрифт: шрифтовете се правят при първото
    /// пипане на темата и после не се сменят. Извън 1–3 значи „без подражаване“.
    /// </summary>
    public static float Emulated { get; } = ReadEmulated();

    /// <summary>Името на променливата, през която се подава подражаваният мащаб.</summary>
    public const string ZoomVariable = "RIOSV_ZOOM";

    private static float ReadEmulated()
    {
        var text = Environment.GetEnvironmentVariable(ZoomVariable)?.Trim().Replace(',', '.').TrimEnd('%');

        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var zoom)) return 1f;

        // „150“ и „1.5“ значат едно и също — хората пишат мащаба, както го
        // виждат в настройките на Windows.
        if (zoom >= 50) zoom /= 100;

        return zoom is >= 1 and <= 3 ? (float)zoom : 1f;
    }

    /// <summary>
    /// Размер, мерен при 100 %, преведен на мащаба на този екран.
    ///
    /// Ползва се за онова, което НЕ е текст: височина на карта, ширина на
    /// колона, най-малка рамка. За самия текст няма нужда — той се мери по
    /// шрифта си.
    /// </summary>
    public static int Scaled(int pixels) => (int)Math.Round(pixels * Zoom);

    /// <summary>
    /// Как се дели работното прозорче: една трета за избора, две трети за
    /// работата.
    ///
    /// Вляво стоят кодовете — списък, който се ЧЕТЕ и от който се избира.
    /// Вдясно се работи: партидите, количествата, датата, кантарът. Дотук
    /// делението беше почти по средата и по-голямата половина отиваше при
    /// списъка, който я не искаше.
    ///
    /// Разделителят си остава подвижен: това е откъде ЗАПОЧВА, а не таван.
    /// </summary>
    public const double WorkSplit = 1.0 / 3.0;

    // ------------------------------------------------------------- шрифтове

    /// <summary>
    /// Windows 11 носи „Segoe UI Variable“ — по-добре нарисуван при едри
    /// размери. На Windows 10 го няма и се пада обратно към „Segoe UI“.
    /// </summary>
    private static readonly string DisplayFamily =
        FirstAvailable("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI");

    private static readonly string TextFamily =
        FirstAvailable("Segoe UI Variable Text", "Segoe UI");

    private static readonly string MonoFamily =
        FirstAvailable("Cascadia Mono", "Consolas", "Courier New");

    // Размерите минават през Emulated: без подражаване той е едно и нищо не
    // се мени; с него буквите са толкова едри, колкото на лаптопа.
    public static Font Display { get; } = new(DisplayFamily, 21f * Emulated, FontStyle.Bold);
    public static Font Title { get; } = new(DisplayFamily, 15.5f * Emulated, FontStyle.Bold);
    public static Font Heading { get; } = new(DisplayFamily, 12.5f * Emulated, FontStyle.Bold);
    public static Font Big { get; } = new(TextFamily, 11f * Emulated);
    public static Font Base { get; } = new(TextFamily, 10f * Emulated);
    public static Font Bold { get; } = new(TextFamily, 10f * Emulated, FontStyle.Bold);
    public static Font Small { get; } = new(TextFamily, 8.75f * Emulated);
    public static Font SmallBold { get; } = new(TextFamily, 8.75f * Emulated, FontStyle.Bold);
    public static Font Mono { get; } = new(MonoFamily, 9.5f * Emulated);

    /// <summary>Иконите на Windows. Правят се на шрифт, затова се и оцветяват като текст.</summary>
    public static Font Icon { get; } = new(
        FirstAvailable("Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol"), 13f * Emulated);

    public static Font IconLarge { get; } = new(Icon.FontFamily, 17f * Emulated);

    /// <summary>Кодовете на иконите съвпадат в „Fluent Icons“ и в „MDL2 Assets“.</summary>
    public static class Glyph
    {
        public const string Keyboard = "";
        public const string Filter = "\uE71C";

        /// <summary>Сметало — балансът е единственият екран, който смята сам.</summary>
        public const string Balance = "\uE8EF";

        /// <summary>Стрелка наляво — свиване на лентата. „Chevron“ сочи надясно.</summary>
        public const string ChevronBack = "\uE76B";

        /// <summary>Палитра — екранът „Изглед“. Дотогава той ползваше стрелка.</summary>
        public const string Palette = "\uE790";

        /// <summary>\u0414\u0432\u0435 \u0441\u0442\u0440\u0435\u043B\u043A\u0438 \u043D\u0430\u0432\u044A\u043D \u2014 \u0441\u043F\u0438\u0441\u044A\u043A\u044A\u0442 \u0441\u0435 \u043E\u0442\u0432\u0430\u0440\u044F \u043D\u0430 \u0446\u044F\u043B \u043F\u0440\u043E\u0437\u043E\u0440\u0435\u0446.</summary>
        public const string Expand = "\uE740";

        /// <summary>\u041A\u0440\u044A\u0441\u0442\u0447\u0435 \u2014 \u043C\u0430\u0445\u0430\u043D\u0435 \u043D\u0430 \u0438\u0437\u0432\u0435\u0441\u0442\u0438\u0435.</summary>
        public const string Close = "\uE711";

        /// <summary>\u0421\u0442\u0440\u0435\u043B\u043A\u0430 \u043D\u0430\u0434\u043E\u043B\u0443 \u2014 \u201E\u043E\u0449\u0435\u201C \u043F\u043E\u0434 \u0441\u043F\u0438\u0441\u044A\u043A\u0430.</summary>
        public const string ChevronDown = "\uE70D";

        /// <summary>\u0421\u0442\u0440\u0435\u043B\u043A\u0430 \u043A\u044A\u043C \u0447\u0435\u0440\u0442\u0430 \u2014 \u0441\u0432\u0430\u043B\u044F\u043D\u0435 \u043D\u0430 \u0438\u0437\u0434\u0430\u043D\u0438\u0435\u0442\u043E.</summary>
        public const string Download = "\uE896";

        /// <summary>\u041F\u0440\u043E\u0437\u043E\u0440\u0435\u0446 \u0441\u044A\u0441 \u0441\u0442\u0440\u0435\u043B\u043A\u0430 \u2014 \u043E\u0442\u0432\u0430\u0440\u044F \u0441\u0435 \u0432 \u0431\u0440\u0430\u0443\u0437\u044A\u0440\u0430.</summary>
        public const string External = "\uE8A7";

        /// <summary>\u0414\u0432\u0435 \u0441\u0442\u0440\u0435\u043B\u043A\u0438 \u0432 \u043A\u0440\u044A\u0433 \u2014 \u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0430 \u043D\u0430\u043D\u043E\u0432\u043E.</summary>
        public const string Sync = "\uE895";

        /// <summary>\u0421\u0442\u0440\u0435\u043B\u043A\u0430 \u0432 \u043A\u0440\u044A\u0433 \u2014 \u0441\u043B\u0430\u0433\u0430\u043D\u0435 \u043D\u0430 \u043D\u043E\u0432\u043E\u0442\u043E \u0438\u0437\u0434\u0430\u043D\u0438\u0435.</summary>
        public const string Upgrade = "\uE777";
        public const string Panel = "";
        public const string Home = "";
        public const string Chart = "";
        public const string Documents = "";
        public const string Settings = "";
        public const string Search = "";
        public const string Add = "";
        public const string Copy = "";
        public const string Rename = "";
        public const string Delete = "";
        public const string Excel = "";
        public const string Import = "";
        public const string Open = "";
        public const string Check = "";
        public const string Shield = "";
        public const string Warning = "";
        public const string Info = "";
        public const string Refresh = "";
        public const string Chevron = "";
        public const string Save = "";
        public const string Play = "";
    }

    private static string FirstAvailable(params string[] families)
    {
        using var installed = new InstalledFontCollection();

        var present = installed.Families
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return families.FirstOrDefault(present.Contains) ?? families[^1];
    }

    // ------------------------------------------------------------- рисуване

    /// <summary>
    /// Истинският цвят зад дадена кутия.
    ///
    /// Прозрачното не е цвят: подадено на Graphics.Clear, то се рисува черно.
    /// Затова се върви нагоре по родителите, докато се намери нещо плътно.
    /// </summary>
    public static Color BackdropOf(Control? control)
    {
        for (var parent = control?.Parent; parent is not null; parent = parent.Parent)
        {
            // Картата е прозрачна отвън, а отвътре е запълнена: нейният цвят е
            // Fill, не BackColor. Без това копче без кант върху цветна карта
            // рисуваше бял правоъгълник — цвета на страницата под картата.
            if (parent is Controls.Card { Fill.A: 255 } card) return card.Fill;

            if (parent.BackColor.A == 255) return parent.BackColor;
        }

        return Canvas;
    }

    /// <summary>
    /// Оцветява родна контрола на Windows според темата.
    ///
    /// Нарисуваните на ръка контроли четат цветовете при рисуване и се
    /// обновяват сами. Текстовите полета, отметките и календарите обаче са на
    /// Windows и си остават бели: върху тъмен фон текстът им става черно на
    /// черно. Затова всяко такова поле минава оттук.
    /// </summary>
    public static void StyleInput(Control control)
    {
        control.ForeColor = Ink;

        switch (control)
        {
            case TextBoxBase or NumericUpDown or ComboBox or ListBox:
                control.BackColor = Current.IsDark ? SurfaceAlt : Canvas;
                break;

            case CheckBox or RadioButton or Label:
                // Прозрачен фон, за да личи подложката отдолу; важи и цветът.
                control.BackColor = Color.Transparent;
                break;

            default:
                control.BackColor = Canvas;
                break;
        }
    }

    /// <summary>
    /// Пребоядисва наведнъж всичко, което никой не е оцветил нарочно.
    ///
    /// Досега всяка контрола се оцветяваше на място при сглобяване и все нещо
    /// оставаше пропуснато — при светла тема пропускът не личи, защото
    /// стойността по подразбиране на Windows е светла, и се вижда чак когато
    /// някой пусне тъмната.
    ///
    /// Правилото тук е просто: пипа се само това, което още стои с цвета по
    /// подразбиране на Windows. Оцветеното вече е оцветено нарочно и не се
    /// пипа — така една обиколка оправя пропуските, без да разваля решенията.
    ///
    /// Вика се веднъж, накрая на сглобяването на прозорец.
    /// </summary>
    public static void Apply(Control root)
    {
        // Присвояване на BackColor на TabControl е без ефект — виж Unthemed.
        if (root is not TabControl && IsDefault(root.BackColor))
            root.BackColor = BackgroundFor(root);
        if (IsDefault(root.ForeColor)) root.ForeColor = ForegroundFor(root);

        switch (root)
        {
            case DataGridView grid:
                grid.BackgroundColor = Canvas;
                grid.GridColor = Border;
                break;

        }

        // Панелите на разделителя и страниците на разделите вече са в
        // Controls — отделно обхождане би ги минало по два пъти.
        foreach (Control child in root.Controls) Apply(child);
    }

    /// <summary>
    /// Контролите, останали с цвят по подразбиране на Windows — ползва се от
    /// тестовете.
    ///
    /// При светла тема такъв пропуск не личи: подразбиращото се е светло и
    /// съвпада. Вижда се чак когато някой пусне тъмната — и тогава е късно.
    /// </summary>
    public static IEnumerable<string> Unthemed(Control root)
    {
        // Сверява се спрямо самия набор, а не спрямо стойностите по
        // подразбиране на Windows. При светла тема двете съвпадат — фонът е
        // бял и в двата случая — и проверка „различно ли е от подразбиращото
        // се“ не може да отличи забравена контрола от нарочно бяла.
        // TabControl е изключение по устройство: неговият BackColor винаги
        // връща системния цвят, а присвояването му не прави нищо. Затова
        // фонът му се рисува на ръка в TabStrip.OnPaintBackground.
        if (root is not TabControl && !FromPalette(root.BackColor))
            yield return $"{root.GetType().Name} „{Describe(root)}“ — фон извън набора ({root.BackColor.Name})";

        foreach (Control child in root.Controls)
            foreach (var found in Unthemed(child))
                yield return found;
    }

    private static bool FromPalette(Color colour)
    {
        if (colour.A == 0) return true;   // прозрачното е валиден избор

        Color[] palette =
        [
            Current.Canvas, Current.Surface, Current.SurfaceAlt,
            Current.AccentLight, Current.AccentSoft, Current.Accent,
            Current.OkSoft, Current.WarnSoft, Current.ErrorSoft,
            Current.Border, Current.BorderSoft
        ];

        return palette.Any(p => p.ToArgb() == colour.ToArgb());
    }

    private static string Describe(Control control) =>
        control.Text.Length > 0 ? control.Text[..Math.Min(30, control.Text.Length)] : control.Name;

    private static bool IsDefault(Color colour) =>
        colour == SystemColors.Control || colour == SystemColors.Window ||
        colour == SystemColors.ControlText || colour == SystemColors.WindowText ||
        colour == Control.DefaultBackColor || colour == Control.DefaultForeColor;

    private static Color BackgroundFor(Control control) => control switch
    {
        TextBoxBase or NumericUpDown or ComboBox or ListBox or ListView or DateTimePicker
            => Current.IsDark ? SurfaceAlt : Canvas,
        CheckBox or RadioButton or Label => Color.Transparent,
        _ => Canvas
    };

    private static Color ForegroundFor(Control control) => control switch
    {
        ListView or ListBox => Ink,
        _ => Ink
    };

    /// <summary>Правоъгълник със заоблени ъгли — основата на всичко нарисувано тук.</summary>
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>Плътен фон със заоблени ъгли и по желание тънък кант.</summary>
    public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color fill, Color? border = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);

        if (border is not { } line) return;

        using var pen = new Pen(line);
        g.DrawPath(pen, path);
    }

    // ------------------------------------------------------------- бутони

    /// <summary>Основното действие на екрана. На екран има точно едно такова.</summary>
    public static Controls.AppButton PrimaryButton(string text) =>
        new(text, Controls.ButtonVariant.Primary) { Height = Theme.Scaled(40) };

    /// <summary>Всичко останало.</summary>
    public static Controls.AppButton SecondaryButton(string text) =>
        new(text, Controls.ButtonVariant.Secondary) { Height = Theme.Scaled(36) };

    public static Controls.AppButton GhostButton(string text, string? glyph = null) =>
        new(text, Controls.ButtonVariant.Ghost) { Height = Theme.Scaled(36), Glyph = glyph };

    public static Controls.AppButton DangerButton(string text, string? glyph = null) =>
        new(text, Controls.ButtonVariant.Danger) { Height = Theme.Scaled(36), Glyph = glyph };
}

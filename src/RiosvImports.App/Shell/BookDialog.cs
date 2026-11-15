using RiosvImports.App.Controls;
using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.App.Shell;

/// <summary>
/// Какъв период да покрие съставеният отчет.
///
/// Изборът е между месеца, който се гледа, и тримесечието, в което той пада.
/// Двете съществуват, защото служат на различни неща: <b>тримесечието се
/// подава</b> — схемата иска <c>quarter</c> и НИСО приема на тримесечие, —
/// а <b>месецът се проверява</b>, преди да се затвори.
///
/// Затова по подразбиране е тримесечието: то е това, което накрая тръгва
/// нанякъде.
/// </summary>
internal sealed class BookDialog : Dialog
{
    private readonly SegmentedControl _scope = new();
    private readonly Label _preview = new();
    private readonly LedgerPeriod _period;
    private readonly Func<LedgerPeriod, LedgerPeriod, (int Rows, int Missing, decimal Waiting)> _count;

    public BookDialog(
        LedgerPeriod period,
        Func<LedgerPeriod, LedgerPeriod, (int Rows, int Missing, decimal Waiting)> count)
    {
        _period = period;
        _count = count;

        Text = Lang.T("Съставяне на отчет", "Compose a report");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(Theme.Scaled(560), Theme.Scaled(300));
        BackColor = Theme.Canvas;
        Font = Theme.Base;

        Controls.Add(BuildBody());
        Controls.Add(BuildButtons());
        Controls.Add(BuildTitle());

        _scope.SelectedIndex = 1;
        Describe();

        Theme.Apply(this);
    }

    /// <summary>Първият месец от избрания период.</summary>
    public LedgerPeriod From => _scope.SelectedIndex == 1 ? Quarter().From : _period;

    /// <summary>Последният месец от избрания период.</summary>
    public LedgerPeriod To => _scope.SelectedIndex == 1 ? Quarter().To : _period;

    private (LedgerPeriod From, LedgerPeriod To) Quarter()
    {
        var first = (_period.Quarter - 1) * 3 + 1;

        return (new LedgerPeriod(_period.Year, first), new LedgerPeriod(_period.Year, first + 2));
    }

    private Control BuildTitle()
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(64),
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.GapLarge, Theme.Gap, Theme.GapLarge, 0)
        };

        host.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = Lang.T("Отчетна книга за оползотворяване", "Recovery and disposal book"),
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
            Padding = new Padding(Theme.GapLarge, 0, Theme.GapLarge, Theme.Gap)
        };

        _scope.SetItems([_period.Title(), Lang.T("Тримесечие", "Quarter")]);
        _scope.Dock = DockStyle.Top;
        _scope.Height = Theme.Scaled(40);
        _scope.SelectedIndexChanged += (_, _) => Describe();

        _preview.Dock = DockStyle.Fill;
        _preview.Font = Theme.Base;
        _preview.ForeColor = Theme.InkMuted;
        _preview.Padding = new Padding(0, Theme.Gap, 0, 0);

        host.Controls.Add(_preview);
        host.Controls.Add(_scope);

        return host;
    }

    private Control BuildButtons()
    {
        // Височината се смята от копчето, а не се избира: 40 пиксела копче в
        // лента от 68 с по 16 отстояние горе и долу остава 4 пиксела извън нея —
        // а лентата е долепена до долния ръб, тоест тези 4 пиксела са извън
        // прозореца и копчето изглежда липсващо.
        var buttonHeight = Theme.Scaled(40);

        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = buttonHeight + 2 * Theme.Gap,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.GapLarge, Theme.Gap, Theme.GapLarge, Theme.Gap)
        };

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Theme.Canvas
        };

        var create = Theme.PrimaryButton(Lang.T("Състави", "Compose"));
        create.Height = buttonHeight;
        create.Margin = Padding.Empty;
        create.Click += (_, _) => DialogResult = DialogResult.OK;

        var cancel = Theme.GhostButton(Lang.T("Отказ", "Cancel"));
        cancel.Height = buttonHeight;
        cancel.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        tools.Controls.Add(create.FitWidth());
        tools.Controls.Add(cancel.FitWidth());

        bar.Controls.Add(tools);
        AcceptButton = create;
        CancelButton = cancel;

        return bar;
    }

    /// <summary>
    /// Казва какво ще влезе — преди да е влязло.
    ///
    /// Броят редове е половината отговор. Другата половина е колко от тях са
    /// без посочен произход: те ще минат проверката на НИСО, но няма да могат
    /// да отговорят на чий е бил отпадъкът, а точно затова се води склад.
    /// </summary>
    private void Describe()
    {
        var (rows, missing, waiting) = _count(From, To);

        var period = From == To
            ? From.Title()
            : Lang.T($"{From.Title()} — {To.Title()}", $"{From.Title()} — {To.Title()}");

        var text = Lang.T(
            $"Период: {period}{Environment.NewLine}" +
            $"Движения за подаване: {rows}",
            $"Period: {period}{Environment.NewLine}" +
            $"Movements to submit: {rows}");

        if (rows == 0)
            text += Environment.NewLine + Environment.NewLine + Lang.T(
                "През този период няма движения. Съставеният отчет ще е празен.",
                "There are no movements in this period. The composed report will be empty.");
        else if (missing > 0)
            text += Environment.NewLine + Environment.NewLine + Lang.T(
                $"{missing} от тях са без посочен произход. Отчетът ще мине, но за тези " +
                "количества складът няма да може да каже от кого са.",
                $"{missing} of them have no stated origin. The report will pass, but the store " +
                "will not be able to say who those quantities came from.");

        // Чакащото в склад „Третиран“ спира съставянето, а не само го отбелязва:
        // отчет, който твърди, че отпадъкът е минал през площадката, докато той
        // още стои на нея, е неверен отчет.
        if (waiting > 0)
            text += Environment.NewLine + Environment.NewLine + Lang.T(
                $"В склад „Третиран“ чакат {SplitMath.Amount(waiting)} т. Отчет не може да се " +
                "състави, докато не се обяви какво е образувано от тях.",
                $"{SplitMath.Amount(waiting)} t are waiting in the “Treated” store. No report can be " +
                "composed until it is stated what was generated from them.");

        _preview.Text = text;

        _preview.ForeColor = waiting > 0
            ? Theme.Error
            : missing > 0 && rows > 0 ? Theme.Warn : Theme.InkMuted;
    }

    /// <summary>Заглавието, което съставеният отчет получава.</summary>
    public string SuggestedTitle(string company) => Lang.T(
        $"Оползотворяване {Title()} — {company}",
        $"Recovery {Title()} — {company}");

    private string Title() => From == To
        ? $"{LedgerPeriod.Short(From.Month)} {From.Year}"
        : $"{From.Quarter}-во тримесечие {From.Year}";

    /// <summary>Периодът като дати — заглавната част на отчета иска тях.</summary>
    public string PeriodStart => From.First.ToString(ValueConverter.BulgarianDateFormat);

    public string PeriodEnd => To.Last.ToString(ValueConverter.BulgarianDateFormat);
}

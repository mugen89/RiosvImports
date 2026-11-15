using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.App.Controls;

/// <summary>
/// „Вземи отгоре“ — редът над партидите при предаването.
///
/// Човек подрежда таблицата, пресява нежеланото, пише в „Предадено общо“ колко
/// тона трябва да излязат и натиска копчето: взима се отгоре надолу, докато
/// сборът стигне, а последният ред се изравнява (<see cref="Handover.FromTop"/>).
/// Копчето само избира — поправките са на човека и стават в таблицата.
///
/// Свое поле за количество няма. Второ поле до кантара би било второ място за
/// едно и също число, а двете рано или късно показват различно. Колко да се
/// вземе, казва собственикът на реда — той знае кантара и кое вече е взето.
/// </summary>
internal sealed class FromTopBar : Panel
{
    private readonly Label _said = new();

    public FromTopBar()
    {
        Dock = DockStyle.Top;
        Height = Theme.Scaled(40);
        BackColor = Theme.Canvas;
        Padding = new Padding(0, 2, 0, Theme.GapSmall);

        var button = Theme.SecondaryButton(Lang.T("Вземи отгоре", "Take from top"));
        button.Dock = DockStyle.Left;
        button.Margin = Padding.Empty;
        button.Click += (_, _) => Requested?.Invoke();
        button.FitWidth();

        new ToolTip().SetToolTip(button, Lang.T(
            "Взима видимите партиди отгоре надолу, в реда на таблицата, докато се събере " +
            "„Предадено общо“; последната се взима само колкото липсва. Скритите от пресяването и " +
            "търсенето не се взимат.",
            "Takes the visible batches from the top down, in the table's order, until the " +
            "“Handed over in total” amount is reached; the last one is taken only as much as is " +
            "missing. Batches hidden by a filter or search are not taken."));

        _said.Dock = DockStyle.Fill;
        _said.Font = Theme.Small;
        _said.ForeColor = Theme.InkMuted;
        _said.TextAlign = ContentAlignment.MiddleLeft;
        _said.AutoEllipsis = true;
        _said.Padding = new Padding(Theme.GapSmall, 0, 0, 0);

        // Пълнещият влиза пръв.
        Controls.Add(_said);
        Controls.Add(button);
    }

    /// <summary>Натиснато е „Вземи отгоре“.</summary>
    public event Action? Requested;

    /// <summary>
    /// Какво ще направи копчето — преди да е натиснато. Същото като при
    /// „Изравни“: числото се казва, преди да се пипне нещо.
    /// </summary>
    public void Hint(string text)
    {
        _said.Text = text;
        _said.ForeColor = Theme.InkMuted;
    }

    /// <summary>Казва какво е взето — или защо не е стигнало.</summary>
    public void Say(string text, bool warn = false)
    {
        _said.Text = text;
        _said.ForeColor = warn ? Theme.Warn : Theme.Ok;
    }

    /// <summary>
    /// Изречението след взимането: колко реда, докъде, и изравнен ли е последният.
    /// </summary>
    public void Report(IReadOnlyList<TreatPick> picks, decimal wanted)
    {
        var got = SplitMath.Round(picks.Sum(p => p.Amount));

        if (picks.Count == 0)
        {
            Say(Lang.T("Няма видими партиди, от които да се вземе.", "No visible batches to take from."), warn: true);
            return;
        }

        if (got < wanted)
        {
            Say(Lang.T(
                $"Видимите партиди стигат само за {SplitMath.Amount(got)} т от {SplitMath.Amount(wanted)} т — взети са всичките {picks.Count}.",
                $"The visible batches only make {SplitMath.Amount(got)} t of {SplitMath.Amount(wanted)} t — all {picks.Count} are taken."),
                warn: true);
            return;
        }

        var last = picks[^1];

        var tail = last.Amount < last.Share.Amount
            ? Lang.T($" · последната, #{last.Share.Batch.Code}, изравнена на {SplitMath.Amount(last.Amount)} от {SplitMath.Amount(last.Share.Amount)} т",
                     $" · the last one, #{last.Share.Batch.Code}, balanced to {SplitMath.Amount(last.Amount)} of {SplitMath.Amount(last.Share.Amount)} t")
            : string.Empty;

        Say(Lang.T($"Взети {picks.Count} партиди · {SplitMath.Amount(got)} т{tail}.",
                   $"{picks.Count} batches taken · {SplitMath.Amount(got)} t{tail}."));
    }

    internal string SaidText => _said.Text;
}

using RiosvImports.Core.Ledger;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Values;

namespace RiosvImports.App.Controls;

/// <summary>
/// Търсенето над двете таблици в работните прозорчета — кодовете и партидите.
///
/// Едно поле за двете, защото въпросът е един: „къде е това“. Човек пише
/// „метал“ и иска кодовете, зад които стои Метал ООД, и само партидите на Метал
/// ООД зад тях; пише „1501“ и иска кода с партидите му. Затова всяка дума се
/// търси и в кода, и в партидата: код остава, щом поне една партида зад него
/// отговаря заедно с него, а партида — щом отговаря заедно с кода си.
///
/// Скритото от търсенето си остава в избора, както при пресяването по колона:
/// двете минават през същия <see cref="GridFilter"/> и щракване по видимото не
/// изважда невидимото.
/// </summary>
internal sealed class StockSearch
{
    public StockSearch()
    {
        Box = new SearchBox(Lang.T(
            "Търсете по код, клиент, партида или документ  (Ctrl+F)",
            "Search by code, client, batch or document  (Ctrl+F)"));
    }

    public SearchBox Box { get; }

    /// <summary>Търсенето е сменено — прозорчето пресява наново.</summary>
    public event Action? Changed;

    private string[] Words() =>
        Box.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>„търсене „метал““ за реда над таблицата, или празно.</summary>
    public string Note() =>
        Words().Length == 0 ? string.Empty : Lang.T($"търсене „{Box.Text.Trim()}“", $"search “{Box.Text.Trim()}”");

    /// <summary>
    /// Връзва търсенето към двете пресявания.
    ///
    /// Кодът на партидите се пита отвън: долу стоят партидите на ТЕКУЩИЯ код, а
    /// кой е той, знае само прозорчето.
    /// </summary>
    public void Attach(GridFilter piles, GridFilter shares, Func<TreatPile, string> describe, Func<TreatPile?> current)
    {
        piles.Also = row => row.Tag is not TreatPile pile || Pile(pile, describe(pile));
        piles.AlsoNote = Note;

        shares.Also = row => row.Tag is not StockShare share ||
            Share(share, current() is { } pile ? describe(pile) : string.Empty);
        shares.AlsoNote = Note;

        Box.TextChanged += (_, _) => Changed?.Invoke();
    }

    /// <summary>Същата проверка без таблица — ползва се от тестовете.</summary>
    internal bool Pile(TreatPile pile, string code)
    {
        var words = Words();
        if (words.Length == 0) return true;

        return words.All(w => Has(code, w)) ||
               pile.Shares.Any(s => words.All(w => Has(code, w) || Has(Text(s), w)));
    }

    internal bool Share(StockShare share, string code)
    {
        var words = Words();

        return words.Length == 0 || words.All(w => Has(code, w) || Has(Text(share), w));
    }

    /// <summary>Всичко, по което една партида се познава — както стои в таблицата.</summary>
    private static string Text(StockShare share) => string.Join(" ",
        "#" + share.Batch.Code,
        share.Batch.PartnerName,
        share.Batch.Eik,
        share.Batch.Document,
        share.Batch.ReceivedOn.ToString(ValueConverter.BulgarianDateFormat));

    private static bool Has(string text, string word) =>
        text.Contains(word, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Редът с полето — над двете таблици, през цялата ширина.
    ///
    /// Полето е с определена ширина, а не разтегнато: разтегнато на лаптоп то
    /// изглежда като заглавие, а не като място за писане.
    /// </summary>
    public Control Row()
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(34) + Theme.GapSmall,
            BackColor = Theme.Canvas,
            Padding = new Padding(0, 0, 0, Theme.GapSmall)
        };

        Box.Dock = DockStyle.Left;
        Box.Width = Theme.Scaled(460);
        Box.Height = Theme.Scaled(34);

        host.Controls.Add(Box);

        return host;
    }
}

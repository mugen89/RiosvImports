using RiosvImports.Core.Ledger;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Values;
using RiosvImports.Data;

namespace RiosvImports.App.Shell;

/// <summary>
/// Кратките списъци, както ги пита складът.
///
/// Прозорчетата на движенията питат едно и също на четири места: „кои стойности
/// да предложа тук“ и „остана ли една-единствена, която да попълня сама“.
/// Въпросът стои тук, а не по веднъж във всяко, защото иначе се разминава —
/// един от четирите остава без попълване и разликата после се обяснява с „при
/// третирането било друго“ (правило 12а).
///
/// Стесняването важи за предлаганото, не за разбираното: написан отвън код
/// влиза какъвто е (правило 13). Попълването е втора крачка и се иска изрично —
/// вж. <see cref="ValueLimits.Fill"/>.
/// </summary>
internal static class BookLimits
{
    /// <summary>Списъците на фирмата за отчетните книги.</summary>
    public static ValueLimits Of(Workspace workspace, long companyId) =>
        workspace.Lists.LimitsFor(companyId, "book");

    /// <summary>
    /// Пълни менюто на едно поле и, ако е останала една стойност, я избира.
    ///
    /// Избира се по индекс, а не с текст: така се вдига SelectedIndexChanged и
    /// това, което следва избора — описанието на дейността например — се
    /// попълва заедно с него. Затова повикващият закача слушателите си ПРЕДИ
    /// да повика този метод.
    /// </summary>
    public static void Offer(ComboBox combo, ValueLimits limits, MoveKind kind, ColumnDef column)
    {
        var node = Moves.Node(kind);

        foreach (var option in limits.Offer(BookNodes.Report, node, column))
            combo.Items.Add(option.Display);

        if (limits.Fill(BookNodes.Report, node, column) is not { } only) return;

        var at = combo.Items.IndexOf(only.Display);
        if (at >= 0) combo.SelectedIndex = at;
    }

    /// <summary>
    /// Кодът, с който да тръгне празно поле за код — или празен текст.
    ///
    /// Кодът се пише отделно от останалите полета: в едни прозорчета той идва
    /// от самия ред, в други стои в клетка на таблица. Затова оттук се връща
    /// само текстът, а кой го слага решава мястото.
    /// </summary>
    public static string Sole(ValueLimits limits, MoveKind kind, ColumnDef? column) =>
        column is null
            ? string.Empty
            : limits.Fill(BookNodes.Report, Moves.Node(kind), column)?.Display ?? string.Empty;
}

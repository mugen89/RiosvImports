using RiosvImports.Core.Excel;
using RiosvImports.Core.Schema;

namespace RiosvImports.App.Editor;

/// <summary>
/// Редът на разделите в редактора.
///
/// Схемата ги изброява в реда, в който ги е написал авторът на XSD файла, а
/// той следва XML-а, не работата. В отчетната книга за оползотворяване това
/// личи: „Получен отпадък“ — с който всичко започва — стои последен, а
/// „Обработен“ е преди „Образуван“, който е преди „Предаден“.
///
/// Пипа се само подредбата на екрана. Изходният XML се сглобява по реда на
/// схемата, независимо от това кой раздел стои първи тук.
/// </summary>
internal static class TabOrder
{
    private static readonly Dictionary<string, string[]> Preferred = new(StringComparer.Ordinal)
    {
        // Пътят на отпадъка през площадката: влиза, третира се, при
        // третирането се образува нов, и накрая се предава.
        ["r_b_recovery_dispose"] =
        [
            "r_b_rd_received",
            "r_b_rd_treated",
            "r_b_rd_generated",
            "r_b_rd_submitted",
            "r_b_recovery_dispose",
            "r_b_notes"
        ]
    };

    /// <summary>
    /// Подрежда листовете за показване. Отчет без собствена подредба остава
    /// както е дошъл, а непознат раздел отива в края — така добавен утре
    /// възел се появява, вместо да изчезне.
    /// </summary>
    public static IReadOnlyList<SheetPlan> Arrange(ReportNode report, IReadOnlyList<SheetPlan> sheets)
    {
        if (!Preferred.TryGetValue(report.ElementName, out var order)) return sheets;

        return [.. sheets.OrderBy(sheet =>
        {
            var at = Array.IndexOf(order, sheet.Node.ElementName);
            return at < 0 ? order.Length : at;
        })];
    }
}

namespace RiosvImports.Core.Values;

/// <summary>
/// Сурова стойност от Excel клетка, преди тълкуване. Пазим и типа, който
/// ClosedXML е разпознал, защото датите и числата идват като DateTime/double,
/// а не като текст.
/// </summary>
public readonly record struct CellValue(string Text, DateTime? Date, double? Number)
{
    public static readonly CellValue Empty = new(string.Empty, null, null);

    public bool IsEmpty => Date is null && Number is null && string.IsNullOrWhiteSpace(Text);

    public static CellValue FromText(string? text) =>
        new(text?.Trim() ?? string.Empty, null, null);
}

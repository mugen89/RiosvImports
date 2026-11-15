namespace RiosvImports.Core.Localization;

/// <summary>
/// Български надписи за полетата, за които XSD файлът не съдържа коментар.
/// Ползва се само като резерва — когато схемата дава описание, то има предимство.
/// </summary>
public static class FieldLabels
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["year"] = "Година на отчета",
        ["eik"] = "ЕИК на задълженото лице",
        ["type"] = "Тип на импорта (винаги REPLACE)",
        ["quarter"] = "Тримесечие",
        ["period_start"] = "Начало на периода",
        ["period_end"] = "Край на периода",
        ["stagenum"] = "Номер на работния лист",
        ["origin"] = "Произход на отпадъка",
        ["ewc"] = "Код на отпадъка по ЕКО",
        ["receiver_name"] = "Име на получателя",
        ["receiver_pin_name"] = "ЕИК / идентификатор на получателя",
        ["type_receiver_code"] = "Тип на получателя",
        ["doc_num_grounds"] = "Основание за предаване (номер на документ)",
        ["amount_ton_handed_over"] = "Количество предаден отпадък, тон",
        ["date_record"] = "Дата на записа",
        ["other_notes"] = "Други бележки"
    };

    /// <summary>
    /// По-кратък надпис за там, където мястото е малко.
    ///
    /// Схемата описва тези две полета с цяло изречение — „Начален период на
    /// данните“. За подсказка и за съобщение при грешка това е добре, но на
    /// един ред с пет полета изяжда ширината на съседите. Пълният надпис
    /// остава в подсказката.
    /// </summary>
    private static readonly Dictionary<string, string> Shorthand = new(StringComparer.OrdinalIgnoreCase)
    {
        ["period_start"] = "Начало период",
        ["period_end"] = "Край период"
    };

    /// <summary>Съкратеният надпис, ако полето има такъв.</summary>
    public static string? Shorter(string fieldName) => Shorthand.GetValueOrDefault(fieldName);

    /// <summary>Връща надписа от схемата, а ако той липсва — резервния.</summary>
    public static string Resolve(string fieldName, string? schemaLabel)
    {
        if (!string.IsNullOrWhiteSpace(schemaLabel) && schemaLabel != fieldName)
            return schemaLabel;

        return Map.TryGetValue(fieldName, out var fallback) ? fallback : fieldName;
    }
}

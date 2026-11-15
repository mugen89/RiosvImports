using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;

namespace RiosvImports.Core.Values;

public readonly record struct ConversionResult(bool Ok, string? Value, string? Error)
{
    public static ConversionResult Success(string value) => new(true, value, null);
    public static ConversionResult Fail(string error) => new(false, null, error);
    public static readonly ConversionResult Skip = new(true, null, null);
}

/// <summary>
/// Превръща това, което потребителят е написал в Excel, в стойността, която
/// схемата очаква в XML.
///
/// Тук са събрани улесненията за българските потребители:
///  • десетична запетая ("1,5") се приема наравно с точка;
///  • дати във формат дд.мм.гггг (както и дд/мм/гггг, дд-мм-гггг, ISO);
///  • ЕКО кодове, писани с интервали ("16 10 04" → "161004");
///  • изборните полета приемат както кода, така и текста от падащото меню.
/// </summary>
public static class ValueConverter
{
    /// <summary>Форматите за дата, които приемаме при въвеждане.</summary>
    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy",
        "dd/MM/yyyy", "d/M/yyyy",
        "dd-MM-yyyy", "d-M-yyyy",
        "yyyy-MM-dd", "yyyy.MM.dd", "yyyy/MM/dd"
    ];

    /// <summary>Форматът, в който показваме дати на потребителя.</summary>
    public const string BulgarianDateFormat = "dd.MM.yyyy";

    private static readonly Regex NonDigits = new(@"[^0-9]", RegexOptions.Compiled);

    /// <summary>
    /// Как се казва колоната в съобщение за грешка.
    ///
    /// Точно това, което пише на заглавието ѝ в таблицата — не името от
    /// схемата. „липсва задължителна стойност за „stagenum““ кара човека да
    /// търси колона с такова име и да не я намери никъде на екрана.
    /// </summary>
    private static string Named(ColumnDef column) => ReportTitles.ColumnShort(column);

    public static ConversionResult Convert(CellValue cell, ColumnDef column)
    {
        var domain = column.Domain;

        if (cell.IsEmpty)
        {
            return column.Required
                ? ConversionResult.Fail(Lang.T($"липсва задължителна стойност за „{Named(column)}“", $"missing required value for “{Named(column)}”"))
                : ConversionResult.Skip;
        }

        return domain.Kind switch
        {
            ValueKind.Enum => ConvertEnum(cell, column),
            ValueKind.Date => ConvertDate(cell, column),
            ValueKind.Integer => ConvertInteger(cell, column),
            ValueKind.Decimal => ConvertDecimal(cell, column),
            _ => ConvertText(cell, column)
        };
    }

    // ------------------------------------------------------------ изборни

    private static ConversionResult ConvertEnum(CellValue cell, ColumnDef column)
    {
        var raw = RawText(cell);
        var domain = column.Domain;

        // 1. Точно съвпадение с кода.
        var hit = domain.Options.FirstOrDefault(o =>
            string.Equals(o.Value, raw, StringComparison.OrdinalIgnoreCase));

        // 2. Целият текст от падащото меню ("04 — Юридическо лице").
        hit ??= domain.Options.FirstOrDefault(o =>
            string.Equals(o.Display, raw, StringComparison.OrdinalIgnoreCase));

        // 3. Кодът е бил написан с интервали или точки — типично за ЕКО
        //    кодовете ("16 10 04") и за кодовете по Базелската конвенция ("Y 1").
        if (hit is null)
        {
            var squashed = Squash(raw);
            hit = domain.Options.FirstOrDefault(o =>
                string.Equals(Squash(o.Value), squashed, StringComparison.OrdinalIgnoreCase));
        }

        // 4. Потребителят е написал само описанието.
        hit ??= domain.Options.FirstOrDefault(o =>
            o.Description is not null &&
            string.Equals(o.Description, raw, StringComparison.OrdinalIgnoreCase));

        // 5. Excel е разпознал кода като число и е изял водещите нули ("4" → "04").
        if (hit is null && cell.Number is { } n && n == Math.Floor(n))
        {
            var asInt = ((long)n).ToString(CultureInfo.InvariantCulture);
            hit = domain.Options.FirstOrDefault(o =>
                      string.Equals(o.Value, asInt, StringComparison.Ordinal))
                  ?? domain.Options.FirstOrDefault(o =>
                      o.Value.TrimStart('0') == asInt && o.Value.Length > asInt.Length);
        }

        if (hit is null)
        {
            var sample = string.Join(", ", domain.Options.Take(8).Select(o => o.Value));
            var more = domain.Options.Count > 8 ? ", …" : string.Empty;
            return ConversionResult.Fail(
                Lang.T($"„{Named(column)}“: недопустима стойност „{raw}“ (допустими: {sample}{more})", $"“{Named(column)}”: value “{raw}” is not allowed (allowed: {sample}{more})"));
        }

        return ConversionResult.Success(hit.Value);
    }

    // ---------------------------------------------------------------- дати

    /// <summary>
    /// Привежда дата към „дд.мм.гггг“, каквото и да е било подадено.
    ///
    /// Разделителят не е уточнен от НИСО и хората лепят дати отвсякъде — от
    /// Excel, от банкови извлечения, от чужди програми. Затова се приемат
    /// точка, наклонена черта и тире, годината от две или четири цифри, ISO
    /// наредбата, а също и голото серийно число на Excel.
    ///
    /// Разборът минава през същия списък формати, който ползва и
    /// преобразуването към XML: ако нещо се покаже прието тук, то ще бъде
    /// прието и после.
    /// </summary>
    public static bool TryNormalizeDate(string? text, out string normalized)
    {
        normalized = string.Empty;

        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw)) return false;

        if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var parsed))
        {
            normalized = parsed.ToString(BulgarianDateFormat, CultureInfo.InvariantCulture);
            return true;
        }

        // Excel понякога подава датата като голо серийно число.
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) &&
            serial is > 0 and < 2_958_466)
        {
            try
            {
                normalized = DateTime.FromOADate(serial).ToString(
                    BulgarianDateFormat, CultureInfo.InvariantCulture);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return false;
    }

    private static ConversionResult ConvertDate(CellValue cell, ColumnDef column)
    {
        if (cell.Date is { } d)
            return ConversionResult.Success(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        // Excel пази датите като серийни числа.
        if (cell.Number is { } serial && serial > 0 && serial < 2_958_466)
        {
            try
            {
                var fromSerial = DateTime.FromOADate(serial);
                return ConversionResult.Success(
                    fromSerial.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            catch (ArgumentException)
            {
                // Не е валидна дата — продължаваме с текстовия разбор.
            }
        }

        var raw = RawText(cell);
        if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var parsed))
        {
            return ConversionResult.Success(
                parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return ConversionResult.Fail(
            Lang.T($"„{Named(column)}“: „{raw}“ не е валидна дата (очакван формат: дд.мм.гггг)", $"“{Named(column)}”: “{raw}” is not a valid date (expected dd.mm.yyyy)"));
    }

    // -------------------------------------------------------------- числа

    private static ConversionResult ConvertInteger(CellValue cell, ColumnDef column)
    {
        var parsed = ParseNumber(cell);
        if (parsed is null)
            return ConversionResult.Fail(Lang.T($"„{Named(column)}“: „{RawText(cell)}“ не е число", $"“{Named(column)}”: “{RawText(cell)}” is not a number"));

        var value = parsed.Value;
        if (value != Math.Truncate(value))
            return ConversionResult.Fail(Lang.T($"„{Named(column)}“: очаква се цяло число, а не „{RawText(cell)}“", $"“{Named(column)}”: a whole number is expected, not “{RawText(cell)}”"));

        var rangeError = CheckRange(value, column);
        if (rangeError is not null) return ConversionResult.Fail(rangeError);

        var text = ((long)value).ToString(CultureInfo.InvariantCulture);

        // Някои кодове са цифрови низове с фиксирана дължина (напр. ЕКАТТЕ).
        text = ApplyFixedWidthPattern(text, column.Domain.Pattern);

        return ConversionResult.Success(text);
    }

    private static ConversionResult ConvertDecimal(CellValue cell, ColumnDef column)
    {
        var parsed = ParseNumber(cell);
        if (parsed is null)
            return ConversionResult.Fail(Lang.T($"„{Named(column)}“: „{RawText(cell)}“ не е число", $"“{Named(column)}”: “{RawText(cell)}” is not a number"));

        var value = parsed.Value;

        var rangeError = CheckRange(value, column);
        if (rangeError is not null) return ConversionResult.Fail(rangeError);

        var digits = column.Domain.FractionDigits ?? 5;
        var rounded = Math.Round(value, digits, MidpointRounding.AwayFromZero);

        // Записваме без излишни нули, но с точка за десетичен знак.
        var text = rounded.ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);
        if (text is "" or "-0") text = "0";

        return ConversionResult.Success(text);
    }

    private static string? CheckRange(decimal value, ColumnDef column)
    {
        var d = column.Domain;

        if (d.MinInclusive is { } min && value < min)
        {
            return min == 0
                ? Lang.T($"„{Named(column)}“: стойността не може да е отрицателна (въведено: {Show(value)})", $"“{Named(column)}”: the value cannot be negative (entered: {Show(value)})")
                : Lang.T($"„{Named(column)}“: стойността трябва да е поне {Show(min)} (въведено: {Show(value)})", $"“{Named(column)}”: the value must be at least {Show(min)} (entered: {Show(value)})");
        }

        if (d.MaxInclusive is { } max && value > max)
            return Lang.T($"„{Named(column)}“: стойността не може да надвишава {Show(max)} (въведено: {Show(value)})", $"“{Named(column)}”: the value cannot exceed {Show(max)} (entered: {Show(value)})");

        return null;
    }

    private static string Show(decimal value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture);

    private static decimal? ParseNumber(CellValue cell)
    {
        if (cell.Number is { } n)
        {
            try { return (decimal)n; }
            catch (OverflowException) { return null; }
        }

        var raw = RawText(cell);
        if (raw.Length == 0) return null;

        // Махаме интервали и неразделящи интервали, ползвани за разделител на хиляди.
        var cleaned = raw.Replace(" ", string.Empty)
                         .Replace(" ", string.Empty)
                         .Replace(" ", string.Empty);

        // Приемаме и запетая, и точка за десетичен знак. Ако има и двете,
        // приемаме, че запетаята е разделител на хиляди (напр. "1,234.56").
        var hasComma = cleaned.Contains(',');
        var hasDot = cleaned.Contains('.');

        if (hasComma && hasDot)
            cleaned = cleaned.Replace(",", string.Empty);
        else if (hasComma)
            cleaned = cleaned.Replace(',', '.');

        return decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Ако типът изисква точно N цифри (напр. [0-9]{5}), допълва с водещи нули.
    /// </summary>
    private static string ApplyFixedWidthPattern(string text, string? pattern)
    {
        if (pattern is null) return text;

        var m = Regex.Match(pattern, @"^\[0-9\]\{(\d+)\}$");
        if (!m.Success) return text;

        var width = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return text.Length < width ? text.PadLeft(width, '0') : text;
    }

    // -------------------------------------------------------------- текст

    private static ConversionResult ConvertText(CellValue cell, ColumnDef column)
    {
        var raw = RawText(cell);
        var d = column.Domain;

        if (d.MaxLength is { } max && raw.Length > max)
        {
            return ConversionResult.Fail(
                Lang.T($"„{Named(column)}“: текстът е {raw.Length} знака, а максимумът е {max}", $"“{Named(column)}”: the text is {raw.Length} characters, the maximum is {max}"));
        }

        if (d.MinLength is { } min && raw.Length < min)
            return ConversionResult.Fail(Lang.T($"„{Named(column)}“: текстът трябва да е поне {min} знака", $"“{Named(column)}”: the text must be at least {min} characters"));

        if (d.Pattern is { } pattern && !Regex.IsMatch(raw, "^(?:" + pattern + ")$"))
        {
            // ЕИК и подобните са цифрови — подсказваме конкретно.
            var digitsOnly = NonDigits.Replace(raw, string.Empty);
            if (pattern.Contains("[0-9]") || pattern.Contains(@"[\d]"))
            {
                if (digitsOnly.Length > 0 &&
                    Regex.IsMatch(digitsOnly, "^(?:" + pattern + ")$"))
                {
                    return ConversionResult.Success(digitsOnly);
                }

                return ConversionResult.Fail(
                    Lang.T($"„{Named(column)}“: „{raw}“ трябва да съдържа само цифри и да отговаря на формата", $"“{Named(column)}”: “{raw}” must be digits only and match the format"));
            }

            return ConversionResult.Fail(Lang.T($"„{Named(column)}“: „{raw}“ не отговаря на изисквания формат", $"“{Named(column)}”: “{raw}” does not match the required format"));
        }

        return ConversionResult.Success(raw);
    }

    // ------------------------------------------------------------ помощни

    private static string RawText(CellValue cell)
    {
        if (!string.IsNullOrWhiteSpace(cell.Text)) return cell.Text.Trim();

        if (cell.Date is { } d)
            return d.ToString(BulgarianDateFormat, CultureInfo.InvariantCulture);

        if (cell.Number is { } n)
        {
            return n == Math.Floor(n) && Math.Abs(n) < 1e15
                ? ((long)n).ToString(CultureInfo.InvariantCulture)
                : n.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    /// <summary>
    /// Число, писано както го пише човек: „12,5“, „12.5“, „1 234,56“.
    ///
    /// Същият разбор, който минава и при внасянето от Excel. Балансът чете
    /// вече записани редове, а те пазят каквото човекът е набрал — ако тук се
    /// четеше по друг начин, едно и също количество щеше да значи различно
    /// нещо на две места в програмата.
    /// </summary>
    public static decimal? TryAmount(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : ParseNumber(CellValue.FromText(text));

    /// <summary>
    /// Кодът, сведен до сравнима форма: „16 10 04“ и „161004“ са един и същ
    /// отпадък и не бива да стават два реда в баланса.
    /// </summary>
    public static string NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Squash(value.Trim());

    /// <summary>Маха интервали, точки и тирета — за сравняване на кодове.</summary>
    private static string Squash(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || c is '.' or '-' or '_') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}

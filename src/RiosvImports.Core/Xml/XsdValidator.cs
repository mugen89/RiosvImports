using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

using RiosvImports.Core.Localization;

namespace RiosvImports.Core.Xml;

/// <summary>
/// Проверява генерирания XML срещу оригиналните XSD файлове на НИСО.
///
/// Държи два набора схеми. Точният иска „dsig:Signature“ така, както го иска и
/// НИСО — неподписан файл се отхвърля още при импорта. Отпуснатият прави подписа
/// незадължителен и служи само докато файлът още не е подписан, за да се провери
/// съдържанието му.
/// </summary>
public sealed class XsdValidator
{
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Dsig = "http://www.w3.org/2000/09/xmldsig#";

    private static readonly string[] SchemaFiles =
    [
        "xmldsig-core-schema.xsd", "common-types.xsd", "ewc.xsd",
        "year.xsd", "quarter.xsd", "book.xsd"
    ];

    private readonly XmlSchemaSet _strict;
    private readonly XmlSchemaSet _relaxed;

    public XsdValidator(string schemaFolder)
    {
        _strict = Load(schemaFolder, signatureOptional: false);
        _relaxed = Load(schemaFolder, signatureOptional: true);
    }

    private static XmlSchemaSet Load(string folder, bool signatureOptional)
    {
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };

        // Подредбата има значение: първо общите, после кореновите схеми.
        foreach (var file in SchemaFiles)
            Add(set, folder, file, signatureOptional);

        set.Compile();
        return set;
    }

    private static void Add(XmlSchemaSet set, string folder, string file, bool signatureOptional)
    {
        var path = Path.Combine(folder, file);
        var doc = XDocument.Load(path);

        if (signatureOptional) MakeSignatureOptional(doc);

        // Базовият адрес позволява на XmlSchemaSet да намери импортираните файлове.
        var settings = new XmlReaderSettings { XmlResolver = new XmlUrlResolver() };
        using var reader = XmlReader.Create(
            new StringReader(doc.ToString()),
            settings,
            new Uri(Path.GetFullPath(path)).AbsoluteUri);

        set.Add(null, reader);
    }

    private static void MakeSignatureOptional(XDocument doc)
    {
        var refs = doc.Descendants(Xs + "element")
                      .Where(e => e.Attribute("ref")?.Value.EndsWith(":Signature", StringComparison.Ordinal) == true);

        foreach (var element in refs)
            element.SetAttributeValue("minOccurs", "0");
    }

    /// <summary>
    /// Връща списък с грешките; празен списък означава валиден файл.
    /// <paramref name="requireSignature"/> = false пропуска само липсващия подпис —
    /// ползва се, докато файлът още не е подписан.
    /// </summary>
    public IReadOnlyList<string> Validate(XDocument document, bool requireSignature = false)
    {
        var errors = new List<string>();

        document.Validate(requireSignature ? _strict : _relaxed, (_, e) =>
        {
            var location = e.Exception is { } ex && ex.LineNumber > 0
                ? Lang.T($"ред {ex.LineNumber.ToString(CultureInfo.InvariantCulture)}: ", $"line {ex.LineNumber.ToString(CultureInfo.InvariantCulture)}: ")
                : string.Empty;

            var prefix = e.Severity == XmlSeverityType.Warning ? Lang.T("Предупреждение — ", "Warning — ") : string.Empty;
            errors.Add(prefix + location + e.Message);
        }, addSchemaInfo: false);

        return errors;
    }

    /// <summary>Проверява, че подписът наистина липсва — за информативно съобщение.</summary>
    public static bool HasSignature(XDocument document) =>
        document.Root?.Element(Dsig + "Signature") is not null;
}

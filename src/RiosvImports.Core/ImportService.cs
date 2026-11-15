using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using RiosvImports.Core.Excel;
using RiosvImports.Core.Import;
using System.Xml.Linq;
using RiosvImports.Core.Schema;
using RiosvImports.Core.Xml;

using RiosvImports.Core.Localization;

namespace RiosvImports.Core;

/// <summary>Къде са записани резултатите от една генерация.</summary>
public sealed record GeneratedFiles(string XmlPath, string LogPath, string ProblemsPath, bool Signed);

/// <summary>
/// Единствената точка, през която приложението работи с ядрото: зарежда
/// схемите, прави шаблони и генерира XML.
/// </summary>
public sealed class ImportService
{
    private readonly Lazy<XsdValidator> _validator;

    public ImportService(string schemaFolder)
    {
        SchemaFolder = schemaFolder;
        Schemas = new XsdReader(schemaFolder).ReadAll();
        _validator = new Lazy<XsdValidator>(() => new XsdValidator(schemaFolder));
    }

    public string SchemaFolder { get; }

    public IReadOnlyList<SchemaDefinition> Schemas { get; }

    public SchemaDefinition GetSchema(string key) =>
        Schemas.First(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Създава шаблон само за избраните видове отчети.</summary>
    public void CreateTemplate(
        SchemaDefinition schema,
        IEnumerable<ReportNode> reports,
        string path,
        IReadOnlyDictionary<string, string>? defaults = null)
    {
        var selected = reports.ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException(Lang.T("Не е избран нито един вид отчет.", "No report type was chosen."));

        var layout = WorkbookLayout.Build(schema, selected);
        new TemplateBuilder(layout, defaults).Save(path);
    }

    /// <summary>
    /// Компилира XSD схемите предварително. Отнема няколко секунди, затова се
    /// извиква на заден план при стартиране, за да не се чака при първия импорт.
    /// </summary>
    public void Warmup() => _ = _validator.Value;

    public ParsedWorkbook ReadWorkbook(string path) =>
        new WorkbookReader(Schemas).Read(path);

    public ImportResult Process(ParsedWorkbook workbook) =>
        new ImportProcessor(_validator.Value).Process(workbook);

    /// <summary>
    /// Записва XML файла заедно с отчета за проверката и списъка с проблемни редове.
    /// Ако е подаден сертификат, файлът се подписва — НИСО отхвърля неподписан файл.
    /// </summary>
    public GeneratedFiles Save(
        ImportResult result,
        string xmlPath,
        string sourceFile,
        X509Certificate2? certificate = null,
        SignatureProfile? profile = null)
    {
        if (result.Xml is null)
            throw new InvalidOperationException(Lang.T("Няма генериран XML, който да бъде записан.", "There is no generated XML to save."));

        var signed = certificate is not null;

        if (signed)
        {
            SignAndWrite(result.Xml, xmlPath, certificate!, profile ?? new SignatureProfile());
        }
        else
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                // Без BOM — така файлът се приема безпроблемно от системата.
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };

            using var writer = XmlWriter.Create(xmlPath, settings);
            result.Xml.Save(writer);
        }

        var directory = Path.GetDirectoryName(xmlPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(xmlPath);

        var logPath = Path.Combine(directory, stem + "_отчет.txt");
        var problemsPath = Path.Combine(directory, stem + "_проблемни_редове.txt");

        File.WriteAllText(logPath, result.BuildLog(sourceFile), new UTF8Encoding(true));
        File.WriteAllText(problemsPath, result.BuildProblemRows(), new UTF8Encoding(true));

        return new GeneratedFiles(xmlPath, logPath, problemsPath, signed);
    }

    /// <summary>
    /// Подписването и записването вървят заедно: подписът важи за точните байтове,
    /// затова документът не се преформатира след това. Отстъпите се слагат преди
    /// подписването, а самият &lt;Signature&gt; остава както го е дал .NET.
    /// </summary>
    private static void SignAndWrite(
        XDocument document, string xmlPath, X509Certificate2 certificate, SignatureProfile profile)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        var buffer = new StringWriter();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
               {
                   Indent = true,
                   IndentChars = "  ",
                   OmitXmlDeclaration = true
               }))
        {
            document.Save(writer);
        }

        var xml = new XmlDocument { PreserveWhitespace = true };
        xml.LoadXml(buffer.ToString());

        XmlSigner.Sign(xml, certificate, profile);

        using (var writer = XmlWriter.Create(xmlPath, settings))
            xml.Save(writer);

        // Самопроверка: по-добре да се разбере тук, отколкото след качването.
        var check = new XmlDocument { PreserveWhitespace = true };
        check.Load(xmlPath);

        // Сертификатът се подава изрично: ако не е вграден в подписа (KeyInfo),
        // проверката няма откъде да вземе публичния ключ.
        if (!XmlSigner.Verify(check, certificate))
        {
            throw new InvalidOperationException(
                Lang.T(
                    "Подписът беше положен, но не се потвърждава при проверка. " +
                    "Файлът не бива да се качва в НИСО.",
                    "The signature was applied but does not verify. " +
                    "The file must not be uploaded to NWIS."));
        }
    }

    /// <summary>Проверява подписания файл срещу схемата без отстъпки.</summary>
    public IReadOnlyList<string> ValidateSignedFile(string xmlPath) =>
        _validator.Value.Validate(XDocument.Load(xmlPath), requireSignature: true);

    /// <summary>Намира папката със схемите спрямо изпълнимия файл.</summary>
    public static string LocateSchemaFolder()
    {
        var candidates = new List<string>();
        var baseDir = AppContext.BaseDirectory;

        candidates.Add(Path.Combine(baseDir, "schemas"));

        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            candidates.Add(Path.Combine(dir.FullName, "schemas"));
            dir = dir.Parent;
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "year.xsd")))
                return candidate;
        }

        throw new DirectoryNotFoundException(
            Lang.T("Не е намерена папка „schemas“ с файловете на схемите (year.xsd, quarter.xsd, book.xsd).", "No “schemas” folder with the schema files was found (year.xsd, quarter.xsd, book.xsd)."));
    }
}

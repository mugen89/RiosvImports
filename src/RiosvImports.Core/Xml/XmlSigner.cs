using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

using RiosvImports.Core.Localization;

namespace RiosvImports.Core.Xml;

/// <summary>Кой алгоритъм се ползва за отпечатъка и за самия подпис.</summary>
public enum SignatureDigest
{
    Sha256,
    Sha1
}

/// <summary>Как се привежда XML-ът в канонична форма преди подписване.</summary>
public enum SignatureCanonicalization
{
    /// <summary>Exclusive C14N — това ползват повечето системи.</summary>
    Exclusive,

    /// <summary>Inclusive C14N — по-старият вариант.</summary>
    Inclusive
}

/// <summary>Видът на подписа.</summary>
public enum SignatureForm
{
    /// <summary>
    /// XAdES-BES — подписът носи и ETSI свойствата (време на подписване и
    /// отпечатък на сертификата). Ръководството на НИСО (стр. 150) иска точно
    /// „envolped (XAdES)“, затова това е подразбирането.
    /// </summary>
    XadesBes,

    /// <summary>
    /// Обикновен XML-DSig — само &lt;ds:Signature&gt; без ETSI свойства.
    /// Минава схемата, защото „ds:Object“ в xmldsig-core-schema.xsd е
    /// незадължителен, но не е това, което ръководството описва.
    /// </summary>
    Dsig
}

/// <summary>
/// Профилът на подписа. НИСО не публикува кой точно очаква, затова стойностите
/// се избират от потребителя; подразбирането следва ръководството.
/// </summary>
public sealed record SignatureProfile(
    SignatureDigest Digest = SignatureDigest.Sha256,
    SignatureCanonicalization Canonicalization = SignatureCanonicalization.Exclusive,
    bool IncludeCertificate = true,
    SignatureForm Form = SignatureForm.XadesBes)
{
    public string DigestUrl => Digest == SignatureDigest.Sha256
        ? SignedXml.XmlDsigSHA256Url
        : SignedXml.XmlDsigSHA1Url;

    public string SignatureUrl => Digest == SignatureDigest.Sha256
        ? SignedXml.XmlDsigRSASHA256Url
        : SignedXml.XmlDsigRSASHA1Url;

    public string CanonicalizationUrl => Canonicalization == SignatureCanonicalization.Exclusive
        ? SignedXml.XmlDsigExcC14NTransformUrl
        : SignedXml.XmlDsigC14NTransformUrl;

    public Transform CanonicalizationTransform() => Canonicalization == SignatureCanonicalization.Exclusive
        ? new XmlDsigExcC14NTransform()
        : new XmlDsigC14NTransform();
}

/// <summary>
/// Полага електронен подпис върху готовия XML.
///
/// Схемите на НИСО изискват &lt;dsig:Signature&gt; като последен елемент в
/// &lt;reports&gt; и системата отхвърля неподписан файл още при проверката срещу
/// схемата. Подписът е „enveloped“ — обхваща целия документ.
/// </summary>
public static class XmlSigner
{
    /// <summary>Пространството на ETSI свойствата (XAdES 1.3.2).</summary>
    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";

    /// <summary>Стойността на „Type“ върху втората препратка — точно тази иска стандартът.</summary>
    private const string SignedPropertiesType = "http://uri.etsi.org/01903#SignedProperties";

    /// <summary>
    /// Подписва документа на място. Работи върху XmlDocument, а не върху
    /// XDocument, защото подписът важи за точните байтове на файла — след
    /// подписване документът не бива да се преформатира.
    /// </summary>
    public static void Sign(XmlDocument document, X509Certificate2 certificate, SignatureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(certificate);

        if (document.DocumentElement is null)
            throw new InvalidOperationException(Lang.T("Документът е празен и не може да бъде подписан.", "The document is empty and cannot be signed."));

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                Lang.T(
                    $"Сертификатът „{Describe(certificate)}“ няма частен ключ и не може да подписва. " +
                    "Изберете сертификата от вашия КЕП.",
                    $"Certificate “{Describe(certificate)}” has no private key and cannot sign. " +
                    "Choose the certificate from your signing device."));
        }

        var signedXml = new XadesSignedXml(document);
        signedXml.SigningKey = PrivateKeyOf(certificate);
        signedXml.SignedInfo!.CanonicalizationMethod = profile.CanonicalizationUrl;
        signedXml.SignedInfo.SignatureMethod = profile.SignatureUrl;

        // Празният URI означава „целият документ“.
        var reference = new Reference(string.Empty) { DigestMethod = profile.DigestUrl };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(profile.CanonicalizationTransform());
        signedXml.AddReference(reference);

        if (profile.Form == SignatureForm.XadesBes)
            AddXadesProperties(document, signedXml, certificate, profile);

        if (profile.IncludeCertificate)
        {
            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.KeyInfo = keyInfo;
        }

        try
        {
            signedXml.ComputeSignature();
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(BuildKeyErrorMessage(ex, profile), ex);
        }

        // Схемата иска подписа последен в <reports>.
        document.DocumentElement.AppendChild(
            document.ImportNode(signedXml.GetXml(), deep: true));
    }

    /// <summary>
    /// Проверява подписа в документа — за самопроверка след записване.
    /// Сертификатът се подава, когато той не е вграден в самия подпис; веригата
    /// не се проверява, защото КЕП-ът невинаги се валидира без интернет.
    /// </summary>
    public static bool Verify(XmlDocument document, X509Certificate2? certificate = null)
    {
        var nodes = document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (nodes.Count == 0) return false;

        var signedXml = new SignedXml(document);
        signedXml.LoadXml((XmlElement)nodes[0]!);

        return certificate is null
            ? signedXml.CheckSignature()
            : signedXml.CheckSignature(certificate, verifySignatureOnly: true);
    }

    /// <summary>Кратко описание на сертификата за съобщения към потребителя.</summary>
    public static string Describe(X509Certificate2 certificate)
    {
        var name = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return string.IsNullOrWhiteSpace(name) ? certificate.Subject : name;
    }

    // ------------------------------------------------------------- XAdES

    /// <summary>
    /// Добавя ETSI свойствата и втората препратка към тях.
    ///
    /// Свойствата стоят в &lt;ds:Object&gt; вътре в подписа, а препратката ги
    /// сочи по „Id“. Затова подписът трябва да има свой „Id“ — иначе
    /// „QualifyingProperties/@Target“ няма към какво да сочи.
    /// </summary>
    private static void AddXadesProperties(
        XmlDocument document, XadesSignedXml signedXml, X509Certificate2 certificate, SignatureProfile profile)
    {
        var id = "xades-" + Guid.NewGuid().ToString("N");
        var propertiesId = id + "-signedprops";

        signedXml.Signature.Id = id;

        var qualifying = document.CreateElement("xades", "QualifyingProperties", XadesNamespace);
        qualifying.SetAttribute("Target", "#" + id);

        var signedProperties = document.CreateElement("xades", "SignedProperties", XadesNamespace);
        signedProperties.SetAttribute("Id", propertiesId);
        qualifying.AppendChild(signedProperties);

        var signatureProperties = document.CreateElement("xades", "SignedSignatureProperties", XadesNamespace);
        signedProperties.AppendChild(signatureProperties);

        // Времето е в UTC със „Z“ — така го иска xs:dateTime в схемата на XAdES.
        var signingTime = document.CreateElement("xades", "SigningTime", XadesNamespace);
        signingTime.InnerText = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        signatureProperties.AppendChild(signingTime);

        signatureProperties.AppendChild(BuildSigningCertificate(document, certificate, profile));

        signedXml.AddObject(new DataObject { Data = qualifying.SelectNodes(".")! });
        signedXml.QualifyingProperties = qualifying;

        // Винаги exclusive: при inclusive канонизация подписващият вижда
        // свойствата извън документа, а проверяващият — вътре в <reports>,
        // и наследеното пространство по подразбиране би развалило отпечатъка.
        var propertiesReference = new Reference("#" + propertiesId)
        {
            DigestMethod = profile.DigestUrl,
            Type = SignedPropertiesType
        };

        propertiesReference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(propertiesReference);
    }

    /// <summary>Отпечатъкът на сертификата — с него проверяващият познава с какво е подписано.</summary>
    private static XmlElement BuildSigningCertificate(
        XmlDocument document, X509Certificate2 certificate, SignatureProfile profile)
    {
        var dsig = SignedXml.XmlDsigNamespaceUrl;

        var signingCertificate = document.CreateElement("xades", "SigningCertificate", XadesNamespace);
        var cert = document.CreateElement("xades", "Cert", XadesNamespace);
        signingCertificate.AppendChild(cert);

        var certDigest = document.CreateElement("xades", "CertDigest", XadesNamespace);
        cert.AppendChild(certDigest);

        var digestMethod = document.CreateElement("ds", "DigestMethod", dsig);
        digestMethod.SetAttribute("Algorithm", profile.DigestUrl);
        certDigest.AppendChild(digestMethod);

        var digestValue = document.CreateElement("ds", "DigestValue", dsig);
        digestValue.InnerText = Convert.ToBase64String(DigestOf(certificate.RawData, profile.Digest));
        certDigest.AppendChild(digestValue);

        var issuerSerial = document.CreateElement("xades", "IssuerSerial", XadesNamespace);
        cert.AppendChild(issuerSerial);

        var issuerName = document.CreateElement("ds", "X509IssuerName", dsig);
        issuerName.InnerText = certificate.IssuerName.Name;
        issuerSerial.AppendChild(issuerName);

        var serialNumber = document.CreateElement("ds", "X509SerialNumber", dsig);
        serialNumber.InnerText = DecimalSerialOf(certificate);
        issuerSerial.AppendChild(serialNumber);

        return signingCertificate;
    }

    private static byte[] DigestOf(byte[] data, SignatureDigest digest) =>
        digest == SignatureDigest.Sha256 ? SHA256.HashData(data) : SHA1.HashData(data);

    /// <summary>
    /// „X509SerialNumber“ е десетично число, а .NET дава серийния номер в
    /// шестнайсетичен вид. Водещата нула пази стойността положителна.
    /// </summary>
    private static string DecimalSerialOf(X509Certificate2 certificate)
    {
        var value = BigInteger.Parse(
            "0" + certificate.SerialNumber, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Докато подписът се изчислява, свойствата още не са в документа и .NET не
    /// ги намира по „Id“. Затова търсенето се допълва тук.
    /// </summary>
    private sealed class XadesSignedXml(XmlDocument document) : SignedXml(document)
    {
        public XmlElement? QualifyingProperties { get; set; }

        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            if (document is not null && base.GetIdElement(document, idValue) is { } found)
                return found;

            return QualifyingProperties is null ? null : FindById(QualifyingProperties, idValue);
        }

        private static XmlElement? FindById(XmlElement element, string id)
        {
            if (element.GetAttribute("Id") == id) return element;

            foreach (var child in element.ChildNodes.OfType<XmlElement>())
                if (FindById(child, id) is { } found) return found;

            return null;
        }
    }

    // ------------------------------------------------------------- помощни

    private static AsymmetricAlgorithm PrivateKeyOf(X509Certificate2 certificate)
    {
        // Ключът на КЕП стои в четеца; тук се получава само дръжка към него и
        // Windows сам поисква ПИН при първото подписване.
        AsymmetricAlgorithm? key = certificate.GetRSAPrivateKey();
        key ??= certificate.GetECDsaPrivateKey();

        return key ?? throw new InvalidOperationException(
            Lang.T(
                $"Частният ключ на „{Describe(certificate)}“ не е достъпен. " +
                "Проверете дали четецът е включен и драйверът на картата е инсталиран.",
                $"The private key of “{Describe(certificate)}” is not available. " +
                "Check that the reader is plugged in and the card driver is installed."));
    }

    private static string BuildKeyErrorMessage(CryptographicException ex, SignatureProfile profile)
    {
        var hint = profile.Digest == SignatureDigest.Sha256
            ? Lang.T("Ако картата е по-стара, тя може да не поддържа SHA-256 — пробвайте SHA-1.", "If the card is older it may not support SHA-256 — try SHA-1.")
            : Lang.T("Проверете дали е въведен верният ПИН и картата не е заключена.", "Check that the right PIN was entered and the card is not locked.");

        return Lang.T("Подписването не беше извършено.\n\n", "The signing did not go through.\n\n") + ex.Message + "\n\n" + hint;
    }
}

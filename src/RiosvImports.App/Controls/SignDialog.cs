using System.Security.Cryptography.X509Certificates;
using RiosvImports.Core.Xml;

using RiosvImports.Core.Localization;

namespace RiosvImports.App.Controls;

/// <summary>Какво е избрал потребителят в прозореца за подписване.</summary>
internal sealed record SigningChoice(X509Certificate2 Certificate, SignatureProfile Profile);

/// <summary>
/// Избор на сертификат за подпис. В .NET 9 няма готов диалог на Windows, затова
/// списъкът се показва тук — само сертификати с частен ключ, годни за подписване.
/// </summary>
internal sealed class SignDialog : Dialog
{
    private readonly ListView _list = new();
    private readonly ComboBox _form = new();
    private readonly ComboBox _digest = new();
    private readonly ComboBox _canonical = new();
    private readonly CheckBox _includeCert = new();
    private readonly Button _ok = Theme.PrimaryButton(Lang.T("Подпиши", "Sign"));

    /// <summary>Показва и сертификатите, които филтърът е отсял.</summary>
    private readonly CheckBox _showAll = new();

    /// <summary>Стои на мястото на списъка, когато няма какво да се покаже.</summary>
    private readonly Label _empty = new();

    private readonly List<CertificateEntry> _certificates;

    /// <param name="Rank">
    /// 2 — прилича на КЕП; 1 — става за подпис, но без белезите на КЕП;
    /// 0 — не се предлага. Виж <see cref="RankOf"/>.
    /// </param>
    private sealed record CertificateEntry(X509Certificate2 Certificate, StoreLocation Location, int Rank)
    {
        public bool Offered => Rank > 0;
    }

    public SignDialog()
    {
        _certificates = LoadCertificates();

        Text = Lang.T("Подписване с електронен подпис", "Signing with an electronic signature");
        Font = Theme.Base;
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(Theme.Scaled(800), Theme.Scaled(520));

        Controls.Add(BuildBody());
        Controls.Add(BuildFooter());
        Controls.Add(BuildHeader());

        FillList();
    }

    public SigningChoice? Choice { get; private set; }

    // -------------------------------------------------------------- изглед

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(64),
            BackColor = Theme.AccentLight,
            Padding = new Padding(16, 10, 16, 10)
        };

        panel.Controls.Add(new Label
        {
            Text = Lang.T("Изберете сертификата на вашия КЕП", "Choose your signing certificate"),
            Font = Theme.Heading,
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(16, 8)
        });

        panel.Controls.Add(new Label
        {
            Text = Lang.T("Ако картата е в четеца, Windows ще поиска ПИН при самото подписване.", "If the card is in the reader, Windows will ask for the PIN at signing time."),
            ForeColor = Color.DimGray,
            AutoSize = true,
            Location = new Point(18, 36)
        });

        return panel;
    }

    private Control BuildBody()
    {
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8) };

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Dock = DockStyle.Fill;
        _list.Columns.Add(Lang.T("Издаден на", "Issued to"), 260);
        _list.Columns.Add(Lang.T("Издател", "Issuer"), 230);
        _list.Columns.Add(Lang.T("Валиден до", "Valid until"), 110);
        _list.Columns.Add(Lang.T("Хранилище", "Store"), 120);
        _list.SelectedIndexChanged += (_, _) => _ok.Enabled = SelectedEntry() is not null;
        _list.DoubleClick += (_, _) => Accept();

        // Празният списък не е ред в списъка: ред се маркира, а после „Подпиши“
        // изглежда като че ли не работи без причина.
        _empty.Dock = DockStyle.Fill;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.ForeColor = Color.DimGray;
        _empty.Visible = false;

        // Филтърът трябва да се заобикаля — чужди карти слагат сертификата в
        // хранилището по свои правила и не бива да оставаме без изход.
        _showAll.Dock = DockStyle.Bottom;
        _showAll.Height = Theme.Scaled(26);
        _showAll.AutoSize = false;
        _showAll.Margin = new Padding(0, 6, 0, 0);
        _showAll.CheckedChanged += (_, _) => FillList();

        // Настройките са в две реда: комбинациите горе, отметката отдолу —
        // така нищо не се отрязва, независимо от ширината на шрифта.
        var options = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Scaled(110),
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(0, 12, 0, 0)
        };

        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        options.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _form.DropDownStyle = ComboBoxStyle.DropDownList;
        _form.Width = Theme.Scaled(210);
        _form.Items.AddRange(["XAdES-BES", Lang.T("Обикновен XML-DSig", "Plain XML-DSig")]);
        _form.SelectedIndex = 0;

        _digest.DropDownStyle = ComboBoxStyle.DropDownList;
        _digest.Width = Theme.Scaled(210);
        _digest.Items.AddRange(["SHA-256", Lang.T("SHA-1 (за стари карти)", "SHA-1 (for older cards)")]);
        _digest.SelectedIndex = 0;

        _canonical.DropDownStyle = ComboBoxStyle.DropDownList;
        _canonical.Width = Theme.Scaled(210);
        _canonical.Items.AddRange(["Exclusive C14N", "Inclusive C14N"]);
        _canonical.SelectedIndex = 0;

        _includeCert.Text = Lang.T("Вграждане на сертификата в подписа (KeyInfo)", "Embed the certificate in the signature (KeyInfo)");
        _includeCert.Checked = true;
        _includeCert.AutoSize = true;
        _includeCert.Margin = new Padding(0, 10, 0, 0);

        options.Controls.Add(Labelled(Lang.T("Вид на подписа:", "Signature type:"), _form), 0, 0);
        options.Controls.Add(Labelled(Lang.T("Алгоритъм:", "Algorithm:"), _digest), 1, 0);
        options.Controls.Add(Labelled(Lang.T("Канонизация:", "Canonicalisation:"), _canonical), 2, 0);
        options.Controls.Add(_includeCert, 0, 1);
        options.SetColumnSpan(_includeCert, 3);

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Scaled(22),
            ForeColor = Color.DimGray,
            Text = Lang.T("Ръководството на НИСО иска XAdES — сменяйте само ако поддръжката каже друго.", "The NWIS manual asks for XAdES — change it only if support says otherwise.")
        };

        // Редът на добавяне определя реда на долепване: последният добавен
        // застава най-близо до ръба. Отгоре надолу: списък, отметка, бележка,
        // настройки.
        host.Controls.Add(_empty);
        host.Controls.Add(_list);
        host.Controls.Add(_showAll);
        host.Controls.Add(hint);
        host.Controls.Add(options);
        return host;
    }

    private static Control Labelled(string text, Control control)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Margin = new Padding(0, 0, 24, 0)
        };

        panel.Controls.Add(new Label { Text = text, AutoSize = true, ForeColor = Color.DimGray });
        panel.Controls.Add(control);
        return panel;
    }

    private Control BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Bottom, Height = Theme.Scaled(58), Padding = new Padding(16, 10, 16, 12) };

        var cancel = new Button
        {
            Text = Lang.T("Отказ", "Cancel"),
            DialogResult = DialogResult.Cancel,
            Width = Theme.Scaled(110),
            Dock = DockStyle.Right
        };

        _ok.Dock = DockStyle.Right;
        _ok.Width = Theme.Scaled(150);
        _ok.Enabled = false;
        _ok.Click += (_, _) => Accept();

        panel.Controls.Add(cancel);
        panel.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.Scaled(10) });
        panel.Controls.Add(_ok);

        CancelButton = cancel;
        return panel;
    }

    // --------------------------------------------------------- сертификати

    /// <summary>
    /// Взима сертификатите от двете обичайни хранилища. КЕП-ът се появява в
    /// „Лични“ на текущия потребител, щом драйверът на картата е инсталиран.
    ///
    /// Взимат се всички с частен ключ; кои от тях се показват решава
    /// <see cref="RankOf"/>, а отметката „покажи всички“ отменя избора му.
    /// </summary>
    private static List<CertificateEntry> LoadCertificates()
    {
        var found = new List<CertificateEntry>();

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

                foreach (var certificate in store.Certificates)
                {
                    if (!certificate.HasPrivateKey) continue;
                    found.Add(new CertificateEntry(certificate, location, RankOf(certificate)));
                }
            }
            catch (Exception)
            {
                // Недостъпно хранилище не е причина прозорецът да не се отвори.
            }
        }

        return found
            .OrderByDescending(e => e.Rank)
            .ThenByDescending(e => e.Certificate.NotAfter)
            .ToList();
    }

    /// <summary>Разширението QCStatements — качественият сертификат го носи.</summary>
    private const string QcStatementsOid = "1.3.6.1.5.5.7.1.3";

    /// <summary>
    /// Колко сертификатът прилича на КЕП.
    ///
    /// Тук не търсим „може ли да подпише“ — почти всичко с частен ключ може.
    /// Търсим „това ли е подписът на човека“, защото всеки друг сертификат
    /// дава технически изряден подпис, който НИСО отхвърля чак след подаване.
    /// На машина за разработка отсяването маха точно трите обичайни находки:
    /// двата сертификата за localhost и този за регистрация на устройството.
    /// </summary>
    /// <returns>2 — прилича на КЕП; 1 — става, но без белезите; 0 — не се предлага.</returns>
    internal static int RankOf(X509Certificate2 certificate)
    {
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();

        // Липсващото разширение вече не значи „всичко е позволено“: точно
        // така сертификатът за регистрация в Entra ID се оказваше в списъка,
        // а понеже е с най-далечна валидност — и пръв по подразбиране.
        if (usage is null) return 0;

        const X509KeyUsageFlags signing =
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation;

        if ((usage.KeyUsages & signing) == 0) return 0;

        // Самоподписаните са машинни — КЕП винаги идва от доставчик.
        if (IsSelfIssued(certificate)) return 0;

        var qualified =
            (usage.KeyUsages & X509KeyUsageFlags.NonRepudiation) != 0 ||
            certificate.Extensions.Any(e => e.Oid?.Value == QcStatementsOid);

        return qualified ? 2 : 1;
    }

    /// <summary>Издателят е самият субект — тоест сертификатът е самоподписан.</summary>
    private static bool IsSelfIssued(X509Certificate2 certificate) =>
        string.Equals(
            certificate.SubjectName.Name,
            certificate.IssuerName.Name,
            StringComparison.OrdinalIgnoreCase);

    private void FillList()
    {
        var hidden = _certificates.Count(e => !e.Offered);

        _showAll.Text = hidden == 0
            ? Lang.T("Показвай всички сертификати", "Show every certificate")
            : Lang.T($"Показвай всички сертификати (скрити: {hidden})", $"Show every certificate ({hidden} hidden)");

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var entry in _certificates.Where(e => _showAll.Checked || e.Offered))
        {
            var certificate = entry.Certificate;
            var expired = certificate.NotAfter < DateTime.Now;

            _list.Items.Add(new ListViewItem(new[]
            {
                XmlSigner.Describe(certificate),
                certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
                certificate.NotAfter.ToString("dd.MM.yyyy"),
                entry.Location == StoreLocation.CurrentUser ? Lang.T("Текущ потребител", "Current user") : Lang.T("Компютър", "Computer")
            })
            {
                Tag = entry,
                ForeColor = expired
                    ? Color.Firebrick
                    : entry.Offered ? SystemColors.ControlText : Color.DimGray
            });
        }

        _list.EndUpdate();

        var any = _list.Items.Count > 0;

        // Никакъв избор по подразбиране: подписването е съзнателно действие,
        // а най-дълго валидният сертификат не е най-вероятният.
        _ok.Enabled = false;

        _list.Visible = any;
        _empty.Visible = !any;

        _empty.Text = _showAll.Checked
            ? Lang.T(
                "В хранилищата няма нито един сертификат с частен ключ.",
                "There is no certificate with a private key in the stores.")
            : Lang.T(
                "Не е намерен сертификат за подпис.\r\n\r\n" +
                "Включете четеца или флашката с КЕП и отворете прозореца отново.",
                "No signing certificate was found.\r\n\r\n" +
                "Plug in the reader or the USB token and open this window again.");

        // Настройките на подписа нямат смисъл без сертификат.
        _form.Enabled = _digest.Enabled = _canonical.Enabled = _includeCert.Enabled = any;
    }

    private CertificateEntry? SelectedEntry() =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as CertificateEntry : null;

    private void Accept()
    {
        if (SelectedEntry() is not { } entry) return;

        var certificate = entry.Certificate;

        // Стига се дотук само през „покажи всички“. Подписът ще е технически
        // изряден, но с чужда самоличност — НИСО го отхвърля след подаването.
        if (!entry.Offered)
        {
            var warning =
                Lang.T(
                    $"„{XmlSigner.Describe(certificate)}“ не прилича на КЕП — това е " +
                    "служебен сертификат на компютъра.\n\nПодписът ще е валиден, но НИСО " +
                    "ще откаже файла.\n\nДа продължим ли въпреки това?",
                    $"“{XmlSigner.Describe(certificate)}” does not look like a signing " +
                    "certificate — it is a machine certificate.\n\nThe signature will be valid, " +
                    "but NWIS will refuse the file.\n\nCarry on anyway?");

            if (MessageBox.Show(this, warning, Lang.T("Неподходящ сертификат", "Unsuitable certificate"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
        }

        if (certificate.NotAfter < DateTime.Now)
        {
            var question =
                Lang.T(
                    $"Сертификатът е изтекъл на {certificate.NotAfter:dd.MM.yyyy}. " +
                    "НИСО най-вероятно ще откаже файла.\n\nДа продължим ли въпреки това?",
                    $"The certificate expired on {certificate.NotAfter:dd.MM.yyyy}. " +
                    "NWIS will most likely refuse the file.\n\nCarry on anyway?");

            if (MessageBox.Show(this, question, Lang.T("Изтекъл сертификат", "Expired certificate"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
        }

        Choice = new SigningChoice(certificate, new SignatureProfile(
            _digest.SelectedIndex == 0 ? SignatureDigest.Sha256 : SignatureDigest.Sha1,
            _canonical.SelectedIndex == 0
                ? SignatureCanonicalization.Exclusive
                : SignatureCanonicalization.Inclusive,
            _includeCert.Checked,
            _form.SelectedIndex == 0 ? SignatureForm.XadesBes : SignatureForm.Dsig));

        DialogResult = DialogResult.OK;
        Close();
    }
}

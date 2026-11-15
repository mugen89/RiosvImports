using RiosvImports.App.Controls;
using RiosvImports.Core;
using RiosvImports.Core.Localization;
using RiosvImports.Core.Schema;

namespace RiosvImports.App.Shell;

/// <summary>
/// Изборът на вид отчет.
///
/// Видовете са четиридесет и два — твърде много за един списък. Затова най-
/// напред се избира видът отчетност, а той свива списъка до няколко реда:
/// годишните са тридесет, тримесечните пет, отчетните книги седем. Едва след
/// това се търси, и то рядко.
///
/// Никъде не се показва име от схемата — само българското заглавие и номерът
/// на приложението, по който хората наричат отчетите помежду си.
/// </summary>
internal sealed class NewReportDialog : Dialog
{
    private readonly ImportService _service;

    private readonly SegmentedControl _kind = new();
    private readonly SearchBox _search = new(Lang.T("Търсете по дума — например „батерии“ или „21“", "Search by word — for example “batteries” or “21”"));
    private readonly ReportList _list = new();
    private readonly Label _count = new();
    private readonly Label _detail = new();

    public NewReportDialog(ImportService service, string? title = null)
    {
        _service = service;

        Text = title ?? Lang.T("Нов отчет", "New report");
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(Theme.Scaled(880), Theme.Scaled(640));
        KeyPreview = true;

        Controls.Add(BuildBody(Text));
        Controls.Add(BuildFooter());

        Refill();

        Theme.Apply(this);
    }

    public SchemaDefinition? Schema { get; private set; }
    public ReportNode? Report { get; private set; }

    private SchemaDefinition Current => _service.Schemas[Math.Max(_kind.SelectedIndex, 0)];

    // ------------------------------------------------------------- устройство

    private Control BuildBody(string title)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(Theme.GapLarge, Theme.GapLarge, Theme.GapLarge, 0),
            BackColor = Theme.Canvas
        };

        var frame = new Card
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            Fill = Theme.Canvas,
            Outline = Theme.Border
        };

        _list.Dock = DockStyle.Fill;
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        _list.DoubleClick += (_, _) => Accept();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;

            Accept();
            e.Handled = true;
        };

        frame.Controls.Add(_list);

        _search.Dock = DockStyle.Top;
        _search.Margin = new Padding(0, 0, 0, Theme.Gap);
        _search.TextChanged += (_, _) => Refill();
        _search.ForwardArrowsTo(_list);

        // Видовете отчетност идват от схемите, но се наричат така, както ги
        // наричат хората, а не както се казват файловете.
        _kind.SetItems(_service.Schemas.Select(ShortKind));
        _kind.Dock = DockStyle.Top;
        _kind.SelectedIndexChanged += (_, _) => { _search.Text = string.Empty; Refill(); };

        var spacerTop = new Panel { Dock = DockStyle.Top, Height = Theme.Gap, BackColor = Theme.Canvas };
        var spacerMid = new Panel { Dock = DockStyle.Top, Height = Theme.Gap, BackColor = Theme.Canvas };

        host.Controls.Add(frame);
        host.Controls.Add(spacerMid);
        host.Controls.Add(_search);
        host.Controls.Add(spacerTop);
        host.Controls.Add(_kind);
        host.Controls.Add(BuildHeading(title));

        return host;
    }

    private Control BuildHeading(string title)
    {
        var host = new Panel { Dock = DockStyle.Top, Height = Theme.Scaled(76), BackColor = Theme.Canvas };

        // Закачените отгоре се редят наопаки: последният добавен застава най-
        // горе. Затова подзаглавието влиза първо, а заглавието — след него.
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(26),
            Text = Lang.T("Изберете вида отчетност, после самия отчет.", "Choose the kind of reporting, then the report itself."),
            Font = Theme.Base,
            ForeColor = Theme.InkMuted
        });

        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(38),
            Text = title,
            Font = Theme.Title,
            ForeColor = Theme.Ink
        });

        return host;
    }

    private Control BuildFooter()
    {
        var host = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Scaled(80),
            Padding = new Padding(Theme.GapLarge, Theme.Gap, Theme.GapLarge, Theme.Gap),
            BackColor = Theme.Canvas
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            Width = Theme.Scaled(290),
            WrapContents = false,
            BackColor = Theme.Canvas
        };

        var ok = Theme.PrimaryButton(Lang.T("Създай", "Create"));
        ok.Glyph = Theme.Glyph.Add;
        ok.Width = Theme.Scaled(150);
        ok.Click += (_, _) => Accept();

        var cancel = Theme.SecondaryButton(Lang.T("Отказ", "Cancel"));
        cancel.Width = Theme.Scaled(110);
        cancel.Height = Theme.Scaled(40);
        cancel.Margin = new Padding(Theme.GapSmall, 0, 0, 0);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var info = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };

        _count.Dock = DockStyle.Top;
        _count.Height = Theme.Scaled(22);
        _count.Font = Theme.Small;
        _count.ForeColor = Theme.InkFaint;

        _detail.Dock = DockStyle.Top;
        _detail.Height = Theme.Scaled(22);
        _detail.Font = Theme.Small;
        _detail.ForeColor = Theme.InkMuted;
        _detail.AutoEllipsis = true;

        info.Controls.Add(_detail);
        info.Controls.Add(_count);

        host.Controls.Add(info);
        host.Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;

        return host;
    }

    /// <summary>Кратките имена на трите вида отчетност.</summary>
    private static string ShortKind(SchemaDefinition schema) => schema.Key switch
    {
        "year" => Lang.T("Годишни", "Annual"),
        "quarter" => Lang.T("Тримесечни", "Quarterly"),
        "book" => Lang.T("Отчетни книги", "Log books"),
        _ => schema.Title
    };

    // ------------------------------------------------------------- списък

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Показалецът стои в списъка, а не в полето за търсене: така стрелките
        // работят веднага, подсказващият текст в полето се вижда, а търсенето
        // започва само от натискането на първата буква — виж по-долу.
        _list.Focus();
    }

    /// <summary>
    /// Писане където и да е в прозореца отива в полето за търсене. Никой не
    /// търси мишката, за да щракне в кутийка, преди да напише „батерии“.
    /// </summary>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);

        if (_search.ContainsFocus || char.IsControl(e.KeyChar)) return;

        _search.Focus();
        _search.Text += e.KeyChar;
        _search.SelectionStart = _search.Text.Length;

        e.Handled = true;
    }

    private void Refill()
    {
        var query = _search.Text.Trim();

        var entries = Current.Reports
            .Select(r => new ReportEntry(r))
            .Where(entry => query.Length == 0 ||
                            entry.Full.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        _list.Fill(entries);

        _count.Text = entries.Count == Current.Reports.Count
            ? Lang.T($"{entries.Count} вида отчет", $"{entries.Count} report types")
            : Lang.T($"{entries.Count} от {Current.Reports.Count} вида", $"{entries.Count} of {Current.Reports.Count} types");

        ShowDetail();
    }

    /// <summary>Пълното заглавие стои долу, за да не се реже в списъка.</summary>
    private void ShowDetail() => _detail.Text = _list.Selected?.Full ?? string.Empty;

    private void Accept()
    {
        if (_list.Selected is not { } entry)
        {
            MessageBox.Show(this, Lang.T("Изберете вид отчет от списъка.", "Choose a report type from the list."), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Schema = Current;
        Report = entry.Report;
        DialogResult = DialogResult.OK;
    }
}

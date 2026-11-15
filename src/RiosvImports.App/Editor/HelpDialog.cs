using RiosvImports.App.Controls;
using RiosvImports.Core.Help;

using RiosvImports.Core.Localization;

namespace RiosvImports.App.Editor;

/// <summary>
/// Упътването за един отчет.
///
/// Excel шаблоните носят лист „УКАЗАНИЯ“ и хората го четат — това е добър
/// признак. Тук същото стои на един клик, но описва самия отчет, а не файла:
/// какви таблици има, какво влиза във всяко поле и какви стойности се приемат.
///
/// Съдържанието се съставя от схемата, не се пише на ръка. Ръчен текст на
/// такова място живее две години и после лъже.
/// </summary>
internal sealed class HelpDialog : Dialog
{
    private readonly ReportGuide _guide;
    private readonly FlowLayoutPanel _stack = new();

    public HelpDialog(ReportGuide guide)
    {
        _guide = guide;

        Text = Lang.T("Как се попълва", "How to fill this in");
        Font = Theme.Base;
        BackColor = Theme.Canvas;
        ClientSize = new Size(Theme.Scaled(940), Theme.Scaled(780));
        MinimumSize = new Size(Theme.Scaled(720), Theme.Scaled(520));
        KeyPreview = true;

        _stack.Dock = DockStyle.Fill;
        _stack.FlowDirection = FlowDirection.TopDown;
        _stack.WrapContents = false;
        _stack.AutoScroll = true;
        _stack.BackColor = Theme.Canvas;
        _stack.Padding = new Padding(Theme.GapLarge, Theme.Gap, Theme.GapLarge, Theme.GapLarge);

        Controls.Add(_stack);
        Controls.Add(BuildHeader());
        Controls.Add(BuildFooter());

        Build();

        Theme.Apply(this);
    }

    /// <summary>
    /// Ширината се раздава отгоре надолу при всяко преоразмеряване.
    ///
    /// Надписите се пренасят чрез MaximumSize с нулева височина: така всеки
    /// сам си изчислява височината, а подредбата в поток събира сбора им.
    /// Опит да се сложи AutoSize на самите карти дава обратното — картата
    /// пита децата за ширина, децата питат картата, и всичко се свива.
    /// </summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var width = Math.Max(360, _stack.ClientSize.Width - _stack.Padding.Horizontal - 4);

        foreach (Control card in _stack.Controls)
        {
            if (card.Width != width) card.Width = width;

            var inner = width - card.Padding.Horizontal;

            foreach (Control child in card.Controls) Spread(child, inner);

            card.Height = card.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical)
                          + card.Padding.Vertical;
        }
    }

    /// <summary>Раздава ширина надолу по дървото и оставя всеки да си каже височината.</summary>
    private static void Spread(Control control, int width)
    {
        if (control is Label label && label.AutoSize)
        {
            label.MaximumSize = new Size(width, 0);
            return;
        }

        control.Width = width;

        foreach (Control child in control.Controls) Spread(child, width - control.Padding.Horizontal);

        if (control is FlowLayoutPanel flow)
        {
            control.Height = flow.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
        }
    }

    // ------------------------------------------------------------- устройство

    private Control BuildHeader()
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.Scaled(92),
            BackColor = Theme.Surface,
            Padding = new Padding(Theme.GapLarge, Theme.Gap, Theme.GapLarge, Theme.Gap)
        };

        host.Controls.Add(new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Scaled(26),
            Font = Theme.Base,
            ForeColor = Theme.InkMuted,
            Text = _guide.Appendix is null
                ? Lang.T("Полетата, отбелязани със звездичка, са задължителни.", "Fields marked with an asterisk are required.")
                : Lang.T($"{_guide.Appendix}   ·   полетата със звездичка са задължителни.", $"{_guide.Appendix}   ·   fields with an asterisk are required.")
        });

        host.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Title,
            ForeColor = Theme.Ink,
            Text = _guide.Title,
            AutoEllipsis = true
        });

        return host;
    }

    private Control BuildFooter()
    {
        var host = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Scaled(70),
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.GapLarge, Theme.Gap, Theme.GapLarge, Theme.Gap)
        };

        var close = Theme.PrimaryButton(Lang.T("Затвори", "Close"));
        close.Dock = DockStyle.Right;
        close.Click += (_, _) => Close();
        close.FitWidth(40);

        host.Controls.Add(close);

        AcceptButton = close;
        CancelButton = close;

        return host;
    }

    private void Build()
    {
        _stack.Controls.Add(Lines(Lang.T("Как се работи", "How it works"), _guide.Steps, numbered: true));
        _stack.Controls.Add(Lines(Lang.T("Правила", "Rules"), _guide.Rules, numbered: false));

        foreach (var table in _guide.Tables) _stack.Controls.Add(TableCard(table));
    }

    // ------------------------------------------------------------- части

    private static Card Shell(string heading, string? subheading = null)
    {
        var card = new Card
        {
            Margin = new Padding(0, 0, 0, Theme.Gap),
            Padding = new Padding(Theme.Gap, Theme.GapSmall, Theme.Gap, Theme.Gap),
            Fill = Theme.SurfaceAlt,
            Outline = Theme.Border,
            Height = Theme.Scaled(60)
        };

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Location = new Point(Theme.Gap, Theme.GapSmall)
        };

        stack.Controls.Add(new Label
        {
            Text = heading,
            Font = Theme.Heading,
            ForeColor = Theme.Accent,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, subheading is null ? Theme.GapSmall : 2)
        });

        if (subheading is not null)
        {
            stack.Controls.Add(new Label
            {
                Text = subheading,
                Font = Theme.Small,
                ForeColor = Theme.InkFaint,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Theme.GapSmall)
            });
        }

        card.Controls.Add(stack);

        return card;
    }

    private static Control Lines(string heading, IReadOnlyList<string> lines, bool numbered)
    {
        var card = Shell(heading);
        var stack = (FlowLayoutPanel)card.Controls[0];

        for (var i = 0; i < lines.Count; i++)
        {
            stack.Controls.Add(new Label
            {
                Text = numbered ? $"{i + 1}.   {lines[i]}" : "•   " + lines[i],
                Font = Theme.Base,
                ForeColor = Theme.InkStrong,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 7)
            });
        }

        return card;
    }

    private static Control TableCard(GuideTable table)
    {
        var card = Shell(table.Title, table.Role);
        var stack = (FlowLayoutPanel)card.Controls[0];

        foreach (var field in table.Fields)
        {
            stack.Controls.Add(new Label
            {
                Text = field.Label + (field.Required ? "  *" : string.Empty),
                Font = Theme.Bold,
                ForeColor = field.Required ? Theme.Ink : Theme.InkStrong,
                AutoSize = true,
                Margin = new Padding(0, Theme.GapSmall, 0, 1)
            });

            stack.Controls.Add(new Label
            {
                Text = Describe(field),
                Font = Theme.Small,
                ForeColor = Theme.InkMuted,
                AutoSize = true,
                Margin = new Padding(14, 0, 0, 0)
            });

            if (field.Values is null) continue;

            stack.Controls.Add(new Label
            {
                Text = field.Values,
                Font = Theme.Small,
                ForeColor = Theme.InkFaint,
                AutoSize = true,
                Margin = new Padding(14, 1, 0, 0)
            });
        }

        return card;
    }

    private static string Describe(GuideField field)
    {
        var parts = new List<string> { field.Kind };

        if (field.Rule is not null) parts.Add(field.Rule);

        return string.Join("   ·   ", parts);
    }

    /// <summary>
    /// Размерите на картите след подреждане — ползва се от тестовете.
    ///
    /// Свита карта е точно това, което се случи първия път: подредбата питаше
    /// картата за ширина, картата питаше децата, а децата — картата. Отвън
    /// изглеждаше като празен прозорец с четири тесни правоъгълника.
    /// </summary>
    internal IReadOnlyList<Size> CardSizes()
    {
        CreateControl();
        PerformLayout();

        return _stack.Controls.Cast<Control>().Select(c => c.Size).ToList();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }

        return base.ProcessCmdKey(ref message, keyData);
    }
}

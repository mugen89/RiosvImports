using System.ComponentModel;

namespace RiosvImports.App.Controls;

/// <summary>
/// Известие на началния екран: заглавие, кога, текст и копчета отдолу.
///
/// Висока колкото текста си, както <see cref="NoticeStrip"/>. Новините идват
/// отвън и са с каквато дължина ги е писал човекът; карта с постоянна височина
/// или реже текста, или стои полупразна. Копчетата се пренасят на втори ред,
/// когато не се побират — не се свиват.
///
/// Подреждането е на ръка, а не с вложени панели: височината зависи от
/// ширината, а ширината идва отвън, от страницата. Вложени панели питат
/// родителя, родителят пита тях — и всичко се сплесква (виж капаните в
/// АРХИТЕКТУРА.md).
/// </summary>
internal sealed class NoticeCard : Card
{
    private readonly Label _title = new();
    private readonly Label _meta = new();
    private readonly Label _body = new();
    private readonly List<AppButton> _actions = [];

    private AppButton? _dismiss;
    private Action? _onDismiss;
    private readonly Dictionary<AppButton, Action> _clicks = [];
    private int _progress = -1;
    private Rectangle _bar;

    public NoticeCard(string title, string meta, string body)
    {
        Fill = Theme.SurfaceAlt;
        Outline = Theme.BorderSoft;
        Stripe = Theme.Accent;
        Margin = new Padding(0, 0, 0, Theme.Scaled(6));
        Padding = new Padding(Theme.Gap + Theme.Scaled(4), Theme.Scaled(10), Theme.Gap, Theme.Scaled(12));

        _title.Font = Theme.Bold;
        _title.ForeColor = Theme.Ink;
        _title.AutoEllipsis = true;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.BackColor = Color.Transparent;

        _meta.Font = Theme.Small;
        _meta.ForeColor = Theme.InkFaint;
        _meta.TextAlign = ContentAlignment.MiddleRight;
        _meta.BackColor = Color.Transparent;

        _body.Font = Theme.Base;
        _body.ForeColor = Theme.InkMuted;
        _body.BackColor = Color.Transparent;

        Controls.Add(_title);
        Controls.Add(_meta);
        Controls.Add(_body);

        Say(title, meta, body);
    }

    /// <summary>Сменя текстовете на място — свалянето обновява заглавието си стотина пъти.</summary>
    public void Say(string title, string meta, string body)
    {
        _title.Text = title;
        _meta.Text = meta;
        _body.Text = body;
        _body.Visible = body.Length > 0;

        Arrange();
    }

    /// <summary>Заглавието — ползва се от тестовете.</summary>
    internal string Heading => _title.Text;

    /// <summary>Текстът — ползва се от тестовете.</summary>
    internal string Message => _body.Text;

    /// <summary>Надписите на копчетата отдолу — ползва се от тестовете.</summary>
    internal IReadOnlyList<string> ActionTexts => [.. _actions.Select(a => a.Text)];

    /// <summary>Може ли да се махне — ползва се от тестовете.</summary>
    internal bool Dismissible => _dismiss is not null;

    /// <summary>Копче отдолу. Основното е едно и стои първо.</summary>
    public AppButton AddAction(string text, string? glyph, ButtonVariant variant, Action click)
    {
        var button = new AppButton(text, variant)
        {
            Glyph = glyph,
            Height = Theme.Scaled(variant == ButtonVariant.Primary ? 36 : 34)
        };

        button.FitWidth(12);
        button.Click += (_, _) => click();

        _actions.Add(button);
        _clicks[button] = click;
        Controls.Add(button);

        Arrange();
        return button;
    }

    /// <summary>
    /// Натиска копчето с този надпис — ползва се от тестовете. Направо, а не с
    /// PerformClick: в непоказан прозорец то не прави нищо.
    /// </summary>
    internal bool Press(string text)
    {
        var button = _actions.FirstOrDefault(a => a.Text == text);
        if (button is null) return false;

        _clicks[button]();
        return true;
    }

    /// <summary>Натиска кръстчето — ползва се от тестовете.</summary>
    internal void PressDismiss() => _onDismiss?.Invoke();

    /// <summary>Кръстчето горе вдясно. Подсказката е и името му за екранния четец.</summary>
    public void AllowDismiss(string tip, ToolTip tips, Action dismiss)
    {
        _dismiss = Theme.GhostButton(string.Empty, Theme.Glyph.Close);
        _dismiss.Size = new Size(Theme.Scaled(30), Theme.Scaled(30));
        _dismiss.AccessibleName = tip;
        _dismiss.Click += (_, _) => dismiss();
        _onDismiss = dismiss;

        tips.SetToolTip(_dismiss, tip);
        Controls.Add(_dismiss);

        Arrange();
    }

    /// <summary>Ивица с напредъка, 0–100. Отрицателно я скрива.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Progress
    {
        get => _progress;
        set
        {
            var next = value < 0 ? -1 : Math.Min(100, value);
            if (next == _progress) return;

            var shown = _progress >= 0;
            _progress = next;

            // Появата и скриването менят височината; самото число — само рисунката.
            if (shown != _progress >= 0) Arrange();
            Invalidate(_bar);
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Arrange();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_progress < 0 || _bar.Width <= 0) return;

        Theme.FillRounded(e.Graphics, _bar, _bar.Height / 2, Theme.BorderSoft);

        var done = (int)Math.Round(_bar.Width * _progress / 100.0);
        if (done <= 0) return;

        Theme.FillRounded(e.Graphics, _bar with { Width = Math.Max(_bar.Height, done) }, _bar.Height / 2, Theme.Accent);
    }

    /// <summary>
    /// Слага всичко по местата му и казва колко висока трябва да е картата.
    /// Тексът се мери със същото, с което прегледът за лаптоп го проверява.
    /// </summary>
    private void Arrange()
    {
        if (Width <= Padding.Horizontal + Theme.Scaled(40)) return;

        var left = Padding.Left;
        var right = Width - Padding.Right;
        var width = right - left;
        var y = Padding.Top;

        var line = Math.Max(_title.Font.Height, _meta.Font.Height) + Theme.Scaled(8);
        var dismissWidth = _dismiss is null ? 0 : _dismiss.Width + Theme.GapSmall;
        var metaWidth = _meta.Text.Length == 0 ? 0 : TextRenderer.MeasureText(_meta.Text, _meta.Font).Width + Theme.Scaled(10);

        // Датата е по-важна от края на дълго заглавие: заглавието се реже с
        // многоточие, датата — никога.
        metaWidth = Math.Min(metaWidth, Math.Max(0, width - dismissWidth - Theme.Scaled(120)));

        _title.SetBounds(left, y, Math.Max(Theme.Scaled(40), width - metaWidth - dismissWidth), line);
        _meta.SetBounds(right - dismissWidth - metaWidth, y, metaWidth, line);
        _meta.Visible = metaWidth > 0;

        _dismiss?.SetBounds(right - _dismiss.Width, y + (line - _dismiss.Height) / 2, _dismiss.Width, _dismiss.Height);

        y += line;

        // По текста, а не по Visible: Visible казва „не“ за всичко в непоказан
        // прозорец, а началният екран се пълни и докато стои скрит зад друг.
        if (_body.Text.Length > 0)
        {
            var need = TextRenderer.MeasureText(_body.Text, _body.Font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak).Height;

            _body.SetBounds(left, y + Theme.Scaled(2), width, need + Theme.Scaled(4));
            y = _body.Bottom;
        }

        if (_progress >= 0)
        {
            _bar = new Rectangle(left, y + Theme.GapSmall, width, Theme.Scaled(6));
            y = _bar.Bottom;
        }
        else
        {
            _bar = Rectangle.Empty;
        }

        if (_actions.Count > 0)
        {
            y += Theme.Scaled(10);

            // Като в поток с пренасяне: каквото не се побира, слиза на нов ред.
            var x = left;
            var top = y;
            var tallest = 0;

            foreach (var action in _actions)
            {
                if (x > left && x + action.Width > right)
                {
                    x = left;
                    top += tallest + Theme.Scaled(6);
                    tallest = 0;
                }

                action.Location = new Point(x, top);
                x += action.Width + Theme.GapSmall;
                tallest = Math.Max(tallest, action.Height);
            }

            y = top + tallest;
        }

        var height = y + Padding.Bottom;
        if (Height != height) Height = height;
    }
}

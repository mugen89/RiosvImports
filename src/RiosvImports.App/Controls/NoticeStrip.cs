namespace RiosvImports.App.Controls;

/// <summary>
/// Лента с предупреждение през цялата ширина на прозореца, висока колкото текста.
///
/// Появява се само когато има какво да каже. Височината се смята от пренесения текст,
/// а не е зададена: предупреждение, отрязано точно там, където назовава движението,
/// е по-лошо от никакво. Закачена отгоре, тя не отстъпва при нисък екран
/// (виж Dialog.KeepWork): обяснението може да се скрие, това — не.
/// </summary>
internal sealed class NoticeStrip : Label
{
    public NoticeStrip()
    {
        AutoSize = false;
        Dock = DockStyle.Top;
        Visible = false;
        Font = Theme.SmallBold;
        ForeColor = Theme.Warn;
        BackColor = Theme.Canvas;
        TextAlign = ContentAlignment.TopLeft;
        Padding = new Padding(Theme.GapLarge, Theme.GapSmall, Theme.GapLarge, 0);
    }

    /// <summary>Казва текста. Празното прибира лентата.</summary>
    public void Say(string text)
    {
        Text = text;
        Visible = text.Length > 0;

        Fit();
    }

    /// <summary>Побира ли се целият текст — за прегледа на екраните.</summary>
    internal bool Fits => Text.Length == 0 || Needed() <= Height;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);

        Fit();
    }

    private void Fit()
    {
        if (Text.Length == 0 || Width <= Padding.Horizontal) return;

        var needed = Needed();

        if (Height != needed) Height = needed;
    }

    private int Needed() =>
        TextRenderer.MeasureText(
            Text,
            Font,
            new Size(Width - Padding.Horizontal, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + Padding.Vertical;
}

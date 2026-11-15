using System.Diagnostics;
using RiosvImports.Core.Localization;

namespace RiosvImports.App;

/// <summary>
/// Отваря адрес в браузъра на човека.
///
/// Само http и https: адресите идват и отвън — от новините — и щракването по
/// тях не бива да пуска нищо друго освен страница.
/// </summary>
internal static class Browser
{
    public static bool Open(string? url, IWin32Window? owner = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner,
                Lang.T("Браузърът не се отвори. Адресът е:\r\n\r\n", "The browser did not open. The address is:\r\n\r\n") +
                uri.AbsoluteUri + "\r\n\r\n" + ex.Message,
                Lang.T("Адресът не се отвори", "The address did not open"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            return false;
        }
    }

    /// <summary>Отваря файл или папка с програмата, която Windows има за тях.</summary>
    public static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Без Notepad или Explorer няма какво да се направи — а и двете ги има.
        }
    }
}

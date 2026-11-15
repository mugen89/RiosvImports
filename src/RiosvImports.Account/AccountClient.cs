using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using RiosvImports.Core.Localization;

namespace RiosvImports.Account;

/// <summary>Грешка от услугата, която има смисъл да се покаже на потребителя.</summary>
public sealed class AccountException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Отговорът при регистрация и при вход.</summary>
public sealed record SignInResult(string RefreshToken, string AccessToken, string Email);

/// <summary>
/// Разговорът със сървъра: регистрация, вход, подновяване и издаване на право
/// на ползване.
///
/// Адресите следват устройството на Supabase, за да може услугата да тръгне
/// без собствен сървър. Ако по-късно отпред застане собствен API, се сменят
/// само адресите в <see cref="AccountOptions"/> — тук нищо не се пипа.
///
/// Всеки метод хвърля <see cref="AccountException"/> с текст на български:
/// повикванията стават при натиснат бутон и съобщението отива право към
/// потребителя.
/// </summary>
public sealed class AccountClient(HttpClient http, AccountOptions options)
{
    private readonly HttpClient _http = http;
    private readonly AccountOptions _options = options;

    /// <summary>Създава профил. Сървърът праща писмо за потвърждение сам.</summary>
    public async Task SignUpAsync(string email, string password, CancellationToken token = default)
    {
        using var request = Build(HttpMethod.Post, _options.AuthBaseUrl, "signup");
        request.Content = JsonContent.Create(new { email, password });

        await SendAsync(request, Lang.T("Регистрацията не беше извършена.", "The sign-up did not go through."), token).ConfigureAwait(false);
    }

    /// <summary>Влиза с електронна поща и парола.</summary>
    public async Task<SignInResult> SignInAsync(string email, string password, CancellationToken token = default)
    {
        using var request = Build(HttpMethod.Post, _options.AuthBaseUrl, "token?grant_type=password");
        request.Content = JsonContent.Create(new { email, password });

        var response = await ReadAsync<TokenResponse>(
            request, Lang.T("Влизането не беше извършено.", "The sign-in did not go through."), token).ConfigureAwait(false);

        return new SignInResult(response.RefreshToken, response.AccessToken, email);
    }

    /// <summary>Подновява достъпа с пазения ключ, без да се пита за парола.</summary>
    public async Task<SignInResult> RefreshAsync(string refreshToken, string email, CancellationToken token = default)
    {
        using var request = Build(HttpMethod.Post, _options.AuthBaseUrl, "token?grant_type=refresh_token");
        request.Content = JsonContent.Create(new { refresh_token = refreshToken });

        var response = await ReadAsync<TokenResponse>(
            request, Lang.T("Достъпът не беше подновен.", "Access was not renewed."), token).ConfigureAwait(false);

        return new SignInResult(response.RefreshToken, response.AccessToken, email);
    }

    /// <summary>
    /// Иска подписано твърдение за абонамента. Устройството се праща, за да
    /// може споделен профил да се забележи — не за да се забрани.
    /// </summary>
    public async Task<string> RequestEntitlementAsync(
        string accessToken, string deviceId, CancellationToken token = default)
    {
        using var request = Build(HttpMethod.Post, _options.FunctionsBaseUrl, "entitlement");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { device = deviceId });

        var response = await ReadAsync<EntitlementResponse>(
            request, Lang.T("Абонаментът не беше проверен.", "The subscription was not checked."), token).ConfigureAwait(false);

        return response.Entitlement;
    }

    // ------------------------------------------------------------- помощни

    private HttpRequestMessage Build(HttpMethod method, Uri baseUrl, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUrl, path));

        // Supabase иска публичния ключ на проекта при всяко повикване.
        request.Headers.TryAddWithoutValidation("apikey", _options.PublicApiKey);

        return request;
    }

    private async Task<T> ReadAsync<T>(HttpRequestMessage request, string failure, CancellationToken token)
    {
        using var response = await SendAsync(request, failure, token).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<T>(token).ConfigureAwait(false)
            ?? throw new AccountException(failure + Lang.T(" Услугата върна празен отговор.", " The service returned an empty answer."));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, string failure, CancellationToken token)
    {
        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AccountException(
                failure + Lang.T("\n\nНяма връзка с услугата. Проверете интернет връзката и опитайте пак.", "\n\nNo connection to the service. Check your internet connection and try again."), ex);
        }

        if (response.IsSuccessStatusCode) return response;

        var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        response.Dispose();

        throw new AccountException(failure + "\n\n" + Explain((int)response.StatusCode, body));
    }

    /// <summary>Превежда отговора на услугата в изречение, което носи полза.</summary>
    private static string Explain(int status, string body) => status switch
    {
        400 when body.Contains("already registered", StringComparison.OrdinalIgnoreCase)
            => Lang.T("Вече има профил с тази електронна поща.", "An account with that email already exists."),
        400 when body.Contains("Password should be", StringComparison.OrdinalIgnoreCase)
            => Lang.T("Паролата е твърде къса — трябва да е поне 8 знака.", "The password is too short — it must be at least 8 characters."),
        400 or 401 => Lang.T("Грешна електронна поща или парола.", "Wrong email or password."),
        403 => Lang.T("Профилът няма достъп. Проверете дали абонаментът е активен.", "The account has no access. Check whether the subscription is active."),
        422 => Lang.T("Електронната поща изглежда невалидна.", "That email address looks invalid."),
        429 => Lang.T("Твърде много опити. Изчакайте минута и опитайте пак.", "Too many attempts. Wait a minute and try again."),
        >= 500 => Lang.T("Услугата не отговаря в момента. Опитайте по-късно.", "The service is not responding right now. Try again later."),
        _ => Lang.T($"Услугата върна грешка {status}.", $"The service returned error {status}.")
    };

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record EntitlementResponse(
        [property: JsonPropertyName("entitlement")] string Entitlement);
}

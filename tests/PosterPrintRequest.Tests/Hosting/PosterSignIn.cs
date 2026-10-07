using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PosterPrintRequest.Tests.Hosting;

internal static class PosterSignIn
{
    internal const string Password = "prueba1";

    public static Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string userName) =>
        SignInAsync(factory, userName, allowAutoRedirect: true);

    public static async Task<HttpClient> SignInAsync(
        WebApplicationFactory<Program> factory,
        string userName,
        bool allowAutoRedirect)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = true
        });
        var response = await PostCredentialsAsync(client, userName, Password);
        if (allowAutoRedirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode || body.Contains("Invalid login attempt.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Sign-in failed for " + userName + ".");
            }
        }
        else if (response.StatusCode != HttpStatusCode.Redirect
            || (response.Headers.Location?.OriginalString ?? "").Contains("error=1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Sign-in failed for " + userName + ".");
        }

        return client;
    }

    public static async Task<HttpResponseMessage> PostCredentialsAsync(HttpClient client, string userName, string password)
    {
        var login = await client.GetAsync("/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        var token = ReadToken(html);
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "account-login",
            ["Input.UserName"] = userName,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
    }

    public static string ReadToken(string html)
    {
        var token = Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        token ??= Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");
        if (token is null)
        {
            throw new InvalidOperationException("The login form did not include an antiforgery token.");
        }

        return System.Net.WebUtility.HtmlDecode(token);
    }

    private static string? Match(string html, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }
}

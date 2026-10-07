using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace PosterPrintRequest.Web;

internal sealed class ApplicationCookieConfigurator : IConfigureNamedOptions<CookieAuthenticationOptions>
{
    private readonly IOptions<SessionSecurityOptions> _session;

    public ApplicationCookieConfigurator(IOptions<SessionSecurityOptions> session)
    {
        _session = session;
    }

    public void Configure(string? name, CookieAuthenticationOptions options)
    {
        if (!string.Equals(name, IdentityConstants.ApplicationScheme, StringComparison.Ordinal))
        {
            return;
        }

        Configure(options);
    }

    public void Configure(CookieAuthenticationOptions options)
    {
        var minutes = _session.Value.IdleTimeoutMinutes;
        if (minutes < 1)
        {
            minutes = 20;
        }

        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = ".PosterPrintRequest.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(minutes);
        options.SlidingExpiration = true;
    }
}

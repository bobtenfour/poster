using Microsoft.AspNetCore.Mvc.Testing;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class PosterWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        InstitutionalTestHost.UseTestAuthentication(builder);
    }
}

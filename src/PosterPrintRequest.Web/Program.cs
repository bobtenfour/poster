using System.Reflection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Components;
using PosterPrintRequest.Web.Requests;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider((context, options) =>
{
    var development = context.HostingEnvironment.IsDevelopment();
    options.ValidateScopes = development;
    options.ValidateOnBuild = development;
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddPosterAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddPosterPrintRequestPersistence(builder.Configuration, builder.Environment.IsProduction());
builder.Services.AddSharedStorage(builder.Configuration, builder.Environment.IsProduction());
builder.Services.AddScoped<DraftFileStore>();
builder.Services.AddScoped<IDraftFileStore>(services => services.GetRequiredService<DraftFileStore>());
builder.Services.AddScoped<IRequesterOptionSource, RequesterOptionCatalog>();
builder.Services.AddScoped<IPosterPreflight, PosterPreflight>();
builder.Services.AddScoped<AcceptedStorage>();
builder.Services.AddScoped<IApprovalSheetCheck, ApprovalSheetCheck>();
builder.Services.AddScoped<IRequesterSubmission, RequesterSubmissionService>();
builder.Services.AddScoped<SignedInAccount>();
builder.Services.AddScoped<AcceptanceLookup>();
builder.Services.AddScoped<ITechnicianWorkflow, TechnicianWorkflow>();
builder.Services.AddScoped<ITechnicianDashboard, TechnicianDashboard>();
builder.Services.AddScoped<ITechnicianLibrary, TechnicianLibrary>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PosterRetentionSchedule>();
builder.Services.AddScoped<IPosterRetention, PosterRetention>();
builder.Services.AddHostedService<PosterRetentionExpiration>();
builder.Services.AddScoped<PrintFolderMigration>();
builder.Services.AddScoped<IPrintingInventory, PrintingInventory>();
builder.Services.AddScoped<IOperatorConfiguration, OperatorConfiguration>();
builder.Services.AddSingleton(services =>
    new PrinterSupplies(services.GetRequiredService<IConfiguration>(), PrinterSupplies.CreateClient()));

// Authentication Contract section 8: Production has no forwarded-header processing; every other environment is unchanged.
var forwardedHeaders = !builder.Environment.IsDevelopment() && !builder.Environment.IsProduction();
if (forwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (forwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.Use(async (context, next) =>
{
    await next();
    if (context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
        && context.Features.Get<IStatusCodePagesFeature>() is { } pages)
    {
        pages.Enabled = false;
    }
});
var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? string.Empty;
var httpsEndpointConfigured = configuredUrls.Contains("https://", StringComparison.OrdinalIgnoreCase);
if (!app.Environment.IsDevelopment() || httpsEndpointConfigured)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAuthentication();
app.UseInstitutionalEntry();
app.UseAuthorization();
app.UseAntiforgery();

app.MapProductionEndpoints();
if (PosterAccess.UsesEntra(app.Environment))
{
    app.MapAccountEndpoints();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var entry = Assembly.GetEntryAssembly()?.GetName().Name;
if (!string.Equals(entry, "ef", StringComparison.OrdinalIgnoreCase))
{
    if (PosterAccess.UsesEntra(app.Environment))
    {
        // Build the Entra sign-in options first so an authentication configuration refusal stops startup.
        app.Services.RequireEntraConfiguration();
    }

    // Resolve the storage service so a storage configuration refusal stops startup.
    _ = app.Services.GetRequiredService<ISharedStorageAccess>();
    await using var scope = app.Services.CreateAsyncScope();
    var migration = scope.ServiceProvider.GetRequiredService<PrintFolderMigration>();
    await migration.MigrateAsync(CancellationToken.None);
}

app.Run();

public partial class Program;

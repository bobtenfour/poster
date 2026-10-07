using System.Reflection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
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

builder.Services.AddPosterAuthentication(builder.Environment, builder.Configuration);
builder.Services.AddPosterPrintRequestPersistence(builder.Configuration);
builder.Services.AddSharedStorage(builder.Configuration);
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
builder.Services.AddScoped<IPrintingInventory, PrintingInventory>();

if (!builder.Environment.IsDevelopment())
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
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
app.UseAuthorization();
app.UseAntiforgery();

app.MapAccountEndpoints();
app.MapProductionEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var entry = Assembly.GetEntryAssembly()?.GetName().Name;
if (!string.Equals(entry, "ef", StringComparison.OrdinalIgnoreCase))
{
    await DemoEvaluationUsersSeeder.SeedAsync(app.Services, app.Environment, app.Logger);
}

app.Run();

public partial class Program;

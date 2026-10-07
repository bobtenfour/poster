using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Tests.Requests;
using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class ConfiguredPosterFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _storageRoot;

    public ConfiguredPosterFactory(string connectionString, string storageRoot)
    {
        _connectionString = connectionString;
        _storageRoot = storageRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}"] = _connectionString,
                [$"{SharedStorageOptions.SectionName}:RootPath"] = _storageRoot
            });
        });

        builder.ConfigureServices(services =>
        {
            var optionsDescriptor = services.SingleOrDefault(service =>
                service.ServiceType == typeof(DbContextOptions<PosterPrintRequestDbContext>));
            if (optionsDescriptor is not null)
            {
                services.Remove(optionsDescriptor);
            }

            var contextDescriptor = services.SingleOrDefault(service =>
                service.ServiceType == typeof(PosterPrintRequestDbContext));
            if (contextDescriptor is not null)
            {
                services.Remove(contextDescriptor);
            }

            services.AddDbContext<PosterPrintRequestDbContext>(options => options.UseSqlServer(_connectionString));
            services.PostConfigure<SharedStorageOptions>(options => options.RootPath = _storageRoot);
        });
    }
}

[Collection("Workflow database")]
public sealed class ProductionReadinessTests : IDisposable
{
    private readonly WorkflowDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-ready-" + Guid.NewGuid().ToString("N"));

    public ProductionReadinessTests(WorkflowDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Health_requires_the_database_and_storage_root_without_revealing_the_path()
    {
        await using var context = _database.CreateContext();
        Assert.True(await ProductionHealth.IsReadyAsync(context, _root, CancellationToken.None));
        Assert.False(await ProductionHealth.IsReadyAsync(context, Path.Combine(_root, "missing"), CancellationToken.None));
    }

    [Fact]
    public async Task Accepted_request_confirmation_files_and_headers_stay_within_the_public_boundary()
    {
        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, _root);
        using var client = await PosterSignIn.SignInAsync(factory, "usera");
        var poster = SamplePosters.Pdf(24, 48);
        string posterId;
        using (var scope = factory.Services.CreateScope())
        {
            var files = scope.ServiceProvider.GetRequiredService<DraftFileStore>();
            var options = scope.ServiceProvider.GetRequiredService<IRequesterOptionSource>();
            var submission = scope.ServiceProvider.GetRequiredService<IRequesterSubmission>();
            var choices = await options.LoadAsync(CancellationToken.None);
            var draft = new RequesterDraft
            {
                DraftId = DraftIds.Create(),
                DepartmentId = choices.Departments.First().Value,
                ReasonId = "",
                Name = "Acceptance Check",
                Room = "18",
                Phone = "555-0130",
                Email = "acceptance@example.edu"
            };
            await using var stream = new MemoryStream(poster);
            var saved = await files.SaveAsync(draft.DraftId, DraftFileRole.Poster, "Poster.pdf", stream, poster.Length, null, CancellationToken.None);
            Assert.True(saved.Saved);
            draft.Poster = new DraftFileState { OriginalFileName = "Poster.pdf", Size = poster.Length, Format = "PDF" };
            var outcome = await submission.SubmitAsync(draft, CancellationToken.None);
            Assert.True(outcome.Saved);
            posterId = outcome.PosterId!;
        }

        var confirmation = await client.GetAsync("/request/accepted/" + posterId);
        confirmation.EnsureSuccessStatusCode();
        var confirmationHtml = await confirmation.Content.ReadAsStringAsync();
        Assert.Contains(posterId, confirmationHtml, StringComparison.Ordinal);
        Assert.Contains("Poster ID", confirmationHtml, StringComparison.Ordinal);
        Assert.Contains("within the next 48 hours", confirmationHtml, StringComparison.Ordinal);
        Assert.Contains("Create the TDX ticket yourself", confirmationHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("WITHOUT-EVENT", confirmationHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, confirmationHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StoragePath", confirmationHtml, StringComparison.Ordinal);
        Assert.Equal("nosniff", confirmation.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", confirmation.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", confirmation.Headers.GetValues("Referrer-Policy").Single());

        var missing = await client.GetAsync("/request/accepted/POSTER-1999-000001");
        missing.EnsureSuccessStatusCode();
        var missingHtml = await missing.Content.ReadAsStringAsync();
        Assert.Contains("Request not found", missingHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, missingHtml, StringComparison.OrdinalIgnoreCase);

        using var technician = await PosterSignIn.SignInAsync(factory, "usero");

        var technicianPage = await technician.GetAsync("/technician/" + posterId);
        technicianPage.EnsureSuccessStatusCode();
        var technicianHtml = await technicianPage.Content.ReadAsStringAsync();
        Assert.Contains("Take this request", technicianHtml, StringComparison.Ordinal);
        Assert.Contains("WITHOUT-EVENT", technicianHtml, StringComparison.Ordinal);
        Assert.Contains(posterId, technicianHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Mark printed", technicianHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, technicianHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-current=\"page\"", technicianHtml, StringComparison.Ordinal);

        var download = await technician.GetAsync("/technician/files/" + posterId + "/poster");
        download.EnsureSuccessStatusCode();
        Assert.Equal(poster, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains("Poster.pdf", download.Content.Headers.ContentDisposition?.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, download.Content.Headers.ToString(), StringComparison.OrdinalIgnoreCase);

        var traversal = await technician.GetAsync("/technician/files/not-a-poster/poster");
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
        Assert.DoesNotContain(_root, await traversal.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var secret = Path.Combine(Directory.GetParent(_root)!.FullName, "secret.pdf");
        await File.WriteAllTextAsync(secret, "secret-bytes");
        await using (var context = _database.CreateContext())
        {
            var departmentId = int.Parse((await new RequesterOptionCatalog(context).LoadAsync(CancellationToken.None)).Departments.First().Value);
            context.PosterRequests.Add(new PosterRequest
            {
                PosterId = "POSTER-2026-000777",
                Name = "Poison Path",
                DepartmentId = departmentId,
                Room = "1",
                Phone = "555-0177",
                Email = "poison@example.edu",
                DateIn = DateTime.Now,
                PosterFile = new PosterFile
                {
                    OriginalFileName = "Poster.pdf",
                    DetectedFormat = "PDF",
                    PageCount = 1,
                    Width = 24,
                    Length = 36,
                    StoragePath = "../secret.pdf"
                },
                PosterProcessing = new PosterProcessing()
            });
            await context.SaveChangesAsync();
        }

        var poisoned = await technician.GetAsync("/technician/files/POSTER-2026-000777/poster");
        Assert.Equal(HttpStatusCode.NotFound, poisoned.StatusCode);
        Assert.DoesNotContain("secret-bytes", await poisoned.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        foreach (var path in new[] { "/health", "/health/ready" })
        {
            var health = await client.GetAsync(path);
            health.EnsureSuccessStatusCode();
            var healthBody = await health.Content.ReadAsStringAsync();
            Assert.Contains("Healthy", healthBody, StringComparison.Ordinal);
            Assert.DoesNotContain(_root, healthBody, StringComparison.OrdinalIgnoreCase);
        }

        if (File.Exists(secret))
        {
            File.Delete(secret);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

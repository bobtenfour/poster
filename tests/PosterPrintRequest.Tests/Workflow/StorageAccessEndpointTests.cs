using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Tests.Requests;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

[Collection("Workflow database")]
public sealed class StorageAccessEndpointTests : IDisposable
{
    private readonly WorkflowDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-endpoint-" + Guid.NewGuid().ToString("N"));

    public StorageAccessEndpointTests(WorkflowDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task A_download_returns_the_stream_opened_by_the_service_and_does_not_reopen_by_path()
    {
        const string relative = "virtual-area/Poster.pdf";
        var bytes = "bytes-served-by-the-service"u8.ToArray();
        var access = new VirtualAccess(relative, bytes);
        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, _root)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<ISharedStorageAccess>(access))));
        await AddPosterAsync("POSTER-2026-000801", relative);
        using var technician = await PosterSignIn.SignInAsync(factory, "usero");

        var response = await technician.GetAsync("/technician/files/POSTER-2026-000801/poster");

        response.EnsureSuccessStatusCode();
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, access.OpenReadCalls);
        Assert.False(File.Exists(Path.Combine(_root, "virtual-area", "Poster.pdf")));
        Assert.Contains("bytes", response.Headers.AcceptRanges);
    }

    [Fact]
    public async Task A_storage_failure_on_download_reveals_no_storage_detail()
    {
        const string relative = "virtual-area/Poster.pdf";
        var access = new VirtualAccess(relative, [1]) { FailOpen = true };
        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, _root)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<ISharedStorageAccess>(access))));
        await AddPosterAsync("POSTER-2026-000802", relative);
        using var technician = await PosterSignIn.SignInAsync(factory, "usero");

        var response = await technician.GetAsync("/technician/files/POSTER-2026-000802/poster");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_root, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("virtual-area", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_production_graph_resolves_every_service_with_scope_and_build_validation()
    {
        IServiceCollection? registered = null;
        using var secret = new ProtectedSecretFile();
        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, _root)
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting(EntraSettings.ClientSecretFileKey, secret.Path);
                builder.UseSetting(
                    "ConnectionStrings:" + PersistenceServiceCollectionExtensions.ConnectionStringName,
                    "Server=placeholder;Database=POSTER;Integrated Security=True;Encrypt=True");
                builder.UseDefaultServiceProvider(options =>
                {
                    options.ValidateOnBuild = true;
                    options.ValidateScopes = true;
                });
                builder.ConfigureTestServices(services =>
                {
                    // Only the physical database is redirected; the production registrations stay.
                    services.RemoveAll<IDbContextOptionsConfiguration<PosterPrintRequestDbContext>>();
                    services.RemoveAll<DbContextOptions<PosterPrintRequestDbContext>>();
                    services.RemoveAll<PosterPrintRequestDbContext>();
                    services.AddDbContext<PosterPrintRequestDbContext>(options => options.UseSqlServer(_database.ConnectionString));
                    registered = services;
                });
            });

        var provider = factory.Services;
        Assert.NotNull(registered);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<PosterPrintRequest.Web.Requests.DraftFileStore>());

        using var scope = provider.CreateScope();
        var failures = new List<string>();
        foreach (var descriptor in registered!.Where(item => !item.ServiceType.IsGenericTypeDefinition && item.ServiceKey is null))
        {
            try
            {
                _ = scope.ServiceProvider.GetService(descriptor.ServiceType);
            }
            catch (Exception exception)
            {
                failures.Add(descriptor.ServiceType.FullName + ": " + exception.GetType().Name);
            }
        }

        Assert.Empty(failures);
        Assert.IsType<SharedStorageAccess>(provider.GetRequiredService<ISharedStorageAccess>());
    }

    private async Task AddPosterAsync(string posterId, string storagePath)
    {
        await using var context = _database.CreateContext();
        await RequesterOptionExamples.EnsureAsync(context);
        var departmentId = int.Parse((await new RequesterOptionCatalog(context).LoadAsync(CancellationToken.None)).Departments.First().Value);
        context.PosterRequests.Add(new PosterRequest
        {
            PosterId = posterId,
            Name = "Stream Check",
            DepartmentId = departmentId,
            DepartmentName = "Department",
            Room = "1",
            Phone = "555-0180",
            Email = "stream@example.edu",
            DateIn = DateTime.Now,
            PosterFile = new PosterFile
            {
                OriginalFileName = "Poster.pdf",
                DetectedFormat = "PDF",
                PageCount = 1,
                Width = 24,
                Length = 36,
                StoragePath = storagePath
            },
            PosterProcessing = new PosterProcessing()
        });
        await context.SaveChangesAsync();
    }

    private sealed class VirtualAccess : ISharedStorageAccess
    {
        private readonly string _path;
        private readonly byte[] _bytes;

        public VirtualAccess(string path, byte[] bytes)
        {
            _path = path;
            _bytes = bytes;
        }

        public int OpenReadCalls { get; private set; }

        public bool FailOpen { get; set; }

        public bool DirectoryExists(string relativePath) => true;

        public bool FileExists(string relativePath) => relativePath == _path;

        public Stream OpenRead(string relativePath)
        {
            OpenReadCalls++;
            if (FailOpen)
            {
                throw new StorageFailureException("OpenRead");
            }

            return new MemoryStream(_bytes, writable: false);
        }

        public void CreateDirectory(string relativePath) => throw new NotSupportedException();

        public IReadOnlyList<StorageEntry> ListEntries(string relativePath) => [];

        public Stream OpenWrite(string relativePath) => throw new NotSupportedException();

        public void CopyFile(string sourceRelative, string destinationRelative) => throw new NotSupportedException();

        public void MoveDirectory(string sourceRelative, string destinationRelative) => throw new NotSupportedException();

        public void DeleteFile(string relativePath) => throw new NotSupportedException();

        public void DeleteDirectory(string relativePath) => throw new NotSupportedException();

        public void HideDirectory(string relativePath) => throw new NotSupportedException();

        public long FileLength(string relativePath) => _bytes.Length;

        public bool IsReparsePoint(string relativePath) => false;
    }
}

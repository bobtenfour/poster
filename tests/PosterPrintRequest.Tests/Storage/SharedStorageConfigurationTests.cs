using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Tests.Storage;

public sealed class SharedStorageConfigurationTests
{
    [Fact]
    public void Initial_shared_storage_root_is_application_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.WebProjectDirectory(), "appsettings.json"), optional: false)
            .Build();

        Assert.Equal(@"C:\posters", configuration[$"{SharedStorageOptions.SectionName}:RootPath"]);
    }

    [Fact]
    public void Shared_storage_root_can_be_replaced_through_configuration()
    {
        var replacedRoot = @"D:\department-posters";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SharedStorageOptions.SectionName}:RootPath"] = replacedRoot
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSharedStorage(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SharedStorageOptions>>().Value;
        Assert.Equal(replacedRoot, options.RootPath);
    }

    [Fact]
    public void Missing_shared_storage_root_fails_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SharedStorageOptions.SectionName}:RootPath"] = " "
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSharedStorage(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            _ = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SharedStorageOptions>>().Value);

        Assert.Contains("Shared storage root path is not configured.", exception.Message);
    }

    [Fact]
    public void Source_does_not_hard_code_the_shared_storage_root()
    {
        var sourceRoot = Path.Combine(RepositoryPaths.Root(), "src");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extension = Path.GetExtension(path);
            if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".razor", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var contents = File.ReadAllText(path);
            if (contents.Contains(@"C:\posters", StringComparison.OrdinalIgnoreCase)
                || contents.Contains(@"C:\\posters", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(path);
            }
        }

        Assert.Empty(offenders);
    }
}

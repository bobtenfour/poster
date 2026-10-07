using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PosterPrintRequest.Infrastructure.Storage;

public static class SharedStorageServiceCollectionExtensions
{
    public static IServiceCollection AddSharedStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SharedStorageOptions>()
            .Bind(configuration.GetSection(SharedStorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RootPath),
                "Shared storage root path is not configured.")
            .ValidateOnStart();

        return services;
    }
}

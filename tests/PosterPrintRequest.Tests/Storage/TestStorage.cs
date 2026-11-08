using Microsoft.Extensions.Logging.Abstractions;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Tests;

internal static class TestStorage
{
    /// <summary>The storage service in process-identity mode over a test root.</summary>
    public static ISharedStorageAccess Access(string root) =>
        new SharedStorageAccess(
            new SharedStorageOptions { RootPath = root },
            NullLogger<SharedStorageAccess>.Instance,
            isProduction: false);
}

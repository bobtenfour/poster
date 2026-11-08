namespace PosterPrintRequest.Infrastructure.Storage;

/// <summary>
/// A storage operation failed. Derives from IOException so callers keep their existing
/// handling. The message and cause never contain a path, account, or server name.
/// </summary>
public sealed class StorageFailureException : IOException
{
    public StorageFailureException(string operation)
        : base("The storage operation " + operation + " failed.")
    {
    }
}

/// <summary>The storage configuration is invalid; the application must not start.</summary>
public sealed class StorageConfigurationException : InvalidOperationException
{
    public StorageConfigurationException(string condition)
        : base(condition)
    {
    }
}

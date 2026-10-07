namespace PosterPrintRequest.Tests;

internal static class RepositoryPaths
{
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PosterPrintRequest.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Solution root was not found.");
    }

    public static string WebProjectDirectory() =>
        Path.Combine(Root(), "src", "PosterPrintRequest.Web");
}

namespace PosterPrintRequest.Web;

public sealed class SessionSecurityOptions
{
    public const string SectionName = "SessionSecurity";

    public int IdleTimeoutMinutes { get; set; } = 20;
}

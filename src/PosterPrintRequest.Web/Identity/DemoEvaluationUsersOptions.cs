namespace PosterPrintRequest.Web;

public sealed class DemoEvaluationUsersOptions
{
    public const string SectionName = "DemoEvaluationUsers";

    public bool Enabled { get; set; }

    public string Password { get; set; } = "";

    public string[] UserNames { get; set; } = [];
}

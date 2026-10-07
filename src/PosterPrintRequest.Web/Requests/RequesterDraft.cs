namespace PosterPrintRequest.Web.Requests;

public sealed class RequesterDraft
{
    public string DraftId { get; set; } = "";

    public string Name { get; set; } = "";

    public string Mentor { get; set; } = "";

    public string DepartmentId { get; set; } = "";

    public string Room { get; set; } = "";

    public string Phone { get; set; } = "";

    public string Email { get; set; } = "";

    public string ReasonId { get; set; } = "";

    public bool LaminationRequested { get; set; }

    public DraftFileState? Poster { get; set; }

    public DraftFileState? ApprovalSheet { get; set; }
}

public sealed class DraftFileState
{
    public string OriginalFileName { get; set; } = "";

    public long Size { get; set; }

    public string Format { get; set; } = "";

    public PosterPreflightResult? Preflight { get; set; }
}

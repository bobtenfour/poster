namespace PosterPrintRequest.Domain;

public sealed class PosterRequest
{
    public int PosterRequestId { get; set; }

    public string PosterId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Mentor { get; set; }

    public int DepartmentId { get; set; }

    public Department Department { get; set; } = null!;

    public string Room { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? SubmittedByUserName { get; set; }

    public int? ReasonId { get; set; }

    public Reason? Reason { get; set; }

    public bool LaminationRequested { get; set; }

    public bool ApprovalSheetUploaded { get; set; }

    public DateTime DateIn { get; set; }

    public PosterFile PosterFile { get; set; } = null!;

    public ApprovalSheet? ApprovalSheet { get; set; }

    public PosterProcessing PosterProcessing { get; set; } = null!;
}

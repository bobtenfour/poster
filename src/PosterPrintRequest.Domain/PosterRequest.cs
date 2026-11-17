namespace PosterPrintRequest.Domain;

public sealed class PosterRequest
{
    public int PosterRequestId { get; set; }

    public string PosterId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Mentor { get; set; }

    public int DepartmentId { get; set; }

    public string DepartmentName { get; set; } = string.Empty;

    public Department Department { get; set; } = null!;

    public string Room { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? SubmittedByAccountName { get; set; }

    public string? SubmittedBySecurityIdentifier { get; set; }

    public Guid? SubmittedByObjectId { get; set; }

    public int? ReasonId { get; set; }

    public string? ReasonName { get; set; }

    public Reason? Reason { get; set; }

    public bool LaminationRequested { get; set; }

    public bool ApprovalSheetUploaded { get; set; }

    public DateTime DateIn { get; set; }

    /// <summary>UTC time the poster directory was moved to temporary retention. Null when it is not retained.</summary>
    public DateTime? TemporaryDeletionAt { get; set; }

    /// <summary>UTC time the retained directory was permanently removed. Null until retention expires.</summary>
    public DateTime? PermanentlyDeletedAt { get; set; }

    /// <summary>Poster directory relative to the shared-storage root before temporary deletion.</summary>
    public string? OriginalDirectory { get; set; }

    /// <summary>Poster title (PHASE 15). Required on new requests; null on requests accepted before PHASE 15.</summary>
    public string? PosterTitle { get; set; }

    /// <summary>TDX ticket number returned by TDX. Written once; unique when set.</summary>
    public int? TdxTicketId { get; set; }

    /// <summary>One of <see cref="TdxTicketStates"/>.</summary>
    public string TdxTicketState { get; set; } = TdxTicketStates.None;

    /// <summary>UTC end of the claim by which one processor owns ticket creation. Null when unclaimed.</summary>
    public DateTime? TdxClaimedUntil { get; set; }

    public List<PosterReplacement> Replacements { get; set; } = [];

    public PosterFile PosterFile { get; set; } = null!;

    public ApprovalSheet? ApprovalSheet { get; set; }

    public PosterProcessing PosterProcessing { get; set; } = null!;
}

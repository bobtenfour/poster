namespace PosterPrintRequest.Domain;

public sealed class Reason
{
    public int ReasonId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool RequiresMentor { get; set; }

    public bool RequiresApprovalSheet { get; set; }

    public ICollection<PosterRequest> PosterRequests { get; set; } = new List<PosterRequest>();
}

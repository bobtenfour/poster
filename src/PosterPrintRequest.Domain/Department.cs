namespace PosterPrintRequest.Domain;

public sealed class Department
{
    public int DepartmentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public ICollection<PosterRequest> PosterRequests { get; set; } = new List<PosterRequest>();
}

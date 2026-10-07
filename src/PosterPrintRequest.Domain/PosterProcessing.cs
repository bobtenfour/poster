namespace PosterPrintRequest.Domain;

public sealed class PosterProcessing
{
    public int PosterProcessingId { get; set; }

    public int PosterRequestId { get; set; }

    public PosterRequest PosterRequest { get; set; } = null!;

    public string? ITPerson { get; set; }

    public DateOnly? Received { get; set; }

    public bool Printed { get; set; }

    public bool Laminated { get; set; }

    public bool Notified { get; set; }

    public DateOnly? DateOut { get; set; }

    public string? PickedUpBy { get; set; }

    public string? Comments { get; set; }
}

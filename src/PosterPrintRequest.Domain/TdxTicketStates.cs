namespace PosterPrintRequest.Domain;

/// <summary>TDX Integration Contract section 7.3.</summary>
public static class TdxTicketStates
{
    /// <summary>Accepted before PHASE 15: no ticket.</summary>
    public const string None = "None";

    /// <summary>No creation may have reached TDX yet; the processor may look up and create.</summary>
    public const string Pending = "Pending";

    public const string Created = "Created";

    /// <summary>A creation may have reached TDX; only lookups are allowed, never a creation.</summary>
    public const string Uncertain = "Uncertain";
}

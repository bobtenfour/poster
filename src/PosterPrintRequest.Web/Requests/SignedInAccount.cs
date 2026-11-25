namespace PosterPrintRequest.Web.Requests;

public sealed class SignedInAccount
{
    public InstitutionalActor? Actor { get; set; }

    /// <summary>Name and email of the signed-in requester (Authentication Contract section 3.9).</summary>
    public RequesterContact? Contact { get; set; }
}

using Microsoft.AspNetCore.Components;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class RequestAccepted : ComponentBase
{
    [Parameter]
    public string? PosterId { get; set; }

    [Inject]
    private AcceptanceLookup Lookup { get; set; } = null!;

    private AcceptanceDetails? Details { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        Details = await Lookup.FindAsync(PosterId, CancellationToken.None);
    }
}

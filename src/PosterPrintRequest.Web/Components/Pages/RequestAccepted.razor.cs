using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class RequestAccepted : ComponentBase
{
    [Parameter]
    public string? PosterId { get; set; }

    [Inject]
    private AcceptanceLookup Lookup { get; set; } = null!;

    [Inject]
    private IInstitutionalActorSource Actors { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private AcceptanceDetails? Details { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        InstitutionalActor? actor = null;
        if (AuthenticationState is not null)
        {
            var state = await AuthenticationState;
            actor = Actors.FromPrincipal(state.User);
        }

        Details = await Lookup.FindAsync(PosterId, actor, CancellationToken.None);
    }
}

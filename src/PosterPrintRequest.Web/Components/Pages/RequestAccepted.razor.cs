using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class RequestAccepted : ComponentBase
{
    [Parameter]
    public string? PosterId { get; set; }

    [Inject]
    private AcceptanceLookup Lookup { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private AcceptanceDetails? Details { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        var name = await SignedInNameAsync();
        Details = await Lookup.FindAsync(PosterId, name, CancellationToken.None);
    }

    private async Task<string?> SignedInNameAsync()
    {
        if (AuthenticationState is null)
        {
            return null;
        }

        var state = await AuthenticationState;
        var name = state.User.Identity?.IsAuthenticated == true ? state.User.Identity.Name : null;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }
}

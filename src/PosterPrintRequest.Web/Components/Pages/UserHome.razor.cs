using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class UserHome
{
    [Inject]
    private PosterPrintRequestDbContext Db { get; set; } = null!;

    [Inject]
    private IInstitutionalActorSource Actors { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private UserActivity Activity { get; set; } = UserActivity.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (AuthenticationState is null)
        {
            return;
        }

        var state = await AuthenticationState;
        var actor = Actors.FromPrincipal(state.User);
        if (actor is null || !actor.IsValid)
        {
            return;
        }

        Activity = await UserActivityQuery.ForActorAsync(Db, actor, CancellationToken.None);
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class UserHome
{
    [Inject]
    private PosterPrintRequestDbContext Db { get; set; } = null!;

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
        var name = state.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Activity = await UserActivityQuery.ForUserAsync(Db, name, CancellationToken.None);
    }
}

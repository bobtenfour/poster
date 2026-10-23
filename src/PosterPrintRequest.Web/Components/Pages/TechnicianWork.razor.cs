using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class TechnicianWork : ComponentBase
{
    private string? _loadedId;

    [Parameter]
    public string? PosterId { get; set; }

    [Inject]
    private ITechnicianWorkflow Workflow { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    [Inject]
    private IInstitutionalActorSource Actors { get; set; } = null!;

    private TechnicianView? View { get; set; }

    private bool Missing { get; set; }

    private bool Busy { get; set; }

    private string LookupId { get; set; } = "";

    private string? LookupError { get; set; }

    private string? Error { get; set; }

    private string TechnicianAccount { get; set; } = "The signed-in identity could not be verified.";

    private InstitutionalActor? Actor { get; set; }

    private string ReceivedOn { get; set; } = "";

    private string DateOut { get; set; } = "";

    private string PickedUpBy { get; set; } = "";

    private string Comments { get; set; } = "";

    private string Stage =>
        View is null ? ""
        : View.Received is null ? "receive"
        : !View.Printed ? "print"
        : View.LaminationRequested && !View.Laminated ? "laminate"
        : !View.Notified ? "notify"
        : View.DateOut is null || string.IsNullOrWhiteSpace(View.PickedUpBy) ? "pickup"
        : "complete";

    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(_loadedId, PosterId, StringComparison.Ordinal))
        {
            return;
        }

        _loadedId = PosterId;
        LookupId = PosterIds.IsPublic(PosterId) ? PosterId! : LookupId;
        LookupError = null;
        await ReloadAsync();
    }

    private Task OnLookupChanged(string? value)
    {
        LookupId = value ?? "";
        return Task.CompletedTask;
    }

    private Task OnReceivedChanged(string? value)
    {
        ReceivedOn = value ?? "";
        return Task.CompletedTask;
    }

    private Task OnDateOutChanged(string? value)
    {
        DateOut = value ?? "";
        return Task.CompletedTask;
    }

    private Task OnPickedUpByChanged(string? value)
    {
        PickedUpBy = value ?? "";
        return Task.CompletedTask;
    }

    private Task OnCommentsChanged(string? value)
    {
        Comments = value ?? "";
        return Task.CompletedTask;
    }

    private void OpenRequest()
    {
        var id = LookupId.Trim();
        if (!PosterIds.IsPublic(id))
        {
            LookupError = "Enter the Poster ID from the acceptance confirmation.";
            return;
        }

        LookupError = null;
        if (string.Equals(id, PosterId, StringComparison.Ordinal))
        {
            return;
        }

        Navigation.NavigateTo("/technician/work/" + id);
    }

    private async Task TakeAsync()
    {
        if (View is null)
        {
            return;
        }

        await RunAsync(() => Workflow.TakeAsync(View.PosterId, Actor, ParseDate(ReceivedOn), Comments, CancellationToken.None));
    }

    private async Task MarkPrintedAsync()
    {
        if (View is null)
        {
            return;
        }

        await RunAsync(() => Workflow.MarkPrintedAsync(View.PosterId, Comments, CancellationToken.None));
    }

    private async Task MarkLaminatedAsync()
    {
        if (View is null)
        {
            return;
        }

        await RunAsync(() => Workflow.MarkLaminatedAsync(View.PosterId, Comments, CancellationToken.None));
    }

    private async Task MarkNotifiedAsync()
    {
        if (View is null)
        {
            return;
        }

        await RunAsync(() => Workflow.MarkNotifiedAsync(View.PosterId, Comments, CancellationToken.None));
    }

    private async Task CompletePickupAsync()
    {
        if (View is null)
        {
            return;
        }

        await RunAsync(() => Workflow.CompletePickupAsync(View.PosterId, ParseDate(DateOut), PickedUpBy, Comments, CancellationToken.None));
    }

    private async Task RunAsync(Func<Task<TechnicianOutcome>> action)
    {
        Busy = true;
        Error = null;
        try
        {
            var outcome = await action();
            if (!outcome.Completed)
            {
                Error = outcome.Message;
                return;
            }

            await ReloadAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ReloadAsync()
    {
        if (!PosterIds.IsPublic(PosterId))
        {
            View = null;
            Missing = !string.IsNullOrWhiteSpace(PosterId);
            return;
        }

        View = await Workflow.FindAsync(PosterId, CancellationToken.None);
        Missing = View is null;
        Comments = View?.Comments ?? "";
        var state = await AuthenticationState.GetAuthenticationStateAsync();
        Actor = Actors.FromPrincipal(state.User);
        TechnicianAccount = Actor is { IsValid: true }
            ? Actor.AccountName
            : "The signed-in identity could not be verified.";
        Error = null;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}

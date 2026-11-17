using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

/// <summary>Poster replacement by its requester (TDX Integration Contract section 17).</summary>
public partial class ReplacePoster : ComponentBase, IDisposable
{
    private readonly Dictionary<string, string> _errors = new();

    [Parameter]
    public string? PosterId { get; set; }

    [Inject]
    private IPosterReplacement Replacement { get; set; } = null!;

    [Inject]
    private IDraftFileStore Files { get; set; } = null!;

    [Inject]
    private IPosterPreflight Preflight { get; set; } = null!;

    [Inject]
    private IInstitutionalActorSource Actors { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private InstitutionalActor? Actor { get; set; }

    private ReplacementTarget? Target { get; set; }

    private bool Loaded { get; set; }

    private bool Done { get; set; }

    private bool Saving { get; set; }

    private bool Uploading { get; set; }

    private bool Checking { get; set; }

    private int Progress { get; set; }

    private int InputKey { get; set; }

    private string DraftId { get; set; } = DraftIds.Create();

    private DraftFileState? Poster { get; set; }

    private string Note { get; set; } = "";

    private string? FormError { get; set; }

    private string? SizeLabel => Poster is null ? null : $"{Poster.Size / 1024.0 / 1024.0:0.#} MB";

    private string? PosterStatus
    {
        get
        {
            if (Uploading)
            {
                return Checking ? "Checking the poster." : $"Uploading {Progress}%.";
            }

            if (Poster?.Preflight is null)
            {
                return Poster is null ? null : "Poster uploaded.";
            }

            return Poster.Preflight.Passed ? "Poster checked." : "Poster needs a correction.";
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (AuthenticationState is not null)
        {
            var state = await AuthenticationState;
            Actor = Actors.FromPrincipal(state.User);
        }

        Target = await Replacement.FindAsync(PosterId, Actor, CancellationToken.None);
        Loaded = true;
    }

    private string? Error(string key) => _errors.TryGetValue(key, out var message) ? message : null;

    private Task OnNoteChanged(string? value)
    {
        Note = value ?? "";
        return Task.CompletedTask;
    }

    private Task RemovePosterAsync()
    {
        Files.Remove(DraftId, DraftFileRole.Poster);
        Poster = null;
        Progress = 0;
        InputKey++;
        _errors.Remove("poster");
        return Task.CompletedTask;
    }

    private async Task OnPosterSelected(InputFileChangeEventArgs args)
    {
        _errors.Remove("poster");
        if (args.FileCount != 1)
        {
            _errors["poster"] = "Choose one file.";
            return;
        }

        var file = args.File;
        var selection = FileSelectionRules.InspectPoster(file.Name, file.Size);
        if (!selection.Succeeded)
        {
            _errors["poster"] = selection.Error!;
            return;
        }

        Uploading = true;
        Progress = 0;
        var progress = new Progress<int>(value =>
        {
            Progress = value;
            _ = InvokeAsync(StateHasChanged);
        });

        try
        {
            await using var stream = file.OpenReadStream(UploadLimits.MaxBytes);
            var saved = await Files.SaveAsync(DraftId, DraftFileRole.Poster, file.Name, stream, file.Size, progress, CancellationToken.None);
            if (!saved.Saved)
            {
                _errors["poster"] = saved.Error ?? "The file could not be stored. Choose it again.";
                return;
            }

            var state = new DraftFileState
            {
                OriginalFileName = Path.GetFileName(file.Name),
                Size = file.Size,
                Format = saved.Format ?? selection.Format!
            };
            Poster = state;
            Checking = true;
            await InvokeAsync(StateHasChanged);
            state.Preflight = await Preflight.InspectStoredPosterAsync(DraftId, CancellationToken.None);
            Checking = false;
            if (state.Preflight is null)
            {
                Poster = null;
                _errors["poster"] = "The file could not be stored. Choose it again.";
            }
            else if (!state.Preflight.Passed)
            {
                _errors["poster"] = state.Preflight.Summary;
            }
        }
        catch (IOException)
        {
            _errors["poster"] = "The file could not be stored. Choose it again.";
        }
        finally
        {
            Uploading = false;
            Checking = false;
        }
    }

    private async Task SubmitAsync()
    {
        FormError = null;
        _errors.Clear();
        Saving = true;
        try
        {
            var outcome = await Replacement.ReplaceAsync(
                PosterId,
                Actor,
                DraftId,
                Poster?.OriginalFileName ?? "",
                Note,
                CancellationToken.None);
            if (!outcome.Completed)
            {
                foreach (var (key, message) in outcome.FieldErrors)
                {
                    _errors[key] = message;
                }

                FormError = outcome.Message;
                return;
            }

            Done = true;
            DraftId = "";
        }
        finally
        {
            Saving = false;
        }
    }

    public void Dispose()
    {
        if (!Done && DraftIds.IsValid(DraftId))
        {
            try
            {
                Files.Remove(DraftId, DraftFileRole.Poster);
            }
            catch (IOException)
            {
            }
        }
    }
}

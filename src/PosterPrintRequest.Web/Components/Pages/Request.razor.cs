using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PosterPrintRequest.Web.Components.Foundation;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class Request : ComponentBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, string> _errors = new();

    [Inject]
    private IRequesterOptionSource Options { get; set; } = null!;

    [Inject]
    private IDraftFileStore Files { get; set; } = null!;

    [Inject]
    private IPosterPreflight Preflight { get; set; } = null!;

    [Inject]
    private IApprovalSheetCheck ApprovalSheets { get; set; } = null!;

    [Inject]
    private IRequesterSubmission Submission { get; set; } = null!;

    [Inject]
    private SignedInAccount Account { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private IJSRuntime Js { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    private RequesterDraft Draft { get; set; } = new();

    private RequesterChoices Choices { get; set; } = new();

    private bool PosterUploading { get; set; }

    private bool CheckingPoster { get; set; }

    private bool ApprovalUploading { get; set; }

    private int PosterProgress { get; set; }

    private int ApprovalProgress { get; set; }

    private int PosterInputKey { get; set; }

    private int ApprovalInputKey { get; set; }

    private bool Saving { get; set; }

    private string? FormError { get; set; }

    private bool Busy => Saving || PosterUploading || ApprovalUploading;

    private bool MentorRequired => SelectedReason?.RequiresMentor == true;

    private bool ApprovalRequired => SelectedReason?.RequiresApprovalSheet == true;

    private ReasonChoice? SelectedReason => Choices.FindReason(Draft.ReasonId);

    private IReadOnlyList<SelectOption> DepartmentOptions => Choices.Departments;

    private IReadOnlyList<SelectOption> ReasonOptions =>
        Choices.Reasons.Select(reason => new SelectOption(reason.Id.ToString(), reason.Name)).ToList();

    private string? PosterStatus
    {
        get
        {
            if (PosterUploading)
            {
                return CheckingPoster ? "Checking the poster." : $"Uploading {PosterProgress}%.";
            }

            if (Draft.Poster?.Preflight is null)
            {
                return Draft.Poster is null ? null : "Poster uploaded.";
            }

            return Draft.Poster.Preflight.Passed ? "Poster checked." : "Poster needs a correction.";
        }
    }

    private string? ApprovalStatus
    {
        get
        {
            if (ApprovalUploading)
            {
                return $"Uploading {ApprovalProgress}%.";
            }

            if (Draft.ApprovalSheet is null)
            {
                return null;
            }

            return _errors.ContainsKey("approval")
                ? "Approval Sheet needs a correction."
                : "Approval Sheet uploaded.";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        Choices = await Options.LoadAsync(CancellationToken.None);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var json = await Js.InvokeAsync<string?>("posterDraft.load");
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        RequesterDraft? stored;
        try
        {
            stored = JsonSerializer.Deserialize<RequesterDraft>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (stored is null || !DraftIds.IsValid(stored.DraftId))
        {
            return;
        }

        if (stored.Poster is not null && !Files.Exists(stored.DraftId, DraftFileRole.Poster))
        {
            stored.Poster = null;
        }
        else if (stored.Poster is not null)
        {
            stored.Poster.Preflight = await Preflight.InspectStoredPosterAsync(stored.DraftId, CancellationToken.None);
            if (stored.Poster.Preflight is null)
            {
                stored.Poster = null;
            }
            else if (!stored.Poster.Preflight.Passed)
            {
                _errors["poster"] = stored.Poster.Preflight.Summary;
            }
        }

        if (stored.ApprovalSheet is not null && !Files.Exists(stored.DraftId, DraftFileRole.ApprovalSheet))
        {
            stored.ApprovalSheet = null;
        }
        else if (stored.ApprovalSheet is not null)
        {
            var approvalError = await ApprovalSheets.ValidateStoredAsync(stored.DraftId, CancellationToken.None);
            if (approvalError is not null)
            {
                _errors["approval"] = approvalError;
            }
        }

        Draft = stored;
        await PersistDraftAsync();
        StateHasChanged();
    }

    private string? Error(string key) => _errors.TryGetValue(key, out var message) ? message : null;

    private async Task OnNameChanged(string? value)
    {
        Draft.Name = value ?? "";
        await PersistDraftAsync();
    }

    private async Task OnMentorChanged(string? value)
    {
        Draft.Mentor = value ?? "";
        await PersistDraftAsync();
    }

    private async Task OnDepartmentChanged(string? value)
    {
        Draft.DepartmentId = value ?? "";
        await PersistDraftAsync();
    }

    private async Task OnRoomChanged(string? value)
    {
        Draft.Room = value ?? "";
        await PersistDraftAsync();
    }

    private async Task OnPhoneChanged(string? value)
    {
        Draft.Phone = value ?? "";
        await PersistDraftAsync();
    }

    private async Task OnEmailChanged(string? value)
    {
        Draft.Email = value ?? "";
        await PersistDraftAsync();
    }

    private async Task OnReasonChanged(string? value)
    {
        Draft.ReasonId = value ?? "";
        if (!ApprovalRequired)
        {
            await RemoveApprovalAsync();
            return;
        }

        await PersistDraftAsync();
    }

    private async Task OnLaminationChanged(bool value)
    {
        Draft.LaminationRequested = value;
        await PersistDraftAsync();
    }

    private Task OnPosterSelected(InputFileChangeEventArgs args) =>
        StoreFileAsync(args, DraftFileRole.Poster);

    private Task OnApprovalSelected(InputFileChangeEventArgs args) =>
        StoreFileAsync(args, DraftFileRole.ApprovalSheet);

    private async Task RemovePosterAsync()
    {
        if (DraftIds.IsValid(Draft.DraftId))
        {
            Files.Remove(Draft.DraftId, DraftFileRole.Poster);
        }

        Draft.Poster = null;
        PosterProgress = 0;
        PosterInputKey++;
        _errors.Remove("poster");
        await PersistDraftAsync();
    }

    private async Task RemoveApprovalAsync()
    {
        if (DraftIds.IsValid(Draft.DraftId))
        {
            Files.Remove(Draft.DraftId, DraftFileRole.ApprovalSheet);
        }

        Draft.ApprovalSheet = null;
        ApprovalProgress = 0;
        ApprovalInputKey++;
        _errors.Remove("approval");
        await PersistDraftAsync();
    }

    private async Task SubmitAsync()
    {
        FormError = null;
        _errors.Clear();
        EnsureDraftId();
        if (AuthenticationState is not null)
        {
            var state = await AuthenticationState;
            var name = state.User.Identity?.IsAuthenticated == true ? state.User.Identity.Name : null;
            Account.UserName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        var outcome = await Submission.SubmitAsync(Draft, CancellationToken.None);
        if (!outcome.Saved)
        {
            foreach (var (key, message) in outcome.FieldErrors)
            {
                _errors[key] = message;
            }

            FormError = outcome.Message;
            return;
        }

        await Js.InvokeVoidAsync("posterDraft.clear");
        if (string.IsNullOrWhiteSpace(outcome.PosterId))
        {
            FormError = "The request could not be accepted. Submit it again.";
            return;
        }

        Navigation.NavigateTo("/request/accepted/" + outcome.PosterId);
    }

    private async Task StoreFileAsync(InputFileChangeEventArgs args, DraftFileRole role)
    {
        var errorKey = role == DraftFileRole.Poster ? "poster" : "approval";
        _errors.Remove(errorKey);
        if (args.FileCount != 1)
        {
            _errors[errorKey] = "Choose one file.";
            return;
        }

        var file = args.File;
        var selection = role == DraftFileRole.Poster
            ? FileSelectionRules.InspectPoster(file.Name, file.Size)
            : FileSelectionRules.InspectApprovalSheet(file.Name, file.Size);
        if (!selection.Succeeded)
        {
            _errors[errorKey] = selection.Error!;
            return;
        }

        EnsureDraftId();
        SetUploading(role, true, 0);
        var progress = new Progress<int>(value =>
        {
            SetProgress(role, value);
            _ = InvokeAsync(StateHasChanged);
        });

        try
        {
            await using var stream = file.OpenReadStream(UploadLimits.MaxBytes);
            var saved = await Files.SaveAsync(
                Draft.DraftId,
                role,
                file.Name,
                stream,
                file.Size,
                progress,
                CancellationToken.None);
            if (!saved.Saved)
            {
                _errors[errorKey] = saved.Error ?? "The file could not be stored. Choose it again.";
                return;
            }

            var state = new DraftFileState
            {
                OriginalFileName = Path.GetFileName(file.Name),
                Size = file.Size,
                Format = saved.Format ?? selection.Format!
            };
            if (role == DraftFileRole.Poster)
            {
                Draft.Poster = state;
                CheckingPoster = true;
                SetProgress(role, 100);
                await InvokeAsync(StateHasChanged);
                state.Preflight = await Preflight.InspectStoredPosterAsync(Draft.DraftId, CancellationToken.None);
                CheckingPoster = false;
                if (state.Preflight is null)
                {
                    Draft.Poster = null;
                    _errors[errorKey] = "The file could not be stored. Choose it again.";
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(state.Preflight.DetectedFormat))
                    {
                        state.Format = state.Preflight.DetectedFormat;
                    }

                    if (!state.Preflight.Passed)
                    {
                        _errors[errorKey] = state.Preflight.Summary;
                    }
                }
            }
            else
            {
                Draft.ApprovalSheet = state;
                var approvalError = await ApprovalSheets.ValidateStoredAsync(Draft.DraftId, CancellationToken.None);
                if (approvalError is not null)
                {
                    _errors[errorKey] = approvalError;
                }
            }
        }
        catch (IOException)
        {
            _errors[errorKey] = "The file could not be stored. Choose it again.";
        }
        finally
        {
            SetUploading(role, false, 100);
        }

        await PersistDraftAsync();
    }

    private void EnsureDraftId()
    {
        if (!DraftIds.IsValid(Draft.DraftId))
        {
            Draft.DraftId = DraftIds.Create();
        }
    }

    private async Task PersistDraftAsync()
    {
        EnsureDraftId();
        await Js.InvokeVoidAsync("posterDraft.save", JsonSerializer.Serialize(Draft, JsonOptions));
    }

    private void SetUploading(DraftFileRole role, bool uploading, int progress)
    {
        if (role == DraftFileRole.Poster)
        {
            PosterUploading = uploading;
            PosterProgress = progress;
            return;
        }

        ApprovalUploading = uploading;
        ApprovalProgress = progress;
    }

    private void SetProgress(DraftFileRole role, int progress)
    {
        if (role == DraftFileRole.Poster)
        {
            PosterProgress = progress;
            return;
        }

        ApprovalProgress = progress;
    }

    private static string? FormatSize(long? size)
    {
        if (size is null)
        {
            return null;
        }

        var value = size.Value;
        if (value < 1024)
        {
            return $"{value} B";
        }

        if (value < 1024 * 1024)
        {
            return $"{value / 1024.0:0.#} KB";
        }

        return $"{value / 1024.0 / 1024.0:0.#} MB";
    }
}

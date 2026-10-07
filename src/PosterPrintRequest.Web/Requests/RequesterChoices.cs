using PosterPrintRequest.Web.Components.Foundation;

namespace PosterPrintRequest.Web.Requests;

public sealed record ReasonChoice(int Id, string Name, bool RequiresMentor, bool RequiresApprovalSheet);

public sealed class RequesterChoices
{
    public IReadOnlyList<SelectOption> Departments { get; init; } = [];

    public IReadOnlyList<ReasonChoice> Reasons { get; init; } = [];

    public ReasonChoice? FindReason(string? reasonId) =>
        int.TryParse(reasonId, out var id)
            ? Reasons.FirstOrDefault(reason => reason.Id == id)
            : null;
}

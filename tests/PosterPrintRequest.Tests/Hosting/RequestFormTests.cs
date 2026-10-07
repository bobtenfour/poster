using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Web.Components.Foundation;
using PosterPrintRequest.Web.Components.Pages;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class RequestFormTests : TestContext
{
    private readonly MemoryFiles _files = new();
    private readonly ScriptedPreflight _preflight = new();

    public RequestFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IRequesterOptionSource>(new FixedOptions());
        Services.AddSingleton<IDraftFileStore>(_files);
        Services.AddSingleton<IPosterPreflight>(_preflight);
        Services.AddSingleton<IApprovalSheetCheck>(new PassingApproval());
        Services.AddSingleton<IRequesterSubmission>(new RejectingSubmission());
        Services.AddScoped<SignedInAccount>();
    }

    [Fact]
    public void Event_selection_controls_mentor_and_approval_sheet()
    {
        var cut = RenderComponent<Request>();

        Assert.Contains("Example Department", cut.Markup);
        Assert.Contains("href=\"/help#poster-size\"", cut.Markup);
        Assert.DoesNotContain("id=\"approval-file\"", cut.Markup);
        Assert.Equal("false", cut.Find("#requester-mentor").GetAttribute("aria-required"));

        cut.Find("#requester-reason").Change("2");
        Assert.Equal("true", cut.Find("#requester-mentor").GetAttribute("aria-required"));
        Assert.DoesNotContain("id=\"approval-file\"", cut.Markup);

        cut.Find("#requester-reason").Change("3");
        Assert.Equal("false", cut.Find("#requester-mentor").GetAttribute("aria-required"));
        Assert.Contains("id=\"approval-file\"", cut.Markup);

        cut.Find("#requester-reason").Change("");
        Assert.DoesNotContain("id=\"approval-file\"", cut.Markup);
    }

    [Fact]
    public void Entered_details_are_saved_for_the_help_round_trip()
    {
        var cut = RenderComponent<Request>();

        cut.Find("#requester-name").Change("Ada Lovelace");

        cut.WaitForAssertion(() =>
        {
            var saved = JSInterop.Invocations.Single(invocation => invocation.Identifier == "posterDraft.save");
            Assert.Contains("Ada Lovelace", saved.Arguments[0]?.ToString());
        });
    }

    [Fact]
    public void Restored_draft_keeps_the_entered_name()
    {
        JSInterop.Setup<string?>("posterDraft.load").SetResult(
            "{\"draftId\":\"0123456789abcdef0123456789abcdef\",\"name\":\"Ada Lovelace\",\"departmentId\":\"1\",\"reasonId\":\"2\"}");

        var cut = RenderComponent<Request>();

        cut.WaitForAssertion(() => Assert.Equal("Ada Lovelace", cut.Find("#requester-name").GetAttribute("value")));
    }

    [Fact]
    public void Submit_reports_missing_fields_without_leaving_the_form()
    {
        var cut = RenderComponent<Request>();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Enter your name.", cut.Markup));
        Assert.Equal("http://localhost/", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Replacing_a_rejected_poster_keeps_the_rest_of_the_request()
    {
        _files.PosterPresent = true;
        _preflight.Stored = new PosterPreflightResult
        {
            Passed = false,
            Summary = "The poster is 40 inches wide. The maximum width is 36 inches. Correct the file and upload it again.",
            DetectedFormat = "PDF",
            PageCount = 1,
            WidthInches = 40,
            LengthInches = 48,
            Checks =
            [
                new PosterPreflightCheck { Label = "Format", Passed = true, Detail = "PDF" },
                new PosterPreflightCheck { Label = "Pages", Passed = true, Detail = "1 page" },
                new PosterPreflightCheck { Label = "Width", Passed = false, Detail = "40 inches wide. The maximum is 36 inches." },
                new PosterPreflightCheck { Label = "Length", Passed = true, Detail = "48 inches long. The maximum is 72 inches." }
            ]
        };
        JSInterop.Setup<string?>("posterDraft.load").SetResult(
            "{\"draftId\":\"0123456789abcdef0123456789abcdef\",\"name\":\"Ada Lovelace\",\"mentor\":\"Grace Hopper\",\"departmentId\":\"1\",\"room\":\"214\",\"phone\":\"555-0100\",\"email\":\"ada@example.edu\",\"reasonId\":\"1\",\"poster\":{\"originalFileName\":\"Wide.pdf\",\"size\":1200,\"format\":\"PDF\"}}");

        var cut = RenderComponent<Request>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("40 inches wide", cut.Markup);
            Assert.Contains("Needs a correction", cut.Markup);
            Assert.Contains("Width is measured across", cut.Markup);
            Assert.Equal("Ada Lovelace", cut.Find("#requester-name").GetAttribute("value"));
        });

        cut.FindAll("button").Single(button => button.TextContent.Contains("Remove", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("40 inches wide", cut.Markup);
            Assert.DoesNotContain("id=\"poster-preflight\"", cut.Markup);
            Assert.Equal("Ada Lovelace", cut.Find("#requester-name").GetAttribute("value"));
            Assert.Equal("Grace Hopper", cut.Find("#requester-mentor").GetAttribute("value"));
            Assert.Equal("214", cut.Find("#requester-room").GetAttribute("value"));
        });
    }

    private sealed class FixedOptions : IRequesterOptionSource
    {
        public Task<RequesterChoices> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RequesterChoices
            {
                Departments = [new SelectOption("1", "Example Department")],
                Reasons =
                [
                    new ReasonChoice(1, "Example event", false, false),
                    new ReasonChoice(2, "Example event, mentor required", true, false),
                    new ReasonChoice(3, "Example event, approval sheet required", false, true)
                ]
            });
    }

    private sealed class MemoryFiles : IDraftFileStore
    {
        public Task<DraftSaveResult> SaveAsync(
            string draftId,
            DraftFileRole role,
            string originalFileName,
            Stream content,
            long size,
            IProgress<int>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DraftSaveResult(true, null, "PDF"));

        public void Remove(string draftId, DraftFileRole role)
        {
        }

        public bool PosterPresent { get; set; }

        public bool Exists(string draftId, DraftFileRole role) =>
            role == DraftFileRole.Poster && PosterPresent;
    }

    private sealed class ScriptedPreflight : IPosterPreflight
    {
        public PosterPreflightResult? Stored { get; set; }

        public PosterPreflightResult Inspect(Stream content) =>
            Stored ?? new PosterPreflightResult { Passed = false, Summary = "Choose the poster file." };

        public Task<PosterPreflightResult?> InspectStoredPosterAsync(string draftId, CancellationToken cancellationToken) =>
            Task.FromResult(Stored);
    }

    private sealed class PassingApproval : IApprovalSheetCheck
    {
        public Task<string?> ValidateStoredAsync(string draftId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class RejectingSubmission : IRequesterSubmission
    {
        public Task<SubmissionOutcome> SubmitAsync(RequesterDraft draft, CancellationToken cancellationToken) =>
            Task.FromResult(SubmissionOutcome.Failure(
                "Correct the highlighted fields.",
                new Dictionary<string, string> { ["name"] = "Enter your name." }));
    }
}

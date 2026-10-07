using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Web.Components.Pages;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class TechnicianPageTests : TestContext
{
    private readonly ScriptedWorkflow _workflow = new();

    public TechnicianPageTests()
    {
        Services.AddSingleton<ITechnicianWorkflow>(_workflow);
        Services.AddSingleton<AuthenticationStateProvider, AnonymousAuthenticationStateProvider>();
    }

    [Fact]
    public void Only_the_receive_action_is_available_before_the_request_is_taken()
    {
        _workflow.View = Sample(received: null, printed: false, lamination: true);
        var cut = RenderComponent<TechnicianWork>(parameters => parameters.Add(page => page.PosterId, "POSTER-2026-000001"));

        Assert.Contains("Take this request", cut.Markup);
        Assert.Contains("id=\"technician-it-person\"", cut.Markup);
        Assert.Contains("id=\"technician-received\"", cut.Markup);
        Assert.Contains("WITHOUT-EVENT/2026/Ada Lovelace - POSTER-2026-000001", cut.Markup);
        Assert.DoesNotContain("Mark printed", cut.Markup);
        Assert.DoesNotContain("Mark laminated", cut.Markup);
        Assert.DoesNotContain("Mark notified", cut.Markup);
        Assert.DoesNotContain("Record pickup", cut.Markup);
    }

    [Fact]
    public void Lamination_does_not_appear_when_it_was_not_requested()
    {
        _workflow.View = Sample(received: new DateOnly(2026, 10, 6), printed: true, lamination: false);
        var cut = RenderComponent<TechnicianWork>(parameters => parameters.Add(page => page.PosterId, "POSTER-2026-000002"));

        Assert.Contains("Mark notified", cut.Markup);
        Assert.Contains("Not requested", cut.Markup);
        Assert.DoesNotContain("Mark laminated", cut.Markup);
        Assert.DoesNotContain("Record pickup", cut.Markup);
    }

    private static TechnicianView Sample(DateOnly? received, bool printed, bool lamination) => new()
    {
        PosterId = received is null ? "POSTER-2026-000001" : "POSTER-2026-000002",
        RequesterName = "Ada Lovelace",
        Department = "Example Department",
        Room = "214",
        Phone = "555-0100",
        Email = "ada@example.edu",
        LaminationRequested = lamination,
        DateIn = new DateTime(2026, 10, 6, 9, 30, 0),
        PosterFileName = "Poster.pdf",
        Folder = "WITHOUT-EVENT/2026/Ada Lovelace - " + (received is null ? "POSTER-2026-000001" : "POSTER-2026-000002"),
        Received = received,
        Printed = printed,
        ItPerson = received is null ? null : "Jordan Lee"
    };

    private sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class ScriptedWorkflow : ITechnicianWorkflow
    {
        public TechnicianView? View { get; set; }

        public Task<TechnicianView?> FindAsync(string? posterId, CancellationToken cancellationToken) =>
            Task.FromResult(View);

        public Task<TechnicianOutcome> TakeAsync(string posterId, string? itPerson, DateOnly? received, string? comments, CancellationToken cancellationToken) =>
            Task.FromResult(TechnicianOutcome.Failure("unused"));

        public Task<TechnicianOutcome> MarkPrintedAsync(string posterId, string? comments, CancellationToken cancellationToken) =>
            Task.FromResult(TechnicianOutcome.Failure("unused"));

        public Task<TechnicianOutcome> MarkLaminatedAsync(string posterId, string? comments, CancellationToken cancellationToken) =>
            Task.FromResult(TechnicianOutcome.Failure("unused"));

        public Task<TechnicianOutcome> MarkNotifiedAsync(string posterId, string? comments, CancellationToken cancellationToken) =>
            Task.FromResult(TechnicianOutcome.Failure("unused"));

        public Task<TechnicianOutcome> CompletePickupAsync(string posterId, DateOnly? dateOut, string? pickedUpBy, string? comments, CancellationToken cancellationToken) =>
            Task.FromResult(TechnicianOutcome.Failure("unused"));
    }
}

using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Web.Tdx;

namespace PosterPrintRequest.Tests.Persistence;

/// <summary>TDX Integration Contract sections 7 and 8A against SQL Server: claim, marker, result and backstop.</summary>
[Collection(SqlServerDatabaseCollection.Name)]
public sealed class TdxTicketStoreTests
{
    private readonly SqlServerDatabaseFixture _database;

    public TdxTicketStoreTests(SqlServerDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Only_one_claim_succeeds_and_results_require_the_claim()
    {
        var id = await AddRequestAsync(TdxTicketStates.Pending);
        var now = DateTime.UtcNow;

        await using var first = _database.CreateContext();
        await using var second = _database.CreateContext();
        var claims = await Task.WhenAll(
            new TdxTicketStore(first).ClaimAsync(id, now, now.AddMinutes(10), CancellationToken.None),
            new TdxTicketStore(second).ClaimAsync(id, now, now.AddMinutes(11), CancellationToken.None));
        var work = Assert.Single(claims, claim => claim is not null)!;

        await using var context = _database.CreateContext();
        var store = new TdxTicketStore(context);
        var stranger = work with { ClaimedUntil = work.ClaimedUntil.AddSeconds(1) };
        Assert.False(await store.MarkUncertainAsync(stranger, CancellationToken.None));
        Assert.True(await store.MarkUncertainAsync(work, CancellationToken.None));
        Assert.Equal(TdxTicketStates.Uncertain, await StateAsync(id));
        Assert.False(await store.CompleteCreatedAsync(stranger, 1, CancellationToken.None));
        Assert.True(await store.CompleteCreatedAsync(work, 910001, CancellationToken.None));
        Assert.False(await store.CompleteCreatedAsync(work, 910002, CancellationToken.None));

        await using var check = _database.CreateContext();
        var saved = await check.PosterRequests.AsNoTracking().SingleAsync(request => request.PosterRequestId == id);
        Assert.Equal(910001, saved.TdxTicketId);
        Assert.Equal(TdxTicketStates.Created, saved.TdxTicketState);
        Assert.Null(saved.TdxClaimedUntil);
        Assert.Null(await new TdxTicketStore(check).ClaimAsync(id, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task An_expired_claim_can_be_taken_and_None_or_Created_requests_are_never_due()
    {
        var pending = await AddRequestAsync(TdxTicketStates.Pending);
        var none = await AddRequestAsync(TdxTicketStates.None);
        var now = DateTime.UtcNow;

        await using var context = _database.CreateContext();
        var store = new TdxTicketStore(context);
        var old = await store.ClaimAsync(pending, now.AddHours(-1), now.AddMinutes(-1), CancellationToken.None);
        Assert.NotNull(old);
        var due = await store.DueAsync(now, 1000, CancellationToken.None);
        Assert.Contains(pending, due);
        Assert.DoesNotContain(none, due);
        var taken = await store.ClaimAsync(pending, now, now.AddMinutes(10), CancellationToken.None);
        Assert.NotNull(taken);
        Assert.False(await store.ReleaseAsync(old!, TdxTicketStates.Pending, CancellationToken.None));
        Assert.True(await store.ReleaseAsync(taken!, TdxTicketStates.Pending, CancellationToken.None));
        Assert.Null(await store.ClaimAsync(none, now, now.AddMinutes(10), CancellationToken.None));
    }

    [Fact]
    public async Task Two_requests_cannot_store_the_same_ticket_number()
    {
        var a = await AddRequestAsync(TdxTicketStates.Pending);
        var b = await AddRequestAsync(TdxTicketStates.Pending);
        var now = DateTime.UtcNow;
        await using var context = _database.CreateContext();
        var store = new TdxTicketStore(context);
        var first = await store.ClaimAsync(a, now, now.AddMinutes(5), CancellationToken.None);
        var second = await store.ClaimAsync(b, now, now.AddMinutes(5), CancellationToken.None);

        Assert.True(await store.CompleteCreatedAsync(first!, 920001, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => store.CompleteCreatedAsync(second!, 920001, CancellationToken.None));
    }

    private async Task<string> StateAsync(int id)
    {
        await using var context = _database.CreateContext();
        return await context.PosterRequests.Where(request => request.PosterRequestId == id).Select(request => request.TdxTicketState).SingleAsync();
    }

    private async Task<int> AddRequestAsync(string state)
    {
        await using var context = _database.CreateContext();
        var department = new Department { Name = "TDX " + Guid.NewGuid().ToString("N")[..8], Active = true };
        var posterId = "POSTER-2026-" + Random.Shared.Next(100000, 999999);
        var request = new PosterRequest
        {
            PosterId = posterId,
            PosterTitle = state == TdxTicketStates.None ? null : "Title",
            TdxTicketState = state,
            Name = "Test Requester",
            Department = department,
            DepartmentName = department.Name,
            Room = "1",
            Phone = "555-0100",
            Email = "test@example.edu",
            DateIn = DateTime.Now,
            PosterFile = new PosterFile
            {
                OriginalFileName = "Poster.pdf",
                DetectedFormat = "PDF",
                PageCount = 1,
                Width = 24m,
                Length = 36m,
                StoragePath = "CRD/" + posterId + "/Poster.pdf"
            },
            PosterProcessing = new PosterProcessing()
        };
        context.PosterRequests.Add(request);
        await context.SaveChangesAsync();
        return request.PosterRequestId;
    }
}

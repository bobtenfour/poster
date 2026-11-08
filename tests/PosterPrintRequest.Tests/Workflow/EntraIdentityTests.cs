using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Tests.Storage;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

/// <summary>Authentication Contract sections 3 and 12: identity, linking, denial, and the presentation snapshot.</summary>
public sealed class EntraActorTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Cookies", PosterClaims.Name, PosterClaims.Roles));

    [Fact]
    public void The_actor_comes_from_oid_onprem_sid_and_the_preferred_username_snapshot_only()
    {
        var oid = Guid.NewGuid();
        var principal = Principal(
            new Claim(PosterClaims.ObjectId, oid.ToString()),
            new Claim(PosterClaims.OnPremisesSecurityIdentifier, "S-1-5-21-1-2-3-4"),
            new Claim(PosterClaims.PreferredUsername, "ada@test.example"),
            new Claim(ClaimTypes.Name, @"DOMAIN\someone-else"),
            new Claim(ClaimTypes.PrimarySid, "S-1-5-21-9-9-9-9"),
            new Claim("groups", "GroupPCAdmins"));

        var actor = new ClaimsActorSource().FromPrincipal(principal);

        Assert.NotNull(actor);
        Assert.Equal(oid, actor.ObjectId);
        Assert.Equal("S-1-5-21-1-2-3-4", actor.OnPremisesSecurityIdentifier);
        Assert.Equal("ada@test.example", actor.PreferredUsername);
        Assert.True(actor.IsValid);
    }

    [Fact]
    public void An_actor_needs_an_object_identifier_and_nothing_else_stands_in_for_it()
    {
        var source = new ClaimsActorSource();

        Assert.Null(source.FromPrincipal(Principal(new Claim(PosterClaims.PreferredUsername, "ada@test.example"))));
        Assert.Null(source.FromPrincipal(Principal(
            new Claim(PosterClaims.OnPremisesSecurityIdentifier, "S-1-5-21-1-2-3-4"),
            new Claim(ClaimTypes.Name, @"DOMAIN\ada"),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("sub", Guid.NewGuid().ToString()))));
        Assert.Null(source.FromPrincipal(Principal(new Claim(PosterClaims.ObjectId, "not-a-guid"))));
        Assert.Null(source.FromPrincipal(Principal(new Claim(PosterClaims.ObjectId, Guid.Empty.ToString()))));
        Assert.Null(source.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PosterClaims.ObjectId, Guid.NewGuid().ToString())]))));
    }

    [Fact]
    public void A_missing_or_unusable_preferred_username_and_sid_stay_null_without_substitution()
    {
        var oid = Guid.NewGuid().ToString();

        var noName = new ClaimsActorSource().FromPrincipal(Principal(
            new Claim(PosterClaims.ObjectId, oid),
            new Claim(PosterClaims.Name, "Ada Lovelace"),
            new Claim(ClaimTypes.Email, "ada@test.example")));
        var badSid = new ClaimsActorSource().FromPrincipal(Principal(
            new Claim(PosterClaims.ObjectId, oid),
            new Claim(PosterClaims.OnPremisesSecurityIdentifier, "not-a-sid")));

        Assert.NotNull(noName);
        Assert.Null(noName.PreferredUsername);
        Assert.Null(noName.OnPremisesSecurityIdentifier);
        Assert.NotNull(badSid);
        Assert.Null(badSid.OnPremisesSecurityIdentifier);
    }
}

[Collection("Workflow database")]
public sealed class EntraIdentityTests
{
    private const string HistoricalSid = "S-1-5-21-5000000000-6000000000-7000000000-2001";

    private readonly WorkflowDatabaseFixture _database;

    public EntraIdentityTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    private static int _sequence = 100;

    private static string NextPosterId() => PosterIds.Format(2041, Interlocked.Increment(ref _sequence));

    private async Task<string> AddRequestAsync(string? sid, string? accountName, Guid? objectId)
    {
        var posterId = NextPosterId();
        await using var context = _database.CreateContext();
        var department = new Department { Name = "Identity " + Guid.NewGuid().ToString("N") };
        context.Departments.Add(department);
        await context.SaveChangesAsync();
        context.PosterRequests.Add(new PosterRequest
        {
            PosterId = posterId,
            Name = "Historical Requester",
            SubmittedBySecurityIdentifier = sid,
            SubmittedByAccountName = accountName,
            SubmittedByObjectId = objectId,
            DepartmentId = department.DepartmentId,
            DepartmentName = department.Name,
            Room = "4",
            Phone = "555-0140",
            Email = "identity@example.edu",
            DateIn = new DateTime(2026, 10, 6, 9, 0, 0),
            PosterFile = new PosterFile
            {
                OriginalFileName = "Poster.pdf",
                DetectedFormat = "PDF",
                PageCount = 1,
                Width = 24,
                Length = 36,
                StoragePath = "WITHOUT-EVENT/2026/" + posterId + "/Poster.pdf"
            },
            PosterProcessing = new PosterProcessing { Printed = true }
        });
        await context.SaveChangesAsync();
        return posterId;
    }

    private async Task<PosterRequest> LoadAsync(string posterId)
    {
        await using var context = _database.CreateContext();
        return await context.PosterRequests.AsNoTracking().SingleAsync(request => request.PosterId == posterId);
    }

    private async Task<(AcceptanceResult Result, CapturingLogger<AcceptanceLookup> Logger)> LookupAsync(string posterId, InstitutionalActor actor)
    {
        await using var context = _database.CreateContext();
        var logger = new CapturingLogger<AcceptanceLookup>();
        var result = await new AcceptanceLookup(context, logger).FindAsync(posterId, actor, CancellationToken.None);
        return (result, logger);
    }

    [Fact]
    public async Task An_exact_onprem_sid_match_links_the_historical_request_to_the_oid_and_keeps_the_historical_fields()
    {
        var oid = Guid.NewGuid();
        var posterId = await AddRequestAsync(HistoricalSid, @"DOMAIN\ada", objectId: null);

        var (result, logger) = await LookupAsync(posterId, new InstitutionalActor(oid, HistoricalSid, "someone.else@test.example"));

        Assert.NotNull(result.Details);
        Assert.Equal(posterId, result.Details.PosterId);
        Assert.False(result.HistoricalRequestNotLinked);
        Assert.Empty(logger.Messages);
        var saved = await LoadAsync(posterId);
        Assert.Equal(oid, saved.SubmittedByObjectId);
        Assert.Equal(HistoricalSid, saved.SubmittedBySecurityIdentifier);
        Assert.Equal(@"DOMAIN\ada", saved.SubmittedByAccountName);
    }

    [Fact]
    public async Task After_linking_access_is_decided_by_the_oid_alone()
    {
        var owner = Guid.NewGuid();
        var posterId = await AddRequestAsync(HistoricalSid, @"DOMAIN\ada", objectId: null);
        Assert.NotNull((await LookupAsync(posterId, new InstitutionalActor(owner, HistoricalSid, null))).Result.Details);

        var sameSidOtherPerson = await LookupAsync(posterId, new InstitutionalActor(Guid.NewGuid(), HistoricalSid, @"DOMAIN\ada"));
        var ownerWithoutSid = await LookupAsync(posterId, new InstitutionalActor(owner, null, null));

        Assert.Null(sameSidOtherPerson.Result.Details);
        Assert.False(sameSidOtherPerson.Result.HistoricalRequestNotLinked);
        Assert.NotNull(ownerWithoutSid.Result.Details);
        Assert.Equal(owner, (await LoadAsync(posterId)).SubmittedByObjectId);
    }

    [Fact]
    public async Task A_matching_preferred_username_with_a_different_oid_gives_no_access_and_never_links()
    {
        var owner = Guid.NewGuid();
        var posterId = await AddRequestAsync(sid: null, "ada@test.example", objectId: owner);

        var (result, _) = await LookupAsync(posterId, new InstitutionalActor(Guid.NewGuid(), null, "ada@test.example"));

        Assert.Null(result.Details);
        Assert.False(result.HistoricalRequestNotLinked);
        Assert.Equal(owner, (await LoadAsync(posterId)).SubmittedByObjectId);
    }

    [Theory]
    [InlineData("different-sid")]
    [InlineData("account-name-only")]
    [InlineData("null-sid-on-request")]
    [InlineData("null-sid-on-actor")]
    [InlineData("different-case")]
    [InlineData("trailing-space")]
    public async Task Anything_but_an_exact_sid_match_does_not_link_and_is_denied_with_the_exact_message(string scenario)
    {
        var requestSid = scenario == "null-sid-on-request" ? null : HistoricalSid;
        var posterId = await AddRequestAsync(requestSid, @"DOMAIN\ada", objectId: null);
        var actorSid = scenario switch
        {
            "different-sid" => "S-1-5-21-5000000000-6000000000-7000000000-2002",
            "account-name-only" => "S-1-5-21-5000000000-6000000000-7000000000-2003",
            "null-sid-on-request" => "S-1-5-21-5000000000-6000000000-7000000000-2001",
            "null-sid-on-actor" => null,
            "different-case" => HistoricalSid.ToLowerInvariant(),
            "trailing-space" => HistoricalSid + " ",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var actor = new InstitutionalActor(Guid.NewGuid(), actorSid, @"DOMAIN\ada");

        var (result, logger) = await LookupAsync(posterId, actor);

        Assert.Null(result.Details);
        Assert.True(result.HistoricalRequestNotLinked);
        Assert.Null((await LoadAsync(posterId)).SubmittedByObjectId);
        Assert.Equal(
            "Your historical request could not be linked to your current account. Please contact a technician for assistance.",
            AcceptanceLookup.HistoricalNotLinkedMessage);
        var entry = Assert.Single(logger.Messages);
        Assert.Contains("AcceptanceLookup", entry, StringComparison.Ordinal);
        Assert.Contains(posterId, entry, StringComparison.Ordinal);
        Assert.DoesNotContain(HistoricalSid, entry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DOMAIN", entry, StringComparison.Ordinal);
        Assert.DoesNotContain(actor.ObjectId.ToString(), entry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Personal_activity_lists_only_requests_owned_by_the_oid_and_links_by_exact_sid()
    {
        var oid = Guid.NewGuid();
        var linkedLater = await AddRequestAsync(HistoricalSid, @"DOMAIN\ada", objectId: null);
        var owned = await AddRequestAsync(sid: null, "ada@test.example", oid);
        var nameOnly = await AddRequestAsync(sid: null, "ada@test.example", objectId: null);
        var other = await AddRequestAsync(HistoricalSid, @"DOMAIN\ada", Guid.NewGuid());

        await using var context = _database.CreateContext();
        var activity = await UserActivityQuery.ForActorAsync(context, new InstitutionalActor(oid, HistoricalSid, "ada@test.example"), CancellationToken.None);

        var ids = activity.Posters.Select(poster => poster.PosterId).ToHashSet();
        Assert.Contains(linkedLater, ids);
        Assert.Contains(owned, ids);
        Assert.DoesNotContain(nameOnly, ids);
        Assert.DoesNotContain(other, ids);
    }

    [Fact]
    public async Task Historical_identity_fields_are_never_overwritten_by_linking()
    {
        var posterId = await AddRequestAsync(HistoricalSid, @"DOMAIN\ada", objectId: null);
        await using (var context = _database.CreateContext())
        {
            await HistoricalLinking.LinkAsync(context, new InstitutionalActor(Guid.NewGuid(), HistoricalSid, "renamed@test.example"), CancellationToken.None);
        }

        var saved = await LoadAsync(posterId);
        Assert.Equal(HistoricalSid, saved.SubmittedBySecurityIdentifier);
        Assert.Equal(@"DOMAIN\ada", saved.SubmittedByAccountName);
        Assert.NotNull(saved.SubmittedByObjectId);
    }

    [Fact]
    public async Task The_confirmation_page_shows_the_exact_message_for_an_unlinked_historical_request()
    {
        var posterId = await AddRequestAsync(HistoricalSid, @"DOMAIN\ada", objectId: null);
        var root = Path.Combine(Path.GetTempPath(), "poster-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var factory = new ConfiguredPosterFactory(_database.ConnectionString, root);
            using var client = await PosterSignIn.SignInAsync(factory, "usera");

            var denied = await client.GetStringAsync("/request/accepted/" + posterId);

            Assert.Contains(AcceptanceLookup.HistoricalNotLinkedMessage, denied, StringComparison.Ordinal);
            Assert.DoesNotContain("Create the TDX ticket", denied, StringComparison.Ordinal);
            Assert.DoesNotContain("Historical Requester", denied, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task The_confirmation_page_links_by_exact_sid_when_the_requester_signs_in_and_looks_it_up()
    {
        var posterId = await AddRequestAsync(InstitutionalTestIdentities.RequesterSid, @"DOMAIN\usera", objectId: null);
        var root = Path.Combine(Path.GetTempPath(), "poster-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var factory = new ConfiguredPosterFactory(_database.ConnectionString, root);
            using var client = await PosterSignIn.SignInAsync(factory, "usera");

            var shown = await client.GetStringAsync("/request/accepted/" + posterId);

            Assert.Contains("Create the TDX ticket", shown, StringComparison.Ordinal);
            Assert.DoesNotContain(AcceptanceLookup.HistoricalNotLinkedMessage, shown, StringComparison.Ordinal);
            Assert.Equal(InstitutionalTestIdentities.RequesterOid, (await LoadAsync(posterId)).SubmittedByObjectId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Technicians_keep_access_to_every_request_whether_or_not_it_is_linked()
    {
        var posterId = await AddRequestAsync(HistoricalSid, @"DOMAIN\ada", objectId: null);
        var root = Path.Combine(Path.GetTempPath(), "poster-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var factory = new ConfiguredPosterFactory(_database.ConnectionString, root);
            using var technician = await PosterSignIn.SignInAsync(factory, "usero");

            var page = await technician.GetAsync("/technician/" + posterId);

            page.EnsureSuccessStatusCode();
            Assert.Contains(posterId, await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Null((await LoadAsync(posterId)).SubmittedByObjectId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task No_mapping_table_exists_and_the_schema_gained_only_the_two_object_identifier_columns()
    {
        await using var context = _database.CreateContext();
        var tables = await context.Database
            .SqlQueryRaw<string>("SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        Assert.DoesNotContain(tables, table => table.Contains("Sid", StringComparison.OrdinalIgnoreCase)
            || table.Contains("Map", StringComparison.OrdinalIgnoreCase)
            || table.Contains("Identity", StringComparison.OrdinalIgnoreCase)
            || table.Contains("Link", StringComparison.OrdinalIgnoreCase));
        var migration = File.ReadAllText(Directory.EnumerateFiles(
            Path.Combine(RepositoryPaths.Root(), "src", "PosterPrintRequest.Infrastructure", "Persistence", "Migrations"),
            "*_AddEntraObjectIdentifiers.cs").Single());
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(migration, "AddColumn<").Count);
        Assert.DoesNotContain("CreateTable", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DropColumn(\n                name: \"Submitted", migration.Split("protected override void Down")[0], StringComparison.Ordinal);
    }
}

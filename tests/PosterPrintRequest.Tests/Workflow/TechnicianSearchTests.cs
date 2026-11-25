using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

/// <summary>Technician Search Contract v1, section 5.2: the status shown in a result row.</summary>
public sealed class TechnicianSearchStatusTests
{
    [Theory]
    [InlineData(true, null, false, false, false, false, null, "Deleted")]
    [InlineData(false, "2041-03-01", true, false, false, true, "2041-03-04", "Picked up")]
    [InlineData(false, null, false, false, false, false, null, "Receive")]
    [InlineData(false, "2041-03-01", false, false, false, false, null, "Print")]
    [InlineData(false, "2041-03-01", true, true, false, false, null, "Laminate")]
    [InlineData(false, "2041-03-01", true, false, false, false, null, "Notify")]
    [InlineData(false, "2041-03-01", true, true, true, true, null, "Pickup")]
    public void Status_follows_the_dashboard_stages(bool deleted, string? received, bool printed, bool lamination, bool laminated, bool notified, string? dateOut, string expected)
    {
        DateOnly? Date(string? value) => value is null ? null : DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, TechnicianSearch.Status(deleted, Date(received), printed, lamination, laminated, notified, Date(dateOut)));
    }

    [Fact]
    public void The_dropdown_starts_on_select_then_lists_four_categories()
    {
        Assert.Equal("Select", PosterSearchModes.SelectionLabel);
        Assert.Equal(["poster", "name", "date", "event"], PosterSearchModes.All.Select(option => option.Value));
        Assert.Equal(["Poster ID", "Name", "Date", "Event"], PosterSearchModes.All.Select(option => option.Label));
        Assert.Equal(PosterSearchModes.PosterId, PosterSearchModes.Normalize(null));
        Assert.Equal(PosterSearchModes.PosterId, PosterSearchModes.Normalize("unknown"));
        Assert.Equal(PosterSearchModes.Date, PosterSearchModes.Normalize("date"));
    }
}

/// <summary>Technician Search Contract v1, sections 4 and 5, against SQL Server.</summary>
[Collection("Workflow database")]
public sealed class TechnicianSearchTests : IDisposable
{
    private readonly WorkflowDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-search-" + Guid.NewGuid().ToString("N"));
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private readonly int _ticketBase = Random.Shared.Next(700_000_000, 900_000_000);

    public TechnicianSearchTests(WorkflowDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        using var context = _database.CreateContext();
        var requests = context.PosterRequests.Where(request => request.Name.EndsWith(_suffix)).ToList();
        context.PosterRequests.RemoveRange(requests);
        context.Reasons.RemoveRange(context.Reasons.Where(reason => reason.Name == "Search event " + _suffix));
        context.Departments.RemoveRange(context.Departments.Where(department => department.Name == "Search " + _suffix));
        context.SaveChanges();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Each_category_finds_its_posters_newest_first()
    {
        var reasonId = await SeedAsync(
            ("Zoë Quintana " + _suffix, new DateTime(2041, 3, 1, 9, 0, 0), true, 1),
            ("ZOE QUINTANA " + _suffix, new DateTime(2041, 3, 2, 9, 0, 0), true, 2),
            ("Other Person " + _suffix, new DateTime(2041, 3, 5, 9, 0, 0), false, 3));
        await using var context = _database.CreateContext();
        var search = new TechnicianSearch(context);

        var byName = await search.ByNameAsync("  zoe quintana " + _suffix + " ", CancellationToken.None);
        Assert.False(byName.More);
        Assert.Equal(["ZOE QUINTANA " + _suffix, "Zoë Quintana " + _suffix], byName.Rows.Select(row => row.Name));
        Assert.Equal(_ticketBase + 2, byName.Rows[0].TicketId);
        Assert.Equal("Search event " + _suffix, byName.Rows[0].EventName);
        Assert.Equal("Receive", byName.Rows[0].Status);

        var byRange = await search.ByDateAsync(new DateOnly(2041, 3, 2), new DateOnly(2041, 3, 4), CancellationToken.None);
        Assert.Equal(["ZOE QUINTANA " + _suffix], byRange.Rows.Where(row => row.Name.EndsWith(_suffix, StringComparison.Ordinal)).Select(row => row.Name));
        var fromOnly = await search.ByDateAsync(new DateOnly(2041, 3, 2), null, CancellationToken.None);
        Assert.Contains(fromOnly.Rows, row => row.Name == "Other Person " + _suffix);
        Assert.DoesNotContain(fromOnly.Rows, row => row.Name == "Zoë Quintana " + _suffix);
        var toOnly = await search.ByDateAsync(null, new DateOnly(2041, 3, 1), CancellationToken.None);
        Assert.Contains(toOnly.Rows, row => row.Name == "Zoë Quintana " + _suffix);
        Assert.DoesNotContain(toOnly.Rows, row => row.Name == "ZOE QUINTANA " + _suffix);

        var byEvent = await search.ByEventAsync(reasonId, CancellationToken.None);
        Assert.Equal(["ZOE QUINTANA " + _suffix, "Zoë Quintana " + _suffix], byEvent.Rows.Select(row => row.Name));
        Assert.Contains(await search.EventsAsync(CancellationToken.None), item => item.Id == reasonId && item.Name == "Search event " + _suffix);
    }

    [Fact]
    public async Task A_list_stops_at_fifty_and_says_more_match()
    {
        var rows = Enumerable.Range(0, TechnicianSearch.MaxRows + 1)
            .Select(index => ("Many " + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + " " + _suffix, new DateTime(2041, 4, 1, 8, 0, 0).AddMinutes(index), false, 10 + index))
            .ToArray();
        await SeedAsync(rows);
        await using var context = _database.CreateContext();

        var result = await new TechnicianSearch(context).ByNameAsync(_suffix, CancellationToken.None);

        Assert.True(result.More);
        Assert.Equal(TechnicianSearch.MaxRows, result.Rows.Count);
        Assert.Equal("Many 50 " + _suffix, result.Rows[0].Name);
    }

    [Fact]
    public async Task The_dashboard_shows_each_category_and_keeps_poster_id_links()
    {
        await SeedAsync(("Dashboard Search " + _suffix, new DateTime(2041, 5, 1, 9, 0, 0), true, 1));
        string posterId;
        await using (var context = _database.CreateContext())
        {
            posterId = (await context.PosterRequests.SingleAsync(request => request.Name == "Dashboard Search " + _suffix)).PosterId;
        }

        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, _root);
        using var client = await PosterSignIn.SignInAsync(factory, "usero");

        var bar = await (await client.GetAsync("/technician")).Content.ReadAsStringAsync();
        Assert.Contains("id=\"workspace-search-by\"", bar, StringComparison.Ordinal);
        // Select is the first option and nothing else is preselected, so the bar opens on Select.
        // The server renderer writes an empty attribute value as a bare attribute: <option value>.
        Assert.Contains("<option value>Select</option>", bar, StringComparison.Ordinal);
        Assert.Equal(0, SelectedOptions(bar));
        Assert.DoesNotContain("value=\"ticket\"", bar, StringComparison.Ordinal);

        var byName = await (await client.GetAsync("/technician?by=name&q=" + Uri.EscapeDataString("dashboard search " + _suffix))).Content.ReadAsStringAsync();
        Assert.Contains("id=\"search-results-table\"", byName, StringComparison.Ordinal);
        Assert.Contains("1 poster found", byName, StringComparison.Ordinal);
        Assert.Contains("href=\"/technician/work/" + posterId + "\"", byName, StringComparison.Ordinal);
        // After a search the bar is back on Select (section 3.4).
        Assert.Equal(0, SelectedOptions(byName));

        Assert.Contains("Enter part of a name.", await (await client.GetAsync("/technician?by=name&q=")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Choose a date.", await (await client.GetAsync("/technician?by=date")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("The start date is after the end date.", await (await client.GetAsync("/technician?by=date&from=2041-05-02&to=2041-05-01")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Choose an event.", await (await client.GetAsync("/technician?by=event&event=")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("1 poster found", await (await client.GetAsync("/technician?by=date&from=2041-05-01&to=2041-05-01")).Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var byPoster = await (await client.GetAsync("/technician?by=poster&q=" + posterId)).Content.ReadAsStringAsync();
        Assert.Contains("Open poster", byPoster, StringComparison.Ordinal);
        var legacy = await (await client.GetAsync("/technician?posterId=" + posterId)).Content.ReadAsStringAsync();
        Assert.Contains("Open poster", legacy, StringComparison.Ordinal);
    }

    // Options marked selected inside the Search by dropdown of the top bar.
    private static int SelectedOptions(string html)
    {
        var start = html.IndexOf("id=\"workspace-search-by\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("</select>", start, StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Count(html[start..end], "<option[^>]*\\bselected");
    }

    private async Task<int> SeedAsync(params (string Name, DateTime DateIn, bool WithEvent, int Ticket)[] rows)
    {
        await using var context = _database.CreateContext();
        var department = await context.Departments.SingleOrDefaultAsync(item => item.Name == "Search " + _suffix)
            ?? context.Departments.Add(new Department { Name = "Search " + _suffix }).Entity;
        var reason = await context.Reasons.SingleOrDefaultAsync(item => item.Name == "Search event " + _suffix)
            ?? context.Reasons.Add(new Reason { Name = "Search event " + _suffix }).Entity;
        await context.SaveChangesAsync();
        foreach (var (name, dateIn, withEvent, ticket) in rows)
        {
            var posterId = "POSTER-2041-" + Random.Shared.Next(100_000, 999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.PosterRequests.Add(new PosterRequest
            {
                PosterId = posterId,
                Name = name,
                DepartmentId = department.DepartmentId,
                DepartmentName = department.Name,
                Room = "1",
                Phone = "555-0100",
                Email = "search@example.edu",
                ReasonId = withEvent ? reason.ReasonId : null,
                ReasonName = withEvent ? reason.Name : null,
                DateIn = dateIn,
                TdxTicketId = _ticketBase + ticket,
                PosterFile = new PosterFile
                {
                    OriginalFileName = "Poster.pdf",
                    DetectedFormat = "PDF",
                    PageCount = 1,
                    Width = 24,
                    Length = 36,
                    StoragePath = "WITHOUT-EVENT/2041/" + posterId + "/Poster.pdf"
                },
                PosterProcessing = new PosterProcessing()
            });
        }

        await context.SaveChangesAsync();
        return reason.ReasonId;
    }
}

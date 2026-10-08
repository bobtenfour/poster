using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

[Collection("Workflow database")]
public sealed class OperatorConfigurationTests
{
    private readonly WorkflowDatabaseFixture _database;

    public OperatorConfigurationTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Dropdown_values_can_be_added_rejected_and_activated_without_losing_history()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var departmentName = "Department " + suffix;
        var eventName = "Event " + suffix;
        var posterId = "POSTER-2097-" + suffix[..6];
        await using var context = _database.CreateContext();
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        var before = await context.Departments.CountAsync();

        Assert.Equal("Enter a department.", (await configuration.AddDepartmentAsync("   ", CancellationToken.None)).Message);
        Assert.Equal("Enter an event.", (await configuration.AddReasonAsync(null, false, false, CancellationToken.None)).Message);
        Assert.True((await configuration.AddDepartmentAsync("  " + departmentName + "  ", CancellationToken.None)).Completed);
        Assert.Equal("That department is already in the list.", (await configuration.AddDepartmentAsync(departmentName.ToUpperInvariant(), CancellationToken.None)).Message);
        Assert.True((await configuration.AddReasonAsync(departmentName, true, true, CancellationToken.None)).Completed);
        Assert.True((await configuration.AddReasonAsync(eventName, true, false, CancellationToken.None)).Completed);
        Assert.Equal("That event is already in the list.", (await configuration.AddReasonAsync("  " + eventName + " ", false, false, CancellationToken.None)).Message);
        Assert.Equal(1, await context.Departments.CountAsync(department => department.Name == departmentName));

        var department = await context.Departments.SingleAsync(item => item.Name == departmentName);
        var reason = await context.Reasons.SingleAsync(item => item.Name == eventName);
        Assert.True(department.Active);
        Assert.True(reason.RequiresMentor);
        Assert.False(reason.RequiresApprovalSheet);
        context.PosterRequests.Add(HistoricalRequest(department.DepartmentId, departmentName, reason.ReasonId, eventName, posterId));
        await context.SaveChangesAsync();

        Assert.True((await configuration.SetDepartmentActiveAsync(department.DepartmentId, false, CancellationToken.None)).Completed);
        Assert.True((await configuration.SetReasonActiveAsync(reason.ReasonId, false, CancellationToken.None)).Completed);
        var choices = await new RequesterOptionCatalog(context).LoadAsync(CancellationToken.None);
        Assert.DoesNotContain(choices.Departments, option => option.Label == departmentName);
        Assert.DoesNotContain(choices.Reasons, option => option.Name == eventName);
        Assert.Equal(before + 1, await context.Departments.CountAsync());
        var departmentCountAfterCatalog = await context.Departments.CountAsync();
        await new RequesterOptionCatalog(context).LoadAsync(CancellationToken.None);
        Assert.Equal(departmentCountAfterCatalog, await context.Departments.CountAsync());

        var saved = await context.PosterRequests
            .Include(request => request.Department)
            .Include(request => request.Reason)
            .SingleAsync(request => request.PosterId == posterId);
        Assert.Equal(departmentName, saved.Department.Name);
        Assert.Equal(departmentName, saved.DepartmentName);
        Assert.Equal(eventName, saved.ReasonName);
        Assert.False(saved.Department.Active);
        Assert.Equal(eventName, saved.Reason!.Name);
        Assert.False(saved.Reason.Active);
        var errors = RequesterSubmissionService.Validate(
            new RequesterDraft
            {
                Name = "Historical Person",
                DepartmentId = department.DepartmentId.ToString(),
                ReasonId = reason.ReasonId.ToString(),
                Room = "1",
                Phone = "555-0199",
                Email = "historical@example.edu"
            },
            choices);
        Assert.Equal("Choose a department.", errors["department"]);
        Assert.Equal("Choose an event from the list, or leave it blank.", errors["reason"]);

        Assert.True((await configuration.SetDepartmentActiveAsync(department.DepartmentId, true, CancellationToken.None)).Completed);
        Assert.True((await configuration.SetReasonActiveAsync(reason.ReasonId, true, CancellationToken.None)).Completed);
        var restored = await new RequesterOptionCatalog(context).LoadAsync(CancellationToken.None);
        Assert.Contains(restored.Departments, option => option.Value == department.DepartmentId.ToString() && option.Label == departmentName);
        Assert.Contains(restored.Reasons, option => option.Id == reason.ReasonId && option.Name == eventName && option.RequiresMentor && !option.RequiresApprovalSheet);
    }

    [Fact]
    public async Task Printer_models_and_consumables_stay_on_record_when_compatibility_is_deactivated()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var printerName = "Plotter " + suffix;
        var code = "M" + suffix[..8];
        await using var context = _database.CreateContext();
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var expiration = DateOnly.FromDateTime(DateTime.Today).AddMonths(4);

        Assert.Equal("Enter a printer model.", (await configuration.AddPrinterModelAsync("  ", CancellationToken.None)).Message);
        Assert.True((await configuration.AddPrinterModelAsync(printerName, CancellationToken.None)).Completed);
        Assert.Equal("That printer model is already in the list.", (await configuration.AddPrinterModelAsync(printerName.ToLowerInvariant(), CancellationToken.None)).Message);
        Assert.Equal("Choose a category.", (await configuration.AddConsumableAsync("", "Film", null, null, CancellationToken.None)).Message);
        Assert.Equal("Enter a material name.", (await configuration.AddConsumableAsync(ConsumableCategory.Cartridge, "   ", code, null, CancellationToken.None)).Message);
        Assert.True((await configuration.AddConsumableAsync(ConsumableCategory.Cartridge, "Black cartridge " + suffix, code, "130 ml", CancellationToken.None)).Completed);
        Assert.Equal("That material is already in this category.", (await configuration.AddConsumableAsync(ConsumableCategory.Cartridge, "black cartridge " + suffix, null, null, CancellationToken.None)).Message);
        Assert.True((await configuration.AddConsumableAsync(ConsumableCategory.Paper, "Black cartridge " + suffix, null, null, CancellationToken.None)).Completed);
        Assert.Equal("That code is already registered.", (await configuration.AddConsumableAsync(ConsumableCategory.Paper, "Other paper " + suffix, code, null, CancellationToken.None)).Message);

        var printer = await context.PrinterModels.SingleAsync(model => model.Name == printerName);
        var cartridge = await context.PrintingConsumables.SingleAsync(item => item.Code == code);
        var paper = await context.PrintingConsumables.SingleAsync(item => item.Category == ConsumableCategory.Paper && item.Name == "Black cartridge " + suffix);
        Assert.True(printer.Active);
        Assert.True(cartridge.Active);
        Assert.True(cartridge.HasExpirationDate);
        Assert.False(paper.HasExpirationDate);
        Assert.Equal(ConsumableStock.Depleted, cartridge.Status);
        Assert.True((await inventory.AddStockAsync(cartridge.PrintingConsumableId, 2, expiration, CancellationToken.None)).Completed);

        Assert.True((await configuration.SetCompatibilityAsync(printer.PrinterModelId, cartridge.PrintingConsumableId, true, CancellationToken.None)).Completed);
        Assert.True((await configuration.SetCompatibilityAsync(printer.PrinterModelId, cartridge.PrintingConsumableId, false, CancellationToken.None)).Completed);
        Assert.True((await configuration.SetPrinterModelActiveAsync(printer.PrinterModelId, false, CancellationToken.None)).Completed);
        Assert.True((await configuration.SetConsumableActiveAsync(cartridge.PrintingConsumableId, false, CancellationToken.None)).Completed);

        var link = Assert.Single(await context.PrinterModelConsumables.Where(item => item.PrinterModelId == printer.PrinterModelId).ToListAsync());
        Assert.False(link.Active);
        Assert.Equal(cartridge.PrintingConsumableId, link.PrintingConsumableId);
        Assert.False((await context.PrinterModels.SingleAsync(model => model.PrinterModelId == printer.PrinterModelId)).Active);
        var stock = Assert.Single(await context.PrintingStockEntries.Where(entry => entry.PrintingConsumableId == cartridge.PrintingConsumableId).ToListAsync());
        Assert.Equal(2, stock.Quantity);
        Assert.Equal(expiration, stock.ExpirationDate);
        var hidden = await inventory.LoadAsync(CancellationToken.None);
        Assert.DoesNotContain(hidden.Cartridges, item => item.PrintingConsumableId == cartridge.PrintingConsumableId);
        Assert.Contains(hidden.Paper, item => item.PrintingConsumableId == paper.PrintingConsumableId);

        Assert.True((await configuration.SetCompatibilityAsync(printer.PrinterModelId, cartridge.PrintingConsumableId, true, CancellationToken.None)).Completed);
        Assert.Equal(1, await context.PrinterModelConsumables.CountAsync(item => item.PrinterModelId == printer.PrinterModelId));
        Assert.True((await context.PrinterModelConsumables.SingleAsync(item => item.PrinterModelId == printer.PrinterModelId)).Active);
        var view = await configuration.LoadAsync(CancellationToken.None);
        Assert.Contains(view.PrinterModels.Single(model => model.Id == printer.PrinterModelId).CompatibleConsumableIds, id => id == cartridge.PrintingConsumableId);
    }

    [Fact]
    public async Task Request_form_offers_only_active_configured_values()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var activeName = "Visible " + suffix;
        var inactiveName = "Hidden " + suffix;
        int activeId;
        int inactiveId;
        await using (var context = _database.CreateContext())
        {
            var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
            Assert.True((await configuration.AddDepartmentAsync(activeName, CancellationToken.None)).Completed);
            Assert.True((await configuration.AddDepartmentAsync(inactiveName, CancellationToken.None)).Completed);
            activeId = (await context.Departments.SingleAsync(item => item.Name == activeName)).DepartmentId;
            inactiveId = (await context.Departments.SingleAsync(item => item.Name == inactiveName)).DepartmentId;
            Assert.True((await configuration.SetDepartmentActiveAsync(inactiveId, false, CancellationToken.None)).Completed);
        }

        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, Path.GetTempPath());
        using var requester = await PosterSignIn.SignInAsync(factory, "usera");
        var request = await requester.GetAsync("/request");
        request.EnsureSuccessStatusCode();
        var requestHtml = await request.Content.ReadAsStringAsync();
        Assert.Contains(activeName, requestHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(inactiveName, requestHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"department-value\"", requestHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"printer-value\"", requestHtml, StringComparison.Ordinal);

        using var operatorClient = await PosterSignIn.SignInAsync(factory, "usero");
        var configurationPage = await operatorClient.GetAsync("/technician/configuration");
        configurationPage.EnsureSuccessStatusCode();
        var configurationHtml = await configurationPage.Content.ReadAsStringAsync();
        Assert.Contains("Printer models", configurationHtml, StringComparison.Ordinal);
        Assert.Contains("Consumables / Materials", configurationHtml, StringComparison.Ordinal);
        Assert.Contains("Add department", configurationHtml, StringComparison.Ordinal);
        Assert.Contains("Search departments...", configurationHtml, StringComparison.Ordinal);
        Assert.Contains(">Show:</label>", configurationHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"department-show\"", configurationHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Status</label>", configurationHtml, StringComparison.Ordinal);
        Assert.Contains(activeName, configurationHtml, StringComparison.Ordinal);
        Assert.Contains("checked", Checkbox(configurationHtml, "department-active-" + activeId), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"department-edit-" + activeId + "\"", configurationHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"department-delete-" + activeId + "\"", configurationHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(inactiveName, configurationHtml, StringComparison.Ordinal);
        Assert.Contains("epx-config-table", configurationHtml, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Edit " + activeName + "\"", configurationHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Edit</button>", configurationHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("department-edit-", requestHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("department-delete-", requestHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("event-edit-", requestHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("event-delete-", requestHtml, StringComparison.Ordinal);

        var denied = await requester.GetAsync("/technician/configuration");
        Assert.Contains("/Account/AccessDenied", denied.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
    }

    [Fact]
    public async Task Departments_can_be_edited_and_deleted_only_when_no_request_references_them()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var originalName = "Clinic " + suffix;
        var otherName = "Other clinic " + suffix;
        var renamed = "Renamed clinic " + suffix;
        var referencedName = "Referenced clinic " + suffix;
        var posterId = "POSTER-2098-" + suffix[..6];
        await using var context = _database.CreateContext();
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        Assert.True((await configuration.AddDepartmentAsync(originalName, CancellationToken.None)).Completed);
        Assert.True((await configuration.AddDepartmentAsync(otherName, CancellationToken.None)).Completed);
        var department = await context.Departments.SingleAsync(item => item.Name == originalName);
        var other = await context.Departments.SingleAsync(item => item.Name == otherName);
        var reason = await context.Reasons.OrderBy(item => item.ReasonId).FirstAsync();
        var departmentId = department.DepartmentId;

        Assert.True((await configuration.UpdateDepartmentAsync(departmentId, "  " + renamed + "  ", CancellationToken.None)).Completed);
        department = await context.Departments.SingleAsync(item => item.DepartmentId == departmentId);
        Assert.Equal(renamed, department.Name);
        Assert.True(department.Active);
        Assert.Equal(departmentId, department.DepartmentId);
        Assert.Equal("Enter a department.", (await configuration.UpdateDepartmentAsync(departmentId, "   ", CancellationToken.None)).Message);
        Assert.Equal(renamed, (await context.Departments.SingleAsync(item => item.DepartmentId == departmentId)).Name);
        Assert.Equal("That department is already in the list.", (await configuration.UpdateDepartmentAsync(departmentId, other.Name.ToUpperInvariant(), CancellationToken.None)).Message);
        Assert.Equal(renamed, (await context.Departments.SingleAsync(item => item.DepartmentId == departmentId)).Name);

        Assert.True((await configuration.DeleteDepartmentAsync(departmentId, CancellationToken.None)).Completed);
        Assert.False(await context.Departments.AnyAsync(item => item.DepartmentId == departmentId));

        Assert.True((await configuration.AddDepartmentAsync(referencedName, CancellationToken.None)).Completed);
        var referenced = await context.Departments.SingleAsync(item => item.Name == referencedName);
        context.PosterRequests.Add(HistoricalRequest(referenced.DepartmentId, referencedName, reason.ReasonId, reason.Name, posterId));
        await context.SaveChangesAsync();
        Assert.True((await configuration.UpdateDepartmentAsync(referenced.DepartmentId, referencedName + " renamed", CancellationToken.None)).Completed);
        var renamedRequest = await context.PosterRequests.AsNoTracking().SingleAsync(request => request.PosterId == posterId);
        Assert.Equal(referencedName, renamedRequest.DepartmentName);
        Assert.Equal(referencedName + " renamed", (await context.Departments.AsNoTracking().SingleAsync(item => item.DepartmentId == referenced.DepartmentId)).Name);
        var before = renamedRequest;

        var rejected = await configuration.DeleteDepartmentAsync(referenced.DepartmentId, CancellationToken.None);
        Assert.Equal("This department cannot be deleted because it is referenced by an existing request.", rejected.Message);
        Assert.False(rejected.Completed);
        var stillThere = await context.Departments.SingleAsync(item => item.DepartmentId == referenced.DepartmentId);
        Assert.Equal(referencedName + " renamed", stillThere.Name);
        Assert.True(stillThere.Active);
        var after = await context.PosterRequests.AsNoTracking().SingleAsync(request => request.PosterId == posterId);
        AssertRequestUnchanged(before, after);

        await using var direct = _database.CreateContext();
        direct.Departments.Remove(await direct.Departments.SingleAsync(item => item.DepartmentId == referenced.DepartmentId));
        var violation = await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync());
        AssertReferenceViolation(violation);

        await using var verify = _database.CreateContext();
        Assert.Equal(referencedName + " renamed", (await verify.Departments.SingleAsync(item => item.DepartmentId == referenced.DepartmentId)).Name);
        AssertRequestUnchanged(before, await verify.PosterRequests.AsNoTracking().SingleAsync(request => request.PosterId == posterId));
    }

    [Fact]
    public async Task Events_can_be_edited_and_deleted_only_when_no_request_references_them()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var originalName = "Symposium " + suffix;
        var otherName = "Other symposium " + suffix;
        var renamed = "Renamed symposium " + suffix;
        var referencedName = "Referenced symposium " + suffix;
        var posterId = "POSTER-2099-" + suffix[..6];
        await using var context = _database.CreateContext();
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        Assert.True((await configuration.AddDepartmentAsync("Department for " + suffix, CancellationToken.None)).Completed);
        Assert.True((await configuration.AddReasonAsync(originalName, true, false, CancellationToken.None)).Completed);
        Assert.True((await configuration.AddReasonAsync(otherName, false, true, CancellationToken.None)).Completed);
        var department = await context.Departments.SingleAsync(item => item.Name == "Department for " + suffix);
        var reason = await context.Reasons.SingleAsync(item => item.Name == originalName);
        var other = await context.Reasons.SingleAsync(item => item.Name == otherName);
        var reasonId = reason.ReasonId;

        Assert.True((await configuration.UpdateReasonAsync(reasonId, "  " + renamed + "  ", true, false, CancellationToken.None)).Completed);
        reason = await context.Reasons.SingleAsync(item => item.ReasonId == reasonId);
        Assert.Equal(renamed, reason.Name);
        Assert.True(reason.RequiresMentor);
        Assert.False(reason.RequiresApprovalSheet);
        Assert.True(reason.Active);
        Assert.Equal(reasonId, reason.ReasonId);
        Assert.Equal("Enter an event.", (await configuration.UpdateReasonAsync(reasonId, " \t ", true, false, CancellationToken.None)).Message);
        Assert.Equal(renamed, (await context.Reasons.SingleAsync(item => item.ReasonId == reasonId)).Name);
        Assert.Equal("That event is already in the list.", (await configuration.UpdateReasonAsync(reasonId, "  " + other.Name + " ", true, false, CancellationToken.None)).Message);
        reason = await context.Reasons.SingleAsync(item => item.ReasonId == reasonId);
        Assert.Equal(renamed, reason.Name);
        Assert.True(reason.RequiresMentor);
        Assert.False(reason.RequiresApprovalSheet);

        Assert.True((await configuration.DeleteReasonAsync(reasonId, CancellationToken.None)).Completed);
        Assert.False(await context.Reasons.AnyAsync(item => item.ReasonId == reasonId));

        Assert.True((await configuration.AddReasonAsync(referencedName, true, true, CancellationToken.None)).Completed);
        var referenced = await context.Reasons.SingleAsync(item => item.Name == referencedName);
        context.PosterRequests.Add(HistoricalRequest(department.DepartmentId, department.Name, referenced.ReasonId, referencedName, posterId));
        await context.SaveChangesAsync();
        Assert.True((await configuration.UpdateReasonAsync(referenced.ReasonId, referencedName + " renamed", true, true, CancellationToken.None)).Completed);
        var renamedRequest = await context.PosterRequests.AsNoTracking().SingleAsync(request => request.PosterId == posterId);
        Assert.Equal(referencedName, renamedRequest.ReasonName);
        Assert.Null(renamedRequest.Mentor);
        Assert.False(renamedRequest.ApprovalSheetUploaded);
        var renamedReason = await context.Reasons.AsNoTracking().SingleAsync(item => item.ReasonId == referenced.ReasonId);
        Assert.Equal(referencedName + " renamed", renamedReason.Name);
        Assert.True(renamedReason.RequiresMentor);
        Assert.True(renamedReason.RequiresApprovalSheet);
        var before = renamedRequest;

        var rejected = await configuration.DeleteReasonAsync(referenced.ReasonId, CancellationToken.None);
        Assert.Equal("This event cannot be deleted because it is referenced by an existing request.", rejected.Message);
        Assert.False(rejected.Completed);
        var stillThere = await context.Reasons.SingleAsync(item => item.ReasonId == referenced.ReasonId);
        Assert.Equal(referencedName + " renamed", stillThere.Name);
        Assert.True(stillThere.RequiresMentor);
        Assert.True(stillThere.RequiresApprovalSheet);
        Assert.True(stillThere.Active);
        var after = await context.PosterRequests.AsNoTracking().SingleAsync(request => request.PosterId == posterId);
        AssertRequestUnchanged(before, after);

        await using var direct = _database.CreateContext();
        direct.Reasons.Remove(await direct.Reasons.SingleAsync(item => item.ReasonId == referenced.ReasonId));
        var violation = await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync());
        AssertReferenceViolation(violation);

        await using var verify = _database.CreateContext();
        var remaining = await verify.Reasons.SingleAsync(item => item.ReasonId == referenced.ReasonId);
        Assert.Equal(referencedName + " renamed", remaining.Name);
        Assert.True(remaining.RequiresMentor);
        Assert.True(remaining.RequiresApprovalSheet);
        AssertRequestUnchanged(before, await verify.PosterRequests.AsNoTracking().SingleAsync(request => request.PosterId == posterId));
    }

    [Fact]
    public async Task Printers_and_materials_delete_only_when_nothing_references_them()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var context = _database.CreateContext();
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);

        Assert.True((await configuration.AddPrinterModelAsync("Plotter " + suffix, CancellationToken.None)).Completed);
        var printer = await context.PrinterModels.SingleAsync(model => model.Name == "Plotter " + suffix);
        Assert.True(printer.Active);
        Assert.True((await configuration.DeletePrinterModelAsync(printer.PrinterModelId, CancellationToken.None)).Completed);
        Assert.False(await context.PrinterModels.AnyAsync(model => model.PrinterModelId == printer.PrinterModelId));

        Assert.True((await configuration.AddPrinterModelAsync("Linked plotter " + suffix, CancellationToken.None)).Completed);
        Assert.True((await configuration.AddConsumableAsync(ConsumableCategory.Cartridge, "Linked cartridge " + suffix, "L" + suffix[..8], "130 ml", CancellationToken.None)).Completed);
        printer = await context.PrinterModels.SingleAsync(model => model.Name == "Linked plotter " + suffix);
        var linked = await context.PrintingConsumables.SingleAsync(item => item.Name == "Linked cartridge " + suffix);
        Assert.True(linked.Active);
        Assert.True((await configuration.SetCompatibilityAsync(printer.PrinterModelId, linked.PrintingConsumableId, true, CancellationToken.None)).Completed);
        Assert.Equal("This printer model cannot be deleted because a material is associated with it.", (await configuration.DeletePrinterModelAsync(printer.PrinterModelId, CancellationToken.None)).Message);
        Assert.Equal("This material cannot be deleted because a printer model is associated with it.", (await configuration.DeleteConsumableAsync(linked.PrintingConsumableId, CancellationToken.None)).Message);
        Assert.True(await context.PrinterModels.AnyAsync(model => model.PrinterModelId == printer.PrinterModelId));
        Assert.True(await context.PrintingConsumables.AnyAsync(item => item.PrintingConsumableId == linked.PrintingConsumableId));

        Assert.True((await configuration.AddConsumableAsync(ConsumableCategory.Paper, "Loose paper " + suffix, null, null, CancellationToken.None)).Completed);
        var paper = await context.PrintingConsumables.SingleAsync(item => item.Name == "Loose paper " + suffix);
        Assert.True((await inventory.AddStockAsync(paper.PrintingConsumableId, 4, null, CancellationToken.None)).Completed);
        Assert.Equal("This material cannot be deleted because inventory stock references it.", (await configuration.DeleteConsumableAsync(paper.PrintingConsumableId, CancellationToken.None)).Message);
        Assert.Equal(4, await context.PrintingStockEntries.Where(entry => entry.PrintingConsumableId == paper.PrintingConsumableId).SumAsync(entry => entry.Quantity));
        Assert.True((await configuration.SetConsumableActiveAsync(paper.PrintingConsumableId, false, CancellationToken.None)).Completed);
        Assert.Equal(4, await context.PrintingStockEntries.Where(entry => entry.PrintingConsumableId == paper.PrintingConsumableId).SumAsync(entry => entry.Quantity));
        var hidden = await inventory.LoadAsync(CancellationToken.None);
        Assert.DoesNotContain(hidden.Paper, item => item.PrintingConsumableId == paper.PrintingConsumableId);
        Assert.False((await inventory.AddStockAsync(paper.PrintingConsumableId, 1, null, CancellationToken.None)).Completed);
        Assert.True((await configuration.SetConsumableActiveAsync(paper.PrintingConsumableId, true, CancellationToken.None)).Completed);
        var restored = await inventory.LoadAsync(CancellationToken.None);
        Assert.Equal(4, restored.Paper.Single(item => item.PrintingConsumableId == paper.PrintingConsumableId).CurrentQuantity);

        await using var direct = _database.CreateContext();
        direct.PrintingConsumables.Remove(await direct.PrintingConsumables.SingleAsync(item => item.PrintingConsumableId == paper.PrintingConsumableId));
        AssertReferenceViolation(await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync()));
        Assert.Equal(4, await context.PrintingStockEntries.Where(entry => entry.PrintingConsumableId == paper.PrintingConsumableId).SumAsync(entry => entry.Quantity));
    }

    private static void AssertRequestUnchanged(PosterRequest before, PosterRequest after)
    {
        Assert.Equal(before.PosterRequestId, after.PosterRequestId);
        Assert.Equal(before.PosterId, after.PosterId);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Mentor, after.Mentor);
        Assert.Equal(before.DepartmentId, after.DepartmentId);
        Assert.Equal(before.DepartmentName, after.DepartmentName);
        Assert.Equal(before.ReasonId, after.ReasonId);
        Assert.Equal(before.ReasonName, after.ReasonName);
        Assert.Equal(before.Room, after.Room);
        Assert.Equal(before.Phone, after.Phone);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.LaminationRequested, after.LaminationRequested);
        Assert.Equal(before.ApprovalSheetUploaded, after.ApprovalSheetUploaded);
        Assert.Equal(before.DateIn, after.DateIn);
    }

    private static void AssertReferenceViolation(Exception exception)
    {
        var found = false;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number == 547)
            {
                found = true;
            }
        }

        Assert.True(found, exception.ToString());
    }

    private static string Checkbox(string html, string id)
    {
        var match = Regex.Match(html, "<input\\b[^>]*id=\"" + Regex.Escape(id) + "\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Missing checkbox " + id);
        return match.Value;
    }

    private static PosterRequest HistoricalRequest(int departmentId, string departmentName, int reasonId, string reasonName, string posterId) => new()
    {
        PosterId = posterId,
        Name = "Historical Person",
        DepartmentId = departmentId,
        DepartmentName = departmentName,
        ReasonId = reasonId,
        ReasonName = reasonName,
        Room = "9",
        Phone = "555-0199",
        Email = "historical@example.edu",
        DateIn = new DateTime(2026, 10, 7, 9, 0, 0),
        PosterFile = new PosterFile
        {
            OriginalFileName = "Poster.pdf",
            DetectedFormat = "PDF",
            PageCount = 1,
            Width = 36,
            Length = 72,
            StoragePath = "WITHOUT-EVENT/2026/" + posterId + "/Poster.pdf"
        },
        PosterProcessing = new PosterProcessing()
    };
}

public sealed class ConfigurationPresentationTests
{
    [Fact]
    public void Active_filter_search_and_a_long_list_stay_on_the_matching_records()
    {
        var records = Enumerable.Range(1, 60)
            .Select(number => new Sample(number, "Item " + number.ToString("00"), number % 5 != 0))
            .ToArray();

        var active = ConfigurationPresentation.Apply(records, ConfigurationPresentation.Active, null, item => item.Name, item => item.Active);
        Assert.Equal(48, active.Count);
        Assert.All(active, item => Assert.True(item.Active));

        var inactive = ConfigurationPresentation.Apply(records, ConfigurationPresentation.Inactive, null, item => item.Name, item => item.Active);
        Assert.Equal(12, inactive.Count);
        Assert.All(inactive, item => Assert.False(item.Active));

        var all = ConfigurationPresentation.Apply(records, ConfigurationPresentation.All, null, item => item.Name, item => item.Active);
        Assert.Equal(60, all.Count);

        var searched = ConfigurationPresentation.Apply(records, ConfigurationPresentation.All, "item 1", item => item.Name, item => item.Active);
        Assert.Contains(searched, item => item.Name == "Item 10");
        Assert.Contains(searched, item => item.Name == "Item 11");
        Assert.DoesNotContain(searched, item => item.Name == "Item 02");

        var activeSearch = ConfigurationPresentation.Apply(records, ConfigurationPresentation.Active, "item 1", item => item.Name, item => item.Active);
        Assert.Contains(activeSearch, item => item.Name == "Item 11");
        Assert.DoesNotContain(activeSearch, item => item.Name == "Item 10");
        Assert.DoesNotContain(activeSearch, item => item.Name == "Item 15");
        Assert.All(activeSearch, item => Assert.True(item.Active));
    }

    private sealed record Sample(int Id, string Name, bool Active);
}

public sealed class OperatorConfigurationSourceTests
{
    [Fact]
    public void Application_source_does_not_require_printer_specific_data()
    {
        var root = Path.Combine(RepositoryPaths.Root(), "src");
        var tokens = new[]
        {
            "C9403A",
            "C9370A",
            "C9371A",
            "C9372A",
            "C9373A",
            "C9374A",
            "C1861A",
            "C6814A",
            "Eagle 105",
            "DesignJet",
            "HP 72",
            "MK Matte Black",
            "Example Department",
            "Example event"
        };
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    hits.Add(Path.GetRelativePath(root, file) + " contains " + token);
                }
            }
        }

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void Configuration_lists_separate_add_from_show_and_search()
    {
        var page = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root(),
            "src",
            "PosterPrintRequest.Web",
            "Components",
            "Pages",
            "TechnicianConfiguration.razor"));

        Assert.Contains("epx-config-add-block", page, StringComparison.Ordinal);
        Assert.Contains("epx-config-filters", page, StringComparison.Ordinal);
        Assert.Contains("epx-config-table", page, StringComparison.Ordinal);
        Assert.DoesNotContain("epx-config-row", page, StringComparison.Ordinal);
        Assert.DoesNotContain(">Edit</button>", page, StringComparison.Ordinal);
        Assert.DoesNotContain(">Status</label>", page, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Count(page, ">Show:</label>"));
        Assert.Contains("placeholder=\"Search departments...\"", page, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Search reason / events...\"", page, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Search printer models...\"", page, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Search consumables / materials...\"", page, StringComparison.Ordinal);
        Assert.Contains("@oninput=\"args => DepartmentSearch", page, StringComparison.Ordinal);
        Assert.Contains("@oninput=\"args => ReasonSearch", page, StringComparison.Ordinal);
        Assert.Contains("@oninput=\"args => PrinterSearch", page, StringComparison.Ordinal);
        Assert.Contains("@oninput=\"args => ConsumableSearch", page, StringComparison.Ordinal);
        Assert.Contains("No departments found.", page, StringComparison.Ordinal);
        Assert.Contains("No reason / events found.", page, StringComparison.Ordinal);
        Assert.Contains("No printer models found.", page, StringComparison.Ordinal);
        Assert.Contains("No consumables / materials found.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("NewDepartmentActive", page, StringComparison.Ordinal);
    }
}

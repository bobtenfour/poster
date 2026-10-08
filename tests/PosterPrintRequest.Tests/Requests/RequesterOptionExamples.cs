using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Tests.Requests;

internal static class RequesterOptionExamples
{
    public static async Task EnsureAsync(PosterPrintRequestDbContext db)
    {
        await EnsureDepartmentAsync(db, "Example Department");
        await EnsureDepartmentAsync(db, "Example Department 2");
        await EnsureReasonAsync(db, "Example event", false, false);
        await EnsureReasonAsync(db, "Example event, mentor required", true, false);
        await EnsureReasonAsync(db, "Example event, approval sheet required", false, true);
        await EnsureReasonAsync(db, "Example event, mentor and approval sheet required", true, true);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDepartmentAsync(PosterPrintRequestDbContext db, string name)
    {
        var department = await db.Departments.FirstOrDefaultAsync(candidate => candidate.Name == name);
        if (department is null)
        {
            db.Departments.Add(new Department { Name = name, Active = true });
            return;
        }

        department.Active = true;
    }

    private static async Task EnsureReasonAsync(PosterPrintRequestDbContext db, string name, bool requiresMentor, bool requiresApprovalSheet)
    {
        var reason = await db.Reasons.FirstOrDefaultAsync(candidate => candidate.Name == name);
        if (reason is null)
        {
            db.Reasons.Add(new Reason
            {
                Name = name,
                Active = true,
                RequiresMentor = requiresMentor,
                RequiresApprovalSheet = requiresApprovalSheet
            });
            return;
        }

        reason.Active = true;
        reason.RequiresMentor = requiresMentor;
        reason.RequiresApprovalSheet = requiresApprovalSheet;
    }
}

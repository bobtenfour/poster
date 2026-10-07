using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Web.Components.Foundation;

namespace PosterPrintRequest.Web.Requests;

public sealed class RequesterOptionCatalog : IRequesterOptionSource
{
    public static readonly string[] DepartmentNames =
    [
        "Example Department",
        "Example Department 2"
    ];

    public static readonly (string Name, bool RequiresMentor, bool RequiresApprovalSheet)[] ReasonNames =
    [
        ("Example event", false, false),
        ("Example event, mentor required", true, false),
        ("Example event, approval sheet required", false, true),
        ("Example event, mentor and approval sheet required", true, true)
    ];

    private readonly PosterPrintRequestDbContext _db;

    public RequesterOptionCatalog(PosterPrintRequestDbContext db)
    {
        _db = db;
    }

    public async Task<RequesterChoices> LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var name in DepartmentNames)
        {
            if (!await _db.Departments.AnyAsync(department => department.Name == name, cancellationToken))
            {
                _db.Departments.Add(new Department { Name = name });
            }
        }

        foreach (var (name, requiresMentor, requiresApprovalSheet) in ReasonNames)
        {
            var existing = await _db.Reasons.FirstOrDefaultAsync(reason => reason.Name == name, cancellationToken);
            if (existing is null)
            {
                _db.Reasons.Add(new Reason
                {
                    Name = name,
                    RequiresMentor = requiresMentor,
                    RequiresApprovalSheet = requiresApprovalSheet
                });
            }
            else
            {
                existing.RequiresMentor = requiresMentor;
                existing.RequiresApprovalSheet = requiresApprovalSheet;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var departments = await _db.Departments
            .OrderBy(department => department.Name)
            .Select(department => new SelectOption(department.DepartmentId.ToString(), department.Name))
            .ToListAsync(cancellationToken);

        var reasons = await _db.Reasons
            .OrderBy(reason => reason.Name)
            .Select(reason => new ReasonChoice(
                reason.ReasonId,
                reason.Name,
                reason.RequiresMentor,
                reason.RequiresApprovalSheet))
            .ToListAsync(cancellationToken);

        return new RequesterChoices
        {
            Departments = departments,
            Reasons = reasons
        };
    }
}

using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Web.Components.Foundation;

namespace PosterPrintRequest.Web.Requests;

public sealed class RequesterOptionCatalog : IRequesterOptionSource
{
    private readonly PosterPrintRequestDbContext _db;

    public RequesterOptionCatalog(PosterPrintRequestDbContext db)
    {
        _db = db;
    }

    public async Task<RequesterChoices> LoadAsync(CancellationToken cancellationToken)
    {
        var departments = await _db.Departments
            .Where(department => department.Active)
            .OrderBy(department => department.Name)
            .Select(department => new SelectOption(department.DepartmentId.ToString(), department.Name))
            .ToListAsync(cancellationToken);

        var reasons = await _db.Reasons
            .Where(reason => reason.Active)
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

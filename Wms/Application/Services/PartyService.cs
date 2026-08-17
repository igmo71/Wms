using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services;

internal class PartyService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<ServiceResult<PartyInfo?>> GetAsync(
    Guid id,
    PartyType type,
    CancellationToken ct = default)
    {
        return type switch
        {
            PartyType.Warehouse => await GetWarehouseAsync(id, ct),

            PartyType.Partner => await GetPartnerAsync(id, ct),

            //PartyType.Individual => await GetIndividualAsync(id, ct),

            //PartyType.OrganizationalUnit => await GetOrganizationalUnitAsync(id, ct),

            _ => ServiceError.Failure("Invalid party type.")
        };
    }

    private async Task<ServiceResult<PartyInfo?>> GetPartnerAsync(Guid id, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    private async Task<ServiceResult<PartyInfo?>> GetWarehouseAsync(Guid id, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.Warehouses
            .Where(x => x.Id == id)
            .Select(x => new PartyInfo(
                x.Id,
                PartyType.Warehouse,
                x.Name ?? string.Empty))
            .SingleOrDefaultAsync(ct);
    }
}

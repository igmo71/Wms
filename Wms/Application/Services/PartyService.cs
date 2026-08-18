using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services;

public class PartyService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<IReadOnlyDictionary<PartyReference, PartyInfo>> GetManyAsync(
        IEnumerable<PartyReference> references,
        CancellationToken ct = default)
    {
        using var activity = AppTracing.StartActivity("Party GetMany", nameof(PartyService));

        var requestedReferences = references
            .Where(x => x.Id != Guid.Empty && IsSupported(x.Type))
            .Distinct()
            .ToList();

        if (requestedReferences.Count == 0)
            return new Dictionary<PartyReference, PartyInfo>();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var result = new Dictionary<PartyReference, PartyInfo>();

        var warehouseIds = GetIds(requestedReferences, PartyType.Warehouse);
        if (warehouseIds.Count > 0)
        {
            var parties = await dbContext.Warehouses
                .AsNoTracking()
                .Where(x => warehouseIds.Contains(x.Id))
                .Select(x => new PartyInfo(x.Id, PartyType.Warehouse, x.Name ?? string.Empty))
                .ToListAsync(ct);

            AddToResult(result, parties);
        }

        var partnerIds = GetIds(requestedReferences, PartyType.Partner);
        if (partnerIds.Count > 0)
        {
            var parties = await dbContext.Partners
                .AsNoTracking()
                .Where(x => partnerIds.Contains(x.Id))
                .Select(x => new PartyInfo(x.Id, PartyType.Partner, x.Name ?? string.Empty))
                .ToListAsync(ct);

            AddToResult(result, parties);
        }

        var individualIds = GetIds(requestedReferences, PartyType.Individual);
        if (individualIds.Count > 0)
        {
            var parties = await dbContext.Individuals
                .AsNoTracking()
                .Where(x => individualIds.Contains(x.Id) && !x.IsFolder)
                .Select(x => new PartyInfo(x.Id, PartyType.Individual, x.Name ?? string.Empty))
                .ToListAsync(ct);

            AddToResult(result, parties);
        }

        var organizationalUnitIds = GetIds(requestedReferences, PartyType.OrganizationalUnit);
        if (organizationalUnitIds.Count > 0)
        {
            var parties = await dbContext.OrganizationalUnits
                .AsNoTracking()
                .Where(x => organizationalUnitIds.Contains(x.Id))
                .Select(x => new PartyInfo(x.Id, PartyType.OrganizationalUnit, x.Name ?? string.Empty))
                .ToListAsync(ct);

            AddToResult(result, parties);
        }

        return result;
    }

    private static List<Guid> GetIds(
        IEnumerable<PartyReference> references,
        PartyType type) => references
            .Where(x => x.Type == type)
            .Select(x => x.Id)
            .Distinct()
            .ToList();

    private static void AddToResult(
        IDictionary<PartyReference, PartyInfo> result,
        IEnumerable<PartyInfo> parties)
    {
        foreach (var party in parties)
            result[new PartyReference(party.Id, party.Type)] = party;
    }

    private static bool IsSupported(PartyType type) => type is
        PartyType.Warehouse or
        PartyType.Partner or
        PartyType.Individual or
        PartyType.OrganizationalUnit;
}

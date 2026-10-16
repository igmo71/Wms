using Microsoft.EntityFrameworkCore;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS;
using Wms.Tests.Infrastructure;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingSourceSynchronizationTests(SqlDatabase database)
{
    [Fact]
    public async Task Failed_read_keeps_last_success_and_repeat_reprojects_without_new_version()
    {
        var refKey = Guid.NewGuid();
        var knownSku = Guid.NewGuid();
        var laterSku = Guid.NewGuid();
        var response = $$"""
            {"value":[{"Ref_Key":"{{refKey}}","Товары":[
              {"LineNumber":"1","Номенклатура_Key":"{{knownSku}}","Количество":2},
              {"LineNumber":"2","Номенклатура_Key":"{{knownSku}}","Количество":"3"},
              {"LineNumber":"3","Номенклатура_Key":"{{laterSku}}","Количество":4},
              {"LineNumber":"4","Номенклатура_Key":"{{knownSku}}","Количество":"unsupported"}
            ]}]}
            """;
        await using (var seed = database.CreateDbContext())
        {
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = knownSku, Name = "Known" });
            await seed.SaveChangesAsync();
        }

        var reader = new StubReader(refKey, response);
        var useCase = new SynchronizeReceivingSource(
            reader, new ReceivingSourceParser(),
            new ReceivingSourceStore(database, new ReceivingSourceParser()), TimeProvider.System);
        Assert.True((await useCase.ExecuteAsync(refKey)).IsSuccess);

        await using (var first = database.CreateDbContext())
        {
            var source = await first.ReceivingSourceDocuments.SingleAsync(x => x.RefKey == refKey);
            var version = await first.ReceivingSourceVersions.Include(x => x.Lines)
                .SingleAsync(x => x.Id == source.LastSuccessfulVersionId);
            Assert.Equal(response, version.SnapshotJson);
            Assert.Equal(4, version.Lines.Count);
            Assert.Equal(3, version.Lines.Count(x => x.StockKeepingUnitId == knownSku));
            Assert.Null(version.CalculatePlan().QuantityFor(knownSku));
            Assert.True(version.CalculatePlan().HasUnknownSku);
            first.StockKeepingUnits.Add(new StockKeepingUnit { Id = laterSku, Name = "Later" });
            await first.SaveChangesAsync();
        }

        Assert.True((await useCase.ExecuteAsync(refKey)).IsSuccess);
        reader.Response = response.Replace("\"Количество\":2", "\"Количество\":6");
        Assert.True((await useCase.ExecuteAsync(refKey)).IsSuccess);
        reader.Response = "{";
        Assert.False((await useCase.ExecuteAsync(refKey)).IsSuccess);
        reader.Fail = true;
        Assert.False((await useCase.ExecuteAsync(refKey)).IsSuccess);

        await using var verify = database.CreateDbContext();
        var saved = await verify.ReceivingSourceDocuments.SingleAsync(x => x.RefKey == refKey);
        var current = await verify.ReceivingSourceVersions.Include(x => x.Lines)
            .SingleAsync(x => x.Id == saved.LastSuccessfulVersionId);
        Assert.Equal(2, await verify.ReceivingSourceVersions.CountAsync(x => x.ReceivingSourceDocumentId == saved.Id));
        Assert.Contains("\"Количество\":6", current.SnapshotJson);
        Assert.Equal(4, current.Lines.Count);
        Assert.Equal(4m, current.CalculatePlan().QuantityFor(laterSku));
        Assert.False(current.CalculatePlan().HasUnknownSku);
        Assert.Null(current.CalculatePlan().QuantityFor(knownSku));
        Assert.NotNull(saved.LastSuccessfulCheckAtUtc);
        Assert.NotNull(saved.LastAttemptAtUtc);
        Assert.Equal("1С недоступна", saved.LastAttemptError);
    }

    private sealed class StubReader(Guid refKey, string response) : IReceivingSourceReader
    {
        public bool Fail { get; set; }
        public string Response { get; set; } = response;

        public Task<OperationResult<ReceivingSourceRead>> ReadAsync(Guid requested, CancellationToken ct) =>
            Task.FromResult<OperationResult<ReceivingSourceRead>>(Fail
                ? OperationError.Failure("1С недоступна")
                : new ReceivingSourceRead(refKey, Response));
    }
}

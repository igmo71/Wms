using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wms.Domain;
using Wms.Integration.OneS;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Picking;

public sealed class ShippingSourceWriterTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid SkuId = Guid.NewGuid();

    [Fact]
    public async Task Matching_target_skips_patch_but_unposted_document_still_posts()
    {
        var original = Source();
        var target = Target(original);
        var built = Value(new ShippingTargetBuilder().Build(target, original));
        var currentJson = JsonNode.Parse(original)!.AsObject();
        var patch = JsonNode.Parse(built.PatchJson)!.AsObject();
        foreach (var (name, value) in patch)
            currentJson[name] = value?.DeepClone();
        currentJson["Posted"] = false;
        var current = Value(new ShippingStoredSnapshotParser()
            .Parse(OrderId, currentJson.ToJsonString()));
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });
        using var client = Client(handler);
        var writer = new ShippingSourceWriter(client);

        Success(await writer.ApplyTargetAsync(target, original, current,
            CancellationToken.None));
        Success(await writer.PostAsync(OrderId, CancellationToken.None));

        Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.Calls[0].Method);
        Assert.EndsWith($"Document_РасходныйОрдерНаТовары(guid'{OrderId:D}')/Post?$format=json",
            handler.Calls[0].Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_writes_target_once_and_failed_post_can_be_retried()
    {
        var original = Source();
        var target = Target(original);
        var current = Value(new ShippingStoredSnapshotParser()
            .Parse(OrderId, original));
        var handler = new RecordingHandler(call =>
            new HttpResponseMessage(call == 2
                ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            {
                Content = new StringContent(call == 2 ? "posting failed" : "{}")
            });
        using var client = Client(handler);
        var writer = new ShippingSourceWriter(client);

        Success(await writer.ApplyTargetAsync(target, original, current,
            CancellationToken.None));
        var firstPost = await writer.PostAsync(OrderId, CancellationToken.None);
        Assert.False(firstPost.IsSuccess);
        Success(await writer.PostAsync(OrderId, CancellationToken.None));

        Assert.Equal(new[] { HttpMethod.Patch, HttpMethod.Post, HttpMethod.Post },
            handler.Calls.Select(x => x.Method));
        using var patch = JsonDocument.Parse(handler.Calls[0].Body);
        Assert.Equal("Отгружен", patch.RootElement.GetProperty("Статус").GetString());
        Assert.Single(patch.RootElement.GetProperty("ОтгружаемыеТовары")
            .EnumerateArray());
        Assert.DoesNotContain("PostingModeOperational", handler.Calls[0].Body);
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://one-c.example/odata/standard.odata/")
    };

    private static ShippingExchangeTarget Target(string source) =>
        Value(ShippingExchangeTarget.Create(Guid.NewGuid(), OrderId, 1,
            source, JsonSerializer.Serialize(new
            {
                Version = 1, Status = "Shipped",
                ApprovedPlan = new[] { new { SkuId, Quantity = 3m } },
                Allocation = new[] { new { SkuId, Quantity = 3m } }
            })));

    private static string Source() => new JsonObject
    {
        ["Ref_Key"] = OrderId.ToString(),
        ["Статус"] = "КОтбору",
        ["Posted"] = true,
        ["ОтгружаемыеТовары"] = new JsonArray
        {
            new JsonObject
            {
                ["LineNumber"] = 1,
                ["Номенклатура_Key"] = SkuId.ToString(),
                ["Количество"] = 3,
                ["КоличествоУпаковок"] = 3,
                ["Действие"] = "Отгрузить",
                ["ЭтоУпаковочныйЛист"] = false,
                ["ЭтоСлужебнаяСтрокаПустогоУпаковочногоЛиста"] = 0
            }
        },
        ["ТоварыПоРаспоряжениям"] = new JsonArray
        {
            new JsonObject
            {
                ["LineNumber"] = 1,
                ["Номенклатура_Key"] = SkuId.ToString(),
                ["Количество"] = 3,
                ["Распоряжение"] = Guid.NewGuid().ToString()
            }
        }
    }.ToJsonString();

    private sealed class RecordingHandler(Func<int, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.ToString(), body));
            return respond(Calls.Count);
        }
    }
}

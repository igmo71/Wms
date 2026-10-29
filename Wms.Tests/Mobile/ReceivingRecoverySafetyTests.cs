using System.Net;
using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Tests.Mobile;

public sealed class ReceivingRecoverySafetyTests
{
    [Theory]
    [InlineData("close")]
    [InlineData("edit")]
    public async Task Lost_response_retries_original_receiving_command_and_blocks_replacement(string kind)
    {
        using var handler = new LostResponseHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://wms.invalid/") };
        var process = new ReceivingOrderReceivingProcess(new MobileReceivingOrderClient(new MobileApiTransport(http)));
        var orderId = Guid.NewGuid();
        var lpnId = Guid.NewGuid();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            ExecuteAsync(process, kind, orderId, lpnId));
        Assert.True(process.HasPendingCommand);
        // A pending command retains its original LPN, quantity and date.
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = ExecuteAsync(process, kind, Guid.NewGuid(), Guid.NewGuid());
        });
        Assert.Single(handler.Requests);

        await process.RetryAsync();
        Assert.False(process.HasPendingCommand);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0], handler.Requests[1]);
        Assert.Single(handler.AppliedRequestIds);
    }

    private static Task<MobileReceivingWorkOrderResponse> ExecuteAsync(
        ReceivingOrderReceivingProcess process, string kind, Guid orderId, Guid lpnId) => kind switch
        {
            "close" => process.CloseLpnAsync(orderId, lpnId, null),
            "edit" => process.EditLpnAsync(orderId, lpnId,
                Guid.Parse("22d96f33-94af-4f27-81cf-49caa448a93d"),
                1.25m, new DateOnly(2027, 1, 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private sealed class LostResponseHandler : HttpMessageHandler
    {
        public List<(string Route, string Body)> Requests { get; } = [];
        public HashSet<Guid> AppliedRequestIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new MobileReceivingWorkOrderResponse(
                        Guid.Empty, "990101-000001", DateTimeOffset.UtcNow, Guid.Empty,
                        MobileReceivingOrderStatus.InReceiving, 1, null,
                        true, 0, [], [], []))
                };
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            using var payload = System.Text.Json.JsonDocument.Parse(body);
            var requestId = payload.RootElement.GetProperty("clientRequestId").GetGuid();
            AppliedRequestIds.Add(requestId);
            if (Requests.Count == 1)
                throw new TaskCanceledException("Response lost after commit.", new TimeoutException());

            // The second request represents a receipt replay.
            return new(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath.EndsWith("/close")
                    ? JsonContent.Create(new MobileReceivingCloseResultResponse(
                        Guid.Empty, 1m, 1m, true, null, null, false))
                    : JsonContent.Create(new MobileReceivingCommandIdResponse(Guid.Empty))
            };
        }
    }
}

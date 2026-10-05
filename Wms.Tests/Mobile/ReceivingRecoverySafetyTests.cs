using System.Net;
using Wms.Mobile.Services;

namespace Wms.Tests.Mobile;

public sealed class ReceivingRecoverySafetyTests
{
    [Theory]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("complete")]
    public async Task Lost_response_retries_original_receiving_command_and_blocks_replacement(string kind)
    {
        using var handler = new LostResponseHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://wms.invalid/") };
        var process = new ReceivingOrderReceivingProcess(new MobileReceivingOrderClient(new MobileApiTransport(http)));
        var orderId = Guid.NewGuid();
        var lpnId = Guid.NewGuid();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            ExecuteAsync(process, kind, orderId, lpnId, "original-fingerprint"));
        Assert.True(process.HasPendingCommand);
        // Refreshed data must not replace the original order, LPN or fingerprint.
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = ExecuteAsync(process, kind, Guid.NewGuid(), Guid.NewGuid(), "refreshed-fingerprint");
        });
        Assert.Single(handler.Requests);

        await process.RetryAsync();
        Assert.False(process.HasPendingCommand);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0], handler.Requests[1]);
        Assert.Single(handler.AppliedRequestIds);
    }

    private static Task<Wms.Contracts.Mobile.V1.MobileReceivingOrderCommandResponse> ExecuteAsync(
        ReceivingOrderReceivingProcess process, string kind, Guid orderId, Guid lpnId,
        string fingerprint) => kind switch
        {
            "close" => process.CloseLicensePlateNumberAsync(orderId, lpnId),
            "cancel" => process.CancelLicensePlateNumberAsync(orderId, lpnId),
            "complete" => process.CompleteAsync(orderId, fingerprint),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private sealed class LostResponseHandler : HttpMessageHandler
    {
        public List<(string Route, string Body)> Requests { get; } = [];
        public HashSet<Guid> AppliedRequestIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            using var payload = System.Text.Json.JsonDocument.Parse(body);
            var requestId = payload.RootElement.GetProperty("clientRequestId").GetGuid();
            AppliedRequestIds.Add(requestId);
            if (Requests.Count == 1)
                throw new TaskCanceledException("Response lost after commit.", new TimeoutException());

            // The second request represents a receipt replay; rendering belongs to UI.
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"details\":null}") };
        }
    }
}

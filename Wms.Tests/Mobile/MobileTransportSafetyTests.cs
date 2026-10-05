using System.Net;
using Wms.Mobile.Services;

namespace Wms.Tests.Mobile;

public sealed class MobileTransportSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_is_uncertain_and_transport_does_not_retry(bool post)
    {
        using var handler = new CancelledRequestHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://wms.invalid/") };
        var transport = new MobileApiTransport(client);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => post
            ? transport.PostAsync("command", new { ClientRequestId = Guid.NewGuid() })
            : transport.GetAsync("order"));

        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_is_not_misreported_as_timeout(bool post)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new CancelledRequestHandler(cancellation.Cancel);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://wms.invalid/") };
        var transport = new MobileApiTransport(client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => post
            ? transport.PostAsync("command", new { ClientRequestId = Guid.NewGuid() }, cancellation.Token)
            : transport.GetAsync("order", cancellation.Token));

        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Http_rejection_keeps_its_existing_recovery_classification(HttpStatusCode status)
    {
        using var client = new HttpClient(new ResponseHandler(status))
        {
            BaseAddress = new Uri("https://wms.invalid/")
        };
        var transport = new MobileApiTransport(client);

        if (status == HttpStatusCode.Conflict)
            await Assert.ThrowsAsync<MobileApiException>(() => transport.PostAsync("command", new { }));
        else
            await Assert.ThrowsAsync<HttpRequestException>(() => transport.PostAsync("command", new { }));
    }

    [Fact]
    public async Task Unreadable_success_is_still_an_uncertain_command_result()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json")
        };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            MobileApiTransport.ReadCommandResponseAsync<object>(
                response, "Результат не подтверждён.", CancellationToken.None));
    }

    private sealed class CancelledRequestHandler(Action? cancelCaller = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            cancelCaller?.Invoke();
            // Deterministic timeout-shaped failure; no network or wall-clock wait.
            return Task.FromException<HttpResponseMessage>(new TaskCanceledException(
                "Response unavailable", new TimeoutException(), cancellationToken));
        }
    }

    private sealed class ResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }
}

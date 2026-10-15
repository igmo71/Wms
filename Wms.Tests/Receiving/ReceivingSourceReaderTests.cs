using System.Net;
using Wms.Integration.OneS;

namespace Wms.Tests.Receiving;

public sealed class ReceivingSourceReaderTests
{
    [Fact]
    public async Task Read_returns_unchanged_source_response_for_requested_ref_key()
    {
        const string responseJson = "{\"value\":[{\"Ref_Key\":\"source\",\"Товары\":[{\"Количество\":\"unsupported\"}]}]}";
        var handler = new SourceHandler(responseJson);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.invalid/odata/standard.odata/")
        };
        var reader = new ReceivingSourceReader(client);
        var refKey = Guid.NewGuid();

        var result = await reader.ReadAsync(refKey, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(refKey, result.Value!.RefKey);
        Assert.Equal(responseJson, result.Value.RawResponseJson);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpMethod.Get, handler.Method);
        var uri = Uri.UnescapeDataString(handler.RequestUri!.AbsoluteUri);
        Assert.Contains("Document_ПриходныйОрдерНаТовары", uri);
        Assert.Contains($"Ref_Key eq guid'{refKey:D}'", uri);
        Assert.Contains("Товары", uri);

        var invalid = await reader.ReadAsync(Guid.Empty, CancellationToken.None);
        Assert.False(invalid.IsSuccess);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class SourceHandler(string responseJson) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }
}

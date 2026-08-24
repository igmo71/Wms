using System.Net;

namespace Wms.Mobile.Services;

internal sealed class MobileApiException(
    HttpStatusCode statusCode,
    string code,
    string safeMessage) : Exception(safeMessage)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

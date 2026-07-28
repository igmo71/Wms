using Wms.WebApp.Integration.OneS.Models;

namespace Wms.WebApp.Integration.OneS.Services;

public class Catalog_УпаковкиЕдиницыИзмерения_Service(OneCClient oneCClient)
{
    private readonly OneCClient _oneCClient = oneCClient;

    public async Task<List<Catalog_УпаковкиЕдиницыИзмерения>?> GetList(CancellationToken ct)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetListUri;
        var rootObject = await _oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }
}

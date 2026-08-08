using Microsoft.AspNetCore.Routing;

namespace Wms.Integration.OneS.Endpoints;

public static class OneCEndpoints
{
    public static IEndpointRouteBuilder MapOneCEndpints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapCatalog_ЗоныДоставки_Endpoints();
        routeBuilder.MapCatalog_Номенклатура_Endpoints();
        routeBuilder.MapCatalog_Склады_Endpoints();
        routeBuilder.MapCatalog_УпаковкиЕдиницыИзмерения_Endpoints();
        routeBuilder.MapDocument_ПриходныйОрдерНаТовары_Endpoints();
        routeBuilder.MapInformationRegister_ШтрихкодыНоменклатуры_Endpoints();

        return routeBuilder;
    }
}

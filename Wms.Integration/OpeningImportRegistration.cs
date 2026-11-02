using Microsoft.Extensions.DependencyInjection;
using Wms.Application.OpeningImports;
using Wms.Integration.OneS;

namespace Wms.Integration;

public static class OpeningImportRegistration
{
    public static IServiceCollection AddOpeningExportReader(this IServiceCollection services) =>
        services.AddSingleton<IOpeningExportReader, OpeningExportReader>();
}

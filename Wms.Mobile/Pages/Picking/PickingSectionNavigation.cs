using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

internal enum PickingSection { Sku, Route, Replenishments, Sources }

internal static class PickingSectionNavigation
{
    public static async Task SwitchAsync(ContentPage current, IServiceProvider services,
        MobilePickingAssemblyResponse assembly, PickingAssemblyProcess process,
        PickingSection section)
    {
        ContentPage page = section switch
        {
            PickingSection.Sku => services.GetRequiredService<PickingSkuPage>(),
            PickingSection.Route => services.GetRequiredService<PickingRoutePage>(),
            PickingSection.Replenishments => services.GetRequiredService<PickingReplenishmentsPage>(),
            PickingSection.Sources => services.GetRequiredService<PickingSourcesPage>(),
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        switch (page)
        {
            case PickingSkuPage sku: sku.Show(assembly, process); break;
            case PickingRoutePage route: route.Show(assembly, process); break;
            case PickingReplenishmentsPage replenishments:
                replenishments.Show(assembly, process); break;
            case PickingSourcesPage sources: sources.Show(assembly, process); break;
        }
        await current.Navigation.PushAsync(page);
        current.Navigation.RemovePage(current);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

internal enum PickingSection { Sku, Route, Sources }

internal static class PickingTitle
{
    public static void Set(Label label, string section, string? number)
    {
        var title = new FormattedString();
        title.Spans.Add(new Span
        {
            Text = section,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold
        });
        if (!string.IsNullOrWhiteSpace(number))
            title.Spans.Add(new Span { Text = $" · {number}", FontSize = 13 });
        label.FormattedText = title;
    }
}

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
            PickingSection.Sources => services.GetRequiredService<PickingSourcesPage>(),
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        switch (page)
        {
            case PickingSkuPage sku: sku.Show(assembly, process); break;
            case PickingRoutePage route: route.Show(assembly, process); break;
            case PickingSourcesPage sources: sources.Show(assembly, process); break;
        }
        await current.Navigation.PushAsync(page);
        current.Navigation.RemovePage(current);
    }
}

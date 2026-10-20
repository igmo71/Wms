using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Data;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Sources
{
    [Inject] private GetReceivingSources ReceivingSources { get; set; } = null!;
    [Inject] private CreateReceivingOrder CreateOrder { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private IReadOnlyList<ReceivingSourceView> _sources = [];
    private readonly HashSet<Guid> _selected = [];
    private (Guid RequestId, Guid[] SourceRefKeys)? _pendingCreate;
    private string? _sourceFilter;
    private string? _error;
    private bool _canManage;
    private bool _busy;

    private IEnumerable<ReceivingSourceView> FilteredSources =>
        string.IsNullOrWhiteSpace(_sourceFilter) ? _sources
            : _sources.Where(x =>
                x.Number?.Contains(_sourceFilter.Trim(), StringComparison.OrdinalIgnoreCase) == true
                || x.RefKey.ToString().Contains(_sourceFilter.Trim(), StringComparison.OrdinalIgnoreCase));

    private IEnumerable<ReceivingSourceView> SelectedSources =>
        _sources.Where(x => x.LinkedOrderId is null && _selected.Contains(x.RefKey));

    private bool CanCreate => _canManage && (_pendingCreate is not null ||
        (_selected.Count > 0 && SelectedSources.Count() == _selected.Count
            && SelectedSources.All(x => x.WarehouseId is not null && x.VersionId is not null)
            && SelectedSources.Select(x => x.WarehouseId).Distinct().Count() == 1));

    private IReadOnlyList<(Guid SkuId, decimal? Quantity)> SelectedPlan
    {
        get
        {
            var sources = SelectedSources.ToArray();
            var skuIds = sources.SelectMany(x => x.PlanBySku.Keys).Distinct().Order().ToArray();
            return skuIds.Select(skuId =>
            {
                decimal? total = 0;
                foreach (var source in sources)
                {
                    if (!source.PlanBySku.TryGetValue(skuId, out var part))
                        continue;
                    if (part is null || total is null)
                    {
                        total = null;
                        continue;
                    }
                    try { total = checked(total.Value + part.Value); }
                    catch (OverflowException) { total = null; }
                }
                return (skuId, total);
            }).ToArray();
        }
    }

    private string SkuName(Guid skuId) =>
        SelectedSources.SelectMany(x => x.SkuNames)
            .FirstOrDefault(x => x.Key == skuId).Value ?? skuId.ToString();

    private static string SourceDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)
            : value ?? "—";

    private static string SourceStatus(ReceivingSourceView source) =>
        source.Posted == false ? "Не проведён"
            : source.DeletionMark == true ? "Помечен удалённым"
            : source.Posted == true && source.DeletionMark == false ? "Актуален"
            : "Неизвестно";

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            _canManage = user.IsInRole(ApplicationRoles.Manager);
            _sources = await ReceivingSources.ListAllAsync();
            _selected.IntersectWith(_sources.Where(x => x.LinkedOrderId is null)
                .Select(x => x.RefKey));
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally { _busy = false; }
    }

    private void ToggleSource(Guid refKey, bool selected)
    {
        if (!_canManage || !_sources.Any(x => x.RefKey == refKey && x.LinkedOrderId is null))
            return;
        _pendingCreate = null;
        if (selected)
            _selected.Add(refKey);
        else
            _selected.Remove(refKey);
    }

    private async Task CreateAsync()
    {
        if (_busy || !CanCreate)
            return;
        _busy = true;
        _error = null;
        try
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            if (!user.IsInRole(ApplicationRoles.Manager))
            {
                _canManage = false;
                _error = "Создать ордер в Web может только менеджер.";
                return;
            }
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                _error = "Не удалось определить пользователя.";
                return;
            }
            _pendingCreate ??= (Guid.NewGuid(), _selected.ToArray());
            var pending = _pendingCreate.Value;
            var result = await CreateOrder.ExecuteAsync(new CreateReceivingOrderRequest(
                pending.RequestId, pending.SourceRefKeys, userId));
            if (result.IsSuccess)
            {
                _pendingCreate = null;
                Navigation.NavigateTo($"receiving-orders/{result.Value}");
            }
            else
            {
                _pendingCreate = null;
                _error = result.Error!.Message;
            }
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally { _busy = false; }
    }
}

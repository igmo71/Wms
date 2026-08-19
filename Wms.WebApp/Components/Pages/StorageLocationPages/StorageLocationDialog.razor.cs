using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StorageLocationPages;

public partial class StorageLocationDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter, EditorRequired] public Warehouse Warehouse { get; set; } = null!;
    [Parameter, EditorRequired] public Zone Zone { get; set; } = null!;
    [Parameter] public StorageLocation? Parent { get; set; }
    [Parameter] public StorageLocation? StorageLocation { get; set; }
    [Inject] private StorageLocationService StorageLocationService { get; set; } = null!;

    private string _name = string.Empty;
    private bool _isFolder;
    private int _number = 1;
    private int _segmentWidth = 2;
    private long? _pickSequence;
    private LocationDimensions _dimensions = new();
    private LocationCoordinates _coordinates = new();
    private bool _isSaving;
    private string? _errorMessage;

    private string FullAddress => $"{Zone.Code}-{StorageLocation?.Code}";

    protected override void OnInitialized()
    {
        if (StorageLocation is null)
            return;

        _name = StorageLocation.Name ?? string.Empty;
        _isFolder = StorageLocation.IsFolder;
        _pickSequence = StorageLocation.PickSequence;
        _dimensions = new LocationDimensions
        {
            Length = StorageLocation.Dimensions.Length,
            Width = StorageLocation.Dimensions.Width,
            Height = StorageLocation.Dimensions.Height,
            Volume = StorageLocation.Dimensions.Volume,
            VolumeFactor = StorageLocation.Dimensions.VolumeFactor,
            MaxWeight = StorageLocation.Dimensions.MaxWeight
        };
        _coordinates = new LocationCoordinates
        {
            X = StorageLocation.Coordinates.X,
            Y = StorageLocation.Coordinates.Y,
            Z = StorageLocation.Coordinates.Z
        };
    }

    private async Task SaveAsync()
    {
        _isSaving = true;
        _errorMessage = null;
        try
        {
            ServiceResult result;
            if (StorageLocation is null)
            {
                result = await StorageLocationService.CreateAsync(new CreateStorageLocationRequest
                {
                    WarehouseId = Warehouse.Id,
                    ZoneId = Zone.Id,
                    ParentId = Parent?.Id,
                    Number = _number,
                    SegmentWidth = _segmentWidth,
                    Name = _name,
                    IsFolder = _isFolder,
                    Dimensions = _dimensions,
                    Coordinates = _coordinates,
                    PickSequence = _pickSequence
                });
            }
            else
            {
                result = await StorageLocationService.UpdateAsync(StorageLocation.Id, new UpdateStorageLocationRequest
                {
                    Name = _name,
                    IsFolder = _isFolder,
                    Dimensions = _dimensions,
                    Coordinates = _coordinates,
                    PickSequence = _pickSequence
                });
            }

            if (!result.IsSuccess)
            {
                _errorMessage = result.Error?.Message ?? "Не удалось сохранить складскую позицию.";
                return;
            }

            MudDialog.Close(DialogResult.Ok(true));
        }
        catch
        {
            _errorMessage = "Не удалось сохранить складскую позицию.";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void Cancel() => MudDialog.Cancel();
}

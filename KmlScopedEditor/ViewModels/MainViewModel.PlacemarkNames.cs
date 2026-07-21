using System.Collections.ObjectModel;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;

namespace KmlScopedEditor.ViewModels;

public partial class MainViewModel
{
    private readonly KmlPlacemarkNameSelectionService
        _placemarkNameSelectionService = new();

    private IReadOnlyList<PlacemarkNameOption> _placemarkNameInventory =
        Array.Empty<PlacemarkNameOption>();

    private KmlDocumentContext? _placemarkNameInventoryContext;
    private string _placemarkNameSearchText = string.Empty;
    private bool _placemarkNameExactMatchOnly;
    private bool _updatingPlacemarkNameSelections;
    private RelayCommand? _selectVisiblePlacemarkNamesCommand;
    private RelayCommand? _clearPlacemarkNameSelectionsCommand;

    /// <summary>
    /// The currently visible name groups after applying the search filter.
    /// Checked groups remain selected when they are filtered out of view.
    /// </summary>
    public ObservableCollection<PlacemarkNameOption> PlacemarkNameOptions { get; }
        = new();

    public string PlacemarkNameSearchText
    {
        get => _placemarkNameSearchText;
        set
        {
            if (SetProperty(ref _placemarkNameSearchText, value))
                RefreshVisiblePlacemarkNameOptions();
        }
    }

    public bool PlacemarkNameExactMatchOnly
    {
        get => _placemarkNameExactMatchOnly;
        set
        {
            if (SetProperty(ref _placemarkNameExactMatchOnly, value))
                RefreshVisiblePlacemarkNameOptions();
        }
    }

    public string PlacemarkNameSearchSummary
    {
        get
        {
            var selectedCount = _placemarkNameInventory.Count(option => option.IsSelected);
            return $"{PlacemarkNameOptions.Count:N0} visible names; " +
                   $"{selectedCount:N0} selected.";
        }
    }

    public RelayCommand SelectVisiblePlacemarkNamesCommand =>
        _selectVisiblePlacemarkNamesCommand ??= new RelayCommand(
            _ => SetVisiblePlacemarkNameSelection(true),
            _ => PlacemarkNameOptions.Count > 0);

    public RelayCommand ClearPlacemarkNameSelectionsCommand =>
        _clearPlacemarkNameSelectionsCommand ??= new RelayCommand(
            _ => ClearPlacemarkNameSelections(),
            _ => _placemarkNameInventory.Any(option => option.IsSelected));

    private IReadOnlyList<PlacemarkNameOption>
        GetSelectedPlacemarkNameOptions()
    {
        EnsurePlacemarkNameInventory();
        return _placemarkNameInventory
            .Where(option => option.IsSelected)
            .ToList();
    }

    private void RefreshVisiblePlacemarkNameOptions()
    {
        PlacemarkNameOptions.Clear();

        if (SelectedSelectionMode.Value !=
            PlacemarkSelectionMode.PlacemarkName)
        {
            RaisePlacemarkNameCommandStates();
            OnPropertyChanged(nameof(PlacemarkNameSearchSummary));
            return;
        }

        EnsurePlacemarkNameInventory();

        var search = PlacemarkNameSearchText?.Trim() ?? string.Empty;

        IEnumerable<PlacemarkNameOption> filtered = _placemarkNameInventory;

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = PlacemarkNameExactMatchOnly
                ? filtered.Where(option => string.Equals(
                    option.DisplayName,
                    search,
                    StringComparison.OrdinalIgnoreCase))
                : filtered.Where(option => option.DisplayName.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase));
        }

        foreach (var option in filtered)
            PlacemarkNameOptions.Add(option);

        RaisePlacemarkNameCommandStates();
        OnPropertyChanged(nameof(PlacemarkNameSearchSummary));
    }

    private void EnsurePlacemarkNameInventory()
    {
        if (_documentContext is null)
        {
            _placemarkNameInventory = Array.Empty<PlacemarkNameOption>();
            _placemarkNameInventoryContext = null;
            return;
        }

        if (ReferenceEquals(
                _placemarkNameInventoryContext,
                _documentContext))
        {
            return;
        }

        _placemarkNameInventory =
            _placemarkNameSelectionService.BuildInventory(_documentContext);
        _placemarkNameInventoryContext = _documentContext;

        foreach (var option in _placemarkNameInventory)
        {
            option.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName !=
                    nameof(PlacemarkNameOption.IsSelected))
                {
                    return;
                }

                OnPropertyChanged(nameof(PlacemarkNameSearchSummary));
                RaisePlacemarkNameCommandStates();

                if (!_updatingPlacemarkNameSelections)
                    ResetCalculatedSelection();
            };
        }
    }

    private void SetVisiblePlacemarkNameSelection(bool isSelected)
    {
        _updatingPlacemarkNameSelections = true;

        try
        {
            foreach (var option in PlacemarkNameOptions)
                option.IsSelected = isSelected;
        }
        finally
        {
            _updatingPlacemarkNameSelections = false;
        }

        ResetCalculatedSelection();
        RaisePlacemarkNameCommandStates();
        OnPropertyChanged(nameof(PlacemarkNameSearchSummary));
    }

    private void ClearPlacemarkNameSelections()
    {
        _updatingPlacemarkNameSelections = true;

        try
        {
            foreach (var option in _placemarkNameInventory)
                option.IsSelected = false;
        }
        finally
        {
            _updatingPlacemarkNameSelections = false;
        }

        ResetCalculatedSelection();
        RaisePlacemarkNameCommandStates();
        OnPropertyChanged(nameof(PlacemarkNameSearchSummary));
    }

    private void RaisePlacemarkNameCommandStates()
    {
        _selectVisiblePlacemarkNamesCommand?.RaiseCanExecuteChanged();
        _clearPlacemarkNameSelectionsCommand?.RaiseCanExecuteChanged();
    }
}

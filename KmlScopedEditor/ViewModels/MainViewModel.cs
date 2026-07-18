using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using DrawingColor = System.Drawing.Color;
using Forms = System.Windows.Forms;

namespace KmlScopedEditor.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly XNamespace KmlNs =
        "http://www.opengis.net/kml/2.2";

    private readonly KmlLoader _loader = new();
    private readonly KmlStyleInspector _styleInspector = new();
    private readonly KmlSelectionService _selectionService = new();
    private readonly KmlBatchEditService _batchEditService = new();
    private readonly KmlPackageService _packageService;

    private CancellationTokenSource? _operationCancellationSource;
    private KmlDocumentContext? _documentContext;
    private KmlTreeNode? _selectedNode;
    private KmlResolvedStyle? _selectedStyle;
    private KmlBatchEditPreview? _editPreview;

    private string _statusText = "Ready.";
    private string _loadedFilePath = string.Empty;
    private string _batchEditMessage =
        "Calculate a placemark selection, enable one or more properties, then preview the changes.";
    private bool _hasUnsavedChanges;

    private SelectionModeOption _selectedSelectionMode;
    private bool _includeSubfolders = true;
    private int _matchedPlacemarkCount;
    private string _selectionSummary =
        "Selection has not been calculated.";

    private IReadOnlyList<XElement> _currentSelection =
        Array.Empty<XElement>();

    private IReadOnlyList<IconTypeOption> _iconImageInventory =
        Array.Empty<IconTypeOption>();

    private IReadOnlyList<IconTypeOption> _iconVariantInventory =
        Array.Empty<IconTypeOption>();

    private bool _isBusy;
    private string _busyTitle = string.Empty;
    private string _busyMessage = string.Empty;
    private string _busyDetail = string.Empty;
    private double _busyProgress;
    private bool _isBusyProgressIndeterminate = true;
    private bool _canCancelCurrentOperation;

    private bool _hasNotification;
    private string _notificationText = string.Empty;
    private NotificationKind _notificationKind = NotificationKind.Information;

    private bool _hasLoadSummary;
    private string _loadSummaryText = string.Empty;

    public ObservableCollection<KmlTreeNode> RootNodes { get; } = new();

    /// <summary>
    /// The icon options currently displayed. Depending on the selected mode,
    /// these are either icon-image groups or icon variants.
    /// </summary>
    public ObservableCollection<IconTypeOption> IconTypes { get; } = new();

    public IReadOnlyList<SelectionModeOption> SelectionModes { get; }

    public IReadOnlyList<XElement> CurrentSelection => _currentSelection;

    public KmlBatchEditSettings EditSettings { get; } = new();

    public SelectionModeOption SelectedSelectionMode
    {
        get => _selectedSelectionMode;
        set
        {
            if (SetProperty(ref _selectedSelectionMode, value))
            {
                OnPropertyChanged(nameof(IsFolderSelectionMode));
                OnPropertyChanged(nameof(IsIconTypeSelectionMode));
                OnPropertyChanged(nameof(IconSelectionPrompt));

                RefreshVisibleIconOptions();
                ResetCalculatedSelection();
            }
        }
    }

    public bool IsFolderSelectionMode =>
        SelectedSelectionMode.Value == PlacemarkSelectionMode.Folder;

    public bool IsIconTypeSelectionMode =>
        SelectedSelectionMode.Value == PlacemarkSelectionMode.IconImage ||
        SelectedSelectionMode.Value == PlacemarkSelectionMode.IconVariant;

    public string IconSelectionPrompt =>
        SelectedSelectionMode.Value == PlacemarkSelectionMode.IconVariant
            ? "Select one or more icon variants (image + icon colour + icon size)"
            : "Select one or more icon images";

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set
        {
            if (SetProperty(ref _includeSubfolders, value))
                ResetCalculatedSelection();
        }
    }

    public int MatchedPlacemarkCount
    {
        get => _matchedPlacemarkCount;
        private set => SetProperty(ref _matchedPlacemarkCount, value);
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetProperty(ref _selectionSummary, value);
    }

    public KmlTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                OnPropertyChanged(nameof(SelectedNodeSummary));
                OnPropertyChanged(nameof(StyleDiagnosticsMessage));

                UpdateSelectedStyle();
                ResetCalculatedSelection();
            }
        }
    }

    public string StyleDiagnosticsMessage
    {
        get
        {
            if (SelectedNode?.NodeType == KmlNodeType.Placemark)
            {
                return
                    "Resolved normal-state style for the selected placemark.";
            }

            return
                "Select a placemark to inspect its effective style.";
        }
    }

    public KmlResolvedStyle? SelectedStyle
    {
        get => _selectedStyle;
        private set => SetProperty(ref _selectedStyle, value);
    }

    public KmlBatchEditPreview? EditPreview
    {
        get => _editPreview;
        private set => SetProperty(ref _editPreview, value);
    }

    public string BatchEditMessage
    {
        get => _batchEditMessage;
        private set => SetProperty(ref _batchEditMessage, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (SetProperty(ref _hasUnsavedChanges, value))
                OnPropertyChanged(nameof(UnsavedChangesDisplay));
        }
    }

    public string UnsavedChangesDisplay =>
        HasUnsavedChanges ? "Unsaved changes" : string.Empty;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string LoadedFilePath
    {
        get => _loadedFilePath;
        set => SetProperty(ref _loadedFilePath, value);
    }

    public string SelectedNodeSummary
    {
        get
        {
            if (SelectedNode is null)
                return "No selection.";

            return
                $"Type: {SelectedNode.NodeType} | " +
                $"Name: {SelectedNode.Name} | " +
                $"Descendant placemarks: " +
                $"{SelectedNode.DescendantPlacemarkCount:N0}";
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                RaiseCommandStates();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string BusyTitle
    {
        get => _busyTitle;
        private set => SetProperty(ref _busyTitle, value);
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetProperty(ref _busyMessage, value);
    }

    public string BusyDetail
    {
        get => _busyDetail;
        private set => SetProperty(ref _busyDetail, value);
    }

    public double BusyProgress
    {
        get => _busyProgress;
        private set
        {
            // ProgressBar.Value rejects NaN and infinity. Long-running
            // operations may briefly report an unknown or invalid percentage,
            // so always expose a finite value within the configured range.
            var safeValue = double.IsFinite(value)
                ? Math.Clamp(value, 0d, 100d)
                : 0d;

            SetProperty(ref _busyProgress, safeValue);
        }
    }

    public bool IsBusyProgressIndeterminate
    {
        get => _isBusyProgressIndeterminate;
        private set => SetProperty(ref _isBusyProgressIndeterminate, value);
    }

    public bool CanCancelCurrentOperation
    {
        get => _canCancelCurrentOperation;
        private set
        {
            if (SetProperty(ref _canCancelCurrentOperation, value))
                CancelOperationCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasNotification
    {
        get => _hasNotification;
        private set => SetProperty(ref _hasNotification, value);
    }

    public string NotificationText
    {
        get => _notificationText;
        private set => SetProperty(ref _notificationText, value);
    }

    public NotificationKind NotificationKind
    {
        get => _notificationKind;
        private set => SetProperty(ref _notificationKind, value);
    }

    public bool HasLoadSummary
    {
        get => _hasLoadSummary;
        private set => SetProperty(ref _hasLoadSummary, value);
    }

    public string LoadSummaryText
    {
        get => _loadSummaryText;
        private set => SetProperty(ref _loadSummaryText, value);
    }

    public AsyncRelayCommand OpenKmlCommand { get; }

    public AsyncRelayCommand SaveAsCommand { get; }

    public AsyncRelayCommand CalculateSelectionCommand { get; }

    public RelayCommand PickIconColorCommand { get; }

    public RelayCommand PickLabelColorCommand { get; }

    public AsyncRelayCommand PreviewChangesCommand { get; }

    public AsyncRelayCommand ApplyChangesCommand { get; }

    public RelayCommand CancelOperationCommand { get; }

    public RelayCommand DismissNotificationCommand { get; }

    public MainViewModel()
    {
        _packageService = new KmlPackageService(_loader);

        SelectionModes = new[]
        {
            new SelectionModeOption
            {
                Value = PlacemarkSelectionMode.Folder,
                Label = "Selected folder"
            },
            new SelectionModeOption
            {
                Value = PlacemarkSelectionMode.IconImage,
                Label = "Icon image"
            },
            new SelectionModeOption
            {
                Value = PlacemarkSelectionMode.IconVariant,
                Label = "Icon variant"
            }
        };

        _selectedSelectionMode = SelectionModes[0];

        OpenKmlCommand =
            new AsyncRelayCommand(
                _ => OpenGoogleEarthFileAsync(),
                _ => !IsBusy);

        SaveAsCommand =
            new AsyncRelayCommand(
                _ => SaveAsAsync(),
                _ => !IsBusy && _documentContext is not null);

        CalculateSelectionCommand =
            new AsyncRelayCommand(
                _ => CalculateSelectionAsync(),
                _ => !IsBusy && _documentContext is not null);

        PickIconColorCommand =
            new RelayCommand(
                _ => PickColor(isIconColor: true),
                _ => !IsBusy);

        PickLabelColorCommand =
            new RelayCommand(
                _ => PickColor(isIconColor: false),
                _ => !IsBusy);

        PreviewChangesCommand =
            new AsyncRelayCommand(
                _ => PreviewChangesAsync(),
                _ => !IsBusy &&
                     _documentContext is not null &&
                     _currentSelection.Count > 0);

        ApplyChangesCommand =
            new AsyncRelayCommand(
                _ => ApplyChangesAsync(),
                _ => !IsBusy && EditPreview?.CanApply == true);

        CancelOperationCommand =
            new RelayCommand(
                _ => CancelCurrentOperation(),
                _ => IsBusy &&
                     CanCancelCurrentOperation &&
                     _operationCancellationSource?.IsCancellationRequested == false);

        DismissNotificationCommand =
            new RelayCommand(_ => HasNotification = false);

        EditSettings.PropertyChanged += (_, _) =>
        {
            InvalidateEditPreview(
                "Style settings changed. Preview the changes again before applying them.");
        };
    }

    private void UpdateSelectedStyle()
    {
        SelectedStyle = null;

        if (_documentContext is null)
            return;

        if (SelectedNode?.NodeType != KmlNodeType.Placemark)
            return;

        if (SelectedNode.SourceElement is null)
            return;

        SelectedStyle = _styleInspector.Inspect(
            SelectedNode.SourceElement,
            _documentContext);
    }

    private async Task CalculateSelectionAsync()
    {
        if (_documentContext is null)
        {
            SelectionSummary = "Open a KML or KMZ file first.";
            ShowNotification(SelectionSummary, NotificationKind.Warning);
            return;
        }

        var selectionMode = SelectedSelectionMode.Value;
        var selectedNode = SelectedNode;
        var includeSubfolders = IncludeSubfolders;
        var selectedIconTypes = IconTypes
            .Where(option => option.IsSelected)
            .ToList();

        if (selectionMode == PlacemarkSelectionMode.Folder &&
            selectedNode is null)
        {
            SelectionSummary = "Select a folder or placemark.";
            ShowNotification(SelectionSummary, NotificationKind.Warning);
            return;
        }

        if (selectionMode != PlacemarkSelectionMode.Folder &&
            selectedIconTypes.Count == 0)
        {
            SelectionSummary =
                "Select at least one icon image or icon variant.";
            ShowNotification(SelectionSummary, NotificationKind.Warning);
            return;
        }

        IReadOnlyList<XElement>? calculatedSelection = null;
        var stopwatch = Stopwatch.StartNew();

        var completed = await RunBusyOperationAsync(
            title: "Calculating placemark selection",
            initialMessage: "Preparing the selected scope...",
            canCancel: true,
            operation: async (progress, cancellationToken) =>
            {
                calculatedSelection = await Task.Run(
                    () => selectionMode == PlacemarkSelectionMode.Folder
                        ? _selectionService.GetPlacemarksForFolder(
                            selectedNode!,
                            includeSubfolders,
                            progress,
                            cancellationToken)
                        : _selectionService.GetPlacemarksForIconTypes(
                            selectedIconTypes,
                            progress,
                            cancellationToken),
                    cancellationToken);
            });

        stopwatch.Stop();

        if (!completed || calculatedSelection is null)
            return;

        _currentSelection = calculatedSelection;
        MatchedPlacemarkCount = _currentSelection.Count;

        if (selectionMode == PlacemarkSelectionMode.Folder)
        {
            var scopeDescription = includeSubfolders
                ? "including nested subfolders"
                : "excluding nested subfolders";

            SelectionSummary =
                $"{MatchedPlacemarkCount:N0} placemarks matched in " +
                $"“{selectedNode!.Name}”, {scopeDescription}.";
        }
        else
        {
            var groupDescription =
                selectionMode == PlacemarkSelectionMode.IconVariant
                    ? "icon variants"
                    : "icon images";

            SelectionSummary =
                $"{MatchedPlacemarkCount:N0} placemarks matched across " +
                $"{selectedIconTypes.Count:N0} selected {groupDescription}.";
        }

        InvalidateEditPreview(
            "Selection calculated. Enable the properties to change, then preview the operation.");

        StatusText =
            $"{SelectionSummary} Completed in {FormatElapsed(stopwatch.Elapsed)}.";

        ShowNotification(
            $"Selection ready: {MatchedPlacemarkCount:N0} placemarks matched.",
            NotificationKind.Success);

        RaiseCommandStates();
    }

    private async Task PreviewChangesAsync()
    {
        if (_documentContext is null || _currentSelection.Count == 0)
            return;

        var selectionSnapshot = _currentSelection.ToList();
        KmlBatchEditPreview? preview = null;
        var stopwatch = Stopwatch.StartNew();

        var completed = await RunBusyOperationAsync(
            title: "Previewing style changes",
            initialMessage: "Inspecting the selected placemarks...",
            canCancel: true,
            operation: async (progress, cancellationToken) =>
            {
                preview = await Task.Run(
                    () => _batchEditService.Preview(
                        selectionSnapshot,
                        _documentContext,
                        EditSettings,
                        progress,
                        cancellationToken),
                    cancellationToken);
            });

        stopwatch.Stop();

        if (!completed || preview is null)
            return;

        EditPreview = preview;
        BatchEditMessage = preview.Summary;
        ApplyChangesCommand.RaiseCanExecuteChanged();

        StatusText = preview.CanApply
            ? $"Preview ready for {preview.PlacemarkCount:N0} placemarks in {FormatElapsed(stopwatch.Elapsed)}."
            : preview.ValidationMessage;

        ShowNotification(
            preview.CanApply
                ? $"Preview ready for {preview.PlacemarkCount:N0} placemarks."
                : preview.ValidationMessage,
            preview.CanApply
                ? NotificationKind.Success
                : NotificationKind.Warning);
    }

    private async Task ApplyChangesAsync()
    {
        if (_documentContext is null ||
            EditPreview?.CanApply != true)
        {
            return;
        }

        var selectionSnapshot = _currentSelection.ToList();
        KmlBatchEditResult? result = null;
        IReadOnlyList<IconTypeOption>? iconImages = null;
        IReadOnlyList<IconTypeOption>? iconVariants = null;
        var stopwatch = Stopwatch.StartNew();

        // Cancellation is deliberately disabled once this mutating operation
        // begins. This prevents a cancellation request from leaving only part
        // of the selected placemarks changed.
        var completed = await RunBusyOperationAsync(
            title: "Applying style changes",
            initialMessage: "Applying the approved changes...",
            canCancel: false,
            operation: async (progress, _) =>
            {
                result = await Task.Run(
                    () => _batchEditService.Apply(
                        selectionSnapshot,
                        _documentContext!,
                        EditSettings,
                        progress,
                        CancellationToken.None));

                progress.Report(new OperationProgress(
                    "Refreshing icon groups...",
                    "Updating the icon image and variant lists",
                    96));

                iconImages = await Task.Run(
                    () => _selectionService.BuildIconImageInventory(
                        _documentContext!,
                        _styleInspector,
                        progress,
                        CancellationToken.None));

                iconVariants = await Task.Run(
                    () => _selectionService.BuildIconVariantInventory(
                        _documentContext!,
                        _styleInspector,
                        progress,
                        CancellationToken.None));
            });

        stopwatch.Stop();

        if (!completed ||
            result is null ||
            iconImages is null ||
            iconVariants is null)
        {
            return;
        }

        SubscribeToIconOptions(iconImages);
        SubscribeToIconOptions(iconVariants);
        _iconImageInventory = iconImages;
        _iconVariantInventory = iconVariants;
        RefreshVisibleIconOptions();

        HasUnsavedChanges = true;
        UpdateSelectedStyle();
        ResetCalculatedSelection();

        BatchEditMessage = result.Summary;
        StatusText =
            $"Changed {result.PlacemarksChanged:N0} placemarks in " +
            $"{FormatElapsed(stopwatch.Elapsed)}. Use Save As to create the output file.";

        ShowNotification(
            $"{result.PlacemarksChanged:N0} placemarks updated successfully.",
            NotificationKind.Success);
    }

    private void PickColor(bool isIconColor)
    {
        var currentText = isIconColor
            ? EditSettings.IconColorText
            : EditSettings.LabelColorText;

        // This application targets Windows only. Suppress the platform analyzer
        // around the Windows Forms colour picker used from the WPF interface.
#pragma warning disable CA1416
        using var dialog = new Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            Color = ParseDisplayColor(currentText)
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;

        var selected =
            $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
#pragma warning restore CA1416

        if (isIconColor)
            EditSettings.IconColorText = selected;
        else
            EditSettings.LabelColorText = selected;
    }

    private static DrawingColor ParseDisplayColor(string? input)
    {
        var value = input?.Trim().TrimStart('#') ?? string.Empty;

        if (value.Length == 8)
            value = value[2..];

        if (value.Length == 6 &&
            int.TryParse(
                value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var rgb))
        {
            return DrawingColor.FromArgb(
                (rgb >> 16) & 0xFF,
                (rgb >> 8) & 0xFF,
                rgb & 0xFF);
        }

        return DrawingColor.White;
    }

    private void ResetCalculatedSelection()
    {
        _currentSelection = Array.Empty<XElement>();
        MatchedPlacemarkCount = 0;
        SelectionSummary =
            "Selection has changed. Calculate the selection again.";

        InvalidateEditPreview(
            "Calculate a placemark selection before previewing style changes.");
        RaiseCommandStates();
    }

    private void InvalidateEditPreview(string message)
    {
        EditPreview = null;
        BatchEditMessage = message;
        ApplyChangesCommand.RaiseCanExecuteChanged();
    }

    private async Task OpenGoogleEarthFileAsync()
    {
        if (HasUnsavedChanges)
        {
            var discardResult = System.Windows.MessageBox.Show(
                "The current file has unsaved changes. Open another file and discard those changes?",
                "Unsaved changes",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (discardResult != System.Windows.MessageBoxResult.Yes)
                return;
        }

        var dialog = new WpfOpenFileDialog
        {
            Filter =
                "Google Earth files (*.kml;*.kmz)|*.kml;*.kmz|" +
                "KML files (*.kml)|*.kml|" +
                "KMZ files (*.kmz)|*.kmz|" +
                "All files (*.*)|*.*",
            Title = "Open Google Earth file"
        };

        if (dialog.ShowDialog() != true)
            return;

        KmlDocumentContext? newContext = null;
        IReadOnlyList<IconTypeOption>? iconImages = null;
        IReadOnlyList<IconTypeOption>? iconVariants = null;
        var stopwatch = Stopwatch.StartNew();

        var completed = await RunBusyOperationAsync(
            title: "Opening Google Earth file",
            initialMessage: "Preparing to read the selected file...",
            canCancel: true,
            operation: async (progress, cancellationToken) =>
            {
                newContext = await Task.Run(
                    () => _packageService.Open(
                        dialog.FileName,
                        progress,
                        cancellationToken),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                iconImages = await Task.Run(
                    () => _selectionService.BuildIconImageInventory(
                        newContext!,
                        _styleInspector,
                        progress,
                        cancellationToken),
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                iconVariants = await Task.Run(
                    () => _selectionService.BuildIconVariantInventory(
                        newContext!,
                        _styleInspector,
                        progress,
                        cancellationToken),
                    cancellationToken);
            });

        stopwatch.Stop();

        if (!completed ||
            newContext is null ||
            iconImages is null ||
            iconVariants is null)
        {
            newContext?.Dispose();
            return;
        }

        SubscribeToIconOptions(iconImages);
        SubscribeToIconOptions(iconVariants);

        var previousContext = _documentContext;
        _documentContext = newContext;
        newContext = null;

        _iconImageInventory = iconImages;
        _iconVariantInventory = iconVariants;

        RootNodes.Clear();
        RootNodes.Add(_documentContext.RootNode);

        RefreshVisibleIconOptions();

        LoadedFilePath = dialog.FileName;
        HasUnsavedChanges = false;

        SelectedNode = _documentContext.RootNode;
        ResetCalculatedSelection();
        SetLoadSummary(_documentContext, stopwatch.Elapsed);

        StatusText =
            $"Loaded {_documentContext.PackageTypeDisplay}: " +
            $"{_documentContext.Placemarks.Count:N0} placemarks, " +
            $"{_iconImageInventory.Count:N0} icon images and " +
            $"{_iconVariantInventory.Count:N0} icon variants in " +
            $"{FormatElapsed(stopwatch.Elapsed)}.";

        ShowNotification(
            $"{Path.GetFileName(dialog.FileName)} loaded successfully.",
            NotificationKind.Success);

        RaiseCommandStates();

        if (previousContext is not null)
        {
            _ = Task.Run(previousContext.Dispose);
        }
    }

    private async Task SaveAsAsync()
    {
        if (_documentContext is null)
            return;

        var extension = _documentContext.IsKmz ? ".kmz" : ".kml";
        var typeName = _documentContext.IsKmz ? "KMZ" : "KML";

        var dialog = new WpfSaveFileDialog
        {
            Filter = _documentContext.IsKmz
                ? "KMZ files (*.kmz)|*.kmz"
                : "KML files (*.kml)|*.kml",
            DefaultExt = extension,
            AddExtension = true,
            FileName =
                $"{Path.GetFileNameWithoutExtension(_documentContext.SourcePath)}" +
                $"_edited{extension}",
            Title = $"Save edited {typeName} as"
        };

        if (dialog.ShowDialog() != true)
            return;

        var stopwatch = Stopwatch.StartNew();

        var completed = await RunBusyOperationAsync(
            title: $"Saving {typeName}",
            initialMessage: $"Preparing the {typeName} output...",
            canCancel: true,
            operation: async (progress, cancellationToken) =>
            {
                await Task.Run(
                    () => _packageService.SaveAs(
                        _documentContext,
                        dialog.FileName,
                        progress,
                        cancellationToken),
                    cancellationToken);
            });

        stopwatch.Stop();

        if (!completed)
            return;

        HasUnsavedChanges = false;
        StatusText =
            $"Saved {typeName}: {dialog.FileName} in " +
            $"{FormatElapsed(stopwatch.Elapsed)}.";

        ShowNotification(
            $"{typeName} saved successfully to {dialog.FileName}",
            NotificationKind.Success);
    }

    private void SubscribeToIconOptions(
        IEnumerable<IconTypeOption> options)
    {
        foreach (var option in options)
        {
            option.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName ==
                    nameof(IconTypeOption.IsSelected))
                {
                    ResetCalculatedSelection();
                }
            };
        }
    }

    private void RefreshVisibleIconOptions()
    {
        IconTypes.Clear();

        IReadOnlyList<IconTypeOption> source =
            SelectedSelectionMode.Value switch
            {
                PlacemarkSelectionMode.IconVariant =>
                    _iconVariantInventory,
                PlacemarkSelectionMode.IconImage =>
                    _iconImageInventory,
                _ => Array.Empty<IconTypeOption>()
            };

        foreach (var option in source)
            IconTypes.Add(option);
    }

    private async Task<bool> RunBusyOperationAsync(
        string title,
        string initialMessage,
        bool canCancel,
        Func<IProgress<OperationProgress>, CancellationToken, Task> operation)
    {
        if (IsBusy)
            return false;

        HasNotification = false;
        BusyTitle = title;
        BusyMessage = initialMessage;
        BusyDetail = string.Empty;
        BusyProgress = 0;
        IsBusyProgressIndeterminate = true;
        CanCancelCurrentOperation = canCancel;

        _operationCancellationSource?.Dispose();
        _operationCancellationSource = new CancellationTokenSource();

        IsBusy = true;

        var progress = new Progress<OperationProgress>(UpdateOperationProgress);

        try
        {
            await operation(progress, _operationCancellationSource.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{title} was cancelled.";
            ShowNotification(
                $"{title} was cancelled. The current document remains unchanged.",
                NotificationKind.Warning);
            return false;
        }
        catch (Exception ex)
        {
            StatusText = $"{title} failed: {ex.Message}";
            ShowNotification(
                $"{title} failed: {ex.Message}",
                NotificationKind.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
            CanCancelCurrentOperation = false;

            _operationCancellationSource.Dispose();
            _operationCancellationSource = null;
        }
    }

    private void UpdateOperationProgress(OperationProgress progress)
    {
        BusyMessage = progress.Message;
        BusyDetail = progress.Detail ?? string.Empty;

        if (progress.Percent is double percent && double.IsFinite(percent))
        {
            BusyProgress = percent;
            IsBusyProgressIndeterminate = false;
        }
        else
        {
            // Unknown, NaN, or infinite percentages must use indeterminate
            // mode. Keep Value at a valid number so WPF cannot throw.
            BusyProgress = 0d;
            IsBusyProgressIndeterminate = true;
        }
    }

    private void CancelCurrentOperation()
    {
        if (!IsBusy ||
            !CanCancelCurrentOperation ||
            _operationCancellationSource is null ||
            _operationCancellationSource.IsCancellationRequested)
        {
            return;
        }

        BusyMessage = "Cancelling operation...";
        BusyDetail =
            "The operation will stop at the next safe processing point.";
        IsBusyProgressIndeterminate = true;

        _operationCancellationSource.Cancel();
        CancelOperationCommand.RaiseCanExecuteChanged();
    }

    private void SetLoadSummary(
        KmlDocumentContext context,
        TimeSpan elapsed)
    {
        var folderCount = context.Document
            .Descendants(KmlNs + "Folder")
            .Count();

        long sourceSize = 0;

        try
        {
            sourceSize = new FileInfo(context.SourcePath).Length;
        }
        catch
        {
            // File-size reporting is optional and must not block loading.
        }

        var lines = new List<string>
        {
            $"File: {Path.GetFileName(context.SourcePath)}",
            $"Type: {context.PackageTypeDisplay}",
            $"Size: {FormatFileSize(sourceSize)}",
            $"Placemarks: {context.Placemarks.Count:N0}",
            $"Folders: {folderCount:N0}",
            $"Shared styles: {context.StylesById.Count:N0}",
            $"Style maps: {context.StyleMapsById.Count:N0}",
            $"Embedded resources: {context.EmbeddedResourceCount:N0}",
            $"Load time: {FormatElapsed(elapsed)}"
        };

        LoadSummaryText = string.Join(Environment.NewLine, lines);
        HasLoadSummary = true;
    }

    private void ShowNotification(
        string message,
        NotificationKind kind)
    {
        NotificationText = message;
        NotificationKind = kind;
        HasNotification = true;
    }

    private void RaiseCommandStates()
    {
        OpenKmlCommand.RaiseCanExecuteChanged();
        SaveAsCommand.RaiseCanExecuteChanged();
        CalculateSelectionCommand.RaiseCanExecuteChanged();
        PickIconColorCommand.RaiseCanExecuteChanged();
        PickLabelColorCommand.RaiseCanExecuteChanged();
        PreviewChangesCommand.RaiseCanExecuteChanged();
        ApplyChangesCommand.RaiseCanExecuteChanged();
        CancelOperationCommand.RaiseCanExecuteChanged();
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes >= 1)
            return $"{elapsed.TotalMinutes:0.0} minutes";

        if (elapsed.TotalSeconds >= 1)
            return $"{elapsed.TotalSeconds:0.0} seconds";

        return $"{elapsed.TotalMilliseconds:0} ms";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
            return "Unknown";

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    public void Dispose()
    {
        _operationCancellationSource?.Cancel();
        _operationCancellationSource?.Dispose();
        _operationCancellationSource = null;

        _documentContext?.Dispose();
        _documentContext = null;
    }
}

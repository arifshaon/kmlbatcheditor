$ErrorActionPreference = 'Stop'

function Replace-Once {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$OldText,
        [Parameter(Mandatory = $true)][string]$NewText
    )

    $content = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n")
    $old = $OldText.Replace("`r`n", "`n")
    $new = $NewText.Replace("`r`n", "`n")

    $firstIndex = $content.IndexOf($old, [System.StringComparison]::Ordinal)
    if ($firstIndex -lt 0) {
        throw "Expected source block was not found in $Path"
    }

    $secondIndex = $content.IndexOf(
        $old,
        $firstIndex + $old.Length,
        [System.StringComparison]::Ordinal)

    if ($secondIndex -ge 0) {
        throw "Expected exactly one source block in $Path, but found more than one"
    }

    $updated = $content.Substring(0, $firstIndex) +
        $new +
        $content.Substring($firstIndex + $old.Length)

    [System.IO.File]::WriteAllText(
        $Path,
        $updated,
        [System.Text.UTF8Encoding]::new($false))
}

$viewModelPath = 'KmlScopedEditor/ViewModels/MainViewModel.cs'
$windowCodeBehindPath = 'KmlScopedEditor/MainWindow.xaml.cs'

Replace-Once -Path $viewModelPath -OldText @'
public class MainViewModel : ViewModelBase, IDisposable
'@ -NewText @'
public partial class MainViewModel : ViewModelBase, IDisposable
'@

Replace-Once -Path $viewModelPath -OldText @'
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
'@ -NewText @'
            new SelectionModeOption
            {
                Value = PlacemarkSelectionMode.Folder,
                Label = "Selected folder"
            },
            new SelectionModeOption
            {
                Value = PlacemarkSelectionMode.PlacemarkName,
                Label = "Placemark name"
            },
            new SelectionModeOption
            {
                Value = PlacemarkSelectionMode.IconImage,
                Label = "Icon image"
            },
'@

Replace-Once -Path $viewModelPath -OldText @'
        var selectedIconTypes = IconTypes
            .Where(option => option.IsSelected)
            .ToList();

        if (selectionMode == PlacemarkSelectionMode.Folder &&
'@ -NewText @'
        var selectedIconTypes = IconTypes
            .Where(option => option.IsSelected)
            .ToList();
        var selectedPlacemarkNames = GetSelectedPlacemarkNameOptions();

        if (selectionMode == PlacemarkSelectionMode.Folder &&
'@

Replace-Once -Path $viewModelPath -OldText @'
        if (selectionMode != PlacemarkSelectionMode.Folder &&
            selectedIconTypes.Count == 0)
        {
            SelectionSummary =
                "Select at least one icon image or icon variant.";
            ShowNotification(SelectionSummary, NotificationKind.Warning);
            return;
        }
'@ -NewText @'
        if (selectionMode == PlacemarkSelectionMode.PlacemarkName &&
            selectedPlacemarkNames.Count == 0)
        {
            SelectionSummary = "Select at least one placemark name.";
            ShowNotification(SelectionSummary, NotificationKind.Warning);
            return;
        }

        if ((selectionMode == PlacemarkSelectionMode.IconImage ||
             selectionMode == PlacemarkSelectionMode.IconVariant) &&
            selectedIconTypes.Count == 0)
        {
            SelectionSummary =
                "Select at least one icon image or icon variant.";
            ShowNotification(SelectionSummary, NotificationKind.Warning);
            return;
        }
'@

Replace-Once -Path $viewModelPath -OldText @'
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
'@ -NewText @'
                calculatedSelection = await Task.Run(
                    () => selectionMode switch
                    {
                        PlacemarkSelectionMode.Folder =>
                            _selectionService.GetPlacemarksForFolder(
                                selectedNode!,
                                includeSubfolders,
                                progress,
                                cancellationToken),
                        PlacemarkSelectionMode.PlacemarkName =>
                            _placemarkNameSelectionService.GetPlacemarksForNames(
                                selectedPlacemarkNames,
                                progress,
                                cancellationToken),
                        _ => _selectionService.GetPlacemarksForIconTypes(
                            selectedIconTypes,
                            progress,
                            cancellationToken)
                    },
                    cancellationToken);
'@

Replace-Once -Path $viewModelPath -OldText @'
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
'@ -NewText @'
        if (selectionMode == PlacemarkSelectionMode.Folder)
        {
            var scopeDescription = includeSubfolders
                ? "including nested subfolders"
                : "excluding nested subfolders";

            SelectionSummary =
                $"{MatchedPlacemarkCount:N0} placemarks matched in " +
                $"“{selectedNode!.Name}”, {scopeDescription}.";
        }
        else if (selectionMode == PlacemarkSelectionMode.PlacemarkName)
        {
            SelectionSummary =
                $"{MatchedPlacemarkCount:N0} placemarks matched across " +
                $"{selectedPlacemarkNames.Count:N0} selected names.";
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
'@

Replace-Once -Path $viewModelPath -OldText @'
        foreach (var option in source)
            IconTypes.Add(option);
    }
'@ -NewText @'
        foreach (var option in source)
            IconTypes.Add(option);

        RefreshVisiblePlacemarkNameOptions();
    }
'@

Replace-Once -Path $windowCodeBehindPath -OldText @'
    private void ArrangeEditorSections()
    {
        MoveActionButtonsBelowOpacityControls();
        MakeStyleDiagnosticsCollapsible();
    }
'@ -NewText @'
    private void ArrangeEditorSections()
    {
        MoveActionButtonsBelowOpacityControls();
        InsertPlacemarkNameSelectionPanel();
        MakeStyleDiagnosticsCollapsible();
    }
'@

Replace-Once -Path $windowCodeBehindPath -OldText @'
    private void MakeStyleDiagnosticsCollapsible()
'@ -NewText @'
    private void InsertPlacemarkNameSelectionPanel()
    {
        var calculateButton = FindLogicalDescendants<System.Windows.Controls.Button>(this)
            .FirstOrDefault(button => string.Equals(
                button.Content?.ToString(),
                "Calculate Selection",
                StringComparison.Ordinal));

        if (calculateButton is null ||
            System.Windows.LogicalTreeHelper.GetParent(calculateButton) is not
                System.Windows.Controls.StackPanel parent ||
            parent.Children.OfType<PlacemarkNameSelectionControl>().Any())
        {
            return;
        }

        var buttonIndex = parent.Children.IndexOf(calculateButton);
        if (buttonIndex < 0)
            return;

        parent.Children.Insert(
            buttonIndex,
            new PlacemarkNameSelectionControl());
    }

    private void MakeStyleDiagnosticsCollapsible()
'@

Write-Host 'Placemark name selection source changes applied successfully.'

using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using KmlScopedEditor.Services;
using Point = System.Windows.Point;

namespace KmlScopedEditor.Views;

public partial class LineEditorWindow
{
    private readonly LineSimplificationService
        _lineSimplificationService =
            new();

    private CancellationTokenSource?
        _simplifyPreviewCancellation;

    private LineSimplificationPreview?
        _currentSimplifyPreview;

    private long _geometryRevision;
    private int _simplifyPreviewRequest;
    private int _lastSimplifySelectionCount = -1;
    private bool _simplifyPanelInitializing;

    private void OnSimplifyOpenClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_workflowStep == 2)
            return;

        _simplifyPanelInitializing = true;

        try
        {
            SimplifyPanel.Visibility =
                Visibility.Visible;

            UpdateSimplifySelectionState();

            if (_selected.Count == 0 &&
                SimplifySelectedRadioButton
                    .IsChecked == true)
            {
                SimplifyAllRadioButton
                    .IsChecked = true;
            }

            UpdateSmoothOnlyWarning();
        }
        finally
        {
            _simplifyPanelInitializing = false;
        }

        ScheduleSimplifyPreview();

        UpdateStatus(
            "Simplify preview opened. Nothing changes until you choose Apply.");
    }

    private void OnSimplifySettingsChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (_simplifyPanelInitializing ||
            !IsLoaded ||
            SimplifyPanel.Visibility !=
            Visibility.Visible)
        {
            return;
        }

        UpdateSmoothOnlyWarning();
        ScheduleSimplifyPreview();
    }

    private void OnSimplifyToleranceKeyUp(
        object sender,
        KeyEventArgs e)
    {
        if (_simplifyPanelInitializing ||
            !IsLoaded ||
            SimplifyPanel.Visibility !=
            Visibility.Visible)
        {
            return;
        }

        UpdateSmoothOnlyWarning();
        ScheduleSimplifyPreview();
    }

    private void OnSimplifyCancelClick(
        object sender,
        RoutedEventArgs e)
    {
        CancelSimplifyPreviewInternal(
            collapsePanel: true);

        UpdatePendingGuidePreview();

        UpdateStatus(
            "Simplify preview cancelled. No geometry was changed.");
    }

    private void OnSimplifyApplyClick(
        object sender,
        RoutedEventArgs e)
    {
        var preview =
            _currentSimplifyPreview;

        if (preview is null ||
            preview.GeometryRevision !=
            _geometryRevision)
        {
            UpdateStatus(
                "The simplify preview is out of date. Recalculating it before Apply.");

            ScheduleSimplifyPreview();
            return;
        }

        if (preview.ChangedLineCount <= 0)
        {
            UpdateStatus(
                "The current simplify settings do not change any lines.");
            return;
        }

        if (preview.HasTopologyWarnings)
        {
            var crossings =
                preview.NewCrossingCount;

            var otherWarnings =
                preview.NewIssues.Count -
                crossings;

            var message =
                "The proposed geometry creates ";

            if (crossings > 0)
            {
                message +=
                    $"{crossings:N0} new crossing(s)";
            }

            if (otherWarnings > 0)
            {
                if (crossings > 0)
                    message += " and ";

                message +=
                    $"{otherWarnings:N0} other new contact/overlap warning(s)";
            }

            message +=
                ". These locations are marked in red on the map.\n\nApply anyway?";

            var result =
                MessageBox.Show(
                    this,
                    message,
                    "Simplify topology warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (result !=
                MessageBoxResult.Yes)
            {
                return;
            }
        }

        var features =
            _geometry.Features;

        var replacements =
            new Dictionary<
                XElement,
                IReadOnlyList<KmlCoordinate>>();

        foreach (var item in
                 preview.Replacements)
        {
            if (!item.Changed ||
                item.FeatureIndex < 0 ||
                item.FeatureIndex >=
                features.Count)
            {
                continue;
            }

            replacements[
                features[
                    item.FeatureIndex]
                    .LineStringElement] =
                item.Coordinates;
        }

        if (replacements.Count == 0)
        {
            UpdateStatus(
                "The simplify preview contains no changed lines to apply.");
            return;
        }

        var snapshot =
            CaptureUndoState(
                UndoActionKind.Simplify);

        var changed =
            _geometry
                .ApplyCoordinateReplacements(
                    replacements);

        if (changed <= 0)
        {
            UpdateStatus(
                "No line coordinates were changed.");
            return;
        }

        PushDocumentUndo(
            snapshot);

        MarkGeometryChanged();

        RefreshSurface();
        UpdatePendingGuidePreview();
        UpdateCutScopeDisplay();

        UpdateStatus(
            $"Simplified {changed:N0} line(s): " +
            $"{preview.OriginalPointCount:N0} → " +
            $"{preview.ResultPointCount:N0} points; " +
            $"maximum measured deviation " +
            $"{preview.MaximumMeasuredDeviationMetres:0.##} m.");
    }

    private void ScheduleSimplifyPreview()
    {
        if (!IsLoaded ||
            SimplifyPanel.Visibility !=
            Visibility.Visible)
        {
            return;
        }

        if (!TryGetSimplifyTolerance(
                out var toleranceMetres,
                out var validationMessage))
        {
            CancelSimplifyPreviewComputation();

            _currentSimplifyPreview = null;
            EditorSurface.ClearSimplifyPreview();

            SimplifySummaryText.Text =
                validationMessage;
            SimplifyWarningText.Text =
                string.Empty;
            ApplySimplifyButton.IsEnabled =
                false;

            return;
        }

        var selectedOnly =
            SimplifySelectedRadioButton
                .IsChecked == true;

        if (selectedOnly &&
            GetValidSelectedLines().Count == 0)
        {
            CancelSimplifyPreviewComputation();

            _currentSimplifyPreview = null;
            EditorSurface.ClearSimplifyPreview();

            SimplifySummaryText.Text =
                "Select one or more lines first.";
            SimplifyWarningText.Text =
                string.Empty;
            ApplySimplifyButton.IsEnabled =
                false;

            return;
        }

        CancelSimplifyPreviewComputation();

        var cancellation =
            new CancellationTokenSource();

        _simplifyPreviewCancellation =
            cancellation;

        var request =
            ++_simplifyPreviewRequest;

        SimplifySummaryText.Text =
            "Calculating preview…";
        SimplifyWarningText.Text =
            string.Empty;
        ApplySimplifyButton.IsEnabled =
            false;

        _ =
            RunSimplifyPreviewAsync(
                request,
                toleranceMetres,
                SimplifySmoothCheckBox
                    .IsChecked == true,
                selectedOnly,
                cancellation.Token);
    }

    private async Task RunSimplifyPreviewAsync(
        int request,
        double toleranceMetres,
        bool smooth,
        bool selectedOnly,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                150,
                cancellationToken);

            if (request !=
                _simplifyPreviewRequest)
            {
                return;
            }

            var revision =
                _geometryRevision;

            var features =
                _geometry.Features;

            var inputs =
                new List<
                    LineSimplificationInput>(
                    features.Count);

            for (var index = 0;
                 index < features.Count;
                 index++)
            {
                var feature =
                    features[index];

                inputs.Add(
                    new LineSimplificationInput(
                        index,
                        feature.Coordinates,
                        !selectedOnly ||
                        _selected.Contains(
                            feature
                                .LineStringElement)));
            }

            var options =
                new LineSimplificationOptions(
                    toleranceMetres,
                    smooth);

            var preview =
                await Task.Run(
                    () =>
                        _lineSimplificationService
                            .Preview(
                                inputs,
                                options,
                                revision,
                                cancellationToken),
                    cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (request !=
                    _simplifyPreviewRequest ||
                revision !=
                    _geometryRevision ||
                SimplifyPanel.Visibility !=
                    Visibility.Visible)
            {
                return;
            }

            _currentSimplifyPreview =
                preview;

            DisplaySimplifyPreview(
                preview);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (request !=
                _simplifyPreviewRequest)
            {
                return;
            }

            _currentSimplifyPreview =
                null;

            EditorSurface
                .ClearSimplifyPreview();

            SimplifySummaryText.Text =
                "Preview could not be calculated.";

            SimplifyWarningText.Text =
                exception.Message;

            ApplySimplifyButton.IsEnabled =
                false;
        }
    }

    private void DisplaySimplifyPreview(
        LineSimplificationPreview preview)
    {
        var previewLines =
            preview.Replacements
                .Select(item =>
                    (IReadOnlyList<Point>)
                    item.Coordinates
                        .Select(
                            _geometry.Project)
                        .ToList())
                .ToList();

        var warningPoints =
            preview.NewIssues
                .Select(issue =>
                    _geometry.Project(
                        issue.Location))
                .ToList();

        EditorSurface.SetSimplifyPreview(
            previewLines,
            warningPoints);

        var percentage =
            preview.OriginalPointCount > 0
                ? (100d *
                   preview.ResultPointCount /
                   preview.OriginalPointCount)
                : 0d;

        var topologyText =
            preview.HasTopologyWarnings
                ? string.Empty
                : " · no new crossings";

        SimplifySummaryText.Text =
            $"{preview.OriginalPointCount:N0} → " +
            $"{preview.ResultPointCount:N0} points " +
            $"({percentage:0.#}%) · " +
            $"max measured deviation " +
            $"{preview.MaximumMeasuredDeviationMetres:0.##} m" +
            topologyText;

        if (preview.HasTopologyWarnings)
        {
            var crossings =
                preview.NewCrossingCount;

            var otherWarnings =
                preview.NewIssues.Count -
                crossings;

            if (crossings > 0 &&
                otherWarnings > 0)
            {
                SimplifyWarningText.Text =
                    $"⚠ {crossings:N0} new crossing(s); " +
                    $"{otherWarnings:N0} other new contact/overlap warning(s).";
            }
            else if (crossings > 0)
            {
                SimplifyWarningText.Text =
                    $"⚠ {crossings:N0} new crossing(s).";
            }
            else
            {
                SimplifyWarningText.Text =
                    $"⚠ {otherWarnings:N0} new contact/overlap warning(s).";
            }
        }
        else
        {
            SimplifyWarningText.Text =
                string.Empty;
        }

        ApplySimplifyButton.IsEnabled =
            preview.ChangedLineCount > 0;
    }

    private bool TryGetSimplifyTolerance(
        out double toleranceMetres,
        out string validationMessage)
    {
        var text =
            SimplifyToleranceComboBox
                .Text?.Trim() ??
            string.Empty;

        if (string.Equals(
                text,
                "Off",
                StringComparison.OrdinalIgnoreCase))
        {
            toleranceMetres = 0d;
            validationMessage =
                string.Empty;
            return true;
        }

        if (!double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out toleranceMetres) &&
            !double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out toleranceMetres))
        {
            validationMessage =
                "Enter a simplify tolerance in metres, or choose Off.";
            return false;
        }

        if (!double.IsFinite(
                toleranceMetres) ||
            toleranceMetres < 0d)
        {
            validationMessage =
                "Simplify tolerance must be zero or a positive number.";
            return false;
        }

        validationMessage =
            string.Empty;
        return true;
    }

    private void UpdateSmoothOnlyWarning()
    {
        var smoothing =
            SimplifySmoothCheckBox
                .IsChecked == true;

        var hasTolerance =
            TryGetSimplifyTolerance(
                out var tolerance,
                out _) &&
            tolerance > 0d;

        SimplifySmoothWarningText
            .Visibility =
            smoothing &&
            !hasTolerance
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void UpdateSimplifySelectionState()
    {
        var count =
            GetValidSelectedLines()
                .Count;

        SimplifySelectedRadioButton
            .Content =
            $"Only selected lines ({count:N0})";

        if (count ==
            _lastSimplifySelectionCount)
        {
            return;
        }

        _lastSimplifySelectionCount =
            count;

        if (SimplifyPanel.Visibility ==
                Visibility.Visible &&
            SimplifySelectedRadioButton
                .IsChecked == true)
        {
            ScheduleSimplifyPreview();
        }
    }

    private void CloseSimplifyPreviewForWorkflowChange(
        int nextStep)
    {
        if (_workflowStep ==
                nextStep ||
            SimplifyPanel.Visibility !=
                Visibility.Visible)
        {
            return;
        }

        CancelSimplifyPreviewInternal(
            collapsePanel: true);
    }

    private void MarkGeometryChanged()
    {
        _geometryRevision++;

        if (SimplifyPanel.Visibility ==
            Visibility.Visible)
        {
            CancelSimplifyPreviewInternal(
                collapsePanel: true);
        }
        else
        {
            CancelSimplifyPreviewComputation();
            _currentSimplifyPreview = null;
            EditorSurface.ClearSimplifyPreview();
        }
    }

    private void CancelSimplifyPreviewInternal(
        bool collapsePanel)
    {
        CancelSimplifyPreviewComputation();

        _currentSimplifyPreview = null;

        EditorSurface.ClearSimplifyPreview();

        ApplySimplifyButton.IsEnabled =
            false;

        SimplifySummaryText.Text =
            "Preview not calculated.";
        SimplifyWarningText.Text =
            string.Empty;

        if (collapsePanel)
        {
            SimplifyPanel.Visibility =
                Visibility.Collapsed;
        }
    }

    private void CancelSimplifyPreviewComputation()
    {
        _simplifyPreviewRequest++;

        if (_simplifyPreviewCancellation is null)
            return;

        _simplifyPreviewCancellation
            .Cancel();

        _simplifyPreviewCancellation
            .Dispose();

        _simplifyPreviewCancellation =
            null;
    }
}

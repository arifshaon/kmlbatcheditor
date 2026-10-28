using System.Windows;
using System.Xml.Linq;
using KmlScopedEditor.Models;
using KmlScopedEditor.Services;

namespace KmlScopedEditor.Views;

public partial class LineEditorWindow : Window
{
    private readonly XDocument _originalDocument;
    private XDocument _workingDocument;
    private KmlLineGeometryService _geometry;
    private readonly HashSet<XElement> _selected = new();
    private readonly Stack<XDocument> _undo = new();

    public LineEditorWindow(
        XDocument sourceDocument,
        string? sourceName = null)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(sourceDocument);

        _originalDocument = new XDocument(sourceDocument);
        _workingDocument = new XDocument(sourceDocument);
        _geometry = new KmlLineGeometryService(_workingDocument);

        if (!string.IsNullOrWhiteSpace(sourceName))
            Title = $"Line Editor - {sourceName}";

        EditorSurface.StrokeCompleted += OnStrokeCompleted;

        Loaded += (_, _) =>
        {
            SetTool(LineEditorTool.Select);
            RefreshSurface(fitView: true);
            UpdateStatus("Ready.");
        };
    }

    public XDocument EditedDocument => new(_workingDocument);

    private void OnSelectToolClick(object sender, RoutedEventArgs e)
    {
        SetTool(LineEditorTool.Select);
    }

    private void OnCutToolClick(object sender, RoutedEventArgs e)
    {
        SetTool(LineEditorTool.Cut);
    }

    private void SetTool(LineEditorTool tool)
    {
        EditorSurface.Tool = tool;
        SelectToolButton.IsChecked = tool == LineEditorTool.Select;
        CutToolButton.IsChecked = tool == LineEditorTool.Cut;

        InstructionText.Text = tool == LineEditorTool.Select
            ? "SELECT: drag a stroke across contour lines or cut fragments to select them. Draw more strokes to add to the selection. Mouse wheel zooms; right- or middle-drag pans."
            : "CUT: drag a stroke across contours where they should be split. Each crossed contour is divided at the actual intersections. Mouse wheel zooms; right- or middle-drag pans.";
    }

    private void OnStrokeCompleted(
        object? sender,
        LineEditorStrokeEventArgs e)
    {
        if (e.Tool == LineEditorTool.Select)
        {
            var tolerance = EditorSurface.WorldToleranceFromPixels(5d);
            var matches = _geometry.FindIntersectingFeatures(
                e.WorldPoints,
                tolerance);

            foreach (var feature in matches)
                _selected.Add(feature.LineStringElement);

            EditorSurface.SetSelection(_selected);

            UpdateStatus(
                matches.Count == 0
                    ? "No lines crossed by that selection stroke."
                    : $"Added {matches.Count:N0} line segment(s) to the selection.");

            return;
        }

        var snapshot = new XDocument(_workingDocument);
        var result = _geometry.CutWithStroke(e.WorldPoints);

        if (result.LinesSplit == 0)
        {
            UpdateStatus(
                "No contours were split. Draw the cut stroke across the contour lines.");
            return;
        }

        _undo.Push(snapshot);
        _selected.Clear();
        RefreshSurface();

        UpdateStatus(
            $"Cut {result.LinesSplit:N0} contour line(s) at " +
            $"{result.IntersectionCount:N0} intersection(s), creating " +
            $"{result.OutputSegments:N0} independently selectable segment(s).");
    }

    private void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0)
        {
            UpdateStatus("Nothing is selected for deletion.");
            return;
        }

        var snapshot = new XDocument(_workingDocument);
        var deleted = _geometry.DeleteLines(_selected);

        if (deleted == 0)
        {
            UpdateStatus("The selected lines could not be deleted.");
            return;
        }

        _undo.Push(snapshot);
        _selected.Clear();
        RefreshSurface();

        UpdateStatus($"Deleted {deleted:N0} selected line segment(s).");
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        _selected.Clear();
        EditorSurface.SetSelection(_selected);
        UpdateStatus("Selection cleared.");
    }

    private void OnInvertSelectionClick(object sender, RoutedEventArgs e)
    {
        var inverted = _geometry.Features
            .Where(feature => !_selected.Contains(feature.LineStringElement))
            .Select(feature => feature.LineStringElement)
            .ToList();

        _selected.Clear();

        foreach (var line in inverted)
            _selected.Add(line);

        EditorSurface.SetSelection(_selected);
        UpdateStatus("Selection inverted.");
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0)
        {
            UpdateStatus("There is no cut or deletion to undo.");
            return;
        }

        _workingDocument = _undo.Pop();
        _geometry = new KmlLineGeometryService(_workingDocument);
        _selected.Clear();

        RefreshSurface();
        UpdateStatus("Last cut/deletion undone.");
    }

    private void OnFitViewClick(object sender, RoutedEventArgs e)
    {
        EditorSurface.FitView();
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (XNode.DeepEquals(
                _originalDocument.Root,
                _workingDocument.Root))
        {
            DialogResult = false;
            Close();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RefreshSurface(bool fitView = false)
    {
        EditorSurface.SetDocument(
            _geometry.Features,
            _selected,
            _geometry.WorldBounds,
            fitView);
    }

    private void UpdateStatus(string message)
    {
        StatusText.Text =
            $"{message}  " +
            $"{_geometry.Features.Count:N0} line segment(s) in document; " +
            $"{_selected.Count:N0} selected; " +
            $"{_undo.Count:N0} undo step(s).";
    }
}

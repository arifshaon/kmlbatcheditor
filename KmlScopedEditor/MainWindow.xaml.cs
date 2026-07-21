using System.ComponentModel;
using System.Windows;
using KmlScopedEditor.Models;
using KmlScopedEditor.ViewModels;
using KmlScopedEditor.Views;

namespace KmlScopedEditor;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(
            System.Windows.Controls.TreeViewItem.SelectedEvent,
            new RoutedEventHandler(OnTreeViewItemSelected));

        ArrangeEditorSections();
    }

    /// <summary>
    /// Keeps the main XAML readable while arranging the newer controls in the
    /// requested workflow order. The action buttons appear directly below the
    /// opacity controls, and style diagnostics are hidden in an expander until
    /// the user chooses to open them.
    /// </summary>
    private void ArrangeEditorSections()
    {
        MoveActionButtonsBelowOpacityControls();
        InsertPlacemarkNameSelectionPanel();
        MakeStyleDiagnosticsCollapsible();
    }

    private void MoveActionButtonsBelowOpacityControls()
    {
        var previewControl = FindLogicalDescendants<PlacemarkPreviewControl>(this)
            .FirstOrDefault();

        var actionPanel = FindLogicalDescendants<System.Windows.Controls.StackPanel>(this)
            .FirstOrDefault(panel =>
            {
                var buttonLabels = panel.Children
                    .OfType<System.Windows.Controls.Button>()
                    .Select(button => button.Content?.ToString())
                    .ToHashSet(StringComparer.Ordinal);

                return buttonLabels.Contains("Preview Changes") &&
                       buttonLabels.Contains("Apply Changes");
            });

        if (previewControl?.Content is not System.Windows.Controls.StackPanel previewRoot ||
            actionPanel is null ||
            System.Windows.LogicalTreeHelper.GetParent(actionPanel) is not
                System.Windows.Controls.Panel currentParent)
        {
            return;
        }

        currentParent.Children.Remove(actionPanel);

        // OpacityControls is the first item in the preview control. Place the
        // action buttons immediately after it and before the visual preview.
        var targetIndex = Math.Min(1, previewRoot.Children.Count);
        previewRoot.Children.Insert(targetIndex, actionPanel);
        actionPanel.Margin = new Thickness(0, 0, 0, 12);
    }

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
    {
        var diagnosticsHeading = FindLogicalDescendants<System.Windows.Controls.TextBlock>(this)
            .FirstOrDefault(textBlock =>
                string.Equals(
                    textBlock.Text,
                    "Style Diagnostics",
                    StringComparison.Ordinal));

        if (diagnosticsHeading is null ||
            System.Windows.LogicalTreeHelper.GetParent(diagnosticsHeading) is not
                System.Windows.Controls.StackPanel parent)
        {
            return;
        }

        var headingIndex = parent.Children.IndexOf(diagnosticsHeading);

        if (headingIndex < 0 ||
            headingIndex + 2 >= parent.Children.Count ||
            parent.Children[headingIndex + 1] is not
                System.Windows.Controls.TextBlock diagnosticsMessage ||
            parent.Children[headingIndex + 2] is not
                System.Windows.Controls.Border diagnosticsBorder)
        {
            return;
        }

        parent.Children.RemoveAt(headingIndex + 2);
        parent.Children.RemoveAt(headingIndex + 1);
        parent.Children.RemoveAt(headingIndex);

        diagnosticsBorder.Margin = new Thickness(0);

        var content = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        content.Children.Add(diagnosticsMessage);
        content.Children.Add(diagnosticsBorder);

        var header = new System.Windows.Controls.TextBlock
        {
            Text = "Style Diagnostics",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };

        var expander = new System.Windows.Controls.Expander
        {
            Header = header,
            Content = content,
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 20)
        };

        parent.Children.Insert(headingIndex, expander);
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;

            if (dependencyObject is T match)
                yield return match;

            foreach (var descendant in
                     FindLogicalDescendants<T>(dependencyObject))
            {
                yield return descendant;
            }
        }
    }

    private void OnTreeViewItemSelected(
        object sender,
        RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element &&
            element.DataContext is KmlTreeNode node &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedNode = node;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel busyViewModel &&
            busyViewModel.IsBusy)
        {
            System.Windows.MessageBox.Show(
                "A file operation is still running. Wait for it to finish, or use the Cancel button before closing the application.",
                "Operation in progress",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            e.Cancel = true;
            return;
        }

        if (DataContext is MainViewModel viewModel &&
            viewModel.HasUnsavedChanges)
        {
            var result = System.Windows.MessageBox.Show(
                "The current file has unsaved changes. Close without saving?",
                "Unsaved changes",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        base.OnClosed(e);
    }
}

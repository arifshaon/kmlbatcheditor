using System.ComponentModel;
using System.Windows;
using KmlScopedEditor.Models;
using KmlScopedEditor.ViewModels;
using KmlScopedEditor.Views;

namespace KmlScopedEditor;

public partial class MainWindow : System.Windows.Window
{
    private System.Xml.Linq.XDocument? _lineEditorSessionDocument;
    private LineEditorSessionState? _lineEditorSessionState;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(
            System.Windows.Controls.TreeViewItem.SelectedEvent,
            new RoutedEventHandler(OnTreeViewItemSelected));    }

    private async void OnLineEditorClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            viewModel.DocumentContext is null)
        {
            System.Windows.MessageBox.Show(
                "Open a KML or KMZ file first.",
                "Line Editor",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var currentDocument =
            viewModel.DocumentContext.Document;

        if (!ReferenceEquals(
                _lineEditorSessionDocument,
                currentDocument))
        {
            _lineEditorSessionDocument =
                currentDocument;
            _lineEditorSessionState = null;
        }

        var editorDocument =
            viewModel.CreateLineEditorDocumentSnapshot();

        var editor = new LineEditorWindow(
            editorDocument,
            System.IO.Path.GetFileName(
                viewModel.DocumentContext.SourcePath),
            _lineEditorSessionState)
        {
            Owner = this
        };

        var accepted = editor.ShowDialog() == true;
        var sessionState = editor.SessionState;

        if (!accepted)
        {
            // Never persist cancelled geometry/history, but remember the
            // user's scope preference even when the editor closes unchanged.
            _lineEditorSessionState = _lineEditorSessionState is null
                ? sessionState with
                {
                    AppliedGuides = Array.Empty<LineEditorSessionGuideState>()
                }
                : _lineEditorSessionState with
                {
                    LimitToSelectedLines = sessionState.LimitToSelectedLines
                };
            return;
        }

        await viewModel.ApplyLineEditorDocumentAsync(
            editor.EditedDocument);

        _lineEditorSessionDocument =
            viewModel.DocumentContext.Document;
        _lineEditorSessionState =
            sessionState;
    }

    private void OnAboutClick(
        object sender,
        RoutedEventArgs e)
    {
        var about =
            new AboutWindow
            {
                Owner = this
            };

        about.ShowDialog();
    }

    private void OnExitClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
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

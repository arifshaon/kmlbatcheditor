using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KmlScopedEditor.Views;

internal enum SplitScopeDecision
{
    Cancel,
    SelectedOnly,
    AllCrossed
}

/// <summary>
/// A deliberate scope choice, not a "don't ask again" prompt. Closing the
/// window or pressing Escape always cancels without changing a guide or line.
/// </summary>
internal sealed class SplitScopeConfirmationWindow : Window
{
    public SplitScopeDecision Decision { get; private set; } =
        SplitScopeDecision.Cancel;

    public SplitScopeConfirmationWindow(int selectedCount, int crossedCount)
    {
        Title = "Confirm split scope";
        Width = 555;
        MinWidth = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(20)
        };

        root.Children.Add(new TextBlock
        {
            Text = selectedCount > 0
                ? "The selected-lines checkbox is off."
                : "No lines are currently selected.",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DarkSlateGray
        });

        root.Children.Add(new TextBlock
        {
            Text = selectedCount > 0
                ? $"{selectedCount:N0} lines are selected, but Apply split would affect up to " +
                  $"{crossedCount:N0} currently crossed line(s) across all pending guides. " +
                  "Choose which lines to split:"
                : $"The pending split guide(s) currently cross {crossedCount:N0} distinct line(s). " +
                  "You can cancel, select the intended lines, and apply again.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 16),
            FontSize = 13
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        if (selectedCount > 0)
        {
            var selectedButton = MakeButton(
                $"Split only the {selectedCount:N0} selected lines",
                isDefault: true);
            selectedButton.Click += (_, _) =>
                Choose(SplitScopeDecision.SelectedOnly);
            buttons.Children.Add(selectedButton);
        }

        var allButton = MakeButton(
            $"Split all {crossedCount:N0} crossed lines");
        allButton.Click += (_, _) => Choose(SplitScopeDecision.AllCrossed);
        buttons.Children.Add(allButton);

        var cancel = MakeButton("Cancel", isDefault: selectedCount == 0);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Choose(SplitScopeDecision.Cancel);
        buttons.Children.Add(cancel);

        root.Children.Add(buttons);
        Content = root;
    }

    private static Button MakeButton(string title, bool isDefault = false) =>
        new()
        {
            Content = title,
            MinHeight = 35,
            Margin = new Thickness(0, 0, 0, 7),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsDefault = isDefault
        };

    private void Choose(SplitScopeDecision decision)
    {
        Decision = decision;
        DialogResult = decision != SplitScopeDecision.Cancel;
        Close();
    }
}

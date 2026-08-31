using System.Windows;
using System.Windows.Controls;

namespace BranchPins.Vsix;

/// <summary>
/// Collects the destination local branch name for a copy-pin-set command.
/// </summary>
internal sealed class BranchNameDialog : Window
{
    private readonly TextBox branchNameTextBox = new TextBox { MinWidth = 300 };

    /// <summary>
    /// Initializes the modal branch-name prompt and its Copy and Cancel actions.
    /// </summary>
    private BranchNameDialog()
    {
        Title = "Copy Current Branch Pins";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "Destination local branch name:", Margin = new Thickness(0, 0, 0, 8) },
                branchNameTextBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0),
                    Children = { CreateButton("Copy", true), CreateButton("Cancel", false) },
                },
            },
        };
    }

    /// <summary>
    /// Displays the modal prompt and returns the trimmed destination branch name when confirmed.
    /// </summary>
    /// <returns>The entered branch name, or <see langword="null"/> when the dialog is cancelled.</returns>
    public static string? Prompt()
    {
        BranchNameDialog dialog = new BranchNameDialog();
        return dialog.ShowDialog() == true ? dialog.branchNameTextBox.Text.Trim() : null;
    }

    /// <summary>
    /// Creates a dialog action button and maps its click result to the modal dialog result.
    /// </summary>
    /// <param name="caption">The text displayed on the button.</param>
    /// <param name="isDefault">Whether the button confirms rather than cancels the dialog.</param>
    /// <returns>The configured action button.</returns>
    private Button CreateButton(string caption, bool isDefault)
    {
        Button button = new Button { Content = caption, IsDefault = isDefault, IsCancel = !isDefault, MinWidth = 75, Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (sender, eventArgs) => DialogResult = isDefault;
        return button;
    }
}

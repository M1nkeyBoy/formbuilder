using System.Windows;

namespace StandaloneUiBuilder;

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Asks whether to save, discard or keep editing unsaved changes.</summary>
public partial class UnsavedChangesDialog : Window
{
    private UnsavedChangesChoice choice = UnsavedChangesChoice.Cancel;

    private UnsavedChangesDialog(string projectName)
    {
        InitializeComponent();
        MessageText.Text = $"Do you want to save your changes to \"{projectName}\"?";
    }

    public static UnsavedChangesChoice Ask(Window owner, string projectName)
    {
        var dialog = new UnsavedChangesDialog(projectName) { Owner = owner };
        dialog.ShowDialog();
        return dialog.choice;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        choice = UnsavedChangesChoice.Save;
        Close();
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        choice = UnsavedChangesChoice.Discard;
        Close();
    }
}

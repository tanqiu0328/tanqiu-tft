using System.Windows;
using Microsoft.Win32;

namespace TanqiuTft.App;

public partial class LibraryStartupDialog : Window
{
    public LibraryStartupDialog()
    {
        InitializeComponent();
    }

    public LibraryStartupChoice Choice { get; private set; }

    public string? SelectedDirectoryPath { get; private set; }

    private void CreateDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = LibraryStartupChoice.CreateDefault;
        DialogResult = true;
    }

    private void OpenExistingButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = "选择已有阵容库",
            Multiselect = false
        };

        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        Choice = LibraryStartupChoice.OpenExisting;
        SelectedDirectoryPath = folderDialog.FolderName;
        DialogResult = true;
    }
}

public enum LibraryStartupChoice
{
    CreateDefault,
    OpenExisting
}

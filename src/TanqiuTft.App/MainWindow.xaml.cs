using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using TanqiuTft.Library;

namespace TanqiuTft.App;

public partial class MainWindow : Window
{
    private readonly LineupLibrarySession _librarySession = new();
    private CancellationTokenSource? _reloadCancellation;
    private bool _settingTagFilter;
    private string? _selectedTag;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ObservableCollection<LineupCardViewModel> Lineups { get; } = [];

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await SelectInitialLibraryAsync())
            {
                Close();
                return;
            }

            await ReloadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"无法打开阵容库：{exception.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private async Task<bool> SelectInitialLibraryAsync()
    {
        if (await _librarySession.TryRestoreAsync())
        {
            return true;
        }

        if (Directory.Exists(LineupLibrary.DefaultDirectoryPath))
        {
            try
            {
                await _librarySession.OpenAndActivateAsync(LineupLibrary.DefaultDirectoryPath);
                return true;
            }
            catch (LineupLibraryException)
            {
                // 无效的默认目录交给启动选择页处理
            }
        }

        while (true)
        {
            var dialog = new LibraryStartupDialog { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return false;
            }

            try
            {
                if (dialog.Choice == LibraryStartupChoice.CreateDefault)
                {
                    await _librarySession.CreateAndActivateAsync(LineupLibrary.DefaultDirectoryPath);
                }
                else
                {
                    await _librarySession.OpenAndActivateAsync(dialog.SelectedDirectoryPath!);
                }

                return true;
            }
            catch (Exception exception)
            {
                ShowLibraryError(exception);
            }
        }
    }

    private async void SwitchLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = "选择另一个阵容库",
            Multiselect = false
        };

        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _librarySession.OpenAndActivateAsync(folderDialog.FolderName);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ShowLibraryError(exception);
        }
    }

    private void OpenLibraryFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_librarySession.ActiveDirectoryPath is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _librarySession.ActiveDirectoryPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowLibraryError(exception);
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var fileDialog = new OpenFileDialog
        {
            Title = "选择阵容一图流",
            Filter = "支持的图片|*.png;*.jpg;*.jpeg|PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg",
            Multiselect = false,
            CheckFileExists = true
        };

        if (fileDialog.ShowDialog(this) == true)
        {
            await ImportAsync(fileDialog.FileName);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
        {
            MessageBox.Show(
                this,
                "请一次只拖入一张 PNG 或 JPG/JPEG 图片",
                "无法导入",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await ImportAsync(files[0]);
    }

    private async Task ImportAsync(string imagePath)
    {
        if (_librarySession.ActiveLibrary is null)
        {
            return;
        }

        var tagSuggestions = await _librarySession.ActiveLibrary.GetTagSuggestionsAsync();
        var dialog = new ImportLineupDialog(imagePath, tagSuggestions) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _librarySession.ActiveLibrary.AddAsync(
                dialog.LineupName,
                imagePath,
                dialog.LineupTags);
            await ReloadAsync();
        }
        catch (LineupLibraryException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "无法保存阵容",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"保存阵容时发生错误：{exception.Message}",
                "无法保存阵容",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ReloadAsync()
    {
        if (_librarySession.ActiveLibrary is null)
        {
            return;
        }

        _reloadCancellation?.Cancel();
        _reloadCancellation?.Dispose();
        _reloadCancellation = new CancellationTokenSource();
        var cancellationToken = _reloadCancellation.Token;

        IReadOnlyList<Lineup> lineups;
        try
        {
            lineups = _selectedTag is null
                ? await _librarySession.ActiveLibrary.SearchAsync(SearchTextBox.Text, cancellationToken)
                : await _librarySession.ActiveLibrary.GetLineupsByTagAsync(_selectedTag, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Lineups.Clear();
        foreach (var lineup in lineups)
        {
            Lineups.Add(new LineupCardViewModel(
                lineup.Name,
                BitmapImageLoader.Load(lineup.ImageBytes),
                lineup.Tags));
        }

        CountText.Text = $"{Lineups.Count} 个阵容";
        EmptyState.Visibility = Lineups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveLibraryText.Text = _librarySession.ActiveDirectoryPath is null
            ? string.Empty
            : $"活动阵容库：{_librarySession.ActiveDirectoryPath}";
    }

    private async void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_settingTagFilter || _librarySession.ActiveLibrary is null)
        {
            return;
        }

        _selectedTag = null;
        await ReloadAsync();
    }

    private async void LineupTagButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Content: string tag })
        {
            return;
        }

        _settingTagFilter = true;
        SearchTextBox.Text = tag;
        SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
        _settingTagFilter = false;
        _selectedTag = tag;
        await ReloadAsync();
    }

    private async void EditLineupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_librarySession.ActiveLibrary is null
            || sender is not System.Windows.Controls.Button { Tag: LineupCardViewModel lineup })
        {
            return;
        }

        var suggestions = await _librarySession.ActiveLibrary.GetTagSuggestionsAsync();
        var dialog = new ImportLineupDialog(
            lineup.Name,
            lineup.Image,
            lineup.Tags,
            suggestions)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _librarySession.ActiveLibrary.UpdateAsync(
                lineup.Name,
                dialog.LineupName,
                dialog.LineupTags);
            await ReloadAsync();
        }
        catch (LineupLibraryException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "无法修改阵容",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowLibraryError(Exception exception)
    {
        var message = exception is LineupLibraryException
            ? exception.Message
            : $"打开阵容库时发生错误：{exception.Message}";
        MessageBox.Show(
            this,
            message,
            "无法打开阵容库",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

}

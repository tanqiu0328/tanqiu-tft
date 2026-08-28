using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using TanqiuTft.Library;

namespace TanqiuTft.App;

public partial class MainWindow : Window
{
    private LineupLibrary? _library;

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
            _library = await LineupLibrary.OpenAsync(LineupLibrary.DefaultDirectoryPath);
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
        if (_library is null)
        {
            return;
        }

        var dialog = new ImportLineupDialog(imagePath) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _library.AddAsync(dialog.LineupName, imagePath);
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
        if (_library is null)
        {
            return;
        }

        var lineups = await _library.GetLineupsAsync();
        Lineups.Clear();
        foreach (var lineup in lineups)
        {
            Lineups.Add(new LineupCardViewModel(lineup.Name, BitmapImageLoader.Load(lineup.ImageBytes)));
        }

        CountText.Text = $"{Lineups.Count} 个阵容";
        EmptyState.Visibility = Lineups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

}

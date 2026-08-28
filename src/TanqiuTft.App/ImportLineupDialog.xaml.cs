using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TanqiuTft.App;

public partial class ImportLineupDialog : Window
{
    public ImportLineupDialog(string imagePath)
    {
        InitializeComponent();
        FileNameText.Text = Path.GetFileName(imagePath);

        try
        {
            PreviewImage.Source = LoadPreview(imagePath);
        }
        catch
        {
            PreviewErrorText.Text = "无法预览这张图片，保存时会再次检查图片格式";
            PreviewErrorText.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => NameTextBox.Focus();
    }

    public string LineupName => NameTextBox.Text;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show(
                this,
                "请输入阵容名称",
                "缺少阵容名称",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static BitmapImage LoadPreview(string imagePath)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(imagePath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}

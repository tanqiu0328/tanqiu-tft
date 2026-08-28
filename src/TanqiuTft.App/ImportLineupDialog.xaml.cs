using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TanqiuTft.App;

public partial class ImportLineupDialog : Window
{
    public ImportLineupDialog(string imagePath, IReadOnlyList<string> tagSuggestions)
    {
        InitializeComponent();
        ConfigureSuggestions(tagSuggestions);
        FileNameText.Text = System.IO.Path.GetFileName(imagePath);

        try
        {
            PreviewImage.Source = BitmapImageLoader.Load(imagePath);
        }
        catch
        {
            PreviewErrorText.Text = "无法预览这张图片，保存时会再次检查图片格式";
            PreviewErrorText.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => NameTextBox.Focus();
    }

    public ImportLineupDialog(
        string name,
        ImageSource image,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> tagSuggestions)
    {
        InitializeComponent();
        Title = "修改阵容";
        HeadingText.Text = "修改阵容";
        DescriptionText.Text = "修改阵容名称和用于查找的定阵 Tag";
        FileNameText.Visibility = Visibility.Collapsed;
        PreviewImage.Source = image;
        NameTextBox.Text = name;
        TagsTextBox.Text = string.Join(Environment.NewLine, tags);
        ConfigureSuggestions(tagSuggestions);
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public string LineupName => NameTextBox.Text;

    public IReadOnlyList<string> LineupTags => TagsTextBox.Text
        .Split(['\r', '\n', ',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void ConfigureSuggestions(IReadOnlyList<string> tagSuggestions)
    {
        TagSuggestions.ItemsSource = tagSuggestions;
        SuggestionsLabel.Visibility = tagSuggestions.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        TagSuggestions.Visibility = SuggestionsLabel.Visibility;
    }

    private void TagSuggestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string tag })
        {
            return;
        }

        var currentTags = LineupTags.ToList();
        if (!currentTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            currentTags.Add(tag);
            TagsTextBox.Text = string.Join(Environment.NewLine, currentTags);
            TagsTextBox.CaretIndex = TagsTextBox.Text.Length;
        }

        TagsTextBox.Focus();
    }

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

}

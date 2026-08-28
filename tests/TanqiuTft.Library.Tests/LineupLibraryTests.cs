using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TanqiuTft.Library;

namespace TanqiuTft.Library.Tests;

public sealed class LineupLibraryTests : IDisposable
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(), $"tanqiu-tft-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task 保存阵容后移动来源图片并重开阵容库仍可读取原图和名称()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var sourceImagePath = Path.Combine(_temporaryDirectory, "source.png");
        var libraryDirectory = Path.Combine(_temporaryDirectory, "library");
        await File.WriteAllBytesAsync(sourceImagePath, ValidPng);

        var library = await LineupLibrary.OpenAsync(libraryDirectory);
        await library.AddAsync("  测试阵容  ", sourceImagePath);

        File.Delete(sourceImagePath);

        var reopenedLibrary = await LineupLibrary.OpenAsync(libraryDirectory);
        var lineup = Assert.Single(await reopenedLibrary.GetLineupsAsync());

        Assert.Equal("测试阵容", lineup.Name);
        Assert.Equal(ValidPng, lineup.ImageBytes);
    }

    [Fact]
    public async Task 导入无法解码的图片时显示中文错误且不留下半条阵容或内部图片()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var sourceImagePath = Path.Combine(_temporaryDirectory, "fake.png");
        var libraryDirectory = Path.Combine(_temporaryDirectory, "library");
        await File.WriteAllTextAsync(sourceImagePath, "这不是图片");
        var library = await LineupLibrary.OpenAsync(libraryDirectory);

        var exception = await Assert.ThrowsAsync<LineupLibraryException>(
            () => library.AddAsync("无效阵容", sourceImagePath));

        Assert.Contains("仅支持可正常打开的 PNG 或 JPG/JPEG 图片", exception.Message);
        Assert.Empty(await library.GetLineupsAsync());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(libraryDirectory, "images")));
    }

    [Fact]
    public async Task 导入文件结构损坏的PNG时显示清晰的中文格式错误()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var sourceImagePath = Path.Combine(_temporaryDirectory, "broken.png");
        await File.WriteAllBytesAsync(sourceImagePath, ValidPng[..20]);
        var library = await LineupLibrary.OpenAsync(Path.Combine(_temporaryDirectory, "library"));

        var exception = await Assert.ThrowsAsync<LineupLibraryException>(
            () => library.AddAsync("损坏图片", sourceImagePath));

        Assert.Equal("仅支持可正常打开的 PNG 或 JPG/JPEG 图片", exception.Message);
    }

    [Fact]
    public async Task 阵容名称忽略首尾空格和英文大小写保持唯一且失败不留下内部图片()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var sourceImagePath = Path.Combine(_temporaryDirectory, "source.png");
        var libraryDirectory = Path.Combine(_temporaryDirectory, "library");
        await File.WriteAllBytesAsync(sourceImagePath, ValidPng);
        var library = await LineupLibrary.OpenAsync(libraryDirectory);
        await library.AddAsync("Fast 8", sourceImagePath);

        var exception = await Assert.ThrowsAsync<LineupLibraryException>(
            () => library.AddAsync("  fast 8  ", sourceImagePath));

        Assert.Equal("阵容名称已存在，请换一个名称", exception.Message);
        Assert.Single(await library.GetLineupsAsync());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(libraryDirectory, "images")));
    }

    [Fact]
    public async Task PNG和JPEG均保留原始字节且阵容默认按创建时间从新到旧排列()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var pngPath = Path.Combine(_temporaryDirectory, "old.png");
        var jpegPath = Path.Combine(_temporaryDirectory, "new.jpeg");
        var jpegBytes = CreateJpeg();
        await File.WriteAllBytesAsync(pngPath, ValidPng);
        await File.WriteAllBytesAsync(jpegPath, jpegBytes);
        var library = await LineupLibrary.OpenAsync(
            Path.Combine(_temporaryDirectory, "library"));

        await library.AddAsync("较早阵容", pngPath);
        await Task.Delay(10);
        await library.AddAsync("较新阵容", jpegPath);

        var lineups = await library.GetLineupsAsync();
        Assert.Equal(["较新阵容", "较早阵容"], lineups.Select(lineup => lineup.Name));
        Assert.Equal(jpegBytes, lineups[0].ImageBytes);
        Assert.Equal(ValidPng, lineups[1].ImageBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static byte[] CreateJpeg()
    {
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgr24,
            palette: null,
            pixels: new byte[] { 128, 64, 32 },
            stride: 3);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

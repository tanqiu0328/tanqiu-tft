using System.IO;
using Microsoft.Data.Sqlite;
using TanqiuTft.Library;

namespace TanqiuTft.Library.Tests;

public sealed class LineupLibrarySessionTests : IDisposable
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(), $"tanqiu-tft-session-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task 创建阵容库后新会话能够重新打开上次的活动阵容库()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "settings", "active-library.txt");
        var libraryDirectory = Path.Combine(_temporaryDirectory, "library");
        var session = new LineupLibrarySession(settingsPath);

        await session.CreateAndActivateAsync(libraryDirectory);

        var restartedSession = new LineupLibrarySession(settingsPath);
        var restored = await restartedSession.TryRestoreAsync();

        Assert.True(restored);
        Assert.Equal(Path.GetFullPath(libraryDirectory), restartedSession.ActiveDirectoryPath);
    }

    [Fact]
    public async Task 记住的阵容库已不存在时不会在原路径创建空阵容库()
    {
        var missingDirectory = Path.Combine(_temporaryDirectory, "missing-library");
        var settingsPath = Path.Combine(_temporaryDirectory, "settings", "active-library.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, missingDirectory);

        var session = new LineupLibrarySession(settingsPath);
        var restored = await session.TryRestoreAsync();

        Assert.False(restored);
        Assert.Null(session.ActiveLibrary);
        Assert.False(Directory.Exists(missingDirectory));
    }

    [Fact]
    public async Task 打开无效目录时显示中文错误并保持当前活动阵容库不变()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "settings", "active-library.txt");
        var validDirectory = Path.Combine(_temporaryDirectory, "valid-library");
        var invalidDirectory = Path.Combine(_temporaryDirectory, "not-a-library");
        Directory.CreateDirectory(invalidDirectory);
        var session = new LineupLibrarySession(settingsPath);
        await session.CreateAndActivateAsync(validDirectory);

        var exception = await Assert.ThrowsAsync<LineupLibraryException>(
            () => session.OpenAndActivateAsync(invalidDirectory));

        Assert.Contains("不是有效的阵容库", exception.Message);
        Assert.Equal(Path.GetFullPath(validDirectory), session.ActiveDirectoryPath);
        Assert.Equal(Path.GetFullPath(validDirectory), await File.ReadAllTextAsync(settingsPath));
    }

    [Fact]
    public async Task 打开记录绝对图片路径的目录时保持当前活动阵容库不变()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "settings", "active-library.txt");
        var validDirectory = Path.Combine(_temporaryDirectory, "valid-library");
        var invalidDirectory = Path.Combine(_temporaryDirectory, "absolute-path-library");
        var sourceImagePath = Path.Combine(_temporaryDirectory, "source.png");
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllBytesAsync(sourceImagePath, ValidPng);
        var session = new LineupLibrarySession(settingsPath);
        await session.CreateAndActivateAsync(validDirectory);
        var invalidLibrary = await LineupLibrary.CreateAsync(invalidDirectory);
        await invalidLibrary.AddAsync("越界阵容", sourceImagePath);
        await ExecuteNonQueryAsync(
            Path.Combine(invalidDirectory, "library.db"),
            "UPDATE lineups SET image_path = $imagePath;",
            sourceImagePath);

        var exception = await Assert.ThrowsAsync<LineupLibraryException>(
            () => session.OpenAndActivateAsync(invalidDirectory));

        Assert.Contains("不是有效的阵容库", exception.Message);
        Assert.Equal(Path.GetFullPath(validDirectory), session.ActiveDirectoryPath);
        Assert.Equal(Path.GetFullPath(validDirectory), await File.ReadAllTextAsync(settingsPath));
    }

    [Fact]
    public async Task 打开缺少阵容名称唯一约束的数据库时显示中文错误()
    {
        var invalidDirectory = Path.Combine(_temporaryDirectory, "no-unique-library");
        Directory.CreateDirectory(Path.Combine(invalidDirectory, "images"));
        var databasePath = Path.Combine(invalidDirectory, "library.db");
        await ExecuteNonQueryAsync(
            databasePath,
            """
            CREATE TABLE lineups (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE,
                image_path TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);

        var exception = await Assert.ThrowsAsync<LineupLibraryException>(
            () => LineupLibrary.OpenExistingAsync(invalidDirectory));

        Assert.Contains("不是有效的阵容库", exception.Message);
    }

    [Fact]
    public async Task 切换阵容库时只显示目标阵容库的数据而不合并()
    {
        var settingsPath = Path.Combine(_temporaryDirectory, "settings", "active-library.txt");
        var firstDirectory = Path.Combine(_temporaryDirectory, "first-library");
        var secondDirectory = Path.Combine(_temporaryDirectory, "second-library");
        var imagePath = Path.Combine(_temporaryDirectory, "source.png");
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllBytesAsync(imagePath, ValidPng);
        var session = new LineupLibrarySession(settingsPath);

        await session.CreateAndActivateAsync(firstDirectory);
        await session.ActiveLibrary!.AddAsync("第一套阵容", imagePath);
        await session.CreateAndActivateAsync(secondDirectory);
        await session.ActiveLibrary!.AddAsync("第二套阵容", imagePath);

        await session.OpenAndActivateAsync(firstDirectory);
        var firstLineups = await session.ActiveLibrary!.GetLineupsAsync();

        Assert.Equal("第一套阵容", Assert.Single(firstLineups).Name);
    }

    [Fact]
    public async Task 复制完整阵容库到不同绝对路径后副本仍能读取内部图片()
    {
        var originalDirectory = Path.Combine(_temporaryDirectory, "original", "library");
        var copiedDirectory = Path.Combine(_temporaryDirectory, "another-place", "copied-library");
        var imagePath = Path.Combine(_temporaryDirectory, "source.png");
        Directory.CreateDirectory(_temporaryDirectory);
        await File.WriteAllBytesAsync(imagePath, ValidPng);
        var original = await LineupLibrary.CreateAsync(originalDirectory);
        await original.AddAsync("可移动阵容", imagePath);

        CopyDirectory(originalDirectory, copiedDirectory);
        Directory.Delete(originalDirectory, recursive: true);

        var copied = await LineupLibrary.OpenExistingAsync(copiedDirectory);
        var lineup = Assert.Single(await copied.GetLineupsAsync());

        Assert.Equal("可移动阵容", lineup.Name);
        Assert.Equal(ValidPng, lineup.ImageBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)));
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(
                childDirectory,
                Path.Combine(destinationDirectory, Path.GetFileName(childDirectory)));
        }
    }

    private static async Task ExecuteNonQueryAsync(
        string databasePath,
        string commandText,
        string? imagePath = null)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        if (imagePath is not null)
        {
            command.Parameters.AddWithValue("$imagePath", imagePath);
        }

        await command.ExecuteNonQueryAsync();
    }
}

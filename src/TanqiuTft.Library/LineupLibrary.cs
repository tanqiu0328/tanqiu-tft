using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace TanqiuTft.Library;

public sealed class LineupLibrary
{
    public static string DefaultDirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "紫云院妙妙屋",
        "阵容库");

    private readonly string _directoryPath;
    private readonly string _connectionString;

    private LineupLibrary(string directoryPath)
    {
        _directoryPath = directoryPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directoryPath, "library.db"),
            Pooling = false
        }.ToString();
    }

    public string DirectoryPath => _directoryPath;

    public static async Task<LineupLibrary> OpenExistingAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        var databasePath = Path.Combine(fullDirectoryPath, "library.db");
        var imagesPath = Path.Combine(fullDirectoryPath, "images");

        if (!Directory.Exists(fullDirectoryPath)
            || !File.Exists(databasePath)
            || !Directory.Exists(imagesPath))
        {
            throw InvalidLibrary();
        }

        var library = new LineupLibrary(fullDirectoryPath);
        try
        {
            await using var connection = await library.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(lineups);";
            var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    columnNames.Add(reader.GetString(1));
                }
            }

            if (!columnNames.IsSupersetOf(["id", "name", "image_path", "created_at"])
                || !await HasValidNameIndexAsync(connection, cancellationToken)
                || !await HasValidImagePathsAsync(connection, fullDirectoryPath, cancellationToken))
            {
                throw InvalidLibrary();
            }

            return library;
        }
        catch (LineupLibraryException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException or IOException)
        {
            throw InvalidLibrary(exception);
        }
    }

    public static async Task<LineupLibrary> CreateAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        if (File.Exists(Path.Combine(fullDirectoryPath, "library.db")))
        {
            return await OpenExistingAsync(fullDirectoryPath, cancellationToken);
        }

        Directory.CreateDirectory(fullDirectoryPath);
        Directory.CreateDirectory(Path.Combine(fullDirectoryPath, "images"));

        var library = new LineupLibrary(fullDirectoryPath);
        await using var connection = await library.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS lineups (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                image_path TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return library;
    }

    public async Task AddAsync(
        string name,
        string sourceImagePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0)
        {
            throw new LineupLibraryException("请输入阵容名称");
        }

        var imageBytes = await File.ReadAllBytesAsync(sourceImagePath, cancellationToken);
        var imageExtension = ValidateImage(imageBytes, sourceImagePath);
        var imageFileName = $"{Guid.NewGuid():N}{imageExtension}";
        var relativeImagePath = Path.Combine("images", imageFileName);
        var internalImagePath = Path.Combine(_directoryPath, relativeImagePath);
        var createdAt = DateTimeOffset.UtcNow;

        var temporaryImagePath = $"{internalImagePath}.tmp";

        try
        {
            await File.WriteAllBytesAsync(temporaryImagePath, imageBytes, cancellationToken);
            File.Move(temporaryImagePath, internalImagePath);

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO lineups (id, name, image_path, created_at)
                VALUES ($id, $name, $imagePath, $createdAt);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$imagePath", relativeImagePath);
            command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            File.Delete(temporaryImagePath);
            File.Delete(internalImagePath);
            throw new LineupLibraryException("阵容名称已存在，请换一个名称", exception);
        }
        catch
        {
            File.Delete(temporaryImagePath);
            File.Delete(internalImagePath);
            throw;
        }
    }

    public async Task<IReadOnlyList<Lineup>> GetLineupsAsync(
        CancellationToken cancellationToken = default)
    {
        var lineups = new List<Lineup>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, image_path, created_at
            FROM lineups
            ORDER BY created_at DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var imagePath = Path.Combine(_directoryPath, reader.GetString(1));
            lineups.Add(new Lineup(
                reader.GetString(0),
                await File.ReadAllBytesAsync(imagePath, cancellationToken),
                DateTimeOffset.Parse(reader.GetString(2))));
        }

        return lineups;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string ValidateImage(byte[] imageBytes, string sourceImagePath)
    {
        var sourceExtension = Path.GetExtension(sourceImagePath).ToLowerInvariant();
        if (sourceExtension is not (".png" or ".jpg" or ".jpeg"))
        {
            throw new LineupLibraryException("仅支持可正常打开的 PNG 或 JPG/JPEG 图片");
        }

        try
        {
            using var stream = new MemoryStream(imageBytes, writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var mimeTypes = decoder.CodecInfo.MimeTypes;

            return mimeTypes.Contains("image/png", StringComparison.OrdinalIgnoreCase)
                ? ".png"
                : mimeTypes.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase)
                    ? ".jpg"
                    : throw new LineupLibraryException("仅支持可正常打开的 PNG 或 JPG/JPEG 图片");
        }
        catch (LineupLibraryException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException
            or FileFormatException
            or InvalidOperationException
            or NotSupportedException)
        {
            throw new LineupLibraryException(
                "仅支持可正常打开的 PNG 或 JPG/JPEG 图片",
                exception);
        }
    }

    private static LineupLibraryException InvalidLibrary(Exception? innerException = null)
    {
        const string message = "所选目录不是有效的阵容库，请选择包含 library.db 和 images 文件夹的完整阵容库";
        return innerException is null
            ? new LineupLibraryException(message)
            : new LineupLibraryException(message, innerException);
    }

    private static async Task<bool> HasValidNameIndexAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var uniqueIndexNames = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA index_list(lineups);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt32(2) == 1)
                {
                    uniqueIndexNames.Add(reader.GetString(1));
                }
            }
        }

        foreach (var indexName in uniqueIndexNames)
        {
            var keyColumns = new List<(string Name, string Collation)>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_xinfo([{indexName.Replace("]", "]]", StringComparison.Ordinal)}]);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt32(5) == 1 && !reader.IsDBNull(2))
                {
                    keyColumns.Add((reader.GetString(2), reader.GetString(4)));
                }
            }

            if (keyColumns is [{ Name: "name", Collation: "NOCASE" }])
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> HasValidImagePathsAsync(
        SqliteConnection connection,
        string libraryDirectoryPath,
        CancellationToken cancellationToken)
    {
        var imagesDirectoryPath = Path.GetFullPath(Path.Combine(libraryDirectoryPath, "images"));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT image_path FROM lineups;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var storedPath = reader.GetString(0);
            if (Path.IsPathRooted(storedPath))
            {
                return false;
            }

            string fullImagePath;
            try
            {
                fullImagePath = Path.GetFullPath(Path.Combine(libraryDirectoryPath, storedPath));
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                return false;
            }

            var pathWithinImages = Path.GetRelativePath(imagesDirectoryPath, fullImagePath);
            if (pathWithinImages == "."
                || pathWithinImages == ".."
                || pathWithinImages.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(pathWithinImages)
                || !File.Exists(fullImagePath))
            {
                return false;
            }
        }

        return true;
    }
}

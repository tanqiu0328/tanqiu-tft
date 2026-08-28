using System.Globalization;
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
                || !await HasValidLineupsAsync(connection, fullDirectoryPath, cancellationToken))
            {
                throw InvalidLibrary();
            }

            await EnsureTagSchemaAsync(connection, cancellationToken);

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
        await EnsureTagSchemaAsync(connection, cancellationToken);
        return library;
    }

    public async Task AddAsync(
        string name,
        string sourceImagePath,
        IEnumerable<string>? tags = null,
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
        var normalizedTags = NormalizeTags(tags);

        var temporaryImagePath = $"{internalImagePath}.tmp";

        try
        {
            await File.WriteAllBytesAsync(temporaryImagePath, imageBytes, cancellationToken);
            File.Move(temporaryImagePath, internalImagePath);

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO lineups (id, name, image_path, created_at)
                VALUES ($id, $name, $imagePath, $createdAt);
                """;
            var lineupId = Guid.NewGuid().ToString("N");
            command.Parameters.AddWithValue("$id", lineupId);
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$imagePath", relativeImagePath);
            command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);

            await SaveTagsAsync(connection, transaction, lineupId, normalizedTags, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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

    public Task AddAsync(
        string name,
        string sourceImagePath,
        CancellationToken cancellationToken)
    {
        return AddAsync(name, sourceImagePath, tags: null, cancellationToken);
    }

    public async Task<IReadOnlyList<Lineup>> GetLineupsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, image_path, created_at
            FROM lineups
            ORDER BY created_at DESC;
            """;

        var rows = await ReadLineupRowsAsync(command, cancellationToken);
        return await LoadLineupsAsync(connection, rows, cancellationToken);
    }

    public async Task UpdateAsync(
        string existingName,
        string newName,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = newName.Trim();
        if (normalizedName.Length == 0)
        {
            throw new LineupLibraryException("请输入阵容名称");
        }

        var normalizedTags = NormalizeTags(tags);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        try
        {
            string? lineupId;
            await using (var findCommand = connection.CreateCommand())
            {
                findCommand.Transaction = transaction;
                findCommand.CommandText = "SELECT id FROM lineups WHERE name = $name COLLATE NOCASE;";
                findCommand.Parameters.AddWithValue("$name", existingName);
                lineupId = (string?)await findCommand.ExecuteScalarAsync(cancellationToken);
            }

            if (lineupId is null)
            {
                throw new LineupLibraryException("找不到要修改的阵容");
            }

            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = "UPDATE lineups SET name = $name WHERE id = $id;";
                updateCommand.Parameters.AddWithValue("$name", normalizedName);
                updateCommand.Parameters.AddWithValue("$id", lineupId);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM lineup_tags WHERE lineup_id = $lineupId;";
                deleteCommand.Parameters.AddWithValue("$lineupId", lineupId);
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await SaveTagsAsync(connection, transaction, lineupId, normalizedTags, cancellationToken);
            await using (var cleanupCommand = connection.CreateCommand())
            {
                cleanupCommand.Transaction = transaction;
                cleanupCommand.CommandText = "DELETE FROM tags WHERE id NOT IN (SELECT tag_id FROM lineup_tags);";
                await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new LineupLibraryException("阵容名称已存在，请换一个名称", exception);
        }
    }

    public async Task<IReadOnlyList<string>> GetTagSuggestionsAsync(
        CancellationToken cancellationToken = default)
    {
        var tags = new List<string>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tags.name
            FROM tags
            WHERE EXISTS (
                SELECT 1 FROM lineup_tags WHERE lineup_tags.tag_id = tags.id
            )
            ORDER BY tags.name COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    public async Task<IReadOnlyList<Lineup>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0)
        {
            return await GetLineupsAsync(cancellationToken);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lineups.id, lineups.name, lineups.image_path, lineups.created_at
            FROM lineups
            WHERE instr(lower(lineups.name), lower($query)) > 0
               OR EXISTS (
                    SELECT 1
                    FROM lineup_tags
                    INNER JOIN tags ON tags.id = lineup_tags.tag_id
                    WHERE lineup_tags.lineup_id = lineups.id
                      AND instr(lower(tags.name), lower($query)) > 0
               )
            ORDER BY CASE
                        WHEN lineups.name = $query COLLATE NOCASE THEN 0
                        WHEN instr(lower(lineups.name), lower($query)) > 0 THEN 1
                        ELSE 2
                     END,
                     lineups.created_at DESC;
            """;
        command.Parameters.AddWithValue("$query", normalizedQuery);
        var rows = await ReadLineupRowsAsync(command, cancellationToken);
        return await LoadLineupsAsync(connection, rows, cancellationToken);
    }

    public async Task<IReadOnlyList<Lineup>> GetLineupsByTagAsync(
        string tag,
        CancellationToken cancellationToken = default)
    {
        var normalizedTag = tag.Trim();
        if (normalizedTag.Length == 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT lineups.id, lineups.name, lineups.image_path, lineups.created_at
            FROM lineups
            WHERE EXISTS (
                SELECT 1
                FROM lineup_tags
                INNER JOIN tags ON tags.id = lineup_tags.tag_id
                WHERE lineup_tags.lineup_id = lineups.id
                  AND tags.name = $tag COLLATE NOCASE
            )
            ORDER BY lineups.created_at DESC;
            """;
        command.Parameters.AddWithValue("$tag", normalizedTag);
        var rows = await ReadLineupRowsAsync(command, cancellationToken);
        return await LoadLineupsAsync(connection, rows, cancellationToken);
    }

    private static async Task<IReadOnlyList<LineupRow>> ReadLineupRowsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new List<LineupRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LineupRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<Lineup>> LoadLineupsAsync(
        SqliteConnection connection,
        IReadOnlyList<LineupRow> rows,
        CancellationToken cancellationToken)
    {
        var lineups = new List<Lineup>(rows.Count);
        foreach (var row in rows)
        {
            lineups.Add(new Lineup(
                row.Name,
                await File.ReadAllBytesAsync(
                    Path.Combine(_directoryPath, row.ImagePath),
                    cancellationToken),
                DateTimeOffset.Parse(row.CreatedAt),
                await GetTagsAsync(connection, row.Id, cancellationToken)));
        }

        return lineups;
    }

    private sealed record LineupRow(string Id, string Name, string ImagePath, string CreatedAt);

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var normalizedTags = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            var normalizedTag = tag.Trim();
            if (normalizedTag.Length > 0 && seen.Add(normalizedTag))
            {
                normalizedTags.Add(normalizedTag);
            }
        }

        return normalizedTags;
    }

    private static async Task EnsureTagSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tags (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE
            );
            CREATE TABLE IF NOT EXISTS lineup_tags (
                lineup_id TEXT NOT NULL REFERENCES lineups(id) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
                position INTEGER NOT NULL,
                PRIMARY KEY (lineup_id, tag_id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveTagsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string lineupId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        for (var position = 0; position < tags.Count; position++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tags (name) VALUES ($name)
                ON CONFLICT(name) DO NOTHING;
                INSERT INTO lineup_tags (lineup_id, tag_id, position)
                SELECT $lineupId, id, $position FROM tags WHERE name = $name COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$name", tags[position]);
            command.Parameters.AddWithValue("$lineupId", lineupId);
            command.Parameters.AddWithValue("$position", position);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<string>> GetTagsAsync(
        SqliteConnection connection,
        string lineupId,
        CancellationToken cancellationToken)
    {
        var tags = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tags.name
            FROM lineup_tags
            INNER JOIN tags ON tags.id = lineup_tags.tag_id
            WHERE lineup_tags.lineup_id = $lineupId
            ORDER BY lineup_tags.position;
            """;
        command.Parameters.AddWithValue("$lineupId", lineupId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
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
                if (reader.GetInt32(2) == 1 && reader.GetInt32(4) == 0)
                {
                    uniqueIndexNames.Add(reader.GetString(1));
                }
            }
        }

        foreach (var indexName in uniqueIndexNames)
        {
            var keyColumns = new List<(string? Name, string Collation)>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_xinfo([{indexName.Replace("]", "]]", StringComparison.Ordinal)}]);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetInt32(5) == 1)
                {
                    keyColumns.Add((
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetString(4)));
                }
            }

            if (keyColumns.Count == 1
                && string.Equals(keyColumns[0].Name, "name", StringComparison.OrdinalIgnoreCase)
                && string.Equals(keyColumns[0].Collation, "NOCASE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> HasValidLineupsAsync(
        SqliteConnection connection,
        string libraryDirectoryPath,
        CancellationToken cancellationToken)
    {
        var imagesDirectoryPath = Path.GetFullPath(Path.Combine(libraryDirectoryPath, "images"));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, image_path, created_at FROM lineups;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            var storedPath = reader.GetString(1);
            var createdAt = reader.GetString(2);
            if (string.IsNullOrWhiteSpace(name)
                || !DateTimeOffset.TryParse(
                    createdAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _)
                || Path.IsPathRooted(storedPath))
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

            try
            {
                var imageBytes = await File.ReadAllBytesAsync(fullImagePath, cancellationToken);
                ValidateImage(imageBytes, fullImagePath);
            }
            catch (Exception exception) when (exception is IOException or LineupLibraryException)
            {
                return false;
            }
        }

        return true;
    }
}

using System.IO;

namespace TanqiuTft.Library;

public sealed class LineupLibrarySession
{
    private readonly string _settingsPath;

    public LineupLibrarySession(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public LineupLibrarySession()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "紫云院妙妙屋",
            "active-library.txt"))
    {
    }

    public LineupLibrary? ActiveLibrary { get; private set; }

    public string? ActiveDirectoryPath => ActiveLibrary?.DirectoryPath;

    public async Task CreateAndActivateAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var library = await LineupLibrary.CreateAsync(directoryPath, cancellationToken);
        await RememberAndActivateAsync(library, directoryPath, cancellationToken);
    }

    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return false;
        }

        var directoryPath = (await File.ReadAllTextAsync(_settingsPath, cancellationToken)).Trim();
        if (directoryPath.Length == 0)
        {
            return false;
        }

        try
        {
            var library = await LineupLibrary.OpenExistingAsync(directoryPath, cancellationToken);
            ActiveLibrary = library;
            return true;
        }
        catch (LineupLibraryException)
        {
            return false;
        }
    }

    public async Task OpenAndActivateAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var library = await LineupLibrary.OpenExistingAsync(directoryPath, cancellationToken);
        await RememberAndActivateAsync(library, library.DirectoryPath, cancellationToken);
    }

    private async Task RememberAndActivateAsync(
        LineupLibrary library,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await File.WriteAllTextAsync(_settingsPath, fullDirectoryPath, cancellationToken);
        ActiveLibrary = library;
    }
}

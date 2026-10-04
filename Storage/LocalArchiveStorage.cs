namespace DonationArchiving.Storage;

public class LocalArchiveStorage : IArchiveStorage
{
    private readonly string _rootPath;
    private readonly bool _simulateWriteFailure;

    public LocalArchiveStorage(string rootPath, bool simulateWriteFailure = false)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _simulateWriteFailure = simulateWriteFailure;
    }

    public async Task<string> WriteAsync(string relativePath, string json)
    {
        if (_simulateWriteFailure)
        {
            throw new IOException("Simulated local archive write failure. Deletion was skipped.");
        }

        var fullPath = GetFullPath(relativePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Archive path has no directory.");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(fullPath, json);

        if (!File.Exists(fullPath))
        {
            throw new IOException($"Archive file was not created: {fullPath}");
        }

        return fullPath;
    }

    public Task<string> ReadAsync(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Archive file was not found.", fullPath);
        }

        return File.ReadAllTextAsync(fullPath);
    }

    public Task<bool> ExistsAsync(string relativePath)
    {
        return Task.FromResult(File.Exists(GetFullPath(relativePath)));
    }

    public string GetFullPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var rootPrefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Archive path must stay inside LocalArchive.");
        }

        return fullPath;
    }
}

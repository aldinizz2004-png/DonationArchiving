namespace DonationArchiving.Storage;

public interface IArchiveStorage
{
    Task<string> WriteAsync(string relativePath, string json);

    Task<string> ReadAsync(string relativePath);

    Task<bool> ExistsAsync(string relativePath);
}

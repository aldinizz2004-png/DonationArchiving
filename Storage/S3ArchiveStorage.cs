using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace DonationArchiving.Storage;

public class S3ArchiveStorage : IArchiveStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;

    public S3ArchiveStorage(IAmazonS3 client, string bucketName)
    {
        _client = client;
        _bucketName = bucketName;
    }

    public async Task<string> WriteAsync(string relativePath, string json)
    {
        var key = NormalizeKey(relativePath);
        var response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            ContentBody = json,
            ContentType = "application/json"
        });

        if (response.HttpStatusCode is not HttpStatusCode.OK)
        {
            throw new IOException($"S3 upload failed with status {response.HttpStatusCode}.");
        }

        return $"s3://{_bucketName}/{key}";
    }

    public async Task<string> ReadAsync(string relativePath)
    {
        var response = await _client.GetObjectAsync(_bucketName, NormalizeKey(relativePath));
        await using var stream = response.ResponseStream;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public async Task<bool> ExistsAsync(string relativePath)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucketName, NormalizeKey(relativePath));
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static string NormalizeKey(string relativePath)
    {
        var key = relativePath.Replace('\\', '/').TrimStart('/');

        if (string.IsNullOrWhiteSpace(key) || key.Split('/').Contains(".."))
        {
            throw new InvalidOperationException("Archive object key is invalid.");
        }

        return key;
    }
}

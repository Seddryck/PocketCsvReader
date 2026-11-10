using Amazon.S3;
using Amazon.S3.Model;
using Azure.Storage.Files.DataLake;
using PocketCsvReader.Configuration;
using DirectoryEntry = PocketCsvReader.AsyncHivePartitionSource.DirectoryEntry;

namespace PocketCsvReader.ObjectStorage;

/// <summary>Discovers Hive-partitioned CSV files using object storage clients.</summary>
public static class ObjectStorageDirectoryExtensions
{
    /// <summary>Selects CSV files under an S3 bucket prefix using asynchronous listing and reads.</summary>
    /// <param name="builder">The CSV reader configuration.</param>
    /// <param name="client">The caller-owned S3 client.</param>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="prefix">The object key prefix for the root directory. Use an empty string for the bucket root.</param>
    /// <param name="searchPattern">A case-sensitive filename glob.</param>
    public static CsvDirectoryBuilder FromDirectory(
        this CsvReaderBuilder builder, IAmazonS3 client, string bucket, string prefix, string searchPattern)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentNullException.ThrowIfNull(prefix);
        CsvReaderBuilder.ValidateDirectorySearchPattern(searchPattern);
        var root = prefix.Length == 0 || prefix.EndsWith('/') ? prefix : prefix + "/";
        return new(builder, new AsyncHivePartitionSource(root, searchPattern, ListAsync, OpenAsync));

        async ValueTask<List<DirectoryEntry>> ListAsync(string path, CancellationToken token)
        {
            var request = new ListObjectsV2Request { BucketName = bucket, Prefix = path, Delimiter = "/" };
            var entries = new List<DirectoryEntry>();
            do
            {
                var page = await client.ListObjectsV2Async(request, token).ConfigureAwait(false);
                foreach (var file in page.S3Objects ?? [])
                {
                    // S3 can store an object for the directory itself.
                    if (!file.Key.EndsWith('/'))
                        entries.Add(new(file.Key, false));
                }
                foreach (var child in page.CommonPrefixes ?? [])
                    entries.Add(new(child, true));
                request.ContinuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
            }
            while (request.ContinuationToken is not null);
            return entries;
        }

        async ValueTask<Stream> OpenAsync(string path, CancellationToken token)
        {
            var response = await client.GetObjectAsync(bucket, path, token).ConfigureAwait(false);
            return response.ResponseStream;
        }
    }

    /// <summary>Selects CSV files under an ADLS Gen2 directory using asynchronous listing and reads.</summary>
    /// <param name="builder">The CSV reader configuration.</param>
    /// <param name="client">The Data Lake filesystem client.</param>
    /// <param name="directory">The path relative to the filesystem. Use an empty string for its root.</param>
    /// <param name="searchPattern">A case-sensitive filename glob.</param>
    public static CsvDirectoryBuilder FromDirectory(
        this CsvReaderBuilder builder, DataLakeFileSystemClient client, string directory, string searchPattern)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(directory);
        CsvReaderBuilder.ValidateDirectorySearchPattern(searchPattern);
        return new(builder, new AsyncHivePartitionSource(directory.Trim('/'), searchPattern, ListAsync, OpenAsync));

        async ValueTask<List<DirectoryEntry>> ListAsync(string path, CancellationToken token)
        {
            var entries = new List<DirectoryEntry>();
            await foreach (var entry in client.GetPathsAsync(path.Length == 0 ? null : path, recursive: false,
                userPrincipalName: false, cancellationToken: token).ConfigureAwait(false))
            {
                entries.Add(new(entry.Name, entry.IsDirectory == true));
            }
            return entries;
        }

        async ValueTask<Stream> OpenAsync(string path, CancellationToken token)
        {
            var download = await client.GetFileClient(path).ReadStreamingAsync(cancellationToken: token).ConfigureAwait(false);
            return download.Value.Content;
        }
    }
}

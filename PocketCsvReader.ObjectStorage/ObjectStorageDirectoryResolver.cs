using Amazon.S3;
using Azure.Storage.Files.DataLake;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.ObjectStorage;

/// <summary>Resolves s3 and abfss directory URIs using registered, caller-owned clients.</summary>
/// <remarks>Register clients before using this resolver to build queries.</remarks>
public sealed class ObjectStorageDirectoryResolver : ICsvDirectoryResolver
{
    private readonly Dictionary<string, IAmazonS3> _s3Clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Host, string FileSystem), DataLakeFileSystemClient> _adlsClients = new();

    /// <summary>Registers a client for an S3 bucket.</summary>
    public ObjectStorageDirectoryResolver AddS3(string bucket, IAmazonS3 client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentNullException.ThrowIfNull(client);
        _s3Clients.Add(bucket, client);
        return this;
    }

    /// <summary>Registers a client for its ADLS Gen2 endpoint and filesystem.</summary>
    public ObjectStorageDirectoryResolver AddAdls(DataLakeFileSystemClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var uri = client.Uri;
        var endpoint = new DataLakeUriBuilder(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.FileSystemName, nameof(client));
        var host = uri.IdnHost;
        var labels = host.Split('.', 3);
        if (labels.Length == 3 && labels[1] == "blob")
            host = $"{labels[0]}.dfs.{labels[2]}";

        _adlsClients.Add((host, endpoint.FileSystemName), client);
        return this;
    }

    CsvDirectoryBuilder ICsvDirectoryResolver.Resolve(CsvReaderBuilder builder, Uri root, string searchPattern)
    {
        if (root.Scheme is not ("s3" or "abfss"))
            throw new NotSupportedException($"Directory URI scheme '{root.Scheme}' is not supported.");

        if (root.Host.Length == 0 || !root.IsDefaultPort || root.Query.Length != 0 || root.Fragment.Length != 0)
            throw new ArgumentException("Use a storage URI with an authority and optional path, without a port, query, or fragment.", nameof(root));

        var host = root.IdnHost;
        var path = root.AbsolutePath;
        // Remove the URI's path separator, preserving any leading slash in the object key.
        var directory = Uri.UnescapeDataString(path.StartsWith('/') ? path[1..] : path);
        if (root.Scheme == "s3")
        {
            if (root.UserInfo.Length != 0)
                throw new ArgumentException("An S3 directory URI must use the bucket as its authority.", nameof(root));
            if (!_s3Clients.TryGetValue(host, out var s3Client))
                throw new InvalidOperationException("No S3 client is registered for this bucket.");
            return builder.FromDirectory(s3Client, host, directory, searchPattern);
        }

        var fileSystem = root.UserInfo;
        if (fileSystem.Length == 0 || fileSystem.Contains(':'))
            throw new ArgumentException("An ADLS directory URI must use a filesystem and endpoint as its authority.", nameof(root));
        if (!_adlsClients.TryGetValue((host, fileSystem), out var adlsClient))
            throw new InvalidOperationException("No ADLS client is registered for this endpoint and filesystem.");
        return builder.FromDirectory(adlsClient, directory, searchPattern);
    }
}

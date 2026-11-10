using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Azure;
using Azure.Storage.Files.DataLake;
using Azure.Storage.Files.DataLake.Models;
using Moq;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.ObjectStorage.Testing;

[TestFixture]
public class ObjectStorageDirectoryTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task S3_PaginatesImmediateChildrenAndSkipsRejectedPrefixes(bool useUri)
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        const string prefix = "orders%20archive/";
        const string accepted = prefix + "tag=A%3DB/";
        const string file = accepted + "orders-01.csv";
        var firstPage = new TaskCompletionSource<ListObjectsV2Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Setup(c => c.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => Matches(r, prefix, null)), token))
            .Returns(firstPage.Task);
        client.Setup(c => c.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => Matches(r, prefix, "page2")), token))
            .ReturnsAsync(new ListObjectsV2Response { CommonPrefixes = [accepted] });
        client.Setup(c => c.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => Matches(r, accepted, null)), token))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = [new() { Key = accepted }, new() { Key = accepted + "ignored.CSV" }, new() { Key = file }]
            });
        var open = new TaskCompletionSource<GetObjectResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var openStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Setup(c => c.GetObjectAsync("analytics", file, token))
            .Returns(() =>
            {
                openStarted.TrySetResult();
                return open.Task;
            });

        var builder = CreateBuilder().WithDirectoryResolver(new ObjectStorageDirectoryResolver()
            .AddS3("other-bucket", Mock.Of<IAmazonS3>(MockBehavior.Strict))
            .AddS3("analytics", client.Object));
        var source = useUri
            ? builder.FromDirectory(new Uri("s3://analytics/orders%2520archive/"), "*.csv")
            : builder.FromDirectory(client.Object, "analytics", prefix, "*.csv");
        var query = source
            .WithPartitions(p => p.HiveStyle("orders-{batch}.csv"))
            .Build()
            .WithPartitionFilter(PartitionCondition.Equal("tag", "A=B"));
        await using var reader = query.ToDataReader();
        client.VerifyNoOtherCalls();
        Assert.Throws<NotSupportedException>(() => query.EnumerateFiles());
        Assert.Throws<NotSupportedException>(() => reader.Read());

        await using var discovery = query.EnumerateFilesAsync(token).GetAsyncEnumerator();
        var discover = discovery.MoveNextAsync().AsTask();
        Assert.That(discover.IsCompleted, Is.False);
        firstPage.SetResult(new()
        {
            CommonPrefixes = [prefix + "tag=other/"],
            IsTruncated = true,
            NextContinuationToken = "page2"
        });
        Assert.That(await discover, Is.True);
        Assert.That(discovery.Current.Path, Is.EqualTo(file));
        Assert.That(discovery.Current.Partition["tag"], Is.EqualTo("A=B"));
        Assert.That(discovery.Current.Partition["batch"], Is.EqualTo("01"));
        Assert.That(await discovery.MoveNextAsync(), Is.False);
        client.Verify(c => c.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var read = reader.ReadAsync(token);
        await openStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(read.IsCompleted, Is.False);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id\n42\n"));
        open.SetResult(new GetObjectResponse { ResponseStream = stream });
        Assert.That(await read, Is.True);
        Assert.That(reader["Id"], Is.EqualTo("42"));
        Assert.That(await reader.ReadAsync(token), Is.False);
        Assert.That(stream.CanRead, Is.False);
    }

    [TestCase(false, "")]
    [TestCase(true, "")]
    [TestCase(true, "orders 2026")]
    public async Task Adls_ListsNonRecursivelyAndLeavesLaterDirectoriesUnvisited(bool useUri, string path)
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var client = new Mock<DataLakeFileSystemClient>(MockBehavior.Strict);
        var fileClient = new Mock<DataLakeFileClient>(MockBehavior.Strict);
        var response = Mock.Of<Response>();
        client.SetupGet(c => c.Uri).Returns(new Uri("https://lake.blob.core.windows.net/warehouse"));
        var prefix = path.Length == 0 ? "" : path + "/";
        var root = path.Length == 0 ? null : path;
        var directory = prefix + "year=2026";
        var file = directory + "/one.csv";
        client.Setup(c => c.GetPathsAsync(root, false, false, token))
            .Returns(AsyncPageable<PathItem>.FromPages(
            [
                Page<PathItem>.FromValues([PathEntry(prefix + "year=2025", true)], "page2", response),
                Page<PathItem>.FromValues(
                    [PathEntry(directory, true), PathEntry(prefix + "year=2027", true)], null, response)
            ]));
        client.Setup(c => c.GetPathsAsync(directory, false, false, token))
            .Returns(AsyncPageable<PathItem>.FromPages(
                [Page<PathItem>.FromValues([PathEntry(file, false)], null, response)]));
        client.Setup(c => c.GetFileClient(file)).Returns(fileClient.Object);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id\n7\n"));
        fileClient.Setup(c => c.ReadStreamingAsync(token)).ReturnsAsync(
            Response.FromValue(DataLakeModelFactory.DataLakeFileReadStreamingResult(stream, null!), response));

        var builder = CreateBuilder().WithDirectoryResolver(new ObjectStorageDirectoryResolver()
            .AddAdls(new DataLakeFileSystemClient(new Uri("https://otherlake.dfs.core.windows.net/warehouse")))
            .AddAdls(new DataLakeFileSystemClient(new Uri("https://lake.dfs.core.windows.net/other-filesystem")))
            .AddAdls(client.Object));
        var source = useUri
            ? builder.FromDirectory(new Uri($"abfss://warehouse@LAKE.dfs.core.windows.net/{Uri.EscapeDataString(path)}"), "*.csv")
            : builder.FromDirectory(client.Object, path, "*.csv");
        var query = source
            .WithPartitions(p => p.HiveStyle())
            .Build()
            .WithPartitionFilter(PartitionCondition.NotEqual("year", "2025"));
        await using (var reader = query.ToDbDataReader())
        {
            Assert.That(await reader.ReadAsync(token), Is.True);
            Assert.That(reader.HasRows, Is.True);
            Assert.That(reader["Id"], Is.EqualTo("7"));
        }
        Assert.That(stream.CanRead, Is.False);
        client.VerifyGet(c => c.Uri, Times.Once);
        client.Verify(c => c.GetPathsAsync(root, false, false, token), Times.Once);
        client.Verify(c => c.GetPathsAsync(directory, false, false, token), Times.Once);
        client.Verify(c => c.GetFileClient(file), Times.Once);
        fileClient.Verify(c => c.ReadStreamingAsync(token), Times.Once);
        client.VerifyNoOtherCalls();
    }

    [TestCase("s3://analytics", "")]
    [TestCase("s3://analytics//orders", "/orders/")]
    public async Task S3_UriResolvesBucketRootAndLeadingSlashInPrefix(string uri, string prefix)
    {
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        client.Setup(c => c.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => Matches(r, prefix, null)), CancellationToken.None))
            .ReturnsAsync(new ListObjectsV2Response());

        var query = CreateBuilder()
            .WithDirectoryResolver(new ObjectStorageDirectoryResolver().AddS3("analytics", client.Object))
            .FromDirectory(new Uri(uri), "*.csv")
            .WithPartitions(p => p.HiveStyle())
            .Build();

        await using var files = query.EnumerateFilesAsync().GetAsyncEnumerator();
        Assert.That(await files.MoveNextAsync(), Is.False);
    }

    [TestCase("s3://analytics/orders?query")]
    [TestCase("s3://analytics/orders#fragment")]
    [TestCase("s3://user@analytics/orders")]
    [TestCase("s3://analytics:123/orders")]
    [TestCase("abfss://lake.dfs.core.windows.net/orders")]
    public void DirectoryUri_RejectsInvalidStorageLocations(string uri)
    {
        var builder = CreateBuilder().WithDirectoryResolver(new ObjectStorageDirectoryResolver());
        Assert.Throws<ArgumentException>(() => builder.FromDirectory(new Uri(uri), "*.csv"));
    }

    [TestCase("s3://unknown/orders")]
    [TestCase("abfss://warehouse@otherlake.dfs.core.windows.net/orders")]
    [TestCase("abfss://other-filesystem@lake.dfs.core.windows.net/orders")]
    public void DirectoryUri_RequiresMatchingClientRegistration(string uri)
    {
        var resolver = new ObjectStorageDirectoryResolver()
            .AddS3("analytics", Mock.Of<IAmazonS3>(MockBehavior.Strict))
            .AddAdls(new DataLakeFileSystemClient(new Uri("https://lake.dfs.core.windows.net/warehouse")));
        var builder = CreateBuilder().WithDirectoryResolver(resolver);
        Assert.Throws<InvalidOperationException>(() => builder.FromDirectory(new Uri(uri), "*.csv"));
    }

    [Test]
    public void DirectoryUri_RequiresAnAbsoluteUriAndAResolverForTheScheme()
    {
        var builder = CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.FromDirectory(new Uri("orders", UriKind.Relative), "*.csv"));
        Assert.Throws<InvalidOperationException>(() => builder.FromDirectory(new Uri("s3://analytics/orders"), "*.csv"));

        builder.WithDirectoryResolver(new ObjectStorageDirectoryResolver());
        Assert.Throws<NotSupportedException>(() => builder.FromDirectory(new Uri("https://example.com/orders"), "*.csv"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task S3_CancelsListingOrOpeningAndClosesReader(bool cancelOpen)
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var list = client.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), token));
        if (cancelOpen)
        {
            list.ReturnsAsync(new ListObjectsV2Response { S3Objects = [new() { Key = "orders/one.csv" }] });
            client.Setup(c => c.GetObjectAsync("analytics", "orders/one.csv", token)).Returns(WaitAsync<GetObjectResponse>);
        }
        else
        {
            list.Returns(WaitAsync<ListObjectsV2Response>);
        }

        await using var reader = CreateBuilder().FromDirectory(client.Object, "analytics", "orders", "*.csv")
            .WithPartitions(p => p.HiveStyle()).Build().ToDataReader();
        var read = reader.ReadAsync(token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(() => read);
        Assert.That(reader.IsClosed, Is.True);
        Assert.That(await reader.ReadAsync(), Is.False);

        async Task<T> WaitAsync<T>()
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new AssertionException("Expected cancellation.");
        }
    }

    private static CsvReaderBuilder CreateBuilder() => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    private static PathItem PathEntry(string name, bool isDirectory)
        => DataLakeModelFactory.PathItem(name, isDirectory, default, default, null, null, null, null);

    private static bool Matches(ListObjectsV2Request request, string prefix, string? continuation)
        => request.BucketName == "analytics" && request.Delimiter == "/" && request.Prefix == prefix
            && request.ContinuationToken == continuation;
}

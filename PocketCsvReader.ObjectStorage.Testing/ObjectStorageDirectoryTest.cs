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
    [Test]
    public async Task S3_PaginatesImmediateChildrenAndSkipsRejectedPrefixes()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        const string prefix = "orders/";
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

        var query = CreateBuilder()
            .FromDirectory(client.Object, "analytics", "orders", "*.csv")
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

    [Test]
    public async Task Adls_ListsNonRecursivelyAndLeavesLaterDirectoriesUnvisited()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var client = new Mock<DataLakeFileSystemClient>(MockBehavior.Strict);
        var fileClient = new Mock<DataLakeFileClient>(MockBehavior.Strict);
        var response = Mock.Of<Response>();
        const string directory = "year=2026";
        const string file = directory + "/one.csv";
        client.Setup(c => c.GetPathsAsync(null, false, false, token))
            .Returns(AsyncPageable<PathItem>.FromPages(
            [
                Page<PathItem>.FromValues([PathEntry("year=2025", true)], "page2", response),
                Page<PathItem>.FromValues(
                    [PathEntry(directory, true), PathEntry("year=2027", true)], null, response)
            ]));
        client.Setup(c => c.GetPathsAsync(directory, false, false, token))
            .Returns(AsyncPageable<PathItem>.FromPages(
                [Page<PathItem>.FromValues([PathEntry(file, false)], null, response)]));
        client.Setup(c => c.GetFileClient(file)).Returns(fileClient.Object);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("Id\n7\n"));
        fileClient.Setup(c => c.ReadStreamingAsync(token)).ReturnsAsync(
            Response.FromValue(DataLakeModelFactory.DataLakeFileReadStreamingResult(stream, null!), response));

        var query = CreateBuilder()
            .FromDirectory(client.Object, "", "*.csv")
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
        client.Verify(c => c.GetPathsAsync(null, false, false, token), Times.Once);
        client.Verify(c => c.GetPathsAsync(directory, false, false, token), Times.Once);
        client.Verify(c => c.GetFileClient(file), Times.Once);
        fileClient.Verify(c => c.ReadStreamingAsync(token), Times.Once);
        client.VerifyNoOtherCalls();
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

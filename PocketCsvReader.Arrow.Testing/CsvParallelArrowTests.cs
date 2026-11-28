using System.Text;
using Apache.Arrow;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Arrow.Testing;

[TestFixture]
public class CsvParallelArrowTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task AsyncBatchesCarryOwnedProvenanceInDeliveryOrder(bool preserveOrder)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Indexed().WithField<int>().WithField<string>())
            .WithProjection("Name", "Id")
            .WithPositionTracking()
            .WithBatchParallelism(2, preserveOrder)
            .Build();
        Func<CancellationToken, ValueTask<Stream>>[] inputs =
        [
            _ => ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n1,Ada\n2,Grace\n"))),
            _ => ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\n3,Linus\n4,Margaret\n")))
        ];
        var batches = new List<CsvArrowBatch>();
        try
        {
            await foreach (var batch in csv.ToArrowBatchesAsync(inputs, batchSize: 3))
                batches.Add(batch);

            Assert.That(batches.Select(batch => batch.Batch.Length), Is.EqualTo(new[] { 3, 1 }));
            var result = batches.SelectMany(batch => batch.Sources.ToArray().Select((source, index) =>
                (source.InputIndex, source.RecordNumber, Id: ((Int32Array)batch.Batch.Column(1)).GetValue(index)))).ToArray();
            Assert.That(result.OrderBy(row => row.Id), Is.EqualTo(new (int, long, int?)[] { (0, 1, 1), (0, 2, 2), (1, 1, 3), (1, 2, 4) }));
            Assert.That(result.GroupBy(row => row.InputIndex).All(group => group.Select(row => row.RecordNumber)
                .SequenceEqual(new[] { 1L, 2L })), Is.True);
            if (preserveOrder)
                Assert.That(result.Select(row => row.Id), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(batches[0].Batch.Schema.FieldsList.Select(field => field.Name), Is.EqualTo(new[] { "Name", "Id" }));
            Assert.That(batches[0].Sources.Span[0].Position!.Value.PhysicalLine, Is.EqualTo(2));
        }
        finally
        {
            foreach (var batch in batches)
                batch.Dispose();
        }
    }
}

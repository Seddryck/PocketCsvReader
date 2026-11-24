using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class CsvProjectionTests
{
    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task ProjectionUsesSourceSchemaAndFilters(CsvInputMode mode)
    {
        var idParses = 0;
        ParseSpan<int> parseId = value => { idParses++; return int.Parse(value) + 100; };
        ParseSpan<int> parseOmitted = _ => throw new InvalidOperationException("Omitted field was parsed.");
        var csv = Builder()
            .WithStreamInitialization(new(ProbeEncoding: false, InputMode: mode))
            .WithProjection("Country", "Id")
            .WithRowNumber()
            .WithSchema(s => s.Indexed().WithField<int>(f => f.WithParser(parseId))
                .WithField<string>().WithField<string>().WithField<int>(f => f.WithParser(parseOmitted)))
            .WithFilter(2, "keep")
            .WithFilter<int>("Id", id => id > 0)
            .WithRowFilter(row => row.FieldCount == 5 && row.GetName(4) == "Unused")
            .Build();

        await using var row = csv.ToDataReader(Input("Id,Status,Country,Unused\n1,drop,BE,bad\n2,keep,\"日本\",bad"));
        Assert.That(await row.ReadAsync(), Is.True);
        Assert.That(row.Fields, Is.EqualTo(new[] { "rowNumber", "Country", "Id" }));
        Assert.That(row.FieldCount, Is.EqualTo(3));
        Assert.That(row.GetInt32(0), Is.EqualTo(2));
        Assert.That(row.GetOrdinal("Id"), Is.EqualTo(2));
        Assert.That(row.GetFieldType(1), Is.EqualTo(typeof(string)));
        Assert.That(row.GetFieldType(2), Is.EqualTo(typeof(int)));
        Assert.That(row.GetRawString(1), Is.EqualTo("\"日本\""));
        Assert.That(row.GetString(1), Is.EqualTo("日本"));
        Assert.That(row.GetInt32(2), Is.EqualTo(102));
        Assert.That(row["Id"], Is.EqualTo(102));
        Assert.That(idParses, Is.EqualTo(1));
        Assert.That(row.TryGetOrdinal("Unused", out _), Is.False);
        Assert.That(await row.ReadAsync(), Is.False);
    }

    [TestCase("Id,Id", "Id", "ambiguous")]
    [TestCase("Id,Country", "Absent", "not found")]
    [TestCase("Id,Country", "id", "not found")]
    public async Task HeaderOnlyInputValidatesProjection(string header, string name, string message)
    {
        var csv = Builder().WithProjection(name).Build();
        using var row = csv.ToDataReader(new StringReader(header));
        Assert.That(Assert.Throws<InvalidDataException>(() => row.Read())!.Message, Does.Contain(message));
        await using var asyncRow = csv.ToDataReader(Input(header));
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await asyncRow.ReadAsync());
        Assert.That(error!.Message, Does.Contain(message));
    }

    [Test]
    public void HeaderOnlyInputExposesProjectedMetadata()
    {
        using var row = Builder().WithProjection("Country", "Id").Build().ToDbDataReader(new StringReader("Id,Country"));
        Assert.That(row.HasRows, Is.False);
        Assert.That(row.FieldCount, Is.EqualTo(2));
        Assert.That(row.GetName(0), Is.EqualTo("Country"));
        Assert.That(row.GetOrdinal("Id"), Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OrdinalsSupportDuplicateHeadersAndHeaderlessInputs(bool header)
    {
        var ordinals = new[] { 2, 0 };
        var csv = Builder().WithDialect(d => d.WithHeader(header)).WithProjection(ordinals).Build();
        ordinals[0] = 1;
        using var row = csv.ToDataReader(new StringReader(header ? "Number,Skip,Number\n1,x,3" : "1,x,3"));
        Assert.That(row.Read(), Is.True);
        Assert.That(row.Fields, Is.EqualTo(header ? new[] { "Number", "Number" } : new[] { "field_2", "field_0" }));
        Assert.That(row.GetInt32(0), Is.EqualTo(3));
        Assert.That(row.GetFieldValue<int>(1), Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OrdinalOutsideSourceWidthFailsAtBinding(bool header)
    {
        using var row = Builder().WithDialect(d => d.WithHeader(header)).WithProjection(2).Build()
            .ToDataReader(new StringReader("A,B"));
        Assert.That(Assert.Throws<InvalidDataException>(() => row.Read())!.Message, Does.Contain("ordinal 2"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task BatchProjectionBindsEachEffectiveHeader(bool repeatedHeaders)
    {
        var csv = Builder().WithDialect(d => d.WithHeaderRepeat(repeatedHeaders))
            .WithProjection("Country", "Id")
            .WithSchema(s => s.Named().WithField<int>("Id").WithField<string>("Country"))
            .Build();

        Func<CancellationToken, ValueTask<Stream>>[] inputs =
        [
            _ => ValueTask.FromResult(Input("Id,Country,Extra\n1,BE,x")),
            _ => ValueTask.FromResult(Input(repeatedHeaders ? "Extra,Country,Id\ny,FR,2" : "2,FR,y"))
        ];
        await using var reader = csv.ToDbDataReader(inputs);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetName(0), Is.EqualTo("Country"));
        Assert.That(reader.GetFieldType(1), Is.EqualTo(typeof(int)));
        Assert.That(reader.GetString(0), Is.EqualTo("BE"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("FR"));
        Assert.That(reader.GetFieldValue<int>(1), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task OmittedColumnsStillParticipateInFramingAndWidthChecks(CsvInputMode mode)
    {
        var csv = Builder().WithStreamInitialization(new(ProbeEncoding: false, InputMode: mode))
            .WithProjection("Id").Build();
        await using var row = csv.ToDataReader(Input("Ignored,Id,Other\n\"multiple\nlines\",42,x\na,43,b,extra"));
        Assert.That(await row.ReadAsync(), Is.True);
        Assert.That(row.GetInt32(0), Is.EqualTo(42));
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await row.ReadAsync());
        Assert.That(error!.Message, Does.Contain("contains 1 more field"));
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void MalformedOmittedFieldStillThrows(CsvInputMode mode)
    {
        var csv = Builder().WithStreamInitialization(new(ProbeEncoding: false, InputMode: mode))
            .WithProjection("Id").Build();
        using var row = csv.ToDataReader(Input("Ignored,Id\n\"bad\"x,42"));
        Assert.Throws<InvalidDataException>(() => row.Read());
    }

    [Test]
    public void EvenAHeaderOnlyLaterFileMustBind()
    {
        var csv = Builder().WithProjection("Id").Build();
        using var reader = csv.ToDataReader(new[] { Input("Id,Country\n1,BE"), Input("Country,Other") });
        Assert.That(reader.Read(), Is.True);
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read())!.Message, Does.Contain("Id"));
    }

    [Test]
    public void ReordersArrayFieldsAndKeepsMissingFieldRules()
    {
        var csv = Builder().WithDialect(d => d.WithArray(a => a.WithDelimiter('|').WithPrefix('[').WithSuffix(']')))
            .WithProjection(2, 1).Build();
        using var row = csv.ToDataReader(new StringReader("A,B,C\nx,[1|2],y\nz,[3|4]"));
        Assert.That(row.Read(), Is.True);
        Assert.That(row.GetArray<int>(1), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(row.Read(), Is.True);
        Assert.That(row.FieldCount, Is.EqualTo(2));
        Assert.That(row.GetValue(0), Is.EqualTo(string.Empty));
        Assert.That(row.GetArray<int>(1), Is.EqualTo(new[] { 3, 4 }));
    }

    [Test]
    public void ObjectMappersSeeProjectedFields()
    {
        var csv = Builder().WithStreamInitialization(new(ProbeEncoding: false, InputMode: CsvInputMode.Utf8Bytes))
            .WithProjection("Name", "Id").Build();
        Assert.That(csv.To<Customer>(Input("Id,Unused,Name\n12,ignored,東京")).Single(), Is.EqualTo(new Customer("東京", 12)));

        var fields = csv.To<string[]>(Input("Id,Unused,Name\n12,ignored,東京"),
            (span, metadata) =>
            {
                Assert.That(span.ToString(), Does.Not.Contain("ignored"));
                var result = new List<string>();
                foreach (var field in metadata)
                    result.Add(span.Slice(field.Value.Start, field.Value.Length).ToString());
                return result.ToArray();
            }).Single();
        Assert.That(fields, Is.EqualTo(new[] { "東京", "12" }));
    }

    [TestCase(CsvInputMode.DecodedChars, false)]
    [TestCase(CsvInputMode.DecodedChars, true)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    public void RawProjectionAvoidsPerRowMaterialization(CsvInputMode mode, bool asynchronous)
    {
        const int rows = 2000;
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("first,東京😀,7,more\n", rows)));
        var csv = Builder().WithDialect(d => d.WithoutHeader())
            .WithStreamInitialization(new(ProbeEncoding: false, InputMode: mode))
            .WithProjection(2).WithRowNumber().Build();

        int ReadAll()
        {
            using var reader = csv.ToDataReader(new MemoryStream(data));
            var count = 0;
            while (asynchronous ? reader.ReadAsync().GetAwaiter().GetResult() : reader.Read())
                count++;
            return count;
        }

        for (var i = 0; i < 3; i++)
            ReadAll();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = ReadAll();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(count, Is.EqualTo(rows));
        // As in CsvStorageReuseTests, the budget includes initialization and
        // refills and rejects a record wrapper or copied metadata per row.
        Assert.That(allocated, Is.LessThan(24L * rows), $"Allocated {allocated} bytes.");
    }

    private sealed record Customer(string Name, int Id);

    private static CsvReaderBuilder Builder() => new CsvReaderBuilder()
        .WithDialect(d => d.WithDelimiter(',').WithLineTerminator("\n").WithDoubleQuote(true));

    private static Stream Input(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));
}

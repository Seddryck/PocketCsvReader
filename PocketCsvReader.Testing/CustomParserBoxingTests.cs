using System.Collections;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class CustomParserBoxingTests
{
    private readonly record struct Amount(long Value);

    private static RuntimeParsersDescriptor Register<T>(ParseFunction<T> parse, bool builder)
    {
        if (builder)
            return new RuntimeParsersDescriptorBuilder().WithParser(parse).Build()!;
        var descriptor = new RuntimeParsersDescriptor();
        descriptor.AddParser(parse);
        return descriptor;
    }

    private static CsvDataReader Reader(Type type, RuntimeParsersDescriptor parsers)
    {
        // No field format: the runtime registration supplies the parser.
        var schema = new SchemaDescriptor.IndexedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(type));
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema, null, parsers);
        var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("3000000")), profile);
        Assert.That(reader.Read(), Is.True);
        return reader;
    }

    private static void CheckReads<T>(ParseFunction<T> parse, T expected, bool builder,
        Func<CsvDataReader, T>? accessor = null)
    {
        var descriptor = Register(parse, builder);
        Assert.That(descriptor.Count, Is.EqualTo(1));
        var legacy = descriptor.Single();
        Assert.That(legacy.Key, Is.EqualTo(typeof(T)));
        Assert.That(legacy.Value("3000000"), Is.EqualTo(expected));
        Assert.That(((IEnumerable)descriptor).Cast<KeyValuePair<Type, ParseFunction>>().Single().Key, Is.EqualTo(typeof(T)));
        Assert.That(descriptor.FindParser(typeof(T))!.Value.Registration, Is.SameAs(parse));
        using var reader = Reader(typeof(T), descriptor);
        Assert.That(reader.GetFieldValue<T>(0), Is.EqualTo(expected));
        Assert.That(reader.GetValue(0), Is.EqualTo(expected));
        Assert.That(reader.GetFieldValue<T>(0), Is.EqualTo(expected));
        if (accessor is not null)
            Assert.That(accessor(reader), Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GenericStringRegistrations_PreserveTypedAndBoxedReadsAndEnumeration(bool builder)
    {
        CheckReads<int>(s => int.Parse(s, CultureInfo.InvariantCulture) + 1, 3000001, builder, r => r.GetInt32(0));
        CheckReads<long>(s => long.Parse(s, CultureInfo.InvariantCulture) * 1000, 3000000000L, builder, r => r.GetInt64(0));
        CheckReads<Amount>(s => new(long.Parse(s, CultureInfo.InvariantCulture)), new(3000000), builder);
        CheckReads<int?>(s => int.Parse(s, CultureInfo.InvariantCulture), 3000000, builder);
        CheckReads<string>(s => "amount:" + s, "amount:3000000", builder, r => r.GetString(0));
    }

    private static long Allocated(Func<long> read)
    {
        const int iterations = 4096;
        for (int i = 0; i < iterations; i++)
            read();
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            checksum += read();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(checksum, Is.GreaterThan(0));
        return bytes;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TypedReads_AllocateOnlyTheExistingInputString(bool builder)
    {
        using var text = Reader(typeof(string), Register<string>(s => s, builder));
        using var integer = Reader(typeof(int), Register<int>(s => int.Parse(s, CultureInfo.InvariantCulture), builder));
        using var wide = Reader(typeof(long), Register<long>(s => long.Parse(s, CultureInfo.InvariantCulture), builder));
        using var amount = Reader(typeof(Amount), Register<Amount>(s => new(long.Parse(s, CultureInfo.InvariantCulture)), builder));
        long stringBytes = Allocated(() => text.GetFieldValue<string>(0).Length);
        Assert.That(stringBytes, Is.GreaterThan(0), "String-based parsers still materialize their input.");
        Assert.That(Allocated(() => integer.GetFieldValue<int>(0)), Is.EqualTo(stringBytes));
        Assert.That(Allocated(() => integer.GetInt32(0)), Is.EqualTo(stringBytes));
        Assert.That(Allocated(() => wide.GetFieldValue<long>(0)), Is.EqualTo(stringBytes));
        Assert.That(Allocated(() => wide.GetInt64(0)), Is.EqualTo(stringBytes));
        Assert.That(Allocated(() => amount.GetFieldValue<Amount>(0).Value), Is.EqualTo(stringBytes));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BoxedReads_AddOnlyTheFinalBox(bool builder)
    {
        using var reader = Reader(typeof(int), Register<int>(s => int.Parse(s, CultureInfo.InvariantCulture), builder));
        // The legacy API boxes exactly once. Reuse the same input and string materialization
        // to compare it with GetValue, excluding setup and lazy adapter compilation.
        var legacy = Register<int>(s => int.Parse(s, CultureInfo.InvariantCulture), builder).Single().Value;
        ReadOnlyMemory<char> input = "3000000".AsMemory();
        long singleBox = Allocated(() => (int)legacy(input.Span.ToString()));
        Assert.That(Allocated(() => (int)reader.GetValue(0)), Is.EqualTo(singleBox));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ParserExceptions_PropagateWithoutWrapping(bool builder)
    {
        var failure = new FormatException("Invalid amount.");
        using var reader = Reader(typeof(int), Register<int>(_ => throw failure, builder));
        Assert.That(Assert.Throws<FormatException>(() => reader.GetInt32(0)), Is.SameAs(failure));
        Assert.That(Assert.Throws<FormatException>(() => reader.GetFieldValue<int>(0)), Is.SameAs(failure));
        Assert.That(Assert.Throws<FormatException>(() => reader.GetValue(0)), Is.SameAs(failure));
    }

    [Test]
    public void RegistrationPrecedenceAndNonGenericApi_RemainCompatible()
    {
        var builder = new RuntimeParsersDescriptorBuilder()
            .WithDefaultParser<int>((ReadOnlySpan<char> _) => -1)
            .WithParser<int>((string _) => 7)
            .WithParser<int>((ReadOnlySpan<char> _) => 8)
            .WithParser<int>((string _) => 9);
        var first = builder.Build()!;
        using var reader = Reader(typeof(int), first);
        Assert.That(reader.GetInt32(0), Is.EqualTo(9));
        builder.WithParser(typeof(int), _ => 10);
        using var legacy = Reader(typeof(int), builder.Build()!);
        Assert.That(legacy.GetInt32(0), Is.EqualTo(10));
        Assert.That(legacy.GetValue(0), Is.EqualTo(10));
        Assert.That(first.Single().Value("ignored"), Is.EqualTo(9));
        var descriptor = Register<int>(_ => 1, false);
        Assert.Throws<ArgumentException>(() => descriptor.AddParser<int>((string _) => 2));
    }
}

using System.Data;
using System.Runtime.CompilerServices;
using Apache.Arrow;
using Apache.Arrow.Types;

namespace PocketCsvReader.Arrow;

/// <summary>
/// Converts records produced by a <see cref="FlatFileReader{TProfile,TDataReader}"/>
/// into bounded Apache Arrow record batches.
/// </summary>
public static class ArrowReaderExtensions
{
    /// <summary>Streams a file as Apache Arrow record batches.</summary>
    /// <remarks>
    /// The returned batches own their Arrow buffers. The caller must dispose each batch.
    /// Empty input produces no batches.
    /// </remarks>
    public static IEnumerable<RecordBatch> ToArrowBatches<TProfile, TDataReader>(
        this FlatFileReader<TProfile, TDataReader> source,
        string filename,
        int batchSize = 65_536)
        where TProfile : IProfile
        where TDataReader : IDataReader
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ValidateBatchSize(batchSize);
        return Enumerate(source.ToDataReader(filename), batchSize);
    }

    /// <summary>Streams an input stream as Apache Arrow record batches.</summary>
    /// <remarks>
    /// Enumeration disposes the data reader and, consequently, follows the source reader's
    /// stream-ownership behavior. The caller must dispose every returned batch.
    /// Empty input produces no batches.
    /// </remarks>
    public static IEnumerable<RecordBatch> ToArrowBatches<TProfile, TDataReader>(
        this FlatFileReader<TProfile, TDataReader> source,
        Stream stream,
        int batchSize = 65_536)
        where TProfile : IProfile
        where TDataReader : IDataReader
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stream);
        ValidateBatchSize(batchSize);
        return Enumerate(source.ToDataReader(stream), batchSize);
    }

    /// <summary>Asynchronously streams a file as Apache Arrow record batches.</summary>
    /// <remarks>
    /// Input reads are asynchronous. The returned batches own their Arrow buffers and must
    /// be disposed by the caller. Empty input produces no batches.
    /// </remarks>
    public static IAsyncEnumerable<RecordBatch> ToArrowBatchesAsync<TProfile, TDataReader>(
        this FlatFileReader<TProfile, TDataReader> source,
        string filename,
        int batchSize = 65_536,
        CancellationToken cancellationToken = default)
        where TProfile : IProfile
        where TDataReader : IDataReader
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ValidateBatchSize(batchSize);
        return EnumerateAsync(source.ToDataReader(filename), batchSize, cancellationToken);
    }

    /// <summary>Asynchronously streams an input stream as Apache Arrow record batches.</summary>
    /// <remarks>
    /// Input reads are asynchronous. Enumeration disposes the data reader and follows its
    /// stream-ownership behavior. The caller must dispose every returned batch.
    /// Empty input produces no batches.
    /// </remarks>
    public static IAsyncEnumerable<RecordBatch> ToArrowBatchesAsync<TProfile, TDataReader>(
        this FlatFileReader<TProfile, TDataReader> source,
        Stream stream,
        int batchSize = 65_536,
        CancellationToken cancellationToken = default)
        where TProfile : IProfile
        where TDataReader : IDataReader
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stream);
        ValidateBatchSize(batchSize);
        return EnumerateAsync(source.ToDataReader(stream), batchSize, cancellationToken);
    }

    private static IEnumerable<RecordBatch> Enumerate(IDataReader reader, int batchSize)
    {
        using (reader)
        {
            if (!reader.Read())
                yield break;

            var layout = ArrowBatchLayout.Create(reader);
            do
            {
                var batch = layout.CreateBatch();
                do
                {
                    batch.Append(reader);
                }
                while (batch.Length < batchSize && reader.Read());

                yield return batch.Build();
            }
            while (!reader.IsClosed && reader.Read());
        }
    }

    private static async IAsyncEnumerable<RecordBatch> EnumerateAsync(
        IDataReader reader,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (reader is not IAsyncDataReader asyncReader)
        {
            reader.Dispose();
            throw new NotSupportedException(
                $"{reader.GetType().Name} must implement {nameof(IAsyncDataReader)} for asynchronous Arrow enumeration.");
        }

        await using (asyncReader.ConfigureAwait(false))
        {
            if (!await asyncReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield break;

            var layout = ArrowBatchLayout.Create(reader);
            var hasMore = true;
            while (hasMore)
            {
                var batch = layout.CreateBatch();
                do
                {
                    batch.Append(reader);
                    if (batch.Length < batchSize)
                        hasMore = await asyncReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                while (hasMore && batch.Length < batchSize);

                yield return batch.Build();

                if (!hasMore)
                    yield break;

                hasMore = await asyncReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void ValidateBatchSize(int batchSize)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");
    }
}

internal sealed class ArrowBatchLayout
{
    private IReadOnlyList<ArrowColumnDefinition> Columns { get; }
    private Schema Schema { get; }

    private ArrowBatchLayout(IReadOnlyList<ArrowColumnDefinition> columns)
    {
        Columns = columns;
        Schema = new Schema(columns.Select(column => column.Field), null);
    }

    public static ArrowBatchLayout Create(IDataRecord record)
    {
        var columns = Enumerable.Range(0, record.FieldCount)
            .Select(index => ArrowColumnDefinition.Create(record.GetName(index), record.GetFieldType(index), index))
            .ToArray();
        return new ArrowBatchLayout(columns);
    }

    public ArrowBatchBuilder CreateBatch()
        => new(Schema, Columns.Select(column => column.CreateWriter()).ToArray());
}

internal sealed class ArrowBatchBuilder
{
    private Schema Schema { get; }
    private IReadOnlyList<ArrowColumnWriter> Columns { get; }
    public int Length { get; private set; }

    public ArrowBatchBuilder(Schema schema, IReadOnlyList<ArrowColumnWriter> columns)
    {
        Schema = schema;
        Columns = columns;
    }

    public void Append(IDataRecord record)
    {
        foreach (var column in Columns)
            column.Append(record);
        Length++;
    }

    public RecordBatch Build()
        => new(Schema, Columns.Select(column => column.Build()), Length);
}

internal sealed class ArrowColumnWriter
{
    private int Ordinal { get; }
    private Action<IDataRecord, int> AppendValue { get; }
    private Action AppendNull { get; }
    private Func<IArrowArray> BuildArray { get; }

    public ArrowColumnWriter(
        int ordinal,
        Action<IDataRecord, int> appendValue,
        Action appendNull,
        Func<IArrowArray> buildArray)
    {
        Ordinal = ordinal;
        AppendValue = appendValue;
        AppendNull = appendNull;
        BuildArray = buildArray;
    }

    public void Append(IDataRecord record)
    {
        if (record.IsDBNull(Ordinal))
            AppendNull();
        else
            AppendValue(record, Ordinal);
    }

    public IArrowArray Build() => BuildArray();
}

internal sealed class ArrowColumnDefinition
{
    public Field Field { get; }
    private Func<ArrowColumnWriter> WriterFactory { get; }

    private ArrowColumnDefinition(Field field, Func<ArrowColumnWriter> writerFactory)
    {
        Field = field;
        WriterFactory = writerFactory;
    }

    public ArrowColumnWriter CreateWriter() => WriterFactory();

    public static ArrowColumnDefinition Create(string name, Type runtimeType, int ordinal)
    {
        var type = Nullable.GetUnderlyingType(runtimeType) ?? runtimeType;

        if (type == typeof(bool))
            return Define(name, BooleanType.Default, ordinal, () =>
            {
                var builder = new BooleanArray.Builder();
                return Writer(ordinal, (record, index) => builder.Append(record.GetBoolean(index)), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(sbyte))
            return Define(name, Int8Type.Default, ordinal, () => Primitive<sbyte, Int8Array, Int8Array.Builder>(new Int8Array.Builder(), ordinal, record => Convert.ToSByte(record.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture)));
        if (type == typeof(byte))
            return Define(name, UInt8Type.Default, ordinal, () => Primitive<byte, UInt8Array, UInt8Array.Builder>(new UInt8Array.Builder(), ordinal, record => Convert.ToByte(record.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture)));
        if (type == typeof(short))
            return Define(name, Int16Type.Default, ordinal, () =>
            {
                var builder = new Int16Array.Builder();
                return Writer(ordinal, (record, index) => builder.Append(record.GetInt16(index)), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(ushort))
            return Define(name, UInt16Type.Default, ordinal, () => Primitive<ushort, UInt16Array, UInt16Array.Builder>(new UInt16Array.Builder(), ordinal, record => Convert.ToUInt16(record.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture)));
        if (type == typeof(int))
            return Define(name, Int32Type.Default, ordinal, () =>
            {
                var builder = new Int32Array.Builder();
                return Writer(ordinal, (record, index) => builder.Append(record.GetInt32(index)), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(uint))
            return Define(name, UInt32Type.Default, ordinal, () => Primitive<uint, UInt32Array, UInt32Array.Builder>(new UInt32Array.Builder(), ordinal, record => Convert.ToUInt32(record.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture)));
        if (type == typeof(long))
            return Define(name, Int64Type.Default, ordinal, () =>
            {
                var builder = new Int64Array.Builder();
                return Writer(ordinal, (record, index) => builder.Append(record.GetInt64(index)), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(ulong))
            return Define(name, UInt64Type.Default, ordinal, () => Primitive<ulong, UInt64Array, UInt64Array.Builder>(new UInt64Array.Builder(), ordinal, record => Convert.ToUInt64(record.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture)));
        if (type == typeof(float))
            return Define(name, FloatType.Default, ordinal, () =>
            {
                var builder = new FloatArray.Builder();
                return Writer(ordinal, (record, index) => builder.Append(record.GetFloat(index)), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(double))
            return Define(name, DoubleType.Default, ordinal, () =>
            {
                var builder = new DoubleArray.Builder();
                return Writer(ordinal, (record, index) => builder.Append(record.GetDouble(index)), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(decimal))
        {
            var arrowType = new Decimal128Type(38, 18);
            return Define(name, arrowType, ordinal, () =>
            {
                var builder = new Decimal128Array.Builder(arrowType);
                return Writer(ordinal, (record, index) => builder.Append(record.GetDecimal(index)), () => builder.AppendNull(), () => builder.Build());
            });
        }
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid))
            return Define(name, StringType.Default, ordinal, () =>
            {
                var builder = new StringArray.Builder();
                return Writer(ordinal, (record, index) => builder.Append(Convert.ToString(record.GetValue(index), System.Globalization.CultureInfo.InvariantCulture)!), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(byte[]))
            return Define(name, BinaryType.Default, ordinal, () =>
            {
                var builder = new BinaryArray.Builder();
                return Writer(ordinal, (record, index) => builder.Append(((byte[])record.GetValue(index)).AsSpan()), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(DateOnly))
            return Define(name, Date32Type.Default, ordinal, () =>
            {
                var builder = new Date32Array.Builder();
                return Writer(ordinal, (record, index) => builder.Append((DateOnly)record.GetValue(index)), () => builder.AppendNull(), () => builder.Build());
            });
        if (type == typeof(DateTime))
        {
            var arrowType = new TimestampType(TimeUnit.Microsecond, TimeZoneInfo.Utc);
            return Define(name, arrowType, ordinal, () =>
            {
                var builder = new TimestampArray.Builder(arrowType);
                return Writer(ordinal, (record, index) =>
                {
                    var value = record.GetDateTime(index);
                    builder.Append(new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
                }, () => builder.AppendNull(), () => builder.Build());
            });
        }
        if (type == typeof(DateTimeOffset))
        {
            var arrowType = new TimestampType(TimeUnit.Microsecond, TimeZoneInfo.Utc);
            return Define(name, arrowType, ordinal, () =>
            {
                var builder = new TimestampArray.Builder(arrowType);
                return Writer(ordinal, (record, index) => builder.Append((DateTimeOffset)record.GetValue(index)), () => builder.AppendNull(), () => builder.Build());
            });
        }
        if (type == typeof(TimeOnly))
        {
            var arrowType = new Time64Type(TimeUnit.Nanosecond);
            return Define(name, arrowType, ordinal, () =>
            {
                var builder = new Time64Array.Builder(arrowType);
                return Writer(ordinal, (record, index) => builder.Append((TimeOnly)record.GetValue(index)), () => builder.AppendNull(), () => builder.Build());
            });
        }

        throw new NotSupportedException(
            $"Field '{name}' uses runtime type '{runtimeType.FullName}', which has no Apache Arrow mapping. " +
            "Configure the PocketCsvReader schema with a supported runtime type before enumeration.");
    }

    private static ArrowColumnDefinition Define(
        string name,
        IArrowType type,
        int ordinal,
        Func<ArrowColumnWriter> writerFactory)
        => new(new Field(name, type, nullable: true), writerFactory);

    private static ArrowColumnWriter Writer(
        int ordinal,
        Action<IDataRecord, int> appendValue,
        Action appendNull,
        Func<IArrowArray> build)
        => new(ordinal, appendValue, appendNull, build);

    private static ArrowColumnWriter Primitive<T, TArray, TBuilder>(
        TBuilder builder,
        int ordinal,
        Func<IDataRecord, T> read)
        where T : struct
        where TArray : IArrowArray
        where TBuilder : PrimitiveArrayBuilder<T, TArray, TBuilder>
        => Writer(ordinal, (record, _) => builder.Append(read(record)), () => builder.AppendNull(), () => builder.Build());
}

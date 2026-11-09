using System.Globalization;
using System.Numerics;
using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

// Accumulates typed Arrow buffers. An input value only needs to live through Append.
internal abstract class ColumnDecoder(Field field)
{
    private readonly ArrowBuffer.BitmapBuilder _validity = new();
    private int _nullCount;

    protected Field Field { get; } = field;
    protected int NullCount => _nullCount;
    public int Length => _validity.Length;

    public static IArrowArray Decode(
        Field field, ReadOnlySpan<char> data, ReadOnlySpan<char> validity, ReadOnlySpan<char> offsets,
        JsonFieldBuffer scratch, CancellationToken token, int capacity = 0)
    {
        try
        {
            var decoder = Create(field, scratch, integration: true);
            decoder.Reserve(capacity);
            var cells = JsonRecordParser.Enumerate(data);
            var flags = JsonRecordParser.Enumerate(validity);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var hasCell = cells.MoveNext();
                var isValid = ReadValidity(ref flags, hasCell);
                if (!hasCell)
                {
                    if (!offsets.IsEmpty)
                        ValidateOffsets(offsets, decoder.Offsets);
                    return decoder.Build();
                }
                decoder.Append(cells.Value, isValid);
            }
        }
        catch (Exception exception) when (IsInvalidValue(exception))
        {
            throw ColumnError(field, exception);
        }
    }

    private static bool ReadValidity(ref JsonRecordParser.Elements flags, bool hasCell)
    {
        if (flags.MoveNext() != hasCell)
            throw new InvalidDataException("VALIDITY and DATA lengths differ.");

        return hasCell && DecodeValidity(flags.Value);
    }

    public static bool DecodeValidity(ReadOnlySpan<char> value) => value switch
    {
        "0" => false,
        "1" => true,
        _ => throw new InvalidDataException("VALIDITY entries must be 0 or 1."),
    };

    private static void ValidateOffsets(ReadOnlySpan<char> json, ReadOnlySpan<int> expected)
    {
        var items = JsonRecordParser.Enumerate(json);
        foreach (var offset in expected)
            ValidateOffset(items.MoveNext(), items.Value, offset);
        if (items.MoveNext())
            throw new InvalidDataException("Too many OFFSET entries.");
    }

    public static void ValidateOffset(bool hasValue, ReadOnlySpan<char> value, int expected)
    {
        if (!hasValue || JsonValues.Count(value) != expected)
            throw new InvalidDataException("OFFSET entries do not match the encoded values.");
    }

    public static ColumnDecoder Create(Field field, JsonFieldBuffer scratch, bool integration = false)
        => field.DataType switch
        {
            BooleanType => new BooleanDecoder(field, integration),
            Int8Type => Integer<sbyte>(field, scratch),
            UInt8Type => Integer<byte>(field, scratch),
            Int16Type => Integer<short>(field, scratch),
            UInt16Type => Integer<ushort>(field, scratch),
            Date32Type when !integration => Date<int>(field, scratch, 1),
            Date64Type when !integration => Date<long>(field, scratch, 86_400_000),
            Int32Type or Date32Type or Time32Type => Integer<int>(field, scratch),
            UInt32Type => Integer<uint>(field, scratch),
            Int64Type or Date64Type or Time64Type or TimestampType or DurationType
                => Integer<long>(field, scratch, allowQuoted: true),
            UInt64Type => Integer<ulong>(field, scratch, allowQuoted: true),
            HalfFloatType => FloatingPoint<Half>(field, scratch),
            FloatType => FloatingPoint<float>(field, scratch),
            DoubleType => FloatingPoint<double>(field, scratch),
            Decimal128Type type => new DecimalDecoder(field, scratch, type.Precision, type.Scale, 16, integration),
            Decimal256Type type => new DecimalDecoder(field, scratch, type.Precision, type.Scale, 32, integration),
            StringType or BinaryType or FixedSizeBinaryType { TypeId: ArrowTypeId.FixedSizedBinary }
                => new BytesDecoder(field, scratch, integration),
            ListType list when !integration && list.ValueDataType is not ListType
                => new ListDecoder(field, Create(list.ValueField, scratch)),
            NullType => new NullDecoder(field),
            _ => throw new NotSupportedException($"Arrow type '{field.DataType.Name}' is unsupported for field '{field.Name}'."),
        };

    public void Reserve(int capacity)
    {
        if (capacity == 0)
            return;

        _validity.Reserve(capacity);
        ReserveValues(capacity);
    }

    public virtual ReadOnlySpan<int> Offsets => default;

    public abstract IArrowArray Build();

    public static bool IsInvalidValue(Exception exception)
        => exception is InvalidDataException or FormatException or OverflowException;

    public static InvalidDataException ColumnError(Field field, Exception exception)
        => new($"Invalid value in column '{field.Name}': {exception.Message}", exception);

    public void Append(ReadOnlySpan<char> value) => Append(value, !JsonValues.IsNull(value));

    public void Append(ReadOnlySpan<char> value, bool isValid)
    {
        if (!isValid && !Field.IsNullable)
            throw new InvalidDataException("A non-nullable field contains a null.");
        AppendValue(value, isValid);
        _validity.Append(isValid);
        if (!isValid)
            _nullCount++;
    }

    protected virtual void ReserveValues(int capacity)
    {
        // Null columns have no value buffer.
    }

    protected abstract void AppendValue(ReadOnlySpan<char> value, bool isValid);

    protected ArrowBuffer BuildValidity() => _nullCount == 0 ? ArrowBuffer.Empty : _validity.Build();

    protected IArrowArray BuildArray(ArrowBuffer values)
        => BuildArray([BuildValidity(), values]);

    protected IArrowArray BuildArray(ArrowBuffer[] buffers)
        => ArrowArrayFactory.BuildArray(new ArrayData(Field.DataType, Length, _nullCount, 0, buffers, []));

    private delegate T ParseCell<T>(ReadOnlySpan<char> value);

    private static ColumnDecoder Integer<T>(Field field, JsonFieldBuffer scratch, bool allowQuoted = false)
        where T : unmanaged, IBinaryInteger<T>
        => new PrimitiveDecoder<T>(field, value =>
            T.Parse(JsonValues.Number(value, scratch, allowQuoted), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));

    private static ColumnDecoder FloatingPoint<T>(Field field, JsonFieldBuffer scratch)
        where T : unmanaged, IFloatingPointIeee754<T>
        => new PrimitiveDecoder<T>(field, value =>
        {
            var parsed = T.Parse(JsonValues.Number(value, scratch, allowQuoted: false), NumberStyles.Float, CultureInfo.InvariantCulture);
            return T.IsFinite(parsed) ? parsed : throw new InvalidDataException("Floating-point value is out of range.");
        });

    private static ColumnDecoder Date<T>(Field field, JsonFieldBuffer scratch, int unitsPerDay)
        where T : unmanaged, IBinaryInteger<T>
        => new PrimitiveDecoder<T>(field, value =>
        {
            const int epochDay = 719162;
            var date = DateOnly.ParseExact(JsonValues.Text(value, scratch), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return T.CreateChecked((long)(date.DayNumber - epochDay) * unitsPerDay);
        });

    private sealed class PrimitiveDecoder<T>(Field field, ParseCell<T> parse) : ColumnDecoder(field)
        where T : unmanaged
    {
        private readonly ArrowBuffer.Builder<T> _values = new();

        protected override void ReserveValues(int capacity) => _values.Reserve(capacity);

        protected override void AppendValue(ReadOnlySpan<char> value, bool isValid)
            => _values.Append(isValid ? parse(value) : default);

        public override IArrowArray Build() => BuildArray(_values.Build());
    }

    private sealed class BooleanDecoder(Field field, bool integration) : ColumnDecoder(field)
    {
        private readonly ArrowBuffer.BitmapBuilder _bits = new();

        protected override void ReserveValues(int capacity) => _bits.Reserve(capacity);

        protected override void AppendValue(ReadOnlySpan<char> value, bool isValid)
        {
            var bit = isValid && (integration ? value switch
            {
                "0" or "false" => false,
                "1" or "true" => true,
                _ => throw new InvalidDataException("Expected a boolean or a 0/1 bit."),
            } : JsonValues.Boolean(value));
            _bits.Append(bit);
        }

        public override IArrowArray Build() => BuildArray(_bits.Build());
    }

    private sealed class BytesDecoder : ColumnDecoder
    {
        private readonly JsonFieldBuffer _scratch;
        private readonly bool _integration;
        private readonly bool _utf8;
        private readonly int? _width;
        private readonly ArrowBuffer.Builder<byte> _bytes = new();
        private readonly ArrowBuffer.Builder<int>? _positions;

        public BytesDecoder(Field field, JsonFieldBuffer scratch, bool integration)
            : base(field)
        {
            _scratch = scratch;
            _integration = integration;
            _utf8 = field.DataType is StringType || !integration;
            _width = (field.DataType as FixedSizeBinaryType)?.ByteWidth;
            if (_width is null)
                _positions = new ArrowBuffer.Builder<int>().Append(0);
        }

        protected override void ReserveValues(int capacity)
        {
            if (_width is int fixedWidth)
                _bytes.Reserve(checked(capacity * fixedWidth));
            else
                _positions?.Reserve(capacity);
        }

        protected override void AppendValue(ReadOnlySpan<char> value, bool isValid)
        {
            if (isValid || (_integration && !JsonValues.IsNull(value)))
                Encode(JsonValues.Text(value, _scratch));
            else if (_width is int fixedWidth)
                Extend(fixedWidth).Clear();
            _positions?.Append(_bytes.Length);
        }

        private void Encode(ReadOnlySpan<char> text)
        {
            if (!_utf8 && text.Length % 2 != 0)
                throw new InvalidDataException("Binary values must contain pairs of hexadecimal digits.");
            var size = _utf8 ? Encoding.UTF8.GetByteCount(text) : text.Length / 2;
            if (_width is int fixedWidth && size != fixedWidth)
                throw new InvalidDataException($"Expected {fixedWidth} bytes in a fixed-size binary value.");

            var buffer = Extend(size);
            if (_utf8)
            {
                Encoding.UTF8.GetBytes(text, buffer);
            }
            else
            {
                for (var index = 0; index < size; index++)
                    buffer[index] = byte.Parse(text.Slice(index * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            }
        }

        private Span<byte> Extend(int size)
        {
            var start = _bytes.Length;
            _bytes.Resize(checked(start + size));
            return _bytes.Span.Slice(start, size);
        }

        public override ReadOnlySpan<int> Offsets => _positions is null ? default : _positions.Span[.._positions.Length];

        public override IArrowArray Build() => _positions is null
            ? BuildArray(_bytes.Build())
            : BuildArray([BuildValidity(), _positions.Build(), _bytes.Build()]);
    }

    private sealed class DecimalDecoder(
        Field field, JsonFieldBuffer scratch, int precision, int scale, int width, bool integration) : ColumnDecoder(field)
    {
        private readonly ArrowBuffer.Builder<byte> _bytes = new();
        private readonly BigInteger _limit = BigInteger.Pow(10, precision);

        protected override void ReserveValues(int capacity) => _bytes.Reserve(checked(capacity * width));

        protected override void AppendValue(ReadOnlySpan<char> value, bool isValid)
        {
            var unscaled = isValid ? Parse(value) : BigInteger.Zero;
            if (BigInteger.Abs(unscaled) >= _limit)
                throw new InvalidDataException($"Decimal value exceeds precision {precision}.");

            Span<byte> cell = stackalloc byte[width];
            cell.Fill(unscaled.Sign < 0 ? (byte)255 : (byte)0);
            if (!unscaled.TryWriteBytes(cell, out _))
                throw new InvalidDataException("Decimal value is out of range.");
            _bytes.Append(cell);
        }

        private BigInteger Parse(ReadOnlySpan<char> value)
        {
            var text = JsonValues.Number(value, scratch, allowQuoted: true);
            return integration
                ? BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                : ParseScaledDecimal(text, scale);
        }

        private static BigInteger ParseScaledDecimal(ReadOnlySpan<char> text, int scale)
        {
            var point = text.IndexOf('.');
            var fractional = point < 0 ? default : text[(point + 1)..];
            var whole = point < 0 ? text : text[..point];
            var value = BigInteger.Parse(whole, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (scale < 0)
            {
                if (!fractional.IsEmpty)
                    throw new InvalidDataException("Fractional values exceed the declared decimal scale.");
                var scaled = BigInteger.DivRem(value, BigInteger.Pow(10, checked(-scale)), out var remainder);
                return remainder.IsZero ? scaled : throw new InvalidDataException("Value exceeds the declared decimal scale.");
            }

            while (!fractional.IsEmpty && fractional[^1] == '0')
                fractional = fractional[..^1];
            if (fractional.Length > scale)
                throw new InvalidDataException("Fractional values exceed the declared decimal scale.");
            value *= BigInteger.Pow(10, scale);
            if (!fractional.IsEmpty)
            {
                var fraction = BigInteger.Parse(fractional, NumberStyles.None, CultureInfo.InvariantCulture);
                value += (text[0] == '-' ? -fraction : fraction) * BigInteger.Pow(10, scale - fractional.Length);
            }
            return value;
        }

        public override IArrowArray Build() => BuildArray(_bytes.Build());
    }

    private sealed class ListDecoder(Field field, ColumnDecoder child) : ColumnDecoder(field)
    {
        private readonly ArrowBuffer.Builder<int> _positions = new ArrowBuffer.Builder<int>().Append(0);

        protected override void ReserveValues(int capacity) => _positions.Reserve(capacity);

        protected override void AppendValue(ReadOnlySpan<char> value, bool isValid)
        {
            if (isValid)
            {
                var items = JsonRecordParser.Enumerate(value);
                while (items.MoveNext())
                    child.Append(items.Value);
            }
            _positions.Append(child.Length);
        }

        public override IArrowArray Build()
            => new ListArray(Field.DataType, Length, _positions.Build(), child.Build(), BuildValidity(), NullCount);
    }

    private sealed class NullDecoder(Field field) : ColumnDecoder(field)
    {
        protected override void AppendValue(ReadOnlySpan<char> value, bool isValid)
        {
            if (isValid)
                throw new InvalidDataException("Expected null.");
        }

        public override IArrowArray Build() => new NullArray(Length);
    }
}

namespace PocketCsvReader.Indexing;

internal static class IndexKeyBinary
{
    internal static void Write(BinaryWriter writer, object value)
    {
        switch (value)
        {
            case string text:
                // Preserve the comparer input exactly, including surrogate code units
                // that a registered parser may return.
                writer.Write(text.Length);
                foreach (var character in text)
                    writer.Write((ushort)character);
                break;
            case DateOnly date: writer.Write(date.DayNumber); break;
            case sbyte number: writer.Write(number); break;
            case byte number: writer.Write(number); break;
            case short number: writer.Write(number); break;
            case ushort number: writer.Write(number); break;
            case int number: writer.Write(number); break;
            case uint number: writer.Write(number); break;
            case long number: writer.Write(number); break;
            case ulong number: writer.Write(number); break;
            case decimal number: writer.Write(number); break;
            default: throw new NotSupportedException($"Index keys of type '{value.GetType()}' are not supported.");
        }
    }

    internal static object Read(BinaryReader reader, Type type)
    {
        try
        {
            if (type == typeof(DateOnly))
                return DateOnly.FromDayNumber(reader.ReadInt32());
            return Type.GetTypeCode(type) switch
            {
                TypeCode.String => ReadText(reader),
                TypeCode.SByte => reader.ReadSByte(),
                TypeCode.Byte => reader.ReadByte(),
                TypeCode.Int16 => reader.ReadInt16(),
                TypeCode.UInt16 => reader.ReadUInt16(),
                TypeCode.Int32 => reader.ReadInt32(),
                TypeCode.UInt32 => reader.ReadUInt32(),
                TypeCode.Int64 => reader.ReadInt64(),
                TypeCode.UInt64 => reader.ReadUInt64(),
                TypeCode.Decimal => reader.ReadDecimal(),
                _ => throw new NotSupportedException($"Index keys of type '{type}' are not supported.")
            };
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The index contains an invalid key.", exception);
        }
    }

    private static string ReadText(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var file = reader.BaseStream;
        if (length < 0 || length > (file.Length - file.Position) / sizeof(ushort))
            throw new InvalidDataException("The index contains an invalid string key length.");
        return string.Create(length, reader, static (text, input) =>
        {
            for (int i = 0; i < text.Length; i++)
                text[i] = (char)input.ReadUInt16();
        });
    }
}

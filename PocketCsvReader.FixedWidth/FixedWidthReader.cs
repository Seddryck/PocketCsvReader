using System.Reflection;
using PocketCsvReader;

namespace PocketCsvReader.FixedWidth;

public sealed class FixedWidthReader : FlatFileReader<FixedWidthProfile, FixedWidthDataReader>
{
    public FixedWidthReader(FixedWidthProfile profile)
        : base(profile)
    { }

    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    protected override FixedWidthDataReader CreateDataReader(Stream stream)
        => new(stream, Profile);

    public IEnumerable<T> To<T>(string filename, Func<FixedWidthDataReader, T>? mapper = null)
        => ReadObjects(OpenRead(filename), mapper);

    public IEnumerable<T> To<T>(Stream stream, Func<FixedWidthDataReader, T>? mapper = null)
        => ReadObjects(stream, mapper);

    private IEnumerable<T> ReadObjects<T>(Stream stream, Func<FixedWidthDataReader, T>? mapper)
    {
        using var reader = ToDataReader(stream);
        var constructor = mapper is null ? ResolveConstructor<T>(reader.FieldCount) : null;
        while (reader.Read())
            yield return mapper is not null ? mapper(reader) : InvokeConstructor<T>(constructor!, reader);
    }

    private static ConstructorInfo ResolveConstructor<T>(int fieldCount)
        => typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(ctor => ctor.GetParameters().Length == fieldCount)
            ?? throw new InvalidOperationException($"Type '{typeof(T).Name}' must expose one public constructor with {fieldCount} parameters.");

    private static T InvokeConstructor<T>(ConstructorInfo constructor, FixedWidthDataReader reader)
    {
        var parameters = constructor.GetParameters();
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            values[i] = GetTypedValue(reader, i, parameters[i].ParameterType);
        return (T)constructor.Invoke(values);
    }

    private static object? GetTypedValue(FixedWidthDataReader reader, int ordinal, Type type)
    {
        var method = typeof(FixedWidthReader)
            .GetMethod(nameof(GetTypedValueCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type);
        try
        {
            return method.Invoke(null, [reader, ordinal]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static T GetTypedValueCore<T>(FixedWidthDataReader reader, int ordinal)
        => reader.GetFieldValue<T>(ordinal);
}

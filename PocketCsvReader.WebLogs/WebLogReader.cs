using System.Reflection;

namespace PocketCsvReader.WebLogs;

public abstract class WebLogReader : FlatFileReader<WebLogProfile, WebLogDataReader>
{
    protected WebLogReader(WebLogProfile profile)
        : base(profile)
    { }

    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    protected override WebLogDataReader CreateDataReader(Stream stream)
        => new(stream, Profile);

    public IEnumerable<T> To<T>(string filename, Func<WebLogDataReader, T>? mapper = null)
        => ReadObjects(OpenRead(filename), mapper);

    public IEnumerable<T> To<T>(Stream stream, Func<WebLogDataReader, T>? mapper = null)
        => ReadObjects(stream, mapper);

    private IEnumerable<T> ReadObjects<T>(Stream stream, Func<WebLogDataReader, T>? mapper)
    {
        using var reader = ToDataReader(stream);
        ConstructorInfo? constructor = null;
        while (reader.Read())
        {
            constructor ??= mapper is null ? ResolveConstructor<T>(reader.FieldCount) : null;
            yield return mapper is not null ? mapper(reader) : InvokeConstructor<T>(constructor!, reader);
        }
    }

    private static ConstructorInfo ResolveConstructor<T>(int fieldCount)
        => typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(candidate => candidate.GetParameters().Length == fieldCount)
            ?? throw new InvalidOperationException($"Type '{typeof(T).Name}' must expose one public constructor with {fieldCount} parameters.");

    private static T InvokeConstructor<T>(ConstructorInfo constructor, WebLogDataReader reader)
    {
        var parameters = constructor.GetParameters();
        var values = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            values[i] = GetTypedValue(reader, i, parameters[i].ParameterType);
        return (T)constructor.Invoke(values);
    }

    private static object? GetTypedValue(WebLogDataReader reader, int ordinal, Type type)
    {
        var method = typeof(WebLogReader)
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

    private static T GetTypedValueCore<T>(WebLogDataReader reader, int ordinal)
        => reader.GetFieldValue<T>(ordinal);
}

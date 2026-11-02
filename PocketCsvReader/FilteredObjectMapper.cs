using System.Linq.Expressions;
using System.Reflection;

namespace PocketCsvReader;

// Default constructor projection must use getters so filters and projection share conversions.
internal static class FilteredObjectMapper<T>
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Func<CsvDataReader, T>> Mappers = new();
    internal static T Map(CsvDataReader reader) => Mappers.GetOrAdd(reader.FieldCount, Create)(reader);

    private static Func<CsvDataReader, T> Create(int count)
    {
        var constructor = typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == count);
        var reader = Expression.Parameter(typeof(CsvDataReader), "reader");
        var getter = typeof(CsvDataReader).GetMethods().Single(m => m.Name == nameof(CsvDataReader.GetFieldValue)
            && m.IsGenericMethodDefinition && m.GetParameters() is [{ ParameterType: var type }] && type == typeof(int));
        var fields = constructor.GetParameters().Select((p, i) =>
            Expression.Call(reader, getter.MakeGenericMethod(p.ParameterType), Expression.Constant(i)));
        return Expression.Lambda<Func<CsvDataReader, T>>(Expression.New(constructor, fields), reader).Compile();
    }
}

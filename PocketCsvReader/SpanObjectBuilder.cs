using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;

public delegate object? Parse(ReadOnlySpan<char> span);

public class SpanObjectBuilder<T>
{
    private Dictionary<Type, Parse> ParserMapping { get; } = new();

    public SpanObjectBuilder()
    {
        var culture = CultureInfo.InvariantCulture;
        ParserMapping.Add(typeof(string), s => s.ToString());
        ParserMapping.Add(typeof(int), s => int.Parse(s, culture));
        ParserMapping.Add(typeof(long), s => long.Parse(s, culture));
        ParserMapping.Add(typeof(short), s => short.Parse(s, culture));
        ParserMapping.Add(typeof(byte), s => byte.Parse(s, culture));
        ParserMapping.Add(typeof(float), s => float.Parse(s, culture));
        ParserMapping.Add(typeof(double), s => double.Parse(s, culture));
        ParserMapping.Add(typeof(decimal), s => decimal.Parse(s, culture));
        ParserMapping.Add(typeof(bool), s => bool.Parse(s));
        ParserMapping.Add(typeof(DateTime), s => DateTime.Parse(s));
        ParserMapping.Add(typeof(DateOnly), s => DateOnly.Parse(s));
        ParserMapping.Add(typeof(TimeOnly), s => TimeOnly.Parse(s));
        ParserMapping.Add(typeof(DateTimeOffset), s => DateTimeOffset.Parse(s));
        ParserMapping.Add(typeof(char), s => s[0]);
    }

    public void SetParser<TField>(Parse parse)
    {
        if (ParserMapping.ContainsKey(typeof(TField)))
            ParserMapping[typeof(TField)] = parse;
        else
            ParserMapping.Add(typeof(TField), parse);
    }

    /// <summary>
    /// Instantiates an object of type <typeparamref name="T"/> by parsing fields from the specified character span using the provided field spans.
    /// </summary>
    /// <param name="span">The input character span containing the serialized field values.</param>
    /// <param name="fieldSpans">A sequence of <see cref="FieldSpan"/> objects defining the positions and lengths of each field within <paramref name="span"/>.</param>
    /// <returns>An instance of <typeparamref name="T"/> with constructor parameters populated from the parsed fields.</returns>
    /// <exception cref="Exception">Thrown if no parser is registered for a required field type.</exception>
    /// <exception cref="FormatException">Thrown if a field cannot be parsed into the expected type, with details about the field index and value.</exception>
    public T Instantiate(ReadOnlySpan<char> span, IEnumerable<FieldSpan> fieldSpans)
    {
        var ctors = typeof(T).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var ctor = ctors.First(c => c.GetParameters().Length == fieldSpans.Count());
        var index = 0;
        var fields = new object?[fieldSpans.Count()];
        foreach (var fieldSpan in fieldSpans)
        {
            var type = ctor.GetParameters()[index].ParameterType;
            if(!ParserMapping.TryGetValue(type, out var parse))
                throw new Exception($"No parser found for type {type}.");
            try
            {
                var field = parse(span.Slice(fieldSpan.Value.Start, fieldSpan.Value.Length));
                fields[index++] = field;
            }
            catch (Exception ex)
            {
                throw new FormatException($"Error parsing field {index} of type {type} for value {span.Slice(fieldSpan.Value.Start, fieldSpan.Value.Length).ToString()}", ex);
            }
        }
        return (T)ctor.Invoke(fields);
    }
}

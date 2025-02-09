using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class RuntimeParsersDescriptor : IEnumerable<KeyValuePair<Type, ParseFunction>>
{
    private Dictionary<Type, ParseFunction> Parsers { get; init; } = [];

    public void AddParser<T>(ParseFunction<T> parse)
        => AddParser(typeof(T), (string str) => parse.Invoke(str)!);

    public void AddParser(Type type, ParseFunction parse)
    {
        var returnType = parse.Method.ReturnType;
        if (!type.IsAssignableTo(returnType))
            throw new ArgumentException($"The provided parser returns {returnType}, which is not assignable from {type}.");

        Parsers.Add(type, parse);
    }

    public int Count => Parsers.Count;

    public IEnumerator<KeyValuePair<Type, ParseFunction>> GetEnumerator()
        => Parsers.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}

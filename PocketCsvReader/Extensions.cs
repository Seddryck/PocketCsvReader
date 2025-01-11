using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
internal static class Extensions
{
    public static T Also<T>(this T obj, Action<T> action)
    {
        action(obj);
        return obj;
    }
}

internal static class DictionaryExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(
        this IDictionary<TKey, TValue> dictionary
        , TKey key
        , TValue item
    )
    {
        if (dictionary.TryGetValue(key, out var value))
            return value;

        dictionary.Add(key, item);
        return item;
    }
}


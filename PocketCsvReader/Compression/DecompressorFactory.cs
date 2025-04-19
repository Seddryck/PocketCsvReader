using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Compression;
public class DecompressorFactory
{
    private Dictionary<string, IDecompressor> Decompressors { get; } = [];

    public IDecompressor GetDecompressor(string key)
    {
        if (Decompressors.TryGetValue(NormalizeKey(key), out var decompressor))
            return decompressor;
        throw new ArgumentOutOfRangeException(nameof(key), $"The compression '{key}' is not registered.");
    }

    protected virtual string NormalizeKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        key = key.ToLowerInvariant();
        if (key.Length > 0 && key[0] == '.')
            key = key.Substring(1);
        return key;
    }

    public DecompressorFactory(Action<DecompressorFactory> initialize)
        => initialize.Invoke(this);

    public void AddOrReplace(string key, IDecompressor decompressor)
    {
        key = NormalizeKey(key);
        if (!Decompressors.TryAdd(key, decompressor))
            Decompressors[key] = decompressor;
    }

    public void AddAlias(string targetKey, params string[] aliasKeys)
    {
        targetKey = NormalizeKey(targetKey);

        if (!Decompressors.TryGetValue(targetKey, out var targetDecompressor))
            throw new ArgumentException($"Target decompressor for key '{targetKey}' not found.");

        foreach (var alias in aliasKeys)
        {
            var aliasKey = NormalizeKey(alias);
            Decompressors[aliasKey] = targetDecompressor;
        }
    }

    public void Clear()
        => Decompressors.Clear();

    public static DecompressorFactory Streaming()
        => new DecompressorFactory((factory) =>
            {
                factory.AddOrReplace("gz", GZipDecompressor.Streaming());
                factory.AddAlias("gz", "gzip");
                factory.AddOrReplace("deflate", DeflateDecompressor.Streaming());
                factory.AddAlias("deflate", "zz", "def");
                factory.AddOrReplace("zip", ZipDecompressor.Streaming());
                factory.AddAlias("zip", "zipfile");
            });
    public static DecompressorFactory Buffered()
        => new DecompressorFactory((factory) =>
            {
                factory.AddOrReplace("gz", GZipDecompressor.Buffered());
                factory.AddAlias("gz", "gzip");
                factory.AddOrReplace("deflate", DeflateDecompressor.Buffered());
                factory.AddAlias("deflate", "zz", "def");
                factory.AddOrReplace("zip", ZipDecompressor.Buffered());
                factory.AddAlias("zip", "zipfile");
            });

    public string[] GetSupportedKeys()
        => Decompressors.Keys.Order().ToArray();
}

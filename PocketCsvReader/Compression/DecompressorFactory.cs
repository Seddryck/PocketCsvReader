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
        throw new KeyNotFoundException();
    }

    protected virtual string NormalizeKey(string key)
    {
        key = key.ToLowerInvariant();
        if (key[0] == '.')
            key = key.Substring(1);
        return key;
    }

    public DecompressorFactory()
    {
        Initialize();
    }

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

    protected virtual void Initialize()
    {
        AddOrReplace("gz", GZipDecompressor.Buffered());
        AddAlias("gz", "gzip");
        AddOrReplace("deflate", DeflateDecompressor.Buffered());
        AddAlias("deflate", "zz", "def");
        AddOrReplace("zip", ZipDecompressor.Buffered());
        AddAlias("zip", "zipfile");
    }
}

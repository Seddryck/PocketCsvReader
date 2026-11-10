namespace PocketCsvReader.Configuration;

/// <summary>Resolves an absolute directory URI to a source for partitioned CSV discovery.</summary>
public interface ICsvDirectoryResolver
{
    /// <summary>Selects a directory source without listing files or opening streams.</summary>
    CsvDirectoryBuilder Resolve(CsvReaderBuilder builder, Uri root, string searchPattern);
}

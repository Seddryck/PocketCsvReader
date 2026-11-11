using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public class CsvReaderBuilder : ReaderBuilder<CsvReaderBuilder>
{
    private DialectDescriptorBuilder _dialectBuilder = new();
    private RowNumberOptions? _rowNumber;
    private ICsvDirectoryResolver? _directoryResolver;

    /// <summary>Selects paths whose partition metadata will be configured with WithPartitions.</summary>
    public CsvFilePathBuilder FromFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new(new(this, paths, null));
    }

    /// <summary>Selects paths and a deferred stream opener for those paths.</summary>
    public CsvFilePathBuilder FromFiles(IEnumerable<string> paths, Func<string, Stream> open)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(open);
        return new(new(this, paths, open));
    }

    /// <summary>Selects files with existing partition metadata.</summary>
    public CsvFileQueryBuilder<TPartition> FromFiles<TPartition>(IEnumerable<PartitionedFile<TPartition>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return new(this, files);
    }

    /// <summary>Prepends a generated integer column, numbered from one across the returned records.</summary>
    public CsvReaderBuilder WithRowNumber(string columnName = "rowNumber")
    {
        var options = new RowNumberOptions(columnName);
        options.Validate();
        _rowNumber = options;
        return this;
    }

    /// <summary>Disables the generated row-number column.</summary>
    public CsvReaderBuilder WithoutRowNumber()
    {
        _rowNumber = null;
        return this;
    }
    private StreamInitializationOptions _streamInitialization = StreamInitializationOptions.Default;

    public CsvReaderBuilder WithStreamInitialization(StreamInitializationOptions options)
    {
        _streamInitialization = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
    private DialectDescriptor? _dialect;

    public CsvReaderBuilder WithDialect(Func<DialectDescriptorBuilder, DialectDescriptorBuilder> func)
    {
        _dialect = null;
        _dialectBuilder = func(_dialectBuilder);
        return this;
    }
    public CsvReaderBuilder WithDialect(DialectDescriptorBuilder dialectBuilder)
    {
        _dialect = null;
        _dialectBuilder = dialectBuilder;
        return this;
    }

    public CsvReaderBuilder WithDialect(DialectDescriptor dialect)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        return this;
    }

    /// <summary>Configures how this builder resolves directory URIs.</summary>
    public CsvReaderBuilder WithDirectoryResolver(ICsvDirectoryResolver resolver)
    {
        _directoryResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    /// <summary>Selects files matching a filename glob beneath a root directory.</summary>
    public CsvDirectoryBuilder FromDirectory(string root, string searchPattern)
        => CreateDirectoryBuilder(root, searchPattern, null);

    /// <summary>Selects files matching a filename glob beneath an absolute directory URI.</summary>
    public CsvDirectoryBuilder FromDirectory(Uri root, string searchPattern)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!root.IsAbsoluteUri)
            throw new ArgumentException("An absolute directory URI is required.", nameof(root));
        HivePartitionSource.ValidateSearchPattern(searchPattern);

        var resolver = _directoryResolver
            ?? throw new InvalidOperationException("Configure a directory URI resolver with WithDirectoryResolver first.");
        return resolver.Resolve(this, root, searchPattern);
    }

    /// <summary>Selects files beneath a root directory and supplies a deferred opener for accepted paths.</summary>
    public CsvDirectoryBuilder FromDirectory(string root, string searchPattern, Func<string, Stream> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return CreateDirectoryBuilder(root, searchPattern, open);
    }

    private CsvDirectoryBuilder CreateDirectoryBuilder(string root, string searchPattern, Func<string, Stream>? open)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        HivePartitionSource.ValidateSearchPattern(searchPattern);
        return new(this, new HivePartitionSource(Path.GetFullPath(root), searchPattern), open);
    }

    public CsvReader Build()
    {
        var profile = new CsvProfile(
            _dialect ?? _dialectBuilder.Build(),
            BuildSchema(),
            BuildResource(),
            BuildParsers())
        {
            ParserOptimizations = BuildParserOptimizations(),
            StreamInitialization = _streamInitialization,
            Filters = BuildFilters(),
            Skip = Skip,
            Take = Take,
            RowNumber = _rowNumber
        };
        return new CsvReader(profile);
    }
}

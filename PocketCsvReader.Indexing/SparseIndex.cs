using System.Globalization;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Indexing;

/// <summary>Describes the column and typed rules used by an index.</summary>
public interface IIndexKey
{
    Type KeyType { get; }
    internal string? ColumnName { get; }
    internal int? ColumnOrdinal { get; }
    internal string CompatibilityId { get; }
    internal object Parse(ReadOnlySpan<char> text);
    internal int Compare(object left, object right);
    internal ulong GetStableHash(object value) => throw new NotSupportedException("This key does not support stable index hashing.");
    internal string? HashCompatibility => null;
    internal string? OrderingCompatibility => null;
    internal IIndexKey ResolveRuntimeParser(RuntimeParsersDescriptor? parsers) => this;
}

/// <summary>Defines a key using library-supported parsing and comparison settings.</summary>
/// <remarks>
/// A file reader's registered runtime parser is used unless a format or culture is specified.
/// Culture settings use standard culture data without user overrides.
/// </remarks>
public sealed class IndexKey<TKey> : IIndexKey
    where TKey : ISpanParsable<TKey>
{
    public Type KeyType => typeof(TKey);
    public string? ColumnName { get; }
    public int? ColumnOrdinal { get; }

    private readonly ParseSpan<TKey> _parse;
    private readonly IComparer<TKey> _comparer;
    private readonly string _compatibilityId;
    private readonly bool _useRuntimeParser;
    private readonly StringComparison _stringComparison;
    private readonly CompareInfo? _comparisonSort;

    public IndexKey(string columnName, string? format = null, CultureInfo? culture = null,
        StringComparison? comparison = null)
        : this(format, culture, comparison)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ColumnName = columnName;
    }

    public IndexKey(int columnOrdinal, string? format = null, CultureInfo? culture = null,
        StringComparison? comparison = null)
        : this(format, culture, comparison)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columnOrdinal);
        ColumnOrdinal = columnOrdinal;
    }

    private IndexKey(string? format, CultureInfo? culture, StringComparison? comparison)
    {
        _useRuntimeParser = format is null && culture is null;
        var effectiveCulture = CultureInfo.GetCultureInfo(culture?.Name ?? "");
        _comparer = Comparer<TKey>.Default;
        if (typeof(TKey) == typeof(string))
        {
            if (format is not null) throw new ArgumentException("A string key has no parse format.", nameof(format));
            var effectiveComparison = comparison ?? StringComparison.Ordinal;
            var comparisonCulture = effectiveComparison switch
            {
                StringComparison.CurrentCulture or StringComparison.CurrentCultureIgnoreCase
                    => culture is null ? CultureInfo.GetCultureInfo(CultureInfo.CurrentCulture.Name) : effectiveCulture,
                StringComparison.InvariantCulture or StringComparison.InvariantCultureIgnoreCase
                    => CultureInfo.InvariantCulture,
                _ => null
            };
            _stringComparison = effectiveComparison;
            _comparisonSort = comparisonCulture?.CompareInfo;
            var comparer = comparisonCulture is null ? StringComparer.FromComparison(effectiveComparison)
                : StringComparer.Create(comparisonCulture, effectiveComparison is
                    StringComparison.CurrentCultureIgnoreCase or StringComparison.InvariantCultureIgnoreCase);
            _comparer = (IComparer<TKey>)(object)comparer;
            _parse = text => (TKey)(object)text.ToString();
            _compatibilityId = SparseIndexSidecar.Fingerprint([
                KeyType.FullName,
                effectiveComparison.ToString(),
                comparisonCulture?.Name,
                comparisonCulture?.CompareInfo.Version.SortId.ToString(),
                comparisonCulture?.CompareInfo.Version.FullVersion.ToString(CultureInfo.InvariantCulture)
            ]);
            return;
        }

        if (comparison is not null)
            throw new ArgumentException("String comparison settings require a string key.", nameof(comparison));

        if (typeof(TKey) == typeof(DateOnly))
        {
            format ??= "yyyy-MM-dd";
            var dateFormat = effectiveCulture.DateTimeFormat;
            if (!IncludesYear(format, dateFormat))
                throw new ArgumentException("An indexed date format must specify the year.", nameof(format));
            _parse = text => (TKey)(object)DateOnly.ParseExact(text, format, effectiveCulture);
            _compatibilityId = SparseIndexSidecar.Fingerprint(
                new string?[] { KeyType.FullName, format, effectiveCulture.Name, dateFormat.DateSeparator,
                    dateFormat.ShortDatePattern, dateFormat.LongDatePattern, dateFormat.Calendar.GetType().FullName,
                    dateFormat.Calendar.TwoDigitYearMax.ToString(CultureInfo.InvariantCulture) }
                .Concat(dateFormat.MonthNames).Concat(dateFormat.MonthGenitiveNames)
                .Concat(dateFormat.AbbreviatedMonthNames).Concat(dateFormat.AbbreviatedMonthGenitiveNames)
                .Concat(dateFormat.DayNames).Concat(dateFormat.AbbreviatedDayNames));
        }
        else if (Type.GetTypeCode(typeof(TKey)) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16
            or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
            or TypeCode.Decimal)
        {
            if (format is not null) throw new ArgumentException("A numeric key has no parse format.", nameof(format));
            _parse = text => TKey.Parse(text, effectiveCulture);
            var numberFormat = effectiveCulture.NumberFormat;
            _compatibilityId = SparseIndexSidecar.Fingerprint([KeyType.FullName, effectiveCulture.Name,
                numberFormat.PositiveSign, numberFormat.NegativeSign, numberFormat.NumberDecimalSeparator,
                numberFormat.NumberGroupSeparator]);
        }
        else
        {
            throw new NotSupportedException($"Index key rules for '{typeof(TKey)}' are not supported.");
        }
    }

    private static bool IncludesYear(string format, DateTimeFormatInfo culture)
    {
        format = format switch
        {
            "d" => culture.ShortDatePattern,
            "D" => culture.LongDatePattern,
            "o" or "O" or "r" or "R" => "yyyy",
            _ => format
        };
        char quote = '\0';
        for (int i = 0; i < format.Length; i++)
        {
            char token = format[i];
            if (token == '\\')
            {
                i++;
            }
            else if (quote != '\0')
            {
                if (token == quote) quote = '\0';
            }
            else if (token is '\'' or '"')
            {
                quote = token;
            }
            else if (token == 'y')
            {
                return true;
            }
        }
        return false;
    }

    string IIndexKey.CompatibilityId => _compatibilityId;
    ulong IIndexKey.GetStableHash(object value) => StableIndexKeyHash.Compute(value, _stringComparison, _comparisonSort);
    private string? OrdinalIgnoreCaseCompatibility => _stringComparison == StringComparison.OrdinalIgnoreCase
        ? SparseIndexSidecar.Fingerprint([CultureInfo.InvariantCulture.CompareInfo.Version.SortId.ToString(),
            Environment.Version.ToString(), System.Runtime.InteropServices.RuntimeInformation.OSDescription]) : null;
    string? IIndexKey.HashCompatibility => OrdinalIgnoreCaseCompatibility;
    string? IIndexKey.OrderingCompatibility => OrdinalIgnoreCaseCompatibility;
    object IIndexKey.Parse(ReadOnlySpan<char> text) => _parse(text)!;
    int IIndexKey.Compare(object left, object right) => _comparer.Compare((TKey)left, (TKey)right);

    IIndexKey IIndexKey.ResolveRuntimeParser(RuntimeParsersDescriptor? parsers)
        => _useRuntimeParser && parsers?.FindParser(KeyType) is { } parser
            ? new RegisteredKey(this, (ParseSpan<TKey>)parser.Span, parser.Registration)
            : this;

    private sealed class RegisteredKey(IndexKey<TKey> key, ParseSpan<TKey> parse, Delegate registration) : IIndexKey
    {
        public Type KeyType => key.KeyType;
        string? IIndexKey.ColumnName => key.ColumnName;
        int? IIndexKey.ColumnOrdinal => key.ColumnOrdinal;
        string IIndexKey.CompatibilityId => key.GetRegisteredCompatibilityId(registration);
        ulong IIndexKey.GetStableHash(object value) => ((IIndexKey)key).GetStableHash(value);
        string? IIndexKey.HashCompatibility => ((IIndexKey)key).HashCompatibility;
        string? IIndexKey.OrderingCompatibility => ((IIndexKey)key).OrderingCompatibility;
        object IIndexKey.Parse(ReadOnlySpan<char> text) => parse(text)!;
        int IIndexKey.Compare(object left, object right) => key._comparer.Compare((TKey)left, (TKey)right);
    }

    private string GetRegisteredCompatibilityId(Delegate registration)
        => SparseIndexSidecar.Fingerprint([
            "runtime-parser-v1", _compatibilityId, .. IndexDelegateIdentity.GetSettings(registration, "runtime parser")
        ]);
}

/// <summary>Defines a sparse index and the number of data records between checkpoints.</summary>
public sealed class SparseIndex
{
    public SparseIndex(IIndexKey key, int checkpointInterval)
        : this(key, checkpointInterval, requiresIndex: true)
    { }

    private SparseIndex(IIndexKey key, int checkpointInterval, bool requiresIndex)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(checkpointInterval);
        Key = key;
        CheckpointInterval = checkpointInterval;
        RequiresIndex = requiresIndex;
    }

    public IIndexKey Key { get; }
    public int CheckpointInterval { get; }
    internal bool RequiresIndex { get; }

    internal SparseIndex ResolveRuntimeParser(RuntimeParsersDescriptor? parsers)
    {
        var key = Key.ResolveRuntimeParser(parsers);
        return ReferenceEquals(key, Key) ? this : new(key, CheckpointInterval, RequiresIndex);
    }

    /// <summary>Requires a usable index for each source. This is the default.</summary>
    public SparseIndex WithRequiredIndex() => RequiresIndex ? this : new(Key, CheckpointInterval, requiresIndex: true);

    /// <summary>Scans a source when no index is available.</summary>
    public SparseIndex WithOptionalIndex() => RequiresIndex ? new(Key, CheckpointInterval, requiresIndex: false) : this;
}

using System.Globalization;

namespace PocketCsvReader;

/// <summary>Describes the column and typed rules used by an index.</summary>
public interface IIndexKey
{
    Type KeyType { get; }
    internal string? ColumnName { get; }
    internal int? ColumnOrdinal { get; }
    internal string CompatibilityId { get; }
    internal object Parse(ReadOnlySpan<char> text);
    internal int Compare(object left, object right);
}

/// <summary>Defines a key using library-supported parsing and comparison settings.</summary>
/// <remarks>Culture settings use standard culture data without user overrides.</remarks>
public sealed class IndexKey<TKey> : IIndexKey
    where TKey : ISpanParsable<TKey>
{
    public Type KeyType => typeof(TKey);
    public string? ColumnName { get; }
    public int? ColumnOrdinal { get; }

    private readonly ParseSpan<TKey> _parse;
    private readonly IComparer<TKey> _comparer;
    private readonly string _compatibilityId;

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
            var comparer = comparisonCulture is null ? StringComparer.FromComparison(effectiveComparison)
                : StringComparer.Create(comparisonCulture, effectiveComparison is
                    StringComparison.CurrentCultureIgnoreCase or StringComparison.InvariantCultureIgnoreCase);
            _comparer = (IComparer<TKey>)(object)comparer;
            _parse = text => (TKey)(object)text.ToString();
            _compatibilityId = SparseIndexSidecar.Fingerprint(
                [KeyType.FullName, effectiveComparison.ToString(), comparisonCulture?.Name,
                 comparisonCulture?.CompareInfo.Version.SortId.ToString(),
                 comparisonCulture?.CompareInfo.Version.FullVersion.ToString(CultureInfo.InvariantCulture)]);
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
            throw new NotSupportedException($"Sparse index rules for '{typeof(TKey)}' are not supported.");
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
    object IIndexKey.Parse(ReadOnlySpan<char> text) => _parse(text)!;
    int IIndexKey.Compare(object left, object right) => _comparer.Compare((TKey)left, (TKey)right);
}

/// <summary>Defines a sparse index and the number of data records between checkpoints.</summary>
public sealed class SparseIndex
{
    public SparseIndex(IIndexKey key, int checkpointInterval)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(checkpointInterval);
        Key = key;
        CheckpointInterval = checkpointInterval;
    }

    public IIndexKey Key { get; }
    public int CheckpointInterval { get; }
}

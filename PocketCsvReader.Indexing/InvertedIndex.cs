using System.Globalization;

namespace PocketCsvReader.Indexing;

/// <summary>Defines a CSV field, a term extractor, and the comparison rules for its inverted index.</summary>
public sealed class InvertedIndex
{
    /// <summary>Indexes string terms extracted from a string field using the field's comparison by default.</summary>
    public InvertedIndex(IndexKey<string> key, Func<string, IEnumerable<string>> extractTerms,
        Func<string, string>? normalizeTerm = null, StringComparison? termComparison = null, CultureInfo? termCulture = null)
        : this(key, CreateRules(key, extractTerms, normalizeTerm, termComparison, termCulture), requiresIndex: true)
    { }

    private InvertedIndex(IIndexKey key, InvertedTermRules termRules, bool requiresIndex)
        => (Key, TermRules, RequiresIndex) = (key, termRules, requiresIndex);

    public IIndexKey Key { get; }
    public Type TermType => TermRules.Key.KeyType;
    internal bool RequiresIndex { get; }
    internal InvertedTermRules TermRules { get; }

    /// <summary>Indexes typed terms extracted from a parsed field value.</summary>
    /// <remarks>When the field and term types match, the field comparison is used by default.</remarks>
    public static InvertedIndex Create<TField, TTerm>(IndexKey<TField> key, Func<TField, IEnumerable<TTerm>> extractTerms,
        Func<TTerm, TTerm>? normalizeTerm = null, StringComparison? termComparison = null, CultureInfo? termCulture = null)
        where TField : ISpanParsable<TField>
        where TTerm : ISpanParsable<TTerm>
        => new(key, CreateRules(key, extractTerms, normalizeTerm, termComparison, termCulture), requiresIndex: true);

    /// <summary>Requires a usable sidecar for each source. This is the default.</summary>
    public InvertedIndex WithRequiredIndex() => RequiresIndex ? this : new(Key, TermRules, requiresIndex: true);

    /// <summary>Scans sources whose sidecars are missing or whose reader configuration cannot use them.</summary>
    public InvertedIndex WithOptionalIndex() => RequiresIndex ? new(Key, TermRules, requiresIndex: false) : this;

    private static InvertedTermRules CreateRules<TField, TTerm>(IndexKey<TField> key,
        Func<TField, IEnumerable<TTerm>> extractTerms, Func<TTerm, TTerm>? normalizeTerm,
        StringComparison? termComparison, CultureInfo? termCulture)
        where TField : ISpanParsable<TField>
        where TTerm : ISpanParsable<TTerm>
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(extractTerms);
        IIndexKey termKey = typeof(TField) == typeof(TTerm) && termComparison is null && termCulture is null
            ? key : new IndexKey<TTerm>(0, culture: termCulture, comparison: termComparison);
        return new TypedTermRules<TField, TTerm>(termKey, extractTerms, normalizeTerm);
    }

    private sealed class TypedTermRules<TField, TTerm>(IIndexKey termKey, Func<TField, IEnumerable<TTerm>> extract,
        Func<TTerm, TTerm>? normalize) : InvertedTermRules(termKey)
    {
        internal override string CompatibilityId => SparseIndexSidecar.Fingerprint([
            Key.CompatibilityId, Key.OrderingCompatibility,
            SparseIndexSidecar.Fingerprint(IndexDelegateIdentity.GetSettings(extract, "term extractor")),
            normalize is null ? null : SparseIndexSidecar.Fingerprint(IndexDelegateIdentity.GetSettings(normalize, "term normalizer"))
        ]);

        internal override IEnumerable<object> Extract(object field)
        {
            var terms = extract((TField)field) ?? throw new InvalidOperationException("A term extractor must return a sequence.");
            foreach (var term in terms)
            {
                if (term is not null)
                    yield return Normalize(term);
            }
        }

        internal override object Normalize(object term)
        {
            if (normalize is null)
                return term;
            return normalize((TTerm)term) ?? throw new InvalidOperationException("A term normalizer must return a non-null term.");
        }
    }
}

internal abstract class InvertedTermRules(IIndexKey key)
{
    internal IIndexKey Key { get; } = key;
    internal abstract string CompatibilityId { get; }
    internal abstract IEnumerable<object> Extract(object field);
    internal abstract object Normalize(object term);
}

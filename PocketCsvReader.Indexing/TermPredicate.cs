namespace PocketCsvReader.Indexing;

/// <summary>Defines the terms to match against an inverted index.</summary>
public sealed class TermPredicate
{
    private readonly Type _termType;
    private readonly object[] _terms;
    private readonly bool _requireAll;

    private TermPredicate(Type termType, object[] terms, bool requireAll)
        => (_termType, _terms, _requireAll) = (termType, terms, requireAll);

    /// <summary>Matches a record containing at least one supplied term.</summary>
    public static TermPredicate ContainsAny<TTerm>(params TTerm[] terms) => Create(terms, requireAll: false);

    /// <summary>Matches a record containing every supplied term. An empty set matches no records.</summary>
    public static TermPredicate ContainsAll<TTerm>(params TTerm[] terms) => Create(terms, requireAll: true);

    internal TermLookup Bind(InvertedIndex index)
    {
        if (_termType != index.TermType)
            throw new ArgumentException($"The inverted index requires '{index.TermType}' terms.");

        var rules = index.TermRules;
        var terms = new SortedSet<object>(Comparer<object>.Create(rules.Key.Compare));
        foreach (var term in _terms)
            terms.Add(rules.Normalize(term));
        return new(rules, terms, _requireAll);
    }

    private static TermPredicate Create<TTerm>(TTerm[] terms, bool requireAll)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var snapshot = new object[terms.Length];
        for (int i = 0; i < terms.Length; i++)
            snapshot[i] = terms[i] ?? throw new ArgumentException("Lookup terms must be non-null.", nameof(terms));
        return new(typeof(TTerm), snapshot, requireAll);
    }
}

internal sealed class TermLookup(InvertedTermRules rules, SortedSet<object> terms, bool requireAll)
{
    internal SortedSet<object> Terms { get; } = terms;
    internal bool RequiresAll { get; } = requireAll;

    internal Func<object, bool> CreateMatcher()
    {
        if (Terms.Count == 0)
            return static _ => false;
        if (!RequiresAll || Terms.Count == 1)
        {
            Func<object, bool> contains = Terms.Contains;
            return field => rules.Extract(field).Any(contains);
        }

        var requested = Terms.ToArray();
        var matched = new bool[requested.Length];
        return field => ContainsAll(field, requested, matched);
    }

    private bool ContainsAll(object field, object[] requested, bool[] matched)
    {
        Array.Clear(matched);
        int remaining = matched.Length;
        foreach (var term in rules.Extract(field))
        {
            int index = Array.BinarySearch(requested, term, Terms.Comparer);
            if (index < 0 || matched[index])
                continue;
            matched[index] = true;
            if (--remaining == 0)
                return true;
        }
        return false;
    }
}

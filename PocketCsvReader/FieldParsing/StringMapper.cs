using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;
public class StringMapper
{
    protected PoolString FetchString { get; }
    private static readonly PoolString defaultPoolString = (span) => span.ToString();

    public StringMapper(PoolString? fetchString = null)
        => FetchString = fetchString ?? defaultPoolString;

    public string Map(ReadOnlySpan<char> buffer)
        => FetchString(buffer);
}

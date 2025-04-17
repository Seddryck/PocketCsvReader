using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;
public class RecordParser : BaseRecordParser<NdjsonProfile>
{
    public RecordParser(StreamReader reader, NdjsonProfile profile)
        : this(reader, profile, ArrayPool<char>.Shared)
    { }

    public RecordParser(StreamReader reader, NdjsonProfile profile, ArrayPool<char>? pool)
        : base(profile, new SingleBuffer(reader, 64*1024, pool), pool, (p) => new NdjsonParser(p.Dialect))
    { }

    protected RecordParser(NdjsonProfile profile, IBufferReader buffer, ArrayPool<char>? pool)
        : base(profile, buffer, pool, (p) => new NdjsonParser(p.Dialect))
    { }
}

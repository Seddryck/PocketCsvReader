using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;
public class BaseRawRecordTest
{
    private class StubRawRecord : BaseRawRecord<CsvProfile>
    {
        public StubRawRecord(CsvProfile profile)
            : base(profile, new StringMapper(profile.ParserOptimizations.PoolString))
        { }
        public override int FieldCount
            => throw new NotImplementedException();

        public override string GetRawString(int i)
            => throw new NotImplementedException();

        protected override NullableSpan GetValueOrThrow(int i)
            => throw new NotImplementedException();
    }

    [Test]
    public void GetDataTypeName_WithIndexedSchema_CorrectNames()
    {
        var schema = new SchemaDescriptorBuilder()
                            .Indexed()   
                            .WithNumberField<decimal>((f) => f.WithDataSourceTypeName("number"))
                            .WithField<string>((f) => f.WithDataSourceTypeName("string"))
                            .Build();
        var profile = new CsvProfile(new DialectDescriptor(), schema);
        var rawRecord = new StubRawRecord(profile);
        Assert.That(rawRecord.GetDataTypeName(0), Is.EqualTo("number"));
        Assert.That(rawRecord.GetDataTypeName(1), Is.EqualTo("string"));
    }
}

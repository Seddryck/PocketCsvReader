using PocketCsvReader;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using Moq;

namespace PocketCsvReader.Testing
{
    [TestFixture]
    public class StreamExtensionsTest
    {
        [Test]
        public void GetStreamEncoding_Financial_CorrectEncodingInfo()
        {
            using (var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.Utf8.csv")
                        ?? throw new FileNotFoundException()
            )
            {
                using var reader = new StreamReader(stream);
                for (int i = 0; i < 3; i++)
                    reader.Read();
                Assert.That(reader.BaseStream.Position, Is.GreaterThan(0));

                reader.Rewind();
                Assert.That(reader.BaseStream.Position, Is.EqualTo(0));
            }
        }
    }
}

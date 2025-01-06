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
    public class EncodingDetectorTest
    {
        [Test]
        [TestCase("Utf16-BE", 2)]
        [TestCase("Utf16-LE", 2)]
        [TestCase("Utf8-BOM", 3)]
        [TestCase("Utf8", 0)]
        public void GetStreamEncoding_Financial_CorrectEncodingInfo(string filename, int BomLength)
        {
            using (var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                        ?? throw new FileNotFoundException()
            )
            {
                var detector = new EncodingDetector();
                var result = detector.GetStreamEncoding(stream);
                Assert.That(result.Encoding.BodyName, Is.EqualTo(filename)!.Using(new EncodingComparer()));
                Assert.That(result.BomBytesCount, Is.EqualTo(BomLength));
            }
        }

        public class EncodingComparer : IEqualityComparer<string>
        {
            public bool Equals(string? x, string? y)
            {
                if (x is null || y is null)
                    return false;

                static string normalize(string s) => s.Replace("-LE", "").Replace("-BOM", "").ToLowerInvariant().Replace("-", "");
                return normalize(x) == normalize(y);
            }

            public int GetHashCode(string obj)
                => obj.ToLowerInvariant().Replace("-", "").GetHashCode();
        }

        private static readonly Encoding[] Encodings =
        {
            Encoding.Unicode,
            Encoding.BigEndianUnicode,
            Encoding.UTF8,
            Encoding.UTF32,
            new UTF32Encoding(true, true),
        };

        [TestCaseSource(nameof(Encodings))]
        public void GetStreamEncoding_Stream_CorrectResult(Encoding encoding)
        {
            using (var stream = new MemoryStream())
            {
                using var writer = new StreamWriter(stream, encoding);
                writer.Write("A,B,C\r\n1,2,3\r\n4,5,6\r\n");
                writer.Flush();
                stream.Position = 0;

                var detector = new EncodingDetector();
                var result = detector.GetStreamEncoding(stream);
                Assert.That(result.Encoding, Is.EqualTo(encoding));
            }
        }

        [TestCaseSource(nameof(Encodings))]
        public void FromMime_StreamWithoutMime_CorrectResult(Encoding encoding)
        {
            using (var stream = new MemoryStream())
            {
                using var writer = new StreamWriter(stream, encoding);
                writer.Write("A,B,C\r\n1,2,3\r\n4,5,6\r\n");
                writer.Flush();
                stream.Position = 0;

                var detector = new EncodingDetector();
                var result = detector.GetStreamEncoding(stream, encoding.BodyName);
                Assert.That(result.Encoding, Is.EqualTo(encoding));
            }
        }

        [TestCase("ISO-8859-2")]
        [TestCase("utf-8")]
        public void FromMime_StreamWithMime_CorrectResult(string mime)
        {
            if (!Encoding.GetEncodings().Any(e => e.Name.Equals(mime, StringComparison.OrdinalIgnoreCase)))
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            using (var stream = new MemoryStream())
            {
                using var writer = new StreamWriter(stream, Encoding.GetEncoding(mime));
                writer.Write("A,B,C\r\n1,2,3\r\n4,5,6\r\n");
                writer.Flush();
                stream.Position = 0;

                var detector = new EncodingDetector();
                var result = detector.GetStreamEncoding(stream, mime);
                Assert.That(result.Encoding.WebName, Is.EqualTo(mime).Using(StringComparer.OrdinalIgnoreCase));
            }
        }
    }
}

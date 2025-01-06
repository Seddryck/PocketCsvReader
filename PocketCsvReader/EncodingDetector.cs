using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public record EncodingInfo(Encoding Encoding, int BomBytesCount)
{ }

public interface IEncodingDetector
{
    EncodingInfo GetStreamEncoding(Stream stream, string? mime = null);
    EncodingInfo GetFileEncoding(string filename, string? mime = null);
}

public class EncodingDetector : IEncodingDetector
{
    /// <summary>
    /// Detects the byte order mark of a streams and returns
    /// an appropriate encoding for the file.
    /// </summary>
    /// <param name="stream">The stream to analyze for the encoding</param>
    /// <returns></returns>
    public virtual EncodingInfo GetStreamEncoding(Stream stream, string? mime = null)
    {
        if (stream == null || !stream.CanRead)
            throw new ArgumentException("The stream is null or not readable.");

        // Default  = Ansi CodePage
        var encoding = Encoding.Default;
        var encodingBytesCount = 0;

        // Detect byte order mark if any - otherwise assume default
        var buffer = new byte[5];
        var n = stream.Read(buffer, 0, 5);

        if (n < 2)
            return new(Encoding.UTF8, 0);

        if (mime is null)
        {
            foreach (var encodingInfo in Encoding.GetEncodings().OrderByDescending(e => e.GetEncoding().Preamble.Length))
            {
                var preamble = encodingInfo.GetEncoding().Preamble;
                if (preamble.Length > 0 && buffer.AsSpan(0, preamble.Length).SequenceEqual(preamble))
                {
                    encoding = encodingInfo.GetEncoding();
                    encodingBytesCount = preamble.Length;
                    break;
                }
            }
            // Fallback to UTF-8 if no BOM matches and it's not the default encoding
            if (encoding.Equals(Encoding.Default))
                (encoding, encodingBytesCount) = (Encoding.UTF8, 0);
        }
        else
        {
            if (!Encoding.GetEncodings().Any(e => e.Name.Equals(mime, StringComparison.OrdinalIgnoreCase)))
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(mime);
            encodingBytesCount = encoding.Preamble.Length > 0 && buffer.AsSpan(0, encoding.Preamble.Length).SequenceEqual(encoding.Preamble)
                                    ? encoding.Preamble.Length
                                    : 0;
        }
        return new(encoding, encodingBytesCount);
    }

    /// <summary>
    /// Detects the byte order mark of a file and returns
    /// an appropriate encoding for the file.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public virtual EncodingInfo GetFileEncoding(string filename, string? mime = null)
    {
        using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 8, false))
            return GetStreamEncoding(stream, mime);
    }
}

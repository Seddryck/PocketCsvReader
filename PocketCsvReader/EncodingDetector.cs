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
    EncodingInfo GetStreamEncoding(Stream stream);
    EncodingInfo GetFileEncoding(string filename);
}

public class EncodingDetector : IEncodingDetector
{ 
    /// <summary>
    /// Detects the byte order mark of a streams and returns
    /// an appropriate encoding for the file.
    /// </summary>
    /// <param name="stream">The stream to analyze for the encoding</param>
    /// <returns></returns>
    public virtual EncodingInfo GetStreamEncoding(Stream stream)
    {
        // Default  = Ansi CodePage
        var encoding = Encoding.Default;

        // Detect byte order mark if any - otherwise assume default
        var buffer = new byte[5];
        var n = stream.Read(buffer, 0, 5);

        if (n < 2)
            return new(Encoding.ASCII, 0);

        var encodingBytesCount = 0;

        if (buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf)
            (encoding, encodingBytesCount) = (Encoding.UTF8, 3);
        else if (buffer[0] == 0xff && buffer[1] == 0xfe && buffer[2] == 0 && buffer[3] == 0)
            (encoding, encodingBytesCount) = (Encoding.UTF32, 4);
        else if (buffer[0] == 0xff && buffer[1] == 0xfe)
            (encoding, encodingBytesCount) = (Encoding.Unicode, 2);
        else if (buffer[0] == 0xfe && buffer[1] == 0xff)
            (encoding, encodingBytesCount) = (Encoding.BigEndianUnicode, 2);
        else if (buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0xfe && buffer[3] == 0xff)
            (encoding, encodingBytesCount) = (Encoding.UTF32, 4);
        //else if (buffer[0] == 0x2b && buffer[1] == 0x2f && buffer[2] == 0x76)
        //    encoding = Encoding.UTF7;

        encoding = encoding.Equals(Encoding.Default) ? Encoding.UTF8 : encoding;
        return new(encoding, encodingBytesCount);
    }

    /// <summary>
    /// Detects the byte order mark of a file and returns
    /// an appropriate encoding for the file.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    public virtual EncodingInfo GetFileEncoding(string filename)
    {
        using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 8, false))
            return GetStreamEncoding(stream);
    }
}

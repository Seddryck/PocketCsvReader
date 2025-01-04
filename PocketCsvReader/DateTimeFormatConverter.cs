using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
internal class DateTimeFormatConverter
{
    // Mapping dictionary for C-style to .NET-style format specifiers
    private static readonly Dictionary<string, string> FormatMap = new()
    {
        { "%a", "ddd" },
        { "%A", "dddd" },
        { "%b", "MMM" },
        { "%B", "MMMM" },
        { "%c", "F" },
        { "%d", "dd" },
        { "%-d", "d" },
        { "%H", "HH" },
        { "%-H", "H" },
        { "%I", "hh" },
        { "%j", "ddd" }, // Day of year
        { "%m", "MM" },
        { "%-m", "M" },
        { "%M", "mm" },
        { "%-M", "m" },
        { "%p", "tt" },
        { "%S", "ss" },
        { "%-S", "s" },
        { "%U", "ww" }, // Week of year (Sunday-start)
        { "%w", "d" },  // Day of week (0-Sunday to 6)
        { "%W", "ww" }, // Week of year (Monday-start)
        { "%x", "d" },  // Date
        { "%X", "T" },  // Time
        { "%y", "yy" },
        { "%Y", "yyyy" },
        { "%z", "z00" },
        { "%:z", "zzz" },
        { "%%", "%" }  // Literal %
    };

    /// <summary>
    /// Transforms a C-style format string into a .NET-style format string.
    /// </summary>
    /// <param name="cStyleFormat">The C-style format string.</param>
    /// <returns>The equivalent .NET-style format string.</returns>
    public string Convert(string cStyleFormat)
    {
        if (string.IsNullOrEmpty(cStyleFormat))
            throw new ArgumentException("Format string cannot be null or empty.", nameof(cStyleFormat));

        foreach (var entry in FormatMap)
            cStyleFormat = cStyleFormat.Replace(entry.Key, entry.Value);

        return cStyleFormat;
    }
}

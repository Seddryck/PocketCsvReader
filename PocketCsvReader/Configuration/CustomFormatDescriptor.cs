using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
internal class CustomFormatDescriptor : ICultureFormatDescriptor
{
    private readonly string _pattern;
    private readonly IFormatProvider _formatProvider;

    public CustomFormatDescriptor(string pattern, IFormatProvider formatProvider)
    {
        _pattern = pattern;
        _formatProvider = formatProvider;
    }

    public string Pattern
        => _pattern;
        
    public IFormatProvider Culture
        => _formatProvider;
}

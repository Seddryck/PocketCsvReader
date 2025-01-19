using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
internal class CustomFormatDescriptor : ICultureFormatDescriptor
{
    private readonly IFormatProvider _formatProvider;

    public CustomFormatDescriptor(IFormatProvider formatProvider)
    {
        _formatProvider = formatProvider;
    }

    public IFormatProvider Culture
        => _formatProvider;
}

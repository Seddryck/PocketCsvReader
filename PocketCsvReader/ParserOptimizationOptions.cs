﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public record ParserOptimizationOptions
(
    bool NoTextQualifier = false,
    bool UnescapeChars = true,
    bool HandleSpecialValues = true,
    bool RowCountAtStart = false,
    bool ExtendIncompleteRecords = true,
    bool ReadAhead = true,
    int BufferSize = 4096,
    PoolString? PoolString = null,
    bool LookupTableChar = true
) { }

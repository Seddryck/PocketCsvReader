﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public record FieldDescriptor
(
    Type Type
    , string? Name = null
)
{ }
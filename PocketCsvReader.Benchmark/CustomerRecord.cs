using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Benchmark;
internal record CustomerRecord
(
    string Firstname,
    string Lastname,
    string Gender,
    DateTime DateOfBirth,
    int Year,
    string Month,
    decimal TotalOrder
)
{ }

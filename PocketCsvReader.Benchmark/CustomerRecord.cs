using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Benchmark;
internal class CustomerRecord
{
        public string Firstname { get; set; }
        public string Lastname { get; set; }
        public string Gender { get; set; }
        public DateTime DateOfBirth { get; set; }
        public int Year { get; set; }
        public string Month { get; set; }
        public decimal TotalOrder { get; set; }
}

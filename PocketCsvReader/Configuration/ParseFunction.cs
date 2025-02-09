using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public delegate object ParseFunction(string input);
public delegate T ParseFunction<T>(string input);

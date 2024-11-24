// See https://aka.ms/new-console-template for more information
using PocketCsvReader.Testing;

var test = new CsvDataReaderTest();
test.Read_TestData_Successful(40_000, false);

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace PocketCsvReader;
public class CsvDataTable
{
    protected RecordParser RecordParser { get; }
    protected Stream Stream { get; }
    protected int BufferSize { get; private set; } = 4 * 1024;

    public CsvDataTable(RecordParser recordParser, Stream stream)
    {
        RecordParser = recordParser;
        Stream = stream;
    }

    public DataTable CreateTable(string[] headers)
    {
        var table = new DataTable();
        foreach (var header in headers)
            table.Columns.Add(header);
        return table;
    }

    public DataTable Read()
    {
        int i;

        using (var reader = new StreamReader(Stream))
        {
            //Move and rewind to be sure that the BOM is not skipped by internal implementation of StreamReader
            var bufferBOM = new char[1];
            reader.Read(bufferBOM, 0, bufferBOM.Length);
            reader.Rewind();

            var (encoding, encodingBytesCount) = new EncodingDetector().GetStreamEncoding(Stream);
            reader.Rewind();

            var count = RecordParser.CountRecords(reader);
            if (count is not null)
                reader.Rewind(encodingBytesCount);

            Span<char> extra = stackalloc char[0];
            Span<char> buffer = stackalloc char[BufferSize];
            var headers = RecordParser.ReadHeader(reader, buffer, ref extra);
            var table = CreateTable(headers);
            if (!RecordParser.Profile.Descriptor.Header)
            {
                reader.Rewind(encodingBytesCount);
                extra = [];
            }
            
            bool isEof = false;
            i = 0;
            while (!isEof)
            {
                i++;
                buffer.Clear();
                var (fields, eof) = RecordParser.ReadNextRecord(reader, buffer, ref extra);
                isEof = eof;

                if (!(isEof && fields.Length == 0))
                {
                    var row = table.NewRow();
                    if (row.ItemArray.Length < fields.Length)
                        throw new InvalidDataException
                        (
                            string.Format
                            (
                        "The record {0} contains {1} more field{2} than expected."
                                , table.Rows.Count + 1 + Convert.ToInt32(RecordParser.Profile.Descriptor.Header)
                                , fields.Length - row.ItemArray.Length
                                , fields.Length - row.ItemArray.Length > 1 ? "s" : string.Empty
                            )
                        );

                    //fill the missing cells
                    if (row.ItemArray.Length > fields.Length)
                    {
                        var list = new List<string?>(fields);
                        while (row.ItemArray.Length > list.Count)
                            list.Add(RecordParser.Profile.MissingCell);
                        fields = [.. list];
                    }

                    row.ItemArray = fields.ToArray();
                    table.Rows.Add(row);
                }
            }

            return table;
        }
    }
}

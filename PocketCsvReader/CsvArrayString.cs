using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public class CsvArrayString : IDisposable
{
    protected CsvProfile Profile { get; }
    protected RecordParser? RecordParser { get; set; }
    protected Stream Stream { get; }
    protected StreamReader? StreamReader { get; private set; }
    protected Memory<char> buffer;

    protected EncodingInfo? EncodingInfo { get; private set; }

    protected bool IsEof { get; private set; } = false;
    public int RowCount { get; private set; } = 0;
    protected int BufferSize { get; private set; } = 4 * 1024;

    public string[]? Fields { get; private set; } = null;

    public CsvArrayString(Stream stream, CsvProfile profile)
    {
        Profile = profile;
        Stream = stream;
    }

    public void Initialize()
    {
        EncodingInfo ??= new EncodingDetector().GetStreamEncoding(Stream);
        StreamReader = new StreamReader(Stream, EncodingInfo!.Encoding, false);
        var bufferBOM = new char[1];
        StreamReader.Read(bufferBOM, 0, bufferBOM.Length);
        StreamReader.Rewind();

        if (EncodingInfo!.BomBytesCount > 0)
            StreamReader.BaseStream.Position = EncodingInfo!.BomBytesCount;

        IsEof = false;
        RowCount = 0;
        RecordParser = new RecordParser(StreamReader, Profile);
    }

    Memory<char> Extra = Memory<char>.Empty;
    public IEnumerable<string?[]> Read()
    {
        if (EncodingInfo is null)
            Initialize();

        while (!IsEof)
        {
            string?[]? values = ReadNextRecord();
            if (values is null)
                yield break;

            yield return values;
        }
    }

    private string?[]? ReadNextRecord()
    {
        if (IsEof)
            return null;

        IsEof = RecordParser!.ReadNextRecord(out var values);

        if (IsEof && (values is null || values.Length == 0))
        {
            values = null;
            Extra = null;
            return null;
        }

        if (RowCount == 0 && Fields is null)
        {
            int unnamedFieldIndex = 0;
            if (RecordParser.Profile.Descriptor.Header)
            {
                Fields = values!.Select(value => value ?? $"field_{unnamedFieldIndex++}").ToArray();
                return ReadNextRecord(); // Skip header and read next record
            }
            else
            {
                Fields = values!.Select(_ => $"field_{unnamedFieldIndex++}").ToArray();
            }
        }
        else
        {
            RowCount++;

            // Handle case with unexpected fields
            if ((Fields?.Length ?? int.MaxValue) < values!.Length)
                throw new InvalidDataException
                (
                    string.Format
                    (
                        "The record {0} contains {1} more field{2} than expected.",
                        RowCount + Convert.ToInt32(RecordParser.Profile.Descriptor.Header),
                        values.Length - Fields!.Length,
                        values.Length - Fields.Length > 1 ? "s" : string.Empty
                    )
                );

            // Fill the missing cells
            if ((Fields?.Length ?? 0) > values.Length)
            {
                var list = new List<string?>(values);
                while (Fields!.Length > list.Count)
                    list.Add(RecordParser.Profile.MissingCell);
                values = list.ToArray();
            }
        }

        return values;
    }

    public void Dispose()
    {
        StreamReader?.Dispose();
        Stream?.Dispose();
        GC.SuppressFinalize(this); // Prevents finalizer from running
    }

    ~CsvArrayString()
    {
        Dispose();
    }
}

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

    public IEnumerable<string?[]> Read()
    {
        var stringMapper = new SpanMapper<string?[]?>((span, fieldSpans) =>
        {
            if (!fieldSpans.Any())
                return null;
            var values = new string[fieldSpans.Count()];
            var index = 0;
            foreach (var fieldSpan in fieldSpans)
                values[index++] = span.Slice(fieldSpan.Start, fieldSpan.Length).ToString();
            return values;
        });

        if (EncodingInfo is null)
            Initialize();

        while (!IsEof)
        {
            if (RowCount == 0 && Profile.Dialect.Header)
                RegisterHeader(RecordParser!.ReadHeaders(), "field_");

            IsEof = RecordParser!.ReadNextRecord(out RecordSpan recordSpan);
            var values = stringMapper.Invoke(recordSpan.Span, recordSpan.FieldSpans);
            if (values is null)
                yield break;
            RowCount++;
            yield return values;
        }
    }

    private void RegisterHeader(string?[][] headers, string unamedPrefix)
    {
        var maxField = headers.Select(x => x.Length).Max();
        var names = (string[])Array.CreateInstance(typeof(string), maxField);

        foreach (var header in headers)
        {
            var last = string.Empty;
            for (int i = 0; i < maxField; i++)
            {
                if (i < header.Length && !string.IsNullOrEmpty(header[i]))
                    last = header[i];
                names[i] = string.IsNullOrEmpty(names[i])
                            ? $"{last}"
                            : $"{names[i]}{Profile.Dialect.HeaderJoin}{last}";
            }
        }
        int unnamedFieldIndex = 0;
        Fields = (RecordParser!.Profile.Dialect.Header
                ? names.Select(value => { unnamedFieldIndex++; return string.IsNullOrWhiteSpace(value) ? $"{unamedPrefix}{unnamedFieldIndex}" : value; })
                : names.Select(_ => $"{unamedPrefix}{unnamedFieldIndex++}")).ToArray();
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

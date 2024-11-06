using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PocketCsvReader
{
    public class CsvReader
    {
        public event ProgressStatusHandler? ProgressStatusChanged;

        protected internal CsvProfile Profile { get; private set; }
        protected int BufferSize { get; private set; }

        public CsvReader()
            : this(CsvProfile.CommaDoubleQuote, 4 * 1024)
        { }

        public CsvReader(CsvProfile profile)
            : this(profile, 4 * 1024)
        { }

        public CsvReader(int bufferSize)
            : this(CsvProfile.SemiColumnDoubleQuote, bufferSize)
        { }

        public CsvReader(CsvProfile profile, int bufferSize)
        {
            this.Profile = profile;
            BufferSize = bufferSize;
        }

        protected void RaiseProgressStatus(string status)
            => ProgressStatusChanged?.Invoke(this, new ProgressStatusEventArgs(status));

        protected void RaiseProgressStatus(string status, int current, int total)
            => ProgressStatusChanged?.Invoke(this, new ProgressStatusEventArgs(string.Format(status, current, total), current, total));

        /// <summary>
        /// Read the CSV file and returns the corresponding DataTable
        /// </summary>
        /// <param name="filename">Name of the CSV file</param>
        /// <returns>A DataTable containing all the records (rows) and fields (columns) available in the CSV file</returns>
        public DataTable ToDataTable(string filename)
        {
            CheckFileExists(filename);
            var (encoding, encodingBytesCount) = GetFileEncoding(filename);

            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.BufferSize))
                return Read(stream, encoding, encodingBytesCount);
        }

        /// <summary>
        /// Reads a CSV file from the specified stream and converts its contents into a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing the CSV data. The stream must be readable and positioned at the beginning of the CSV content.</param>
        /// <returns>A <see cref="DataTable"/> populated with rows and columns representing all records and fields in the CSV file.</returns>
        /// <remarks>
        /// This method reads the entire CSV content, assuming that each line represents a new row and each comma-separated value represents a field within that row.
        /// </remarks>
        public DataTable ToDataTable(Stream stream)
        {
            var (encoding, encodingBytesCount) = GetStreamEncoding(stream);

            return Read(stream, encoding, encodingBytesCount);
        }

        /// <summary>
        /// Reads the specified CSV file and returns an <see cref="IDataReader"/> for iterating over its records and fields.
        /// </summary>
        /// <param name="filename">The name or full path of the CSV file to read.</param>
        /// <returns>An <see cref="IDataReader"/> instance for sequentially reading each record and field in the CSV file.</returns>
        /// <remarks>
        /// This method provides an <see cref="IDataReader"/> for efficient, read-only, forward-only access to CSV data,
        /// suitable for large files or cases where full file loading into memory is unnecessary.
        /// </remarks>
        //public IDataReader ToDataReader(string filename)
        //{
        //    CheckFileExists(filename);
        //    var (encoding, encodingBytesCount) = GetFileEncoding(filename);

        //    using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.BufferSize))
        //        return Read(stream, encoding, encodingBytesCount);
        //}

        /// <summary>
        /// Reads the CSV data from the provided stream and returns an <see cref="IDataReader"/> for efficient record-by-record access.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing CSV data, positioned at the beginning of the content.</param>
        /// <returns>An <see cref="IDataReader"/> that allows sequential access to each record and field in the CSV file.</returns>
        /// <remarks>
        /// This method processes the CSV data from the stream and provides an <see cref="IDataReader"/> for forward-only, read-only access,
        /// ideal for handling large datasets without loading the entire file into memory at once.
        /// </remarks>
        public IDataReader ToDataReader(Stream stream)
        {
            var dataReader = new CsvDataReader(this, stream);
            return dataReader;
        }

        /// <summary>
        /// Read the CSV file, overriding the value of isFirstRowHeader defined in the profile.
        /// </summary>
        /// <param name="filename">Name of the CSV file</param>
        /// <param name="isFirstRowHeader">Overrides the value of isFirstRowHeader defined in the profile</param>
        /// <returns>A DataTable containing all the records (rows) and fields (columns) available in the CSV file</returns>
        public DataTable ToDataTable(string filename, bool isFirstRowHeader)
        {
            CheckFileExists(filename);
            var (encoding, encodingBytesCount) = GetFileEncoding(filename);
            Profile.Descriptor.Header = isFirstRowHeader;

            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
                return Read(stream, encoding, encodingBytesCount);
        }

        protected virtual void CheckFileExists(string filename)
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException($"The file {filename} was not found.", filename);
        }

        protected internal DataTable Read(Stream stream)
            => Read(stream, Encoding.UTF8, 0);

        protected internal DataTable Read(Stream stream, Encoding encoding, int encodingBytesCount)
        {
            RaiseProgressStatus("Starting to process the CSV file ...");
            int i;

            using (var reader = new StreamReader(stream, encoding, false))
            {
                //Move and rewind to be sure that the BOM is not skipped by internal implementation of StreamReader
                var bufferBOM = new char[1];
                reader.Read(bufferBOM, 0, bufferBOM.Length);
                Rewind(reader);

                var count = CountRecords(reader);
                Rewind(reader);
                var table = DefineFields(reader, encodingBytesCount);
                Rewind(reader);

                if (encodingBytesCount > 0)
                    reader.BaseStream.Position = encodingBytesCount;

                bool isEof = false;
                i = 0;
                Span<char> buffer = stackalloc char[BufferSize];
                Span<char> extra = stackalloc char[0];

                while (!isEof)
                {
                    if (count.HasValue)
                        RaiseProgressStatus($"Loading row {i} of {count} ...", i, count.Value);
                    else
                        RaiseProgressStatus($"Loading row {i}{(count.HasValue ? $" of {count}" : string.Empty)} ...");

                    i++;
                    buffer.Clear();
                    var (fields, eof) = ReadNextRecord(reader, buffer, ref extra);
                    isEof = eof;

                    if ((i != 1 || !Profile.Descriptor.Header) && !(isEof && fields.Length == 0))
                    {
                        var row = table.NewRow();
                        if (row.ItemArray.Length < fields.Length)
                            throw new InvalidDataException
                            (
                                string.Format
                                (
                                    "The record {0} contains {1} more field{2} than expected."
                                    , table.Rows.Count + 1 + Convert.ToInt32(Profile.Descriptor.Header)
                                    , fields.Length - row.ItemArray.Length
                                    , fields.Length - row.ItemArray.Length > 1 ? "s" : string.Empty
                                )
                            );

                        //fill the missing cells
                        if (row.ItemArray.Length > fields.Length)
                        {
                            var list = new List<string?>(fields);
                            while (row.ItemArray.Length > list.Count)
                                list.Add(Profile.MissingCell);
                            fields = [.. list];
                        }

                        row.ItemArray = fields.ToArray();
                        table.Rows.Add(row);
                    }

                }
                RaiseProgressStatus("CSV file fully processed.");

                return table;
            }
        }

        protected internal static void Rewind(StreamReader reader)
        {
            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();
        }

        protected internal virtual DataTable DefineFields(StreamReader reader, int encodingBytesCount)
        {
            //Get first record to know the count of fields
            RaiseProgressStatus("Defining fields");
            var columnCount = 0;
            var columnNames = new List<string>();
            var firstLine = GetFirstRecord(reader, Profile.Descriptor.LineTerminator, BufferSize);
            if (encodingBytesCount > 0)
                firstLine = firstLine.Substring(encodingBytesCount, firstLine.Length - encodingBytesCount);
            if (firstLine.EndsWith(Profile.Descriptor.LineTerminator))
                firstLine = firstLine.Substring(0, firstLine.Length - Profile.Descriptor.LineTerminator.Length);
            columnCount = firstLine.Split(Profile.Descriptor.Delimiter).Length;
            if (Profile.Descriptor.Header)
                columnNames.AddRange(GetFields(firstLine, Profile.Descriptor.Delimiter, Profile.Descriptor.QuoteChar, Profile.Descriptor.EscapeChar, string.Empty)!);

            //Correctly define the columns for the table
            var table = new DataTable();
            for (int c = 0; c < columnCount; c++)
            {
                if (columnNames.Count == 0)
                    table.Columns.Add(string.Format("No name {0}", c.ToString()), typeof(string));
                else
                    table.Columns.Add(columnNames[c], typeof(string));
            }
            RaiseProgressStatus($"{table.Columns.Count} field{(table.Columns.Count > 1 ? "s were" : " was")}  identified.");


            return table;
        }

        /// <summary>
        /// Detects the byte order mark of a streams and returns
        /// an appropriate encoding for the file.
        /// </summary>
        /// <param name="stream">The stream to analyze for the encoding</param>
        /// <returns></returns>
        protected internal virtual (Encoding, int) GetStreamEncoding(Stream stream)
        {
            // Default  = Ansi CodePage
            var encoding = Encoding.Default;

            // Detect byte order mark if any - otherwise assume default
            var buffer = new byte[5];
            var n = stream.Read(buffer, 0, 5);

            if (n < 2)
                return (Encoding.ASCII, 0);

            var encodingBytesCount = 0;

            if (buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf)
                (encoding, encodingBytesCount) = (Encoding.UTF8, 3);
            else if (buffer[0] == 0xff && buffer[1] == 0xfe)
                (encoding, encodingBytesCount) = (Encoding.Unicode, 2);
            else if (buffer[0] == 0xfe && buffer[1] == 0xff)
                (encoding, encodingBytesCount) = (Encoding.BigEndianUnicode, 2);
            else if (buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0xfe && buffer[3] == 0xff)
                (encoding, encodingBytesCount) = (Encoding.UTF32, 4);
            //else if (buffer[0] == 0x2b && buffer[1] == 0x2f && buffer[2] == 0x76)
            //    encoding = Encoding.UTF7;

            encoding = encoding.Equals(Encoding.Default) ? Encoding.UTF8 : encoding;
            RaiseProgressStatus($"Encoding bytes was set to {encoding}{(encodingBytesCount > 0 ? $"and {encodingBytesCount} byte is used by the BOM" : string.Empty)}.");
            return (encoding, encodingBytesCount);
        }

        /// <summary>
        /// Detects the byte order mark of a file and returns
        /// an appropriate encoding for the file.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        protected virtual (Encoding, int) GetFileEncoding(string filename)
        {
            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 8, false))
                return GetStreamEncoding(stream);
        }

        protected virtual int? CountRecords(StreamReader reader)
        {
            if (Profile.PerformanceOptmized)
                return null;

            RaiseProgressStatus("Counting records ...");
            var count = CountRecordSeparators(reader);
            count -= Convert.ToInt16(Profile.Descriptor.Header);
            RaiseProgressStatus($"{count} record{(count > 1 ? "s were" : " was")} identified.");

            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();
            return count;
        }

        protected virtual int CountRecordSeparators(StreamReader reader)
        {
            int i = 0;
            int n = 0;
            int j = 0;
            bool separatorAtEnd = false;
            bool isCommentLine = false;
            bool isFirstCharOfLine = true;

            do
            {
                char[] buffer = new char[BufferSize];
                n = reader.Read(buffer, 0, BufferSize);
                if (n > 0 && i == 0)
                    i = 1;


                foreach (var c in buffer)
                {
                    if (c != '\0')
                    {
                        if (c == Profile.Descriptor.CommentChar && isFirstCharOfLine)
                            isCommentLine = true;
                        isFirstCharOfLine = false;

                        separatorAtEnd = false;
                        if (c == Profile.Descriptor.LineTerminator[j])
                        {
                            j++;
                            if (j == Profile.Descriptor.LineTerminator.Length)
                            {
                                if (!isCommentLine)
                                    i++;
                                j = 0;
                                separatorAtEnd = true;
                                isCommentLine = false;
                                isFirstCharOfLine = true;
                            }
                        }
                        else
                            j = 0;
                    }
                }
            } while (n > 0);

            if (separatorAtEnd)
                i -= 1;

            if (isCommentLine)
                i -= 1;

            return i;
        }

        protected virtual string?[] GetFields(ReadOnlySpan<char> record, char fieldSeparator, char textQualifier, char escapeTextQualifier, string emptyCell)
        {
            var fields = new List<string?>();
            var fieldStart = 0;
            var startsByTextQualifier = false;
            var endsByTextQualifier = false;
            var isEscaped = false;

            for (var fieldPos = 0; fieldPos < record.Length; fieldPos++)
            {
                if (fieldPos == fieldStart && record[fieldPos] == textQualifier)
                    startsByTextQualifier = true;
                else if (record[fieldPos] == textQualifier && !isEscaped)
                    endsByTextQualifier = true;
                else if (record[fieldPos] != fieldSeparator)
                    endsByTextQualifier = false;

                if (fieldPos == record.Length - 1
                        || (record[fieldPos] == fieldSeparator
                                && startsByTextQualifier == endsByTextQualifier)
                        )
                {

                    if (fieldPos == record.Length - 1 && record[fieldPos] != fieldSeparator)
                        fieldPos += 1;

                    var field = startsByTextQualifier
                                    ? record.Slice(fieldStart + 1, fieldPos - fieldStart - 2)
                                    : record.Slice(fieldStart, fieldPos - fieldStart);

                    if (field.Length == 0)
                        fields.Add(emptyCell);
                    else if (field.ToString() == "(null)")
                        fields.Add(null);
                    else if (field.Contains(escapeTextQualifier))
                    {
                        var candidate = field.ToString();
                        CheckTextQualifierEscapation(candidate, textQualifier, escapeTextQualifier);
                        fields.Add(candidate.Replace(new string(new[] { escapeTextQualifier, textQualifier }), textQualifier.ToString()));
                    }
                    else
                        fields.Add(field.ToString());
                    fieldStart = fieldPos + 1;
                    startsByTextQualifier = false;
                    endsByTextQualifier = false;
                }

                if (fieldPos < record.Length && record[fieldPos] == escapeTextQualifier && fieldPos != fieldStart)
                    isEscaped = true;
                else
                    isEscaped = false;
            }
            return [.. fields];
        }


        private static void CheckTextQualifierEscapation(string value, char textQualifier, char escapeTextQualifier)
        {
            if (string.IsNullOrEmpty(value))
                return;

            if (!value.Contains(textQualifier))
                return;

            var indexes = new List<int>();
            int j = -1;
            do
            {
                j = value.IndexOf(textQualifier, j + 1);
                if (j != -1)
                    indexes.Add(j);

            } while (j != -1 && j < value.Length - 1);

            if (textQualifier == escapeTextQualifier)
            {
                if (indexes.Count() == 1)
                    throw new InvalidDataException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {indexes[0]}");

                var i = 1;
                while (i < indexes.Count())
                {
                    if ((i + 1) % 2 == 0)
                    {
                        if (indexes[i - 1] != indexes[i] - 1)
                            throw new InvalidDataException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {i}");
                    }
                    else if (i == indexes.Count - 1 || indexes[i + 1] != indexes[i] + 1)
                        throw new InvalidDataException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {i}");
                    i += 1;
                }
            }
            else
                foreach (var index in indexes)
                    if (index == 0 || value[index - 1] != escapeTextQualifier)
                        throw new ArgumentException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {index}");
        }

        protected virtual string GetFirstRecord(StreamReader reader, string recordSeparator, int bufferSize)
        {
            var stringBuilder = new StringBuilder();
            int j = 0;

            while (true)
            {
                char[] buffer = new char[bufferSize];
                reader.Read(buffer, 0, bufferSize);

                foreach (var c in buffer)
                {

                    if (c != '\0')
                    {
                        stringBuilder.Append(c);
                        if (c == recordSeparator[j])
                        {
                            j++;
                            if (j == recordSeparator.Length)
                                return stringBuilder.ToString();
                        }
                        else
                            j = 0;
                    }
                    else
                        return stringBuilder.ToString();
                }
            }
        }

        private static ReadOnlySpan<char> Prepend(string prefix, ReadOnlySpan<char> value)
        {
            Span<char> buffer = new char[prefix.Length + value.Length];
            prefix.AsSpan().CopyTo(buffer);
            value.CopyTo(buffer.Slice(prefix.Length));
            return buffer;
        }
        protected virtual (string?[], bool) ReadNextRecord(Span<char> buffer)
        {
            Span<char> extra = buffer;
            return ReadNextRecord(null, buffer, ref extra);
        }

        protected internal virtual (string?[], bool) ReadNextRecord(StreamReader? reader, Span<char> buffer, ref Span<char> extra)
        {
            var bufferSize = 0;
            var index = 0;
            var eof = false;
            var isFirstCharOfRecord = true;
            var indexRecordSeparator = 0;
            var isFirstCharOfField = true;
            var fields = new List<string?>();
            var indexFieldStart = 0;
            var isCommentLine = false;
            var isFieldWithTextQualifier = false;
            var isEndingByTextQualifier = false;
            var isTextQualifierEscaped = false;
            Span<char> longField = stackalloc char[0];
            var longFieldIndex = 0;
            var isLastCharDelimiter = false;

            if (extra.Length > 0)
            {
                extra.CopyTo(buffer);
                bufferSize = extra.Length;
            }
            else
            {
                bufferSize = reader?.ReadBlock(buffer) ?? throw new ArgumentNullException(nameof(reader));
                eof = bufferSize == 0;
            }

            while (!eof && index < bufferSize)
            {
                char c = buffer[index];
                if (c == '\0')
                {
                    eof = true;
                    break;
                }

                if (isFirstCharOfRecord)
                {
                    isCommentLine = c == Profile.Descriptor.CommentChar;
                    isFirstCharOfRecord = false;
                }

                if (isFirstCharOfField)
                {
                    isFieldWithTextQualifier = c == Profile.Descriptor.QuoteChar;
                    isFirstCharOfField = false;
                    isEndingByTextQualifier = false;
                    isTextQualifierEscaped = false;
                }
                else if (c != Profile.Descriptor.Delimiter && c != Profile.Descriptor.LineTerminator[indexRecordSeparator] && !isFirstCharOfField)
                {
                    isEndingByTextQualifier = c == Profile.Descriptor.QuoteChar && !isTextQualifierEscaped;
                    isTextQualifierEscaped = c == Profile.Descriptor.EscapeChar && !isTextQualifierEscaped;
                }

                if (c == Profile.Descriptor.Delimiter && !isCommentLine && (isFieldWithTextQualifier == isEndingByTextQualifier))
                {
                    if (longFieldIndex == 0)
                        fields.Add(ReadField(buffer, indexFieldStart, index, isFieldWithTextQualifier, isEndingByTextQualifier));
                    else
                    {
                        fields.Add(ReadField(longField, longFieldIndex, buffer, index, isFieldWithTextQualifier, isEndingByTextQualifier));
                        longField = ArrayPool<char>.Shared.Rent(0);
                        longFieldIndex = 0;
                    }
                    isFirstCharOfField = true;
                    indexFieldStart = index + 1;
                }

                if (c == Profile.Descriptor.LineTerminator[indexRecordSeparator])
                {
                    indexRecordSeparator++;
                    if (indexRecordSeparator == Profile.Descriptor.LineTerminator.Length)
                    {
                        if (!isCommentLine)
                        {
                            if (indexFieldStart <= index + longFieldIndex - Profile.Descriptor.LineTerminator.Length)
                            {
                                if (longFieldIndex == 0)
                                    fields.Add(ReadField(buffer, indexFieldStart, index - Profile.Descriptor.LineTerminator.Length + 1, isFieldWithTextQualifier, isEndingByTextQualifier));
                                else
                                {
                                    fields.Add(ReadField(longField, longFieldIndex, buffer, index - Profile.Descriptor.LineTerminator.Length + 1, isFieldWithTextQualifier, isEndingByTextQualifier));
                                    longField = ArrayPool<char>.Shared.Rent(0);
                                    longFieldIndex = 0;
                                }
                            }

                            extra = ArrayPool<char>.Shared.Rent(bufferSize - index - 1);
                            extra = extra.Slice(0, bufferSize - index - 1);
                            buffer.Slice(index + 1, bufferSize - index - 1).CopyTo(extra);
                            buffer.Clear();
                            return (fields.ToArray(), false);
                        }
                        else
                        {
                            bufferSize = bufferSize - index;
                            buffer = buffer.Slice(index + 1);
                            isCommentLine = false;
                            index = -1;
                            indexFieldStart = 0;
                        }
                        isFirstCharOfRecord = true;
                        isFirstCharOfField = true;
                        indexRecordSeparator = 0;
                        isFieldWithTextQualifier = false;
                        isEndingByTextQualifier = false;
                    }
                }
                else
                    indexRecordSeparator = 0;



                if (++index == bufferSize)
                {
                    if (longField.Length >= longFieldIndex + index - indexFieldStart)
                    {
                        buffer.Slice(indexFieldStart, index - indexFieldStart).CopyTo(longField.Slice(longFieldIndex));
                    }
                    else
                    {
                        var newArray = ArrayPool<char>.Shared.Rent(longFieldIndex + index - indexFieldStart);
                        longField.CopyTo(newArray);
                        buffer.Slice(indexFieldStart, index - indexFieldStart).ToArray().CopyTo(newArray, longFieldIndex);
                        longField = newArray;
                    }

                    longFieldIndex += index - indexFieldStart;
                    indexFieldStart = 0;
                    bufferSize = reader?.ReadBlock(buffer) ?? throw new ArgumentNullException(nameof(reader));
                    eof = bufferSize == 0;
                    index = 0;
                    if (eof)
                        isLastCharDelimiter = true;
                }
            }

            if (eof && (index != indexFieldStart || longFieldIndex > 0 || isLastCharDelimiter) && !isCommentLine)
                if (longFieldIndex == 0)
                    if (isLastCharDelimiter)
                        fields.Add(Profile.EmptyCell);
                    else
                        fields.Add(ReadField(buffer, indexFieldStart, index, isFieldWithTextQualifier, isEndingByTextQualifier));
                else
                    fields.Add(ReadField(longField, longFieldIndex, buffer, index, isFieldWithTextQualifier, isEndingByTextQualifier));

            return (fields.ToArray(), eof);
        }

        protected internal string? ReadField(Span<char> longField, int longFieldIndex, ReadOnlySpan<char> buffer, int currentIndex, bool isFieldWithTextQualifier, bool isFieldEndingByTextQualifier)
        {
            if (longField.Length >= longFieldIndex + currentIndex)
            {
                buffer.Slice(0, currentIndex + 1).CopyTo(longField.Slice(longFieldIndex));
            }
            else
            {
                var newArray = ArrayPool<char>.Shared.Rent(longFieldIndex + currentIndex);
                longField.CopyTo(newArray);
                buffer.Slice(0, currentIndex).ToArray().CopyTo(newArray, longFieldIndex);
                longField = newArray;
            }
            return ReadField(longField, 0, longFieldIndex + currentIndex, isFieldWithTextQualifier, isFieldEndingByTextQualifier);
        }

        protected internal string? ReadField(ReadOnlySpan<char> buffer, int indexFieldStart, int currentIndex, bool isFieldWithTextQualifier, bool isFieldEndingByTextQualifier)
        {
            if (isFieldWithTextQualifier != isFieldEndingByTextQualifier)
                if (isFieldWithTextQualifier)
                    throw new InvalidDataException($"the token {buffer.Slice(indexFieldStart, currentIndex - indexFieldStart)} is starting by a text-qualifier but not ending by a text-qualifier.");
                else
                    throw new InvalidDataException($"the token {buffer.Slice(indexFieldStart, currentIndex - indexFieldStart)} is ending by a text-qualifier but not starting by a text-qualifier.");

            var field = isFieldWithTextQualifier
                            ? buffer.Slice(indexFieldStart + 1, currentIndex - indexFieldStart - 2)
                            : buffer.Slice(indexFieldStart, currentIndex - indexFieldStart);

            if (field.Length == 0)
                return Profile.EmptyCell;
            else if (field.ToString() == "(null)" && !isFieldWithTextQualifier)
                return null;
            else if (field.Contains(Profile.Descriptor.EscapeChar))
            {
                var candidate = field.ToString();
                CheckTextQualifierEscapation(candidate, Profile.Descriptor.QuoteChar, Profile.Descriptor.EscapeChar);
                return candidate.Replace(new string(new[] { Profile.Descriptor.EscapeChar, Profile.Descriptor.QuoteChar }), Profile.Descriptor.QuoteChar.ToString());
            }
            else
                return field.ToString();
        }
    }
}

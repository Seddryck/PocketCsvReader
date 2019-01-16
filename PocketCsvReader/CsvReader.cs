﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace PocketCsvReader
{
    public class CsvReader
    {
        public event ProgressStatusHandler ProgressStatusChanged;

        protected CsvProfile Profile { get; private set; }
        protected int BufferSize { get; private set; }

        public CsvReader()
            : this(CsvProfile.CommaDoubleQuote, 512)
        {
        }

        public CsvReader(CsvProfile profile)
            : this(profile, 512)
        {
        }

        public CsvReader(int bufferSize)
            : this(CsvProfile.SemiColumnDoubleQuote, bufferSize)
        {
        }

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
            var encoding = GetFileEncoding(filename, out var encodingBytesCount);

            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
                return Read(stream, encoding, encodingBytesCount, Profile.FirstRowHeader, Profile.RecordSeparator, Profile.FieldSeparator, Profile.TextQualifier, Profile.EscapeTextQualifier, Profile.EmptyCell, Profile.MissingCell);
        }

        /// <summary>
        /// Read the CSV file and returns the corresponding DataTable
        /// </summary>
        /// <param name="filename">Name of the CSV file</param>
        /// <returns>A DataTable containing all the records (rows) and fields (columns) available in the CSV file</returns>
        public DataTable ToDataTable(Stream stream)
        {
            var encoding = GetStreamEncoding(stream, out var encodingBytesCount);

            return Read(stream, encoding, encodingBytesCount, Profile.FirstRowHeader, Profile.RecordSeparator, Profile.FieldSeparator, Profile.TextQualifier, Profile.EscapeTextQualifier, Profile.EmptyCell, Profile.MissingCell);
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
            var encoding = GetFileEncoding(filename, out var encodingBytesCount);

            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
                return Read(stream, encoding, encodingBytesCount, isFirstRowHeader, Profile.RecordSeparator, Profile.FieldSeparator, Profile.TextQualifier, Profile.EscapeTextQualifier, Profile.EmptyCell, Profile.MissingCell);
        }

        protected virtual void CheckFileExists(string filename)
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException($"The file {filename} was not found.", filename);
        }

        protected internal DataTable Read(Stream stream)
            => this.Read(stream, Encoding.UTF8, 0, Profile.FirstRowHeader, Profile.RecordSeparator, Profile.FieldSeparator, Profile.TextQualifier, Profile.EscapeTextQualifier, Profile.EmptyCell, Profile.MissingCell);

        protected internal DataTable Read(Stream stream, Encoding encoding, int encodingBytesCount, bool isFirstRowHeader, string recordSeparator, char fieldSeparator, char textQualifier, char escapeTextQualifier, string emptyCell, string missingCell)
        {
            RaiseProgressStatus("Starting to process the CSV file ...");
            int i = 0;

            using (var reader = new StreamReader(stream, encoding, false))
            {
                //Move and rewind to be sure that the BOM is not skipped by internal implementation of StreamReader
                var buffer = new char[1];
                reader.Read(buffer, 0, buffer.Length);
                Rewind(reader);

                var count = CountRecords(reader, Profile.RecordSeparator, isFirstRowHeader, Profile.PerformanceOptmized);
                Rewind(reader);
                var table = DefineFields(reader, recordSeparator, fieldSeparator, textQualifier, escapeTextQualifier, isFirstRowHeader, encodingBytesCount);
                Rewind(reader);

                bool isLastRecord = false;
                i = 0;
                var alreadyRead = string.Empty;
                var extraRead = string.Empty;

                while (!isLastRecord)
                {
                    if (count.HasValue)
                        RaiseProgressStatus($"Loading row {i} of {count} ...", i, count.Value);
                    else
                        RaiseProgressStatus($"Loading row {i}{(count.HasValue ? $" of {count}" : string.Empty)} ...");

                    var records = GetNextRecords(reader, recordSeparator, BufferSize, alreadyRead, out extraRead);
                    foreach (var record in records)
                    {
                        var recordToParse = record;

                        if (i == 0 && encodingBytesCount > 0)
                            recordToParse = recordToParse.Substring(encodingBytesCount, recordToParse.Length - encodingBytesCount);

                        i++;
                        if (i != 1 || !isFirstRowHeader)
                        {
                            isLastRecord = IsLastRecord(recordToParse);
                            var cleanRecord = CleanRecord(recordToParse, recordSeparator);
                            var cells = SplitLine(cleanRecord, fieldSeparator, textQualifier, escapeTextQualifier, emptyCell).ToList();
                            var row = table.NewRow();
                            if (row.ItemArray.Length < cells.Count)
                                throw new InvalidDataException
                                (
                                    string.Format
                                    (
                                        "The record {0} contains {1} more field{2} than expected."
                                        , table.Rows.Count + 1 + Convert.ToInt32(isFirstRowHeader)
                                        , cells.Count - row.ItemArray.Length
                                        , cells.Count - row.ItemArray.Length > 1 ? "s" : string.Empty
                                    )
                                );

                            //fill the missing cells
                            while (row.ItemArray.Length > cells.Count)
                                cells.Add(missingCell);

                            row.ItemArray = cells.ToArray();
                            table.Rows.Add(row);
                        }
                    }
                    alreadyRead = extraRead;
                    isLastRecord |= records.Count() == 0;
                }
                RaiseProgressStatus("CSV file fully processed.");

                return table;
            }
        }

        private static void Rewind(StreamReader reader)
        {
            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();
        }

        protected virtual DataTable DefineFields(StreamReader reader, string recordSeparator, char fieldSeparator, char textQualifier, char escapeTextQualifier, bool isFirstRowHeader, int encodingBytesCount)
        {
            //Get first record to know the count of fields
            RaiseProgressStatus("Defining fields");
            var columnCount = 0;
            var columnNames = new List<string>();
            var firstLine = GetFirstRecord(reader, recordSeparator, BufferSize);
            if (encodingBytesCount > 0)
                firstLine = firstLine.Substring(encodingBytesCount, firstLine.Length - encodingBytesCount);
            if (firstLine.EndsWith(recordSeparator))
                firstLine = firstLine.Substring(0, firstLine.Length - recordSeparator.Length);
            columnCount = firstLine.Split(fieldSeparator).Length;
            if (isFirstRowHeader)
                columnNames.AddRange(SplitLine(firstLine, fieldSeparator, textQualifier, escapeTextQualifier, string.Empty));


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

        protected virtual int CalculateEncodingBytes(StreamReader reader)
        {
            //Check if the first byte is BOM or not
            var buffer = new char[4];
            reader.Read(buffer, 0, 4);
            var encodingBytesCount = (new string(buffer)[0] == 65279) ? 1 : 0;
            RaiseProgressStatus($"Encoding bytes was set to {encodingBytesCount}.");

            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();
            return encodingBytesCount;
        }

        /// <summary>
        /// Detects the byte order mark of a streams and returns
        /// an appropriate encoding for the file.
        /// </summary>
        /// <param name="stream">The stream to analyze for the encoding</param>
        /// <returns></returns>
        protected virtual Encoding GetStreamEncoding(Stream stream, out int encodingBytesCount)
        {
            // Default  = Ansi CodePage
            var encoding = Encoding.Default;

            // Detect byte order mark if any - otherwise assume default
            var buffer = new byte[5];
            stream.Read(buffer, 0, 5);

            if (buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf)
                encoding = Encoding.UTF8;
            else if (buffer[0] == 0xff && buffer[1] == 0xfe)
                encoding = Encoding.Unicode;
            else if (buffer[0] == 0xfe && buffer[1] == 0xff)
                encoding = Encoding.BigEndianUnicode;
            else if (buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0xfe && buffer[3] == 0xff)
                encoding = Encoding.UTF32;
            else if (buffer[0] == 0x2b && buffer[1] == 0x2f && buffer[2] == 0x76)
                encoding = Encoding.UTF7;

            encodingBytesCount = Convert.ToInt32(encoding != Encoding.Default);
            encoding = encoding == Encoding.Default ? Encoding.UTF8 : encoding;

            return encoding;
        }

        /// <summary>
        /// Detects the byte order mark of a file and returns
        /// an appropriate encoding for the file.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        protected virtual Encoding GetFileEncoding(string filename, out int encodingBytesCount)
        {
            using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                return GetStreamEncoding(stream, out encodingBytesCount);
        }

        protected virtual int? CountRecords(StreamReader reader, string recordSeparator, bool isFirstRowHeader, bool isPerformanceOptimized)
        {
            if (isPerformanceOptimized)
                return null;

            RaiseProgressStatus("Counting records ...");
            var count = CountRecordSeparators(reader, recordSeparator, BufferSize);
            count -= Convert.ToInt16(isFirstRowHeader);
            RaiseProgressStatus($"{count} record{(count > 1 ? "s were" : " was")} identified.");

            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();
            return count;
        }

        protected virtual int CountRecordSeparators(StreamReader reader, string recordSeparator, int bufferSize)
        {
            int i = 0;
            int n = 0;
            int j = 0;
            bool separatorAtEnd = false;

            do
            {
                char[] buffer = new char[bufferSize];
                n = reader.Read(buffer, 0, bufferSize);
                if (n > 0 && i == 0)
                    i = 1;


                foreach (var c in buffer)
                {
                    if (c != '\0')
                    {
                        separatorAtEnd = false;
                        if (c == recordSeparator[j])
                        {
                            j++;
                            if (j == recordSeparator.Length)
                            {
                                i++;
                                j = 0;
                                separatorAtEnd = true;
                            }
                        }
                        else
                            j = 0;
                    }
                }
            } while (n > 0);

            if (separatorAtEnd)
                i -= 1;

            return i;
        }

        protected virtual IEnumerable<string> SplitLine(string row, char fieldSeparator, char textQualifier, char escapeTextQualifier, string emptyCell)
        {
            var tokens = new List<string>(row.Split(fieldSeparator));

            var startByTextQualifier = false;
            var compositeToken = new StringBuilder();

            foreach (var token in tokens)
            {
                var endByTextQualifier = false;
                if (string.IsNullOrEmpty(token))
                {
                    if (!startByTextQualifier)
                        yield return token == null ? null : emptyCell;
                    else
                        compositeToken.Append(fieldSeparator);
                }
                else
                {
                    startByTextQualifier |= token[0] == textQualifier;
                    endByTextQualifier = token[token.Length - 1] == textQualifier && token.Length != 1;
                    if (endByTextQualifier)
                        endByTextQualifier = new string(token.Reverse().Take(2).ToArray()) != new string(new[] { textQualifier, escapeTextQualifier });
                    compositeToken.Append(token);

                    if (startByTextQualifier && endByTextQualifier || (!startByTextQualifier && !endByTextQualifier))
                    {
                        startByTextQualifier = false;
                        var value = RemoveTextQualifier(compositeToken.ToString(), textQualifier, escapeTextQualifier);
                        compositeToken.Clear();
                        if (string.IsNullOrEmpty(value))
                            yield return value == null ? null : emptyCell;
                        else
                            yield return value;
                    }
                    else
                        compositeToken.Append(fieldSeparator);
                }
            }
        }

        protected virtual string RemoveTextQualifier(string item, char textQualifier, char escapeTextQualifier)
        {
            var escapeToken = new string(new[] { escapeTextQualifier, textQualifier });

            if (string.IsNullOrEmpty(item))
                return string.Empty;

            if (item == "(null)")
                return null;

            if (item.Length == 1)
                return item;

            if (item == escapeToken)
                return string.Empty;

            if (item[0] == textQualifier && item[item.Length - 1] == textQualifier)
            {
                var candidate = item.Substring(1, item.Length - 2);
                CheckTextQualifierEscapation(candidate, textQualifier, escapeTextQualifier);
                return candidate.Replace(escapeToken, textQualifier.ToString());
            }

            return item;
        }

        private void CheckTextQualifierEscapation(string value, char textQualifier, char escapeTextQualifier)
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
                    throw new ArgumentException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {indexes[0]}");

                var i = 1;
                while (i < indexes.Count())
                {
                    if ((i + 1) % 2 == 0)
                    {
                        if (indexes[i - 1] != indexes[i] - 1)
                            throw new ArgumentException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {i}");
                    }
                    else if (i == indexes.Count - 1 || indexes[i + 1] != indexes[i] + 1)
                        throw new ArgumentException($"the token {value} contains a text-qualifier not preceded by a an escape-text-qualifier at the position {i}");
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
            int n = 0;
            int j = 0;

            while (true)
            {
                char[] buffer = new char[bufferSize];
                n = reader.Read(buffer, 0, bufferSize);

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

        protected virtual IEnumerable<string> GetNextRecords(StreamReader reader, string recordSeparator, int bufferSize, string alreadyRead, out string extraRead)
        {
            int n = 0;
            int j = 0;
            var stringBuilder = new StringBuilder();
            var records = new List<string>();
            var eof = false;

            extraRead = string.Empty;
            stringBuilder.Append(alreadyRead);
            j = IdentifyPartialRecordSeparator(alreadyRead, recordSeparator);

            do
            {
                var buffer = new char[bufferSize];
                n = reader.Read(buffer, 0, bufferSize);


                if (n > 0)
                {
                    foreach (var c in buffer)
                    {
                        stringBuilder.Append(c);

                        if (c == '\0')
                        {
                            eof = true;
                            break;
                        }


                        if (c == recordSeparator[j])
                        {
                            j++;
                            if (j == recordSeparator.Length)
                            {
                                records.Add(stringBuilder.ToString());
                                stringBuilder.Clear();
                                j = 0;
                            }

                        }
                        else
                            j = 0;
                    }
                }
                else
                {
                    eof = true;
                    stringBuilder.Append('\0');
                }


            } while (records.Count == 0 && !eof);

            if (eof && stringBuilder.Length > 0 && stringBuilder[0] != '\0')
                records.Add(stringBuilder.ToString());

            if (stringBuilder.Length > 0)
                extraRead = stringBuilder.ToString();

            return records;
        }

        protected virtual int IdentifyPartialRecordSeparator(string text, string recordSeparator)
        {
            int i = Math.Min(recordSeparator.Length - 1, text.Length);
            while (i > 0)
            {
                if (text.EndsWith(recordSeparator.Substring(0, i)))
                    return i;
                i--;
            }
            return 0;
        }

        protected virtual string CleanRecord(string record, string recordSeparator)
        {
            int i = 0;
            while (record.Length > i && record[record.Length - 1 - i] == '\0')
                i++;

            if (i > 0)
                record = record.Remove(record.Length - i, i);

            if (record.EndsWith(recordSeparator))
                return record.Remove(record.Length - recordSeparator.Length, recordSeparator.Length);

            return record;
        }

        protected virtual bool IsLastRecord(string record) => string.IsNullOrEmpty(record) || record.EndsWith("\0");
    }
}

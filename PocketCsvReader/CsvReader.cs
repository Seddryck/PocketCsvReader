using System.Buffers;
using System.Data;
using System.IO;
using System.Text;

namespace PocketCsvReader
{
    public class CsvReader
    {
        public event ProgressStatusHandler? ProgressStatusChanged;
        protected IEncodingDetector EncodingDetector { get; set; } = new EncodingDetector();
        
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
            Profile = profile;
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
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            return new CsvDataTable(stream, Profile).Read();
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
            return new CsvDataTable(stream, Profile).Read();
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
        public IDataReader ToDataReader(string filename)
        {
            CheckFileExists(filename);
            var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.ParserOptimizations.BufferSize);
            return new CsvDataReader(stream, Profile);
        }

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
            return new CsvDataReader(stream, Profile);
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
        public IEnumerable<string?[]> ToArrayString(string filename)
        {
            CheckFileExists(filename);
            var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.ParserOptimizations.BufferSize);
            return new CsvArrayString(stream, Profile).Read();
        }

        /// <summary>
        /// Reads the CSV data from the provided stream and returns an <see cref="IDataReader"/> for efficient record-by-record access.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing CSV data, positioned at the beginning of the content.</param>
        /// <returns>An <see cref="IDataReader"/> that allows sequential access to each record and field in the CSV file.</returns>
        /// <remarks>
        /// This method processes the CSV data from the stream and provides an <see cref="IDataReader"/> for forward-only, read-only access,
        /// ideal for handling large datasets without loading the entire file into memory at once.
        /// </remarks>
        public IEnumerable<string?[]> ToArrayString(Stream stream)
        {
            return new CsvArrayString(stream, Profile).Read();
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
        public IEnumerable<T> To<T>(string filename, SpanMapper<T>? spanMapper = null)
        {
            CheckFileExists(filename);
            var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.ParserOptimizations.BufferSize);
            return new CsvObjectReader<T>(stream, Profile, spanMapper).Read();
        }

        /// <summary>
        /// Reads the CSV data from the provided stream and returns an <see cref="IEnumerable<T>"/> for efficient object-by-object access.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> containing CSV data, positioned at the beginning of the content.</param>
        /// <returns>An <see cref="IDataReader"/> that allows sequential access to each record and field in the CSV file.</returns>
        /// <remarks>
        /// This method processes the CSV data from the stream and provides an <see cref="IDataReader"/> for forward-only, read-only access,
        /// ideal for handling large datasets without loading the entire file into memory at once.
        /// </remarks>
        public IEnumerable<T> To<T>(Stream stream, SpanMapper<T>? spanMapper = null)
        {
            return new CsvObjectReader<T>(stream, Profile, spanMapper).Read();
        }

        protected virtual void CheckFileExists(string filename)
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException($"The file {filename} was not found.", filename);
        }
    }
}

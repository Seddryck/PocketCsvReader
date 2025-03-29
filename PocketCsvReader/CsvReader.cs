using System.Buffers;
using System.Data;
using System.IO;
using System.Text;

namespace PocketCsvReader
{
    /// <summary>
    /// Provides functionality for reading and parsing CSV files or streams into various formats such as 
    /// <see cref="DataTable"/>, <see cref="IDataReader"/>, or strongly-typed objects.
    /// </summary>
    /// <remarks>
    /// The <see cref="CsvReader"/> class is designed for flexibility and performance when working with CSV data.
    /// It supports customizable profiles for parsing and encoding detection. Use this class to load CSV data
    /// into memory or stream it efficiently, depending on your application’s requirements.
    /// </remarks>
    public class CsvReader
    {
        public event ProgressStatusHandler? ProgressStatusChanged;
        protected IEncodingDetector EncodingDetector { get; set; } = new EncodingDetector();

        protected internal CsvProfile Profile { get; private set; }
        public DialectDescriptor Dialect { get => Profile.Dialect; }

        protected int BufferSize { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvReader"/> class with default settings.
        /// </summary>
        /// <remarks>
        /// The default settings include a profile using comma as the delimiter and double quotes for escaping,
        /// with a Buffer size of 4 KB.
        /// </remarks>
        public CsvReader()
            : this(CsvProfile.CommaDoubleQuote, 4 * 1024)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvReader"/> class with the specified CSV profile.
        /// </summary>
        /// <param name="profile">
        /// The <see cref="CsvProfile"/> that defines the delimiter, quote handling, and other parsing rules.
        /// </param>
        public CsvReader(CsvProfile profile)
            : this(profile, 4 * 1024)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvReader"/> class with the specified Buffer size.
        /// </summary>
        /// <param name="bufferSize">The size of the Buffer used for reading CSV data.</param>
        /// <remarks>
        /// A Buffer size of at least 4 KB is recommended for optimal performance.
        /// </remarks>
        public CsvReader(int bufferSize)
            : this(CsvProfile.SemiColumnDoubleQuote, bufferSize)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvReader"/> class with the specified CSV profile and Buffer size.
        /// </summary>
        /// <param name="profile">
        /// The <see cref="CsvProfile"/> that defines the delimiter, quote handling, and other parsing rules.
        /// </param>
        /// <param name="bufferSize">The size of the Buffer used for reading CSV data.</param>
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
        /// Reads a CSV file and converts its contents into a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="filename">The full path of the CSV file to read.</param>
        /// <returns>
        /// A <see cref="DataTable"/> containing all records (rows) and fields (columns) parsed from the CSV file.
        /// </returns>
        /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist.</exception>
        /// <remarks>
        /// This method reads the entire CSV content into memory. Ensure sufficient memory is available
        /// for processing large files. Encoding is detected automatically using the <see cref="IEncodingDetector"/> implementation.
        /// </remarks>
        public DataTable ToDataTable(string filename)
        {
            CheckFileExists(filename);
            using var stream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            return new CsvDataTable(stream, Profile).Read();
        }

        /// <summary>
        /// Reads CSV data from a stream and converts it into a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="stream">
        /// The <see cref="Stream"/> containing the CSV data. The stream must be readable and positioned
        /// at the start of the CSV content.
        /// </param>
        /// <returns>
        /// A <see cref="DataTable"/> populated with records and fields parsed from the stream.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if the stream is null.</exception>
        /// <remarks>
        /// This method does not close the provided stream. It assumes all rows and fields follow the
        /// configured CSV profile.
        /// </remarks>
        public DataTable ToDataTable(Stream stream)
        {
            return new CsvDataTable(stream, Profile).Read();
        }

        /// <summary>
        /// Opens a CSV file and provides an <see cref="IDataReader"/> for efficient record-by-record access.
        /// </summary>
        /// <param name="filename">The full path of the CSV file to read.</param>
        /// <returns>
        /// An <see cref="CsvDataReader"/> instance for sequential, read-only access to the CSV records and fields.
        /// </returns>
        /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist.</exception>
        /// <remarks>
        /// This method is designed for scenarios where loading the entire file into memory is impractical,
        /// such as processing large datasets. The caller must dispose of the <see cref="CsvDataReader"/> after use.
        /// </remarks>
        public CsvDataReader ToDataReader(string filename)
        {
            CheckFileExists(filename);
            var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.ParserOptimizations.BufferSize);
            return new CsvDataReader(stream, Profile);
        }

        /// <summary>
        /// Opens a CSV file and provides an <see cref="IDataReader"/> for efficient record-by-record access.
        /// </summary>
        /// <param name="filename">The full path of the CSV file to read.</param>
        /// <returns>
        /// An <see cref="CsvDataReader"/> instance for sequential, read-only access to the CSV records and fields.
        /// </returns>
        /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist.</exception>
        /// <remarks>
        /// This method is designed for scenarios where loading the entire file into memory is impractical,
        /// such as processing large datasets. The caller must dispose of the <see cref="CsvDataReader"/> after use.
        /// </remarks>
        public CsvBatchDataReader ToDataReader(string[] filenames)
        {
            var streams = new List<Stream>();
            foreach (var filename in filenames)
            {
                CheckFileExists(filename);
                var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.ParserOptimizations.BufferSize);
                streams.Add(stream);
            }
            return new CsvBatchDataReader([.. streams], Profile);
        }

        /// <summary>
        /// Reads CSV data from a stream and provides an <see cref="IDataReader"/> for record-by-record access.
        /// </summary>
        /// <param name="stream">
        /// The <see cref="Stream"/> containing the CSV data. The stream must be readable and positioned
        /// at the start of the content.
        /// </param>
        /// <returns>
        /// An <see cref="CsvDataReader"/> instance for sequential, read-only access to the CSV records and fields.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if the stream is null.</exception>
        /// <remarks>
        /// This method does not manage the lifecycle of the stream; the caller is responsible for closing it.
        /// </remarks>
        public CsvDataReader ToDataReader(Stream stream)
        {
            return new CsvDataReader(stream, Profile);
        }

        /// <summary>
        /// Reads CSV data from a set of streams and provides an <see cref="IDataReader"/> for record-by-record access.
        /// </summary>
        /// <param name="streams">
        /// The array of <see cref="stream"/> containing the CSV data. The streams must be readable and positioned
        /// at the start of the content.
        /// </param>
        /// <returns>
        /// An <see cref="CsvBatchDataReader"/> instance for sequential, read-only access to the CSV records and fields.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if the streams contain no element.</exception>
        /// <remarks>
        /// This method does not manage the lifecycle of the stream; the caller is responsible for closing it.
        /// </remarks>
        public CsvBatchDataReader ToDataReader(Stream[] streams)
        {
            return new CsvBatchDataReader(streams, Profile);
        }

        /// <summary>
        /// Reads a CSV file and returns an enumerable of arrays representing records as fields.
        /// </summary>
        /// <param name="filename">The full path of the CSV file to read.</param>
        /// <returns>
        /// An enumerable of string arrays, where each array represents the fields of a single record.
        /// </returns>
        /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist.</exception>
        /// <remarks>
        /// This method provides a lightweight approach to accessing CSV data without materializing it into
        /// a <see cref="DataTable"/> or <see cref="IDataReader"/>.
        /// </remarks>
        public IEnumerable<string?[]> ToArrayString(string filename)
        {
            CheckFileExists(filename);
            var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.ParserOptimizations.BufferSize);
            return new CsvArrayString(stream, Profile).Read();
        }

        /// <summary>
        /// Reads CSV data from a stream and returns an enumerable of arrays representing records as fields.
        /// </summary>
        /// <param name="stream">
        /// The <see cref="Stream"/> containing CSV data. The stream must be readable and positioned
        /// at the start of the content.
        /// </param>
        /// <returns>
        /// An enumerable of string arrays, where each array represents the fields of a single record.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if the stream is null.</exception>
        /// <remarks>
        /// This method does not close the provided stream and assumes that the CSV content adheres to the
        /// configured profile.
        /// </remarks>
        public IEnumerable<string?[]> ToArrayString(Stream stream)
        {
            return new CsvArrayString(stream, Profile).Read();
        }

        /// <summary>
        /// Reads a CSV file and maps its records into objects of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of objects to map the CSV records to.</typeparam>
        /// <param name="filename">The full path of the CSV file to read.</param>
        /// <param name="spanMapper">
        /// An optional delegate for mapping CSV fields to object properties. If null, default mapping is used.
        /// </param>
        /// <returns>
        /// An enumerable of <typeparamref name="T"/> objects created from the CSV data.
        /// </returns>
        /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist.</exception>
        /// <remarks>
        /// This method provides a strongly typed way to consume CSV data. Ensure that <typeparamref name="T"/>
        /// matches the structure of the CSV records.
        /// </remarks>
        public IEnumerable<T> To<T>(string filename, SpanMapper<T>? spanMapper = null)
        {
            CheckFileExists(filename);
            var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, Profile.ParserOptimizations.BufferSize);
            return new CsvObjectReader<T>(stream, Profile, spanMapper).Read();
        }

        /// <summary>
        /// Reads CSV data from a stream and maps its records into objects of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of objects to map the CSV records to.</typeparam>
        /// <param name="stream">
        /// The <see cref="Stream"/> containing CSV data. The stream must be readable and positioned
        /// at the start of the content.
        /// </param>
        /// <param name="spanMapper">
        /// An optional delegate for mapping CSV fields to object properties. If null, default mapping is used.
        /// </param>
        /// <returns>
        /// An enumerable of <typeparamref name="T"/> objects created from the CSV data.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if the stream is null.</exception>
        /// <remarks>
        /// This method does not close the provided stream. Use it when you want to work with strongly typed
        /// objects instead of raw data.
        /// </remarks>
        public IEnumerable<T> To<T>(Stream stream, SpanMapper<T>? spanMapper = null)
        {
            return new CsvObjectReader<T>(stream, Profile, spanMapper).Read();
        }

        /// <summary>
        /// Checks whether the specified file exists and throws an exception if it does not.
        /// </summary>
        /// <param name="filename">The name of the file to check.</param>
        /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
        protected virtual void CheckFileExists(string filename)
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException($"The file {filename} was not found.", filename);
        }
    }
}

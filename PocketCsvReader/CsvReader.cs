using System.Buffers;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
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
    /// into memory or stream it efficiently, depending on your application's requirements.
    /// </remarks>
    public class CsvReader : FlatFileReader<CsvProfile, CsvDataReader>
    {
        public event ProgressStatusHandler? ProgressStatusChanged;
        protected IEncodingDetector EncodingDetector { get; set; } = new EncodingDetector();

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
            : this(CsvProfile.CommaDoubleQuote)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvReader"/> class with the specified CSV profile.
        /// </summary>
        /// <param name="profile">
        /// The <see cref="CsvProfile"/> that defines the delimiter, quote handling, and other parsing rules.
        /// </param>
        public CsvReader(CsvProfile profile)
            : base(profile ?? throw new ArgumentNullException(nameof(profile)))
        {
            if (profile.ParserOptimizations.BufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(profile), "The profile buffer size must be positive.");
            BufferSize = profile.ParserOptimizations.BufferSize;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvReader"/> class with the specified Buffer size.
        /// </summary>
        /// <param name="bufferSize">The size of the Buffer used for reading CSV data.</param>
        /// <remarks>
        /// A Buffer size of at least 4 KB is recommended for optimal performance.
        /// </remarks>
        public CsvReader(int bufferSize)
            : this(CsvProfile.CommaDoubleQuote, bufferSize)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="CsvReader"/> class with the specified CSV profile and Buffer size.
        /// </summary>
        /// <param name="profile">
        /// The <see cref="CsvProfile"/> that defines the delimiter, quote handling, and other parsing rules.
        /// </param>
        /// <param name="bufferSize">The size of the Buffer used for reading CSV data.</param>
        public CsvReader(CsvProfile profile, int bufferSize)
            : base(WithBufferSize(profile, bufferSize))
        {
            BufferSize = bufferSize;
        }

        private static CsvProfile WithBufferSize(CsvProfile profile, int bufferSize)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize), bufferSize, "Buffer size must be positive.");

            return new CsvProfile(profile.Dialect, profile.Schema, profile.Resource, profile.Parsers)
            {
                StreamInitialization = profile.StreamInitialization,
                ParserOptimizations = profile.ParserOptimizations with { BufferSize = bufferSize },
                BadDataPolicy = profile.BadDataPolicy
            };
        }

        protected override int StreamBufferSize => BufferSize;

        protected override CsvDataReader CreateDataReader(Stream stream)
            => new(stream, Profile);

        protected void RaiseProgressStatus(string status)
            => ProgressStatusChanged?.Invoke(this, new ProgressStatusEventArgs(status));

        protected void RaiseProgressStatus(string status, int current, int total)
            => ProgressStatusChanged?.Invoke(this, new ProgressStatusEventArgs(string.Format(status, current, total), current, total));

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
            if (filenames == null || filenames.Length == 0)
                throw new ArgumentException("File names collection cannot be null or empty.", nameof(filenames));

            IEnumerable<Stream> fileToStream(string[] filenames)
            {
                foreach (var filename in filenames)
                {
                    yield return OpenRead(filename);
                }
            }

            return new CsvBatchDataReader(fileToStream(filenames), Profile);
        }

        /// <summary>
        /// Reads CSV data from a set of streams and provides an <see cref="IDataReader"/> for record-by-record access.
        /// </summary>
        /// <param name="streams">
        /// The enumerable of <see cref="stream"/> containing the CSV data. The streams must be readable and positioned
        /// at the start of the content.
        /// </param>
        /// <returns>
        /// An <see cref="CsvBatchDataReader"/> instance for sequential, read-only access to the CSV records and fields.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if the streams contain no element.</exception>
        /// <remarks>
        /// This method does not manage the lifecycle of the stream; the caller is responsible for closing it.
        /// </remarks>
        public CsvBatchDataReader ToDataReader(IEnumerable<Stream> streams)
        {
            if (streams == null)
                throw new ArgumentNullException(nameof(streams), "Streams collection cannot be null.");

            if (!streams.Any())
                throw new ArgumentException("Streams collection cannot be empty.", nameof(streams));

            return new CsvBatchDataReader(streams, Profile);
        }

        /// <summary>
        /// Reads CSV data from a set of stream openers (lazy - evaluated) and provides an
        /// <see cref="IDataReader"/> for record-by-record access.
        /// </summary>
        /// <param name="openers">
        /// Functions returning a <see cref="Stream"/> positioned at the start of the CSV content. Streams are opened lazily
        /// at the start of the content.
        /// </param>
        /// <returns>
        /// An <see cref="CsvBatchDataReader"/> instance for sequential, read-only access to the CSV records and fields.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if the streams contain no element.</exception>
        /// <remarks>
        /// This method does not manage the lifecycle of the stream; the caller is responsible for closing it.
        /// </remarks>
        public CsvBatchDataReader ToDataReader(IEnumerable<Func<Stream>> openers)
        {
            if (openers == null)
                throw new ArgumentNullException(nameof(openers), "Stream openers collection cannot be null.");

            if (!openers.Any())
                throw new ArgumentException("Stream openers collection cannot be empty.", nameof(openers));

            return new CsvBatchDataReader(openers, Profile);
        }

        public System.Data.Common.DbDataReader ToDbDataReader(string[] filenames)
            => new DbDataReaderAdapter(ToDataReader(filenames));

        public System.Data.Common.DbDataReader ToDbDataReader(IEnumerable<Stream> streams)
            => new DbDataReaderAdapter(ToDataReader(streams));

        public System.Data.Common.DbDataReader ToDbDataReader(IEnumerable<Func<Stream>> openers)
            => new DbDataReaderAdapter(ToDataReader(openers));

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
            return ReadObjects(filename, spanMapper);

            IEnumerable<T> ReadObjects(string path, SpanMapper<T>? mapper)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                using var reader = new CsvObjectReader<T>(stream, Profile, mapper);
                foreach (var value in reader.Read())
                    yield return value;
            }
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
            ArgumentNullException.ThrowIfNull(stream);
            return ReadObjects(stream, spanMapper);

            IEnumerable<T> ReadObjects(Stream source, SpanMapper<T>? mapper)
            {
                using var reader = new CsvObjectReader<T>(source, Profile, mapper, leaveOpen: true);
                foreach (var value in reader.Read())
                    yield return value;
            }
        }

        /// <summary>
        /// Asynchronously streams CSV records from a file and maps them to objects of type
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of objects to map the CSV records to.</typeparam>
        /// <param name="filename">The full path of the CSV file to read.</param>
        /// <param name="spanMapper">
        /// An optional delegate for mapping CSV fields to object properties. If null, the same
        /// constructor-based mapping as <see cref="To{T}(string, SpanMapper{T}?)"/> is used.
        /// </param>
        /// <param name="cancellationToken">A token used to cancel asynchronous input reads.</param>
        /// <returns>A lazy, forward-only asynchronous sequence of mapped records.</returns>
        /// <remarks>
        /// The file is opened when enumeration starts and is closed when enumeration completes,
        /// is cancelled, fails, or the enumerator is disposed.
        /// </remarks>
        public async IAsyncEnumerable<T> ToAsync<T>(
            string filename,
            SpanMapper<T>? spanMapper = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CheckFileExists(filename);
            await using var stream = new FileStream(
                filename, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            await using var reader = new CsvDataReader(stream, Profile, leaveOpen: true);
            var mapper = spanMapper ?? new SpanMapper<T>(new SpanObjectBuilder<T>().Instantiate);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield return reader.MapCurrent(mapper);
        }

        /// <summary>
        /// Asynchronously streams CSV records from a stream and maps them to objects of type
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type of objects to map the CSV records to.</typeparam>
        /// <param name="stream">A readable stream positioned at the start of the CSV content.</param>
        /// <param name="spanMapper">
        /// An optional delegate for mapping CSV fields to object properties. If null, the same
        /// constructor-based mapping as <see cref="To{T}(Stream, SpanMapper{T}?)"/> is used.
        /// </param>
        /// <param name="cancellationToken">A token used to cancel asynchronous input reads.</param>
        /// <returns>A lazy, forward-only asynchronous sequence of mapped records.</returns>
        /// <remarks>
        /// The caller retains ownership of <paramref name="stream"/>. The stream is not closed when
        /// enumeration completes, is cancelled, fails, or the enumerator is disposed.
        /// </remarks>
        public async IAsyncEnumerable<T> ToAsync<T>(
            Stream stream,
            SpanMapper<T>? spanMapper = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            await using var reader = new CsvDataReader(stream, Profile, leaveOpen: true);
            var mapper = spanMapper ?? new SpanMapper<T>(new SpanObjectBuilder<T>().Instantiate);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield return reader.MapCurrent(mapper);
        }
    }
}

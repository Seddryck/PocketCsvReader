using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Chrononuensis;
using nietras.SeparatedValues;
using PocketCsvReader.Chrononuensis;
using PocketCsvReader.Configuration;
using TinyCsvParser;
using TinyCsvParser.Models;
using TinyCsvParser.TypeConverters;

namespace PocketCsvReader.Benchmark.RealWorkloadWithCsv;

[MemoryDiagnoser]
public class RealWorkloadWithCsvBenchmark
{
    private const int RecordCount = 50_000;
    private const string DateFormat = "yyyy-MM-dd";
    private const string BerkeleyUniversity = "University of California, Berkeley";

    private static readonly string[] Universities =
    [
        "KU Leuven", "Ghent University", "Université libre de Bruxelles", "Université de Liège",
        BerkeleyUniversity, "Universiteit Antwerpen",
    ];

    private static readonly string[] FirstNames = ["Amira", "Chloé", "Liam", "Nora", "Sofía", "Youssef"];
    private static readonly string[] LastNames = ["Benali", "De Smet", "García", "Janssens", "Nguyen", "O'Connor"];

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly SepReaderOptions SepOptions = new(nietras.SeparatedValues.Sep.New(','))
    {
        CultureInfo = Culture,
        Unescape = true,
    };

    private static readonly Sylvan.Data.Csv.CsvDataReaderOptions SylvanOptions = new()
    {
        Culture = Culture,
        Delimiter = ',',
    };

    private static readonly global::CsvHelper.CsvReaderOptions CsvHelperOptions = new()
    {
        Separator = ',',
        FormatProvider = Culture,
    };

    private readonly Consumer _consumer = new();
    private byte[] _csv = [];
    private CsvProfile _profile = null!;
    private TinyCsvParser.CsvParser<Enrollment> _tinyCsvParser = null!;
    private IEnumerator<Enrollment>? _expectedRecords;

    [Params(CsvScenario.Utf8, CsvScenario.Utf16, CsvScenario.Utf8WithQuotedUniversity,
        CsvScenario.Utf8WithFullName, CsvScenario.Utf8WithYearQuarter)]
    public CsvScenario Scenario { get; set; }

    private bool ReadsYearQuarter => Scenario == CsvScenario.Utf8WithYearQuarter;

    [GlobalSetup]
    public void Setup()
    {
        var csv = new StringBuilder("University,FirstName,LastName,Quarter,Score,Credits,RegistrationDate\r\n");

        foreach (var record in GenerateRecords())
        {
            var university = record.University.AsSpan().IndexOfAny(',', '"') >= 0
                ? "\"" + record.University.Replace("\"", "\"\"") + "\""
                : record.University;
            csv.Append(Culture,
                $"{university},{record.FirstName},{record.LastName},{record.Quarter},{record.Score:F1},{record.Credits},{record.RegistrationDate.ToString(DateFormat, Culture)}\r\n");
        }

        var isUtf16 = Scenario == CsvScenario.Utf16;
        var encoding = isUtf16 ? Encoding.Unicode : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var csvText = csv.ToString();
        var preamble = encoding.Preamble;
        _csv = new byte[preamble.Length + encoding.GetByteCount(csvText)];
        preamble.CopyTo(_csv);
        encoding.GetBytes(csvText, _csv.AsSpan(preamble.Length));
        var parsers = ReadsYearQuarter ? new RuntimeParsersDescriptorBuilder().UseChrononuensis(Culture).Build() : null;
        _profile = new(new(Header: true), parsers: parsers)
        {
            StreamInitialization = isUtf16 ? new() : StreamInitializationOptions.Utf8,
        };

        _tinyCsvParser = new(CsvOptions.Rfc4180 with { Delimiter = ',', SkipHeader = true },
            new EnrollmentMapping(ReadsYearQuarter));

        VerifyRecords(PocketCsvReader);
        VerifyRecords(Sep);
        VerifyRecords(Sylvan);
        VerifyRecords(CsvHelper);
        VerifyRecords(SoftCircuits);
        VerifyRecords(TinyCsvParser);
    }

    [Benchmark(Baseline = true)]
    public void PocketCsvReader()
    {
        using var stream = OpenCsv();
        using var reader = new CsvDataReader(stream, _profile);

        var recordsRead = 0;
        while (reader.Read())
        {
            var quarter = ReadsYearQuarter ? null : reader.GetString(3);
            YearQuarter? yearQuarter = ReadsYearQuarter ? reader.GetFieldValue<YearQuarter>(3) : null;
            Consume(reader.GetString(0), reader.GetString(1), reader.GetString(2), quarter,
                reader.GetDecimal(4), reader.GetInt16(5), reader.GetDate(6), yearQuarter);
            recordsRead++;
        }

        VerifyRecordCount(recordsRead);
    }

    [Benchmark]
    public void Sep()
    {
        using var stream = OpenCsv();
        using var reader = SepOptions.From(stream);

        var recordsRead = 0;
        foreach (var row in reader)
        {
            var quarter = ReadsYearQuarter ? null : row[3].ToString();
            YearQuarter? yearQuarter = ReadsYearQuarter ? YearQuarter.Parse(row[3].Span, Culture) : null;
            Consume(row[0].ToString(), row[1].ToString(), row[2].ToString(), quarter,
                row[4].Parse<decimal>(), row[5].Parse<short>(), row[6].Parse<DateOnly>(), yearQuarter);
            recordsRead++;
        }

        VerifyRecordCount(recordsRead);
    }

    [Benchmark]
    public void Sylvan()
    {
        using var stream = OpenCsv();
        using var text = new StreamReader(stream);
        using var reader = global::Sylvan.Data.Csv.CsvDataReader.Create(text, SylvanOptions);

        var recordsRead = 0;
        while (reader.Read())
        {
            var quarter = ReadsYearQuarter ? null : reader.GetString(3);
            YearQuarter? yearQuarter = ReadsYearQuarter ? YearQuarter.Parse(reader.GetFieldSpan(3), Culture) : null;
            Consume(reader.GetString(0), reader.GetString(1), reader.GetString(2), quarter,
                reader.GetDecimal(4), reader.GetInt16(5), reader.GetDate(6), yearQuarter);
            recordsRead++;
        }

        VerifyRecordCount(recordsRead);
    }

    [Benchmark]
    public void CsvHelper()
    {
        using var stream = OpenCsv();
        using var reader = global::CsvHelper.CsvReader.Create(stream, CsvHelperOptions);
        reader.ReadHeader();

        var recordsRead = 0;
        while (reader.Read())
        {
            var quarter = reader.GetString(3);
            YearQuarter? yearQuarter = ReadsYearQuarter ? YearQuarter.Parse(quarter.AsSpan(), Culture) : null;
            Consume(reader.GetString(0), reader.GetString(1), reader.GetString(2), quarter,
                reader.Parse<decimal>(4), reader.Parse<short>(5), reader.ParseExact<DateOnly>(6, DateFormat), yearQuarter);
            recordsRead++;
        }

        VerifyRecordCount(recordsRead);
    }

    [Benchmark]
    public void SoftCircuits()
    {
        using var stream = OpenCsv();
        using var reader = new global::SoftCircuits.CsvParser.CsvReader(stream,
            detectEncodingFromByteOrderMarks: Scenario == CsvScenario.Utf16);
        reader.Read();

        var recordsRead = 0;
        string[]? row = null;
        while (reader.ReadRow(ref row))
        {
            var quarter = row![3];
            YearQuarter? yearQuarter = ReadsYearQuarter ? YearQuarter.Parse(quarter.AsSpan(), Culture) : null;
            Consume(row[0], row[1], row[2], quarter, decimal.Parse(row[4], Culture), short.Parse(row[5], Culture),
                DateOnly.ParseExact(row[6], DateFormat, Culture), yearQuarter);
            recordsRead++;
        }

        VerifyRecordCount(recordsRead);
    }

    [Benchmark]
    public void TinyCsvParser()
    {
        using var stream = OpenCsv();

        var recordsRead = 0;
        foreach (var result in _tinyCsvParser.Read(stream))
        {
            if (!result.IsSuccess)
                throw new InvalidOperationException($"TinyCsvParser could not parse line {result.LineNumber}.");

            var record = result.Result;
            Consume(record.University, record.FirstName, record.LastName, record.Quarter,
                record.Score, record.Credits, record.RegistrationDate, record.ParsedQuarter);
            recordsRead++;
        }

        VerifyRecordCount(recordsRead);
    }

    private IEnumerable<Enrollment> GenerateRecords()
    {
        var random = new Random(42);
        var firstRegistration = new DateOnly(2022, 1, 1);

        for (var i = 0; i < RecordCount; i++)
        {
            var university = Universities[random.Next(Universities.Length)];
            var firstName = FirstNames[random.Next(FirstNames.Length)];
            var lastName = LastNames[random.Next(LastNames.Length)];
            var year = random.Next(2024, 2027);
            var quarter = random.Next(1, 5);
            var score = random.Next(201) / 10m;
            var credits = (short)random.Next(1, 181);
            var registrationDate = firstRegistration.AddDays(random.Next(730));

            yield return new()
            {
                University = Scenario == CsvScenario.Utf8WithQuotedUniversity && university == BerkeleyUniversity
                    ? "University of California, \"Berkeley\""
                    : university,
                FirstName = firstName,
                LastName = lastName,
                Quarter = string.Create(Culture, $"{year}-Q{quarter}"),
                ParsedQuarter = ReadsYearQuarter ? new YearQuarter(year, quarter) : null,
                Score = score,
                Credits = credits,
                RegistrationDate = registrationDate,
            };
        }
    }

    private static void VerifyRecordCount(int recordsRead)
    {
        if (recordsRead != RecordCount)
            throw new InvalidOperationException($"Expected {RecordCount} records but read {recordsRead}.");
    }

    private MemoryStream OpenCsv() => new(_csv, writable: false);

    private void VerifyRecords(Action read)
    {
        using var records = GenerateRecords().GetEnumerator();
        _expectedRecords = records;
        try
        {
            read();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{read.Method.Name} failed to read the expected records.", exception);
        }
        finally
        {
            _expectedRecords = null;
        }
    }

    private void Consume(string university, string firstName, string lastName, string? quarter,
        decimal score, short credits, DateOnly registrationDate, YearQuarter? yearQuarter)
    {
        var joinNames = Scenario == CsvScenario.Utf8WithFullName;
        var fullName = joinNames ? string.Concat(firstName, " ", lastName) : null;

        if (_expectedRecords is { } records)
        {
            if (!records.MoveNext())
                throw new InvalidOperationException("Found an unexpected extra record.");

            var expected = records.Current;
            var quarterMatches = ReadsYearQuarter ? Nullable.Equals(yearQuarter, expected.ParsedQuarter) : quarter == expected.Quarter;
            if (university != expected.University || firstName != expected.FirstName ||
                lastName != expected.LastName || !quarterMatches || score != expected.Score ||
                credits != expected.Credits || registrationDate != expected.RegistrationDate)
                throw new InvalidOperationException($"Parsed record does not match the expected record for {expected.FirstName} {expected.LastName}.");

            if (joinNames && fullName != $"{expected.FirstName} {expected.LastName}")
                throw new InvalidOperationException($"Joined name does not match {expected.FirstName} {expected.LastName}.");
        }

        _consumer.Consume(university);
        _consumer.Consume(fullName ?? firstName);
        _consumer.Consume(lastName);
        if (yearQuarter is { } parsedQuarter)
            _consumer.Consume(parsedQuarter);
        else
            _consumer.Consume(quarter ?? throw new InvalidOperationException("No quarter value was read."));
        _consumer.Consume(score);
        _consumer.Consume(credits);
        _consumer.Consume(registrationDate.DayNumber);
    }

    public enum CsvScenario
    {
        Utf8,
        Utf16,
        Utf8WithQuotedUniversity,
        Utf8WithFullName,
        Utf8WithYearQuarter,
    }

    private sealed class Enrollment
    {
        public string University { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Quarter { get; set; } = string.Empty;
        public YearQuarter? ParsedQuarter { get; set; }
        public decimal Score { get; set; }
        public short Credits { get; set; }
        public DateOnly RegistrationDate { get; set; }
    }

    private sealed class EnrollmentMapping : CsvMapping<Enrollment>
    {
        public EnrollmentMapping(bool readYearQuarter)
        {
            MapProperty(1, record => record.FirstName);
            MapProperty(2, record => record.LastName);
            if (!readYearQuarter)
                MapProperty(3, record => record.Quarter);
            MapProperty(4, record => record.Score);
            MapProperty(5, record => record.Credits);
            MapProperty(6, record => record.RegistrationDate, new RegistrationDateConverter());

            // The property mapper uses GetSpan, which leaves doubled quotes in the value.
            MapUsing((Enrollment record, ref CsvRow row) =>
            {
                record.University = row.GetString(0);
                if (readYearQuarter)
                    record.ParsedQuarter = YearQuarter.Parse(row.GetSpan(3), Culture);
                return MapUsingResult.Success();
            });
        }
    }

    private sealed class RegistrationDateConverter : NonNullableConverter<DateOnly>
    {
        protected override bool InternalConvert(ReadOnlySpan<char> value, out DateOnly result)
            => DateOnly.TryParseExact(value, DateFormat, Culture, DateTimeStyles.None, out result);
    }
}

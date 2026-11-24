using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace PocketCsvReader.Benchmark.RealWorkloadWithCsv;

[MemoryDiagnoser]
public class RealWorkloadWithCsvBenchmark
{
    private const int RecordCount = 50_000;

    private static readonly string[] Universities =
    [
        "KU Leuven", "Ghent University", "Université libre de Bruxelles", "Université de Liège",
        "\"University of California, Berkeley\"", "Universiteit Antwerpen",
    ];

    private static readonly string[] FirstNames = ["Amira", "Chloé", "Liam", "Nora", "Sofía", "Youssef"];
    private static readonly string[] LastNames = ["Benali", "De Smet", "García", "Janssens", "Nguyen", "O'Connor"];

    private readonly CsvProfile _profile = new(new(Header: true))
    {
        StreamInitialization = StreamInitializationOptions.Utf8,
    };

    private readonly Consumer _consumer = new();
    private byte[] _csv = [];

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        var firstRegistration = new DateOnly(2022, 1, 1);
        var csv = new StringBuilder("University,FirstName,LastName,Quarter,Score,Credits,RegistrationDate\r\n");

        for (var i = 0; i < RecordCount; i++)
        {
            var university = Universities[random.Next(Universities.Length)];
            var firstName = FirstNames[random.Next(FirstNames.Length)];
            var lastName = LastNames[random.Next(LastNames.Length)];
            var year = random.Next(2024, 2027);
            var quarter = random.Next(1, 5);
            var score = random.Next(201) / 10m;
            var credits = random.Next(1, 181);
            var registrationDate = firstRegistration.AddDays(random.Next(730));

            csv.Append(CultureInfo.InvariantCulture,
                $"{university},{firstName},{lastName},{year}-Q{quarter},{score:F1},{credits},{registrationDate:yyyy-MM-dd}\r\n");
        }

        _csv = Encoding.UTF8.GetBytes(csv.ToString());
    }

    [Benchmark(Description = "Real workload with CSV")]
    public void ReadAllFields()
    {
        using var stream = new MemoryStream(_csv, writable: false);
        using var reader = new CsvDataReader(stream, _profile);

        var recordsRead = 0;
        while (reader.Read())
        {
            _consumer.Consume(reader.GetString(0));
            _consumer.Consume(reader.GetString(1));
            _consumer.Consume(reader.GetString(2));
            _consumer.Consume(reader.GetString(3));
            _consumer.Consume(reader.GetDecimal(4));
            _consumer.Consume(reader.GetInt16(5));
            _consumer.Consume(reader.GetDate(6).DayNumber);
            recordsRead++;
        }

        if (recordsRead != RecordCount)
            throw new InvalidOperationException($"Expected {RecordCount} records but read {recordsRead}.");
    }
}

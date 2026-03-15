using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using FaceRecognitionApi.Data;
using FaceRecognitionApi.Models;

namespace FaceRecognitionApi.Services;

public class CsvImportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CsvImportService> _logger;

    public CsvImportService(AppDbContext db, ILogger<CsvImportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> ImportAsync(string csvFilePath)
    {
        if (!File.Exists(csvFilePath))
        {
            _logger.LogWarning("CSV file not found: {Path}", csvFilePath);
            return 0;
        }

        using var stream = File.OpenRead(csvFilePath);
        var count = await ImportFromStreamAsync(stream);
        _logger.LogInformation("Imported {Count} records from {Path}", count, csvFilePath);
        return count;
    }

    public async Task<int> ImportFromStreamAsync(Stream csvStream)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
        };

        using var reader = new StreamReader(csvStream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        var records = new List<Person>();
        await foreach (var record in csv.GetRecordsAsync<CsvRecord>())
        {
            var name = ExtractName(record.Label);
            records.Add(new Person
            {
                Id = record.Id,
                Name = name,
                ImageFileName = record.Label,
            });
        }

        if (records.Count == 0)
        {
            _logger.LogWarning("No records found in uploaded CSV stream.");
            return 0;
        }

        _db.Persons.RemoveRange(_db.Persons);
        await _db.Persons.AddRangeAsync(records);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Imported {Count} records from uploaded CSV.", records.Count);
        return records.Count;
    }

    public static string ExtractName(string label)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(label);
        var lastUnderscore = nameWithoutExt.LastIndexOf('_');
        if (lastUnderscore > 0)
        {
            return nameWithoutExt[..lastUnderscore].Trim();
        }
        return nameWithoutExt.Trim();
    }

    private class CsvRecord
    {
        [CsvHelper.Configuration.Attributes.Name("id")]
        public int Id { get; set; }
        [CsvHelper.Configuration.Attributes.Name("label")]
        public string Label { get; set; } = string.Empty;
    }
}

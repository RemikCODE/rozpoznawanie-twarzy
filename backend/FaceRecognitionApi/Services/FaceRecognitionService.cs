using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FaceRecognitionApi.Data;
using FaceRecognitionApi.Models;
using Microsoft.EntityFrameworkCore;

namespace FaceRecognitionApi.Services;

public class FaceRecognitionService : IFaceRecognitionService
{
    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<FaceRecognitionService> _logger;

    public FaceRecognitionService(
        AppDbContext db,
        HttpClient httpClient,
        IConfiguration config,
        ILogger<FaceRecognitionService> logger)
    {
        _db = db;
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<RecognitionResult> RecognizeAsync(Stream imageStream, string fileName)
    {
        RecognitionResult result;
        var mlServiceUrl = _config["MlService:Url"];

        if (string.IsNullOrWhiteSpace(mlServiceUrl))
        {
            _logger.LogWarning("ML service URL is not configured. Set 'MlService:Url' in appsettings.");
            result = new RecognitionResult
            {
                Found = false,
                Message = "Face recognition ML service is not configured.",
            };
        }
        else
        {
            result = await CallMlServiceAsync(imageStream, fileName, mlServiceUrl);
        }

        await SaveLogAsync(result, fileName);
        return result;
    }

    private const int MlServiceMaxRetries = 30;
    private const int MlServiceRetryDelaySeconds = 10;

    private async Task<RecognitionResult> CallMlServiceAsync(Stream imageStream, string fileName, string mlServiceUrl)
    {
        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await imageStream.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }

        for (int attempt = 1; attempt <= MlServiceMaxRetries; attempt++)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                var imageContent = new StreamContent(new MemoryStream(imageBytes));
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
                content.Add(imageContent, "image", fileName);

                var response = await _httpClient.PostAsync(mlServiceUrl, content);

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < MlServiceMaxRetries)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation(
                        "ML service not ready (attempt {Attempt}/{Max}), retrying in {Delay}s. Response: {Body}",
                        attempt, MlServiceMaxRetries, MlServiceRetryDelaySeconds, body);
                    await Task.Delay(TimeSpan.FromSeconds(MlServiceRetryDelaySeconds));
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var errorMessage = ExtractMlErrorMessage(body) ?? response.StatusCode.ToString();
                    _logger.LogWarning("ML service returned {Status}. Body: {Body}", response.StatusCode, body);
                    return new RecognitionResult
                    {
                        Found = false,
                        Message = $"ML service error: {errorMessage}",
                    };
                }

                var mlResult = await response.Content.ReadFromJsonAsync<MlServiceResponse>();

                if (mlResult == null || string.IsNullOrWhiteSpace(mlResult.Label))
                {
                    return new RecognitionResult { Found = false, Message = "No face recognized." };
                }

                var recognizedName = CsvImportService.ExtractName(mlResult.Label);

                var person = await _db.Persons
                    .Where(p => p.ImageFileName == mlResult.Label || p.Name == recognizedName)
                    .FirstOrDefaultAsync();

                if (person == null)
                {
                    _logger.LogInformation(
                        "Person '{Name}' (label: {Label}) not found in DB – auto-inserting.",
                        recognizedName, mlResult.Label);

                    person = new Person
                    {
                        Name = recognizedName,
                        ImageFileName = mlResult.Label,
                    };
                    _db.Persons.Add(person);
                    await _db.SaveChangesAsync();
                }

                return new RecognitionResult
                {
                    Found = true,
                    Person = person,
                    Confidence = mlResult.Confidence,
                    Message = "Face recognized successfully.",
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling ML service (attempt {Attempt}/{Max})", attempt, MlServiceMaxRetries);
                if (attempt < MlServiceMaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(MlServiceRetryDelaySeconds));
                    continue;
                }

                return new RecognitionResult
                {
                    Found = false,
                    Message = $"Error communicating with ML service: {ex.Message}",
                };
            }
        }

        throw new InvalidOperationException("Unexpected exit from ML service retry loop.");
    }

    private static string? ExtractMlErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var prop))
                return prop.GetString();
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private async Task SaveLogAsync(RecognitionResult result, string sourceFileName)
    {
        _db.RecognitionLogs.Add(new RecognitionLog
        {
            RecognizedAt = DateTime.UtcNow,
            Found = result.Found,
            PersonName = result.Person?.Name ?? string.Empty,
            Confidence = result.Confidence,
            Message = result.Message,
            ImageFileName = sourceFileName,
        });
        await _db.SaveChangesAsync();
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };
    }

    private string? GetMlServiceBaseUrl()
    {
        var url = _config["MlService:Url"];
        if (string.IsNullOrWhiteSpace(url)) return null;
        return new Uri(url).GetLeftPart(UriPartial.Authority);
    }

    public async Task<string?> AddPersonAsync(string name, Stream imageStream, string fileName)
    {
        var baseUrl = GetMlServiceBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("ML service URL is not configured. Set 'MlService:Url' in appsettings.");
            return null;
        }

        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await imageStream.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(name), "name");
        var imageContent = new StreamContent(new MemoryStream(imageBytes));
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
        content.Add(imageContent, "image", fileName);

        var response = await _httpClient.PostAsync($"{baseUrl}/add-person", content);
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<AddPersonResponse>();
        return doc?.Filename;
    }

    private class MlServiceResponse
    {
        public string Label { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    private class AddPersonResponse
    {
        public string Filename { get; set; } = string.Empty;
    }
}

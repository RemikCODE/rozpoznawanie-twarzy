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

        _logger.LogInformation("Starting recognition for file: {FileName}", fileName);
        _logger.LogInformation("ML Service URL: {Url}", mlServiceUrl);

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
            // Najpierw sprawdź health ML service
            var isHealthy = await CheckMlServiceHealthAsync(mlServiceUrl);
            if (!isHealthy)
            {
                result = new RecognitionResult
                {
                    Found = false,
                    Message = "ML service is not healthy or not responding",
                };
            }
            else
            {
                result = await CallMlServiceAsync(imageStream, fileName, mlServiceUrl);
            }
        }

        await SaveLogAsync(result, fileName);
        return result;
    }

    private async Task<bool> CheckMlServiceHealthAsync(string mlServiceUrl)
    {
        try
        {
            var healthUrl = mlServiceUrl.Replace("/recognize", "/health");
            _logger.LogInformation("Checking ML service health at: {Url}", healthUrl);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.GetAsync(healthUrl, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("ML service health response: {Content}", content);

                var health = JsonSerializer.Deserialize<MlHealthResponse>(content);
                if (health?.embeddings_ready == true)
                {
                    _logger.LogInformation("ML service ready with {Count} images", health.images_in_dataset);
                    return true;
                }
                else
                {
                    _logger.LogWarning("ML service not ready: embeddings_ready = false");
                    return false;
                }
            }

            _logger.LogWarning("ML service health check failed with status: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check ML service health");
            return false;
        }
    }

    private async Task<RecognitionResult> CallMlServiceAsync(Stream imageStream, string fileName, string mlServiceUrl)
    {
        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await imageStream.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }

        _logger.LogInformation("Calling ML service with image size: {Size} bytes", imageBytes.Length);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            using var content = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
            content.Add(imageContent, "image", fileName);

            _logger.LogInformation("Sending request to ML service...");
            var response = await _httpClient.PostAsync(mlServiceUrl, content, cts.Token);

            _logger.LogInformation("ML service response status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("ML service error: {Error}", errorBody);

                return new RecognitionResult
                {
                    Found = false,
                    Message = $"ML service error: {response.StatusCode} - {errorBody}",
                };
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("ML service response body: {Body}", responseBody);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var mlResult = JsonSerializer.Deserialize<MlServiceResponse>(responseBody, options);

            if (mlResult == null || string.IsNullOrWhiteSpace(mlResult.Label))
            {
                _logger.LogInformation("No face recognized in the image. mlResult is {Result}", mlResult == null ? "null" : "not null");
                return new RecognitionResult { Found = false, Message = "No face recognized." };
            }
            if (mlResult == null || string.IsNullOrWhiteSpace(mlResult.Label))
            {
                _logger.LogInformation("No face recognized in the image");
                return new RecognitionResult { Found = false, Message = "No face recognized." };
            }

            _logger.LogInformation("Face recognized: {Label} with confidence {Confidence}",
                mlResult.Label, mlResult.Confidence);

            // === POPRAWIONE: wyciągamy tylko nazwę pliku bez ścieżki ===
            var fileNameOnly = Path.GetFileName(mlResult.Label);
            var recognizedName = CsvImportService.ExtractName(fileNameOnly);

            _logger.LogInformation("Looking for person - fileNameOnly: {FileName}, recognizedName: {Name}",
                fileNameOnly, recognizedName);

            // Szukamy po nazwie pliku (bez ścieżki) lub po imieniu i nazwisku
            var person = await _db.Persons
                .Where(p => p.ImageFileName == fileNameOnly || p.Name == recognizedName)
                .FirstOrDefaultAsync();

            if (person == null)
            {
                _logger.LogInformation("Auto-adding '{0}' (label: {1})", recognizedName, fileNameOnly);
                person = new Person
                {
                    Name = recognizedName,
                    ImageFileName = fileNameOnly,
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
        catch (TaskCanceledException)
        {
            _logger.LogError("ML service timeout after 60 seconds");
            return new RecognitionResult
            {
                Found = false,
                Message = "ML service timeout - recognition took too long",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML service error");
            return new RecognitionResult
            {
                Found = false,
                Message = $"ML service error: {ex.Message}",
            };
        }
    }

    private async Task SaveLogAsync(RecognitionResult result, string sourceFileName)
    {
        try
        {
            _db.RecognitionLogs.Add(new RecognitionLog
            {
                RecognizedAt = DateTime.UtcNow,
                Found = result.Found,
                PersonName = result.Person?.Name ?? "",
                Confidence = result.Confidence,
                Message = result.Message,
                ImageFileName = sourceFileName,
            });
            await _db.SaveChangesAsync();
            _logger.LogInformation("Saved recognition log for {FileName}", sourceFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save log");
        }
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

    public async Task<string?> AddPersonAsync(string name, Stream imageStream, string fileName)
    {
        var baseUrl = GetMlServiceBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;

        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await imageStream.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(name), "name");
        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
        content.Add(imageContent, "image", fileName);

        try
        {
            var response = await _httpClient.PostAsync($"{baseUrl}/add-person", content);
            response.EnsureSuccessStatusCode();
            var doc = await response.Content.ReadFromJsonAsync<AddPersonResponse>();
            return doc?.Filename;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddPerson failed");
            return null;
        }
    }

    private string? GetMlServiceBaseUrl()
    {
        var url = _config["MlService:Url"];
        if (string.IsNullOrWhiteSpace(url)) return null;

        var uri = new Uri(url);
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    private class MlServiceResponse
    {
        public string Label { get; set; } = "";
        public double Confidence { get; set; }
    }

    private class MlHealthResponse
    {
        public bool embeddings_ready { get; set; }
        public int images_in_dataset { get; set; }
    }

    private class AddPersonResponse
    {
        public string Filename { get; set; } = "";
    }
}
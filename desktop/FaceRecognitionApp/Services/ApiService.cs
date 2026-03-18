using System.Net.Http.Headers;
using System.Text.Json;
using FaceRecognitionApp.Models;

namespace FaceRecognitionApp.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

#if WINDOWS
    private const string BackendBaseUrl = "http://localhost:5233";
#elif ANDROID
    private const string BackendBaseUrl = "http://172.19.234.2:5233";  
#elif IOS || MACCATALYST
    private const string BackendBaseUrl = "http://192.168.0.251:5233";
#else
    private const string BackendBaseUrl = "http://192.168.0.251:5233";
#endif

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(BackendBaseUrl);
        _httpClient.Timeout = RequestTimeout;
    }

    public async Task<RecognitionResult?> RecognizeAsync(Stream imageStream, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var imgContent = new StreamContent(imageStream);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
        form.Add(imgContent, "image", fileName);

        var response = await _httpClient.PostAsync("api/faces/recognize", form);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<RecognitionResult>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<Person?> AddPersonAsync(string name, Stream imageStream, string fileName)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        var imgContent = new StreamContent(imageStream);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
        form.Add(imgContent, "image", fileName);

        var response = await _httpClient.PostAsync("api/persons", form);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Person>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<List<RecognitionLog>> GetRecentLogsAsync(int limit = 20)
    {
        var json = await _httpClient.GetStringAsync($"api/recognitions?limit={limit}");
        return JsonSerializer.Deserialize<List<RecognitionLog>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private static string GetMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".bmp"            => "image/bmp",
            _                 => "image/jpeg",
        };
}

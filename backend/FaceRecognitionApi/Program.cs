using FaceRecognitionApi.Data;
using FaceRecognitionApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=face_recognition.db";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<CsvImportService>();

// Konfiguracja HttpClient z większym timeoutem i lepszym logowaniem
builder.Services.AddHttpClient<IFaceRecognitionService, FaceRecognitionService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2); // 2 minuty timeout
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Pozwól na przekierowania
    AllowAutoRedirect = true,
    // Ignoruj błędy certyfikatów SSL (jeśli używasz HTTPS z self-signed cert)
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
});

builder.Services.AddControllers();
builder.Services.AddRazorPages();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Face Recognition API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Inicjalizacja bazy danych
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    if (!db.Persons.Any())
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Seeding database with initial data...");

        var datasetPath = builder.Configuration["DatasetPath"];
        if (!string.IsNullOrWhiteSpace(datasetPath) && Directory.Exists(datasetPath))
        {
            var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".bmp" };

            var files = Directory.EnumerateFiles(datasetPath, "*", SearchOption.AllDirectories)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f)
                .ToList();

            if (files.Count > 0)
            {
                var records = files
                    .GroupBy(f => FaceRecognitionApi.Services.CsvImportService.ExtractName(Path.GetFileName(f)))
                    .Select(g => new FaceRecognitionApi.Models.Person
                    {
                        Name = g.Key,
                        ImageFileName = Path.GetFileName(g.First()),
                    })
                    .ToList();

                db.Persons.AddRange(records);
                await db.SaveChangesAsync();
                logger.LogInformation("Auto-seeded {Count} persons from dataset folder", records.Count);
            }
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Face Recognition API v1"));
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors();
app.MapControllers();
app.MapRazorPages();

app.Run();

public partial class Program { }
using FaceRecognitionApi.Data;
using FaceRecognitionApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=face_recognition.db";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<CsvImportService>();
builder.Services.AddHttpClient<IFaceRecognitionService, FaceRecognitionService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
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
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    if (!db.Persons.Any())
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

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
                logger.LogInformation("Auto-seeded {Count} persons from dataset folder: {Path}", records.Count, datasetPath);
            }
        }
        else
        {
            var csvPath = Path.Combine(AppContext.BaseDirectory, "Data", "faces.csv");
            if (File.Exists(csvPath))
            {
                var csvImport = scope.ServiceProvider.GetRequiredService<CsvImportService>();
                var count = await csvImport.ImportAsync(csvPath);
                logger.LogInformation("Auto-seeded {Count} persons from bundled faces.csv", count);
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

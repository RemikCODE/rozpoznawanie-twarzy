using Microsoft.EntityFrameworkCore;
using FaceRecognitionApi.Models;

namespace FaceRecognitionApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Person> Persons { get; set; }
    public DbSet<RecognitionLog> RecognitionLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ImageFileName).IsRequired().HasMaxLength(500);
        });

        modelBuilder.Entity<RecognitionLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PersonName).HasMaxLength(200);
            entity.Property(e => e.Message).HasMaxLength(500);
            entity.Property(e => e.ImageFileName).HasMaxLength(500);
        });
    }
}

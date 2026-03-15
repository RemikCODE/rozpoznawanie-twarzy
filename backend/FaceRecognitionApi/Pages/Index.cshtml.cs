using FaceRecognitionApi.Data;
using FaceRecognitionApi.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FaceRecognitionApi.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<RecognitionLog> Logs { get; set; } = [];

    public async Task OnGetAsync()
    {
        Logs = await _db.RecognitionLogs
            .OrderByDescending(r => r.RecognizedAt)
            .Take(20)
            .ToListAsync();
    }
}

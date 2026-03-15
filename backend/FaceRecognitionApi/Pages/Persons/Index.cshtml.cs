using FaceRecognitionApi.Data;
using FaceRecognitionApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FaceRecognitionApi.Pages.Persons;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Person> Persons { get; set; } = [];
    public int Total { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);
    public string? Search { get; set; }

    public async Task OnGetAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        CurrentPage = Math.Max(1, page);
        PageSize = pageSize is >= 1 and <= 200 ? pageSize : 50;
        Search = search;

        var query = _db.Persons.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(p => p.Name.Contains(Search));
        }

        Total = await query.CountAsync();
        Persons = await query
            .OrderBy(p => p.Id)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}

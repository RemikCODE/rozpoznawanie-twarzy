using FaceRecognitionApi.Data;
using FaceRecognitionApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FaceRecognitionApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecognitionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public RecognitionsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RecognitionLog>>> GetRecent([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        var logs = await _db.RecognitionLogs
            .OrderByDescending(r => r.RecognizedAt)
            .Take(limit)
            .ToListAsync();
        return Ok(logs);
    }
}

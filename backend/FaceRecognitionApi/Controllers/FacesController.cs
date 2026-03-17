using FaceRecognitionApi.Models;
using FaceRecognitionApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace FaceRecognitionApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FacesController : ControllerBase
{
    private readonly IFaceRecognitionService _recognitionService;

    public FacesController(IFaceRecognitionService recognitionService)
    {
        _recognitionService = recognitionService;
    }

    [HttpPost("recognize")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(RecognitionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Recognize(IFormFile image)
    {
        if (image == null || image.Length == 0)
        {
            return BadRequest(new { message = "An image file is required." });
        }

        await using var stream = image.OpenReadStream();
        var result = await _recognitionService.RecognizeAsync(stream, image.FileName);
        return Ok(result);
    }
}

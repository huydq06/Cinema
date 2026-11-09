using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;

namespace WEB_BTL.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Policies.ManageCinema)]
public class UploadController(IFileStorage storage) : ControllerBase
{
    /// <summary>
    /// Upload một ảnh. Trả về URL public hoặc lỗi.
    /// </summary>
    [HttpPost("image")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadImage([FromForm] IFormFile file, [FromForm] string category = "posters")
    {
        if (file is null)
        {
            return BadRequest(new { error = "Thiếu file upload." });
        }

        var result = await storage.SaveImageAsync(file, category, HttpContext.RequestAborted);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(new { url = result.Url });
    }
}

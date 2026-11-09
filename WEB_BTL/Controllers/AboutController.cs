using Microsoft.AspNetCore.Mvc;

namespace WEB_BTL.Controllers;

[Route("gioi-thieu")]
public class AboutController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;

namespace WEB_BTL.Controllers;

[Route("quan-tri/nguoi-dung")]
[Authorize(Policy = Policies.ManageCinema)]
public class AdminUsersController(IAccountService accounts) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Roles"] = await accounts.ListRoleNamesAsync(ct);
        return View(await accounts.ListAsync(ct));
    }

    [HttpPost("{id:int}/kich-hoat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive, CancellationToken ct)
    {
        var result = await accounts.SetActiveAsync(id, isActive, User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded
                ? (isActive ? "Đã mở khóa tài khoản." : "Đã khóa tài khoản và thu hồi phiên đăng nhập.")
                : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/vai-tro")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(int id, string roleName, CancellationToken ct)
    {
        var result = await accounts.SetRoleAsync(id, roleName, User.GetUserId(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? $"Đã đổi vai trò thành {roleName}." : result.ErrorMessage;
        return RedirectToAction(nameof(Index));
    }
}

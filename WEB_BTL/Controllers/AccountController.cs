using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEB_BTL.Application;
using WEB_BTL.Application.DTOs;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;
using WEB_BTL.Models;

namespace WEB_BTL.Controllers;

[Route("account")]
public class AccountController(IAccountService accounts) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginRequest());
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginRequest request, string? returnUrl, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(request);
        }

        var result = await accounts.ValidateCredentialsAsync(request.Email, request.Password, ct);
        if (!result.Succeeded || result.Value is null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Đăng nhập thất bại.");
            return View(request);
        }

        await SignInAsync(result.Value, request.RememberMe);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return result.Value.Role switch
        {
            Roles.Admin => RedirectToAction("Index", "Dashboard"),
            Roles.Staff => RedirectToAction("Index", "Counter"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    [HttpGet("register")]
    public IActionResult Register() => View(new RegisterRequest());

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(request);
        }

        var result = await accounts.RegisterAsync(request, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Đăng ký thất bại.");
            return View(request);
        }

        var login = await accounts.GetCurrentAsync(result.Value, ct);
        if (login is not null)
        {
            await SignInAsync(login, rememberMe: false);
        }

        TempData["Success"] = "Đăng ký thành công. Chào mừng bạn!";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var current = await accounts.GetCurrentAsync(userId, ct);
        if (current is null)
        {
            return Challenge();
        }

        return View(current);
    }

    [Authorize]
    [HttpGet("change-password")]
    public IActionResult ChangePassword() => View(new ChangePasswordRequest());

    [Authorize]
    [HttpPost("change-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(request);
        }

        var result = await accounts.ChangePasswordAsync(User.GetUserId(), request, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Đổi mật khẩu thất bại.");
            return View(request);
        }

        // SecurityVersion đã tăng nên cookie hiện tại bị thu hồi; đăng nhập lại là bắt buộc.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "Đã đổi mật khẩu. Vui lòng đăng nhập lại.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("denied")]
    public IActionResult Denied() => View();

    private async Task SignInAsync(CurrentUserDto user, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(ClaimNames.SecurityVersion, user.SecurityVersion.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(rememberMe ? TimeSpan.FromDays(14) : TimeSpan.FromHours(8))
            });
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}

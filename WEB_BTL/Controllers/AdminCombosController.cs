using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEB_BTL.Application.Services;
using WEB_BTL.Domain.Constants;
using WEB_BTL.Domain.Entities;
using WEB_BTL.Infrastructure.Data;

namespace WEB_BTL.Controllers;

[Authorize(Policy = Policies.ManageCinema)]
[Route("quan-tri/combo")]
public class AdminCombosController(CinemaDbContext db, IAuditService audit) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var combos = await db.FoodCombos.OrderBy(c => c.Price).ToListAsync();
        return View(combos);
    }

    [HttpGet("tao-moi")]
    public IActionResult Create() => View(new FoodCombo { IsAvailable = true });

    [HttpPost("tao-moi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FoodCombo input)
    {
        ValidateCombo(input);

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var combo = new FoodCombo
        {
            Name = input.Name.Trim(),
            Description = NullIfBlank(input.Description),
            Price = input.Price,
            ImageUrl = NullIfBlank(input.ImageUrl),
            IsAvailable = input.IsAvailable
        };

        db.FoodCombos.Add(combo);
        await db.SaveChangesAsync();

        await audit.LogAsync(User.GetUserId(), "Combo.Created", nameof(FoodCombo),
            combo.ComboId.ToString(), new { combo.Name, combo.Price }, HttpContext.RequestAborted);

        TempData["Success"] = $"Đã thêm combo {combo.Name}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/sua")]
    public async Task<IActionResult> Edit(int id)
    {
        var combo = await db.FoodCombos.FindAsync(id);
        return combo is null ? NotFound() : View(combo);
    }

    [HttpPost("{id:int}/sua")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FoodCombo input)
    {
        var combo = await db.FoodCombos.FindAsync(id);
        if (combo is null)
        {
            return NotFound();
        }

        ValidateCombo(input);
        if (!ModelState.IsValid)
        {
            input.ComboId = id;
            return View(input);
        }

        combo.Name = input.Name.Trim();
        combo.Description = NullIfBlank(input.Description);
        combo.Price = input.Price;
        combo.ImageUrl = NullIfBlank(input.ImageUrl);
        combo.IsAvailable = input.IsAvailable;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty, "Dữ liệu đã được người khác cập nhật. Vui lòng tải lại trang.");
            return View(input);
        }

        await audit.LogAsync(User.GetUserId(), "Combo.Updated", nameof(FoodCombo),
            id.ToString(), new { combo.Name, combo.Price, combo.IsAvailable }, HttpContext.RequestAborted);

        TempData["Success"] = $"Đã cập nhật combo {combo.Name}.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateCombo(FoodCombo input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            ModelState.AddModelError(nameof(input.Name), "Nhập tên combo.");
        }

        if (input.Price < 0)
        {
            ModelState.AddModelError(nameof(input.Price), "Giá không được âm.");
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

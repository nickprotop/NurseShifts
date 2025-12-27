using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NurseShifts.Data;
using NurseShifts.Models;

namespace NurseShifts.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private readonly AppDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public SettingsController(AppDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var settings = await _context.SystemSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new SystemSettings();
            _context.SystemSettings.Add(settings);
            await _context.SaveChangesAsync();
        }
        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SystemSettings model)
    {
        if (ModelState.IsValid)
        {
            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            if (settings != null)
            {
                settings.DefaultContractedHoursPerWeek = model.DefaultContractedHoursPerWeek;
                settings.DefaultMaxOvertimeHoursPerWeek = model.DefaultMaxOvertimeHoursPerWeek;
                settings.DefaultMinRestHoursBetweenShifts = model.DefaultMinRestHoursBetweenShifts;
                settings.DefaultMaxConsecutiveWorkDays = model.DefaultMaxConsecutiveWorkDays;
                settings.Language = model.Language;

                await _context.SaveChangesAsync();

                // Set the culture cookie
                Response.Cookies.Append(
                    ".AspNetCore.Culture",
                    $"c={model.Language}|uic={model.Language}",
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
                );

                TempData["Success"] = _localizer["SavedSuccessfully"].Value;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ChangePassword()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        if (newPassword != confirmPassword)
        {
            ModelState.AddModelError(string.Empty, _localizer["PasswordMismatch"]);
            return View();
        }

        var settings = await _context.SystemSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            ModelState.AddModelError(string.Empty, _localizer["SettingsNotFound"]);
            return View();
        }

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, settings.AdminPasswordHash))
        {
            ModelState.AddModelError(string.Empty, _localizer["IncorrectPassword"]);
            return View();
        }

        settings.AdminPasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _context.SaveChangesAsync();

        TempData["Success"] = _localizer["SavedSuccessfully"].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SetLanguage(string language, string returnUrl)
    {
        var settings = await _context.SystemSettings.FirstOrDefaultAsync();
        if (settings != null)
        {
            settings.Language = language;
            await _context.SaveChangesAsync();
        }

        // Set the culture cookie
        Response.Cookies.Append(
            ".AspNetCore.Culture",
            $"c={language}|uic={language}",
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
        );

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }
}

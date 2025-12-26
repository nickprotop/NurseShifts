using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;

namespace NurseShifts.Controllers;

[Authorize]
public class ClinicsController : Controller
{
    private readonly AppDbContext _context;

    public ClinicsController(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var clinics = await _context.Clinics
            .Include(c => c.HeadNurse)
            .Include(c => c.Nurses)
            .ToListAsync();
        return View(clinics);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var clinic = await _context.Clinics
            .Include(c => c.HeadNurse)
            .Include(c => c.Nurses)
            .Include(c => c.ShiftConfigurations)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (clinic == null) return NotFound();

        return View(clinic);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Clinic clinic)
    {
        if (ModelState.IsValid)
        {
            _context.Add(clinic);
            await _context.SaveChangesAsync();

            // Create default shift configurations
            foreach (ShiftType shiftType in Enum.GetValues<ShiftType>())
            {
                _context.ShiftConfigurations.Add(new ShiftConfiguration
                {
                    ClinicId = clinic.Id,
                    ShiftType = shiftType,
                    RequiredNurses = 2,
                    RequiresResponsibleNurse = true,
                    ShiftDurationHours = 8
                });
            }
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
        return View(clinic);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var clinic = await _context.Clinics
            .Include(c => c.Nurses)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (clinic == null) return NotFound();

        ViewBag.Nurses = new SelectList(
            clinic.Nurses.Where(n => n.IsActive),
            "Id", "FullName", clinic.HeadNurseId);

        return View(clinic);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Clinic clinic)
    {
        if (id != clinic.Id) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(clinic);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Clinics.AnyAsync(c => c.Id == id))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }

        var existingClinic = await _context.Clinics.Include(c => c.Nurses).FirstOrDefaultAsync(c => c.Id == id);
        ViewBag.Nurses = new SelectList(
            existingClinic?.Nurses.Where(n => n.IsActive) ?? Enumerable.Empty<Nurse>(),
            "Id", "FullName", clinic.HeadNurseId);

        return View(clinic);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var clinic = await _context.Clinics
            .Include(c => c.Nurses)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (clinic == null) return NotFound();

        return View(clinic);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var clinic = await _context.Clinics.FindAsync(id);
        if (clinic != null)
        {
            _context.Clinics.Remove(clinic);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // Shift Configuration
    public async Task<IActionResult> Configure(int? id)
    {
        if (id == null) return NotFound();

        var clinic = await _context.Clinics
            .Include(c => c.ShiftConfigurations)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (clinic == null) return NotFound();

        return View(clinic);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configure(int id, bool autoAssignHeadNurseMorning, List<ShiftConfiguration> configs)
    {
        var clinic = await _context.Clinics
            .Include(c => c.ShiftConfigurations)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (clinic == null) return NotFound();

        // Update head nurse auto-assign setting
        clinic.AutoAssignHeadNurseMorning = autoAssignHeadNurseMorning;

        // Update shift configurations
        foreach (var config in configs)
        {
            config.ClinicId = id;
            var existing = clinic.ShiftConfigurations.FirstOrDefault(c => c.ShiftType == config.ShiftType);
            if (existing != null)
            {
                existing.RequiredNurses = config.RequiredNurses;
                existing.RequiredSeniorNurses = config.RequiredSeniorNurses;
                existing.ShiftDurationHours = config.ShiftDurationHours;
                existing.RequiresResponsibleNurse = config.RequiresResponsibleNurse;
            }
            else
            {
                _context.ShiftConfigurations.Add(config);
            }
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveConfiguration(int clinicId, List<ShiftConfiguration> configurations)
    {
        foreach (var config in configurations)
        {
            config.ClinicId = clinicId;
            if (config.Id == 0)
            {
                _context.ShiftConfigurations.Add(config);
            }
            else
            {
                _context.ShiftConfigurations.Update(config);
            }
        }
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = clinicId });
    }
}

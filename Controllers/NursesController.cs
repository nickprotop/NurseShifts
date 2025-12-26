using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;

namespace NurseShifts.Controllers;

[Authorize]
public class NursesController : Controller
{
    private readonly AppDbContext _context;

    public NursesController(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(int? clinicId)
    {
        var query = _context.Nurses
            .Include(n => n.PrimaryClinic)
            .AsQueryable();

        if (clinicId.HasValue)
        {
            query = query.Where(n => n.PrimaryClinicId == clinicId.Value);
            ViewBag.ClinicFilter = clinicId.Value;
        }

        ViewBag.Clinics = new SelectList(await _context.Clinics.ToListAsync(), "Id", "Name", clinicId);

        return View(await query.OrderBy(n => n.LastName).ThenBy(n => n.FirstName).ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var nurse = await _context.Nurses
            .Include(n => n.PrimaryClinic)
            .Include(n => n.ClinicAssignments).ThenInclude(ca => ca.Clinic)
            .Include(n => n.Leaves.OrderByDescending(l => l.StartDate).Take(5))
            .Include(n => n.ShiftWishes.Where(w => w.Date >= DateOnly.FromDateTime(DateTime.Today)).OrderBy(w => w.Date).Take(10))
            .Include(n => n.OvertimeBalances.OrderByDescending(o => o.WeekStartDate).Take(4))
            .FirstOrDefaultAsync(n => n.Id == id);

        if (nurse == null) return NotFound();

        return View(nurse);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Clinics = new SelectList(await _context.Clinics.ToListAsync(), "Id", "Name");
        ViewBag.AllClinics = await _context.Clinics.ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Nurse nurse, int[] borrowableClinics)
    {
        if (ModelState.IsValid)
        {
            _context.Add(nurse);
            await _context.SaveChangesAsync();

            // Add borrowable clinic assignments
            foreach (var clinicId in borrowableClinics)
            {
                if (clinicId != nurse.PrimaryClinicId)
                {
                    _context.NurseClinicAssignments.Add(new NurseClinicAssignment
                    {
                        NurseId = nurse.Id,
                        ClinicId = clinicId,
                        CanBeBorrowed = true
                    });
                }
            }
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        ViewBag.Clinics = new SelectList(await _context.Clinics.ToListAsync(), "Id", "Name", nurse.PrimaryClinicId);
        ViewBag.AllClinics = await _context.Clinics.ToListAsync();
        return View(nurse);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var nurse = await _context.Nurses
            .Include(n => n.ClinicAssignments)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (nurse == null) return NotFound();

        ViewBag.Clinics = new SelectList(await _context.Clinics.ToListAsync(), "Id", "Name", nurse.PrimaryClinicId);
        ViewBag.AllClinics = await _context.Clinics.ToListAsync();
        ViewBag.BorrowableClinics = nurse.ClinicAssignments.Where(ca => ca.CanBeBorrowed).Select(ca => ca.ClinicId).ToList();

        return View(nurse);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Nurse nurse, int[] borrowableClinics)
    {
        if (id != nurse.Id) return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(nurse);

                // Update borrowable clinic assignments
                var existingAssignments = await _context.NurseClinicAssignments
                    .Where(nca => nca.NurseId == nurse.Id)
                    .ToListAsync();
                _context.NurseClinicAssignments.RemoveRange(existingAssignments);

                foreach (var clinicId in borrowableClinics)
                {
                    if (clinicId != nurse.PrimaryClinicId)
                    {
                        _context.NurseClinicAssignments.Add(new NurseClinicAssignment
                        {
                            NurseId = nurse.Id,
                            ClinicId = clinicId,
                            CanBeBorrowed = true
                        });
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Nurses.AnyAsync(n => n.Id == id))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Clinics = new SelectList(await _context.Clinics.ToListAsync(), "Id", "Name", nurse.PrimaryClinicId);
        ViewBag.AllClinics = await _context.Clinics.ToListAsync();
        ViewBag.BorrowableClinics = borrowableClinics.ToList();
        return View(nurse);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var nurse = await _context.Nurses
            .Include(n => n.PrimaryClinic)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (nurse == null) return NotFound();

        return View(nurse);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var nurse = await _context.Nurses.FindAsync(id);
        if (nurse != null)
        {
            _context.Nurses.Remove(nurse);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var nurse = await _context.Nurses.FindAsync(id);
        if (nurse != null)
        {
            nurse.IsActive = !nurse.IsActive;
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;

namespace NurseShifts.Controllers;

[Authorize]
public class LeaveController : Controller
{
    private readonly AppDbContext _context;

    public LeaveController(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(LeaveStatus? status, int? nurseId)
    {
        var query = _context.NurseLeaves
            .Include(l => l.Nurse)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(l => l.Status == status.Value);
        }

        if (nurseId.HasValue)
        {
            query = query.Where(l => l.NurseId == nurseId.Value);
        }

        ViewBag.StatusFilter = status;
        ViewBag.NurseFilter = nurseId;
        ViewBag.Nurses = new SelectList(
            await _context.Nurses.Where(n => n.IsActive).OrderBy(n => n.LastName).ToListAsync(),
            "Id", "FullName", nurseId);

        return View(await query.OrderByDescending(l => l.StartDate).ToListAsync());
    }

    public async Task<IActionResult> Create(int? nurseId)
    {
        ViewBag.Nurses = new SelectList(
            await _context.Nurses.Where(n => n.IsActive).OrderBy(n => n.LastName).ToListAsync(),
            "Id", "FullName", nurseId);

        return View(new NurseLeave { NurseId = nurseId ?? 0 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NurseLeave leave)
    {
        if (leave.EndDate < leave.StartDate)
        {
            ModelState.AddModelError("EndDate", "End date must be after start date.");
        }

        if (ModelState.IsValid)
        {
            leave.CreatedAt = DateTime.UtcNow;
            _context.Add(leave);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Nurses = new SelectList(
            await _context.Nurses.Where(n => n.IsActive).OrderBy(n => n.LastName).ToListAsync(),
            "Id", "FullName", leave.NurseId);

        return View(leave);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var leave = await _context.NurseLeaves.FindAsync(id);
        if (leave == null) return NotFound();

        ViewBag.Nurses = new SelectList(
            await _context.Nurses.Where(n => n.IsActive).OrderBy(n => n.LastName).ToListAsync(),
            "Id", "FullName", leave.NurseId);

        return View(leave);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, NurseLeave leave)
    {
        if (id != leave.Id) return NotFound();

        if (leave.EndDate < leave.StartDate)
        {
            ModelState.AddModelError("EndDate", "End date must be after start date.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(leave);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.NurseLeaves.AnyAsync(l => l.Id == id))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Nurses = new SelectList(
            await _context.Nurses.Where(n => n.IsActive).OrderBy(n => n.LastName).ToListAsync(),
            "Id", "FullName", leave.NurseId);

        return View(leave);
    }

    [HttpPost]
    public async Task<IActionResult> Approve(int id)
    {
        var leave = await _context.NurseLeaves.FindAsync(id);
        if (leave != null)
        {
            leave.Status = LeaveStatus.Approved;
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Reject(int id)
    {
        var leave = await _context.NurseLeaves.FindAsync(id);
        if (leave != null)
        {
            leave.Status = LeaveStatus.Rejected;
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var leave = await _context.NurseLeaves
            .Include(l => l.Nurse)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (leave == null) return NotFound();

        return View(leave);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var leave = await _context.NurseLeaves.FindAsync(id);
        if (leave != null)
        {
            _context.NurseLeaves.Remove(leave);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;
using NurseShifts.Models.ViewModels;
using NurseShifts.Services;

namespace NurseShifts.Controllers;

[Authorize]
public class ScheduleController : Controller
{
    private readonly AppDbContext _context;
    private readonly IScheduleService _scheduleService;
    private readonly IValidationService _validationService;
    private readonly INurseAvailabilityService _availabilityService;

    public ScheduleController(
        AppDbContext context,
        IScheduleService scheduleService,
        IValidationService validationService,
        INurseAvailabilityService availabilityService)
    {
        _context = context;
        _scheduleService = scheduleService;
        _validationService = validationService;
        _availabilityService = availabilityService;
    }

    public async Task<IActionResult> Index(int? clinicId, DateOnly? startDate, int days = 7)
    {
        var clinics = await _context.Clinics.ToListAsync();
        ViewBag.Clinics = new SelectList(clinics, "Id", "Name", clinicId);

        if (!clinicId.HasValue && clinics.Any())
        {
            clinicId = clinics.First().Id;
        }

        var start = startDate ?? DateOnly.FromDateTime(DateTime.Today);
        var end = start.AddDays(days - 1);

        var viewModel = new ScheduleViewModel
        {
            ClinicId = clinicId ?? 0,
            StartDate = start,
            Days = days,
            Assignments = clinicId.HasValue
                ? await _scheduleService.GetScheduleAsync(clinicId.Value, start, end)
                : new List<ShiftAssignment>(),
            Clinic = clinicId.HasValue ? await _context.Clinics.FindAsync(clinicId.Value) : null
        };

        // Build schedule grid
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var daySchedule = new DaySchedule { Date = date };

            foreach (ShiftType shiftType in Enum.GetValues<ShiftType>())
            {
                var shiftAssignments = viewModel.Assignments
                    .Where(a => a.Date == date && a.ShiftType == shiftType)
                    .ToList();

                var config = await _context.ShiftConfigurations
                    .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.ShiftType == shiftType);

                var validation = clinicId.HasValue
                    ? await _validationService.ValidateShiftCoverageAsync(clinicId.Value, date, shiftType)
                    : new ValidationResult { IsValid = true };

                daySchedule.Shifts.Add(new ShiftSchedule
                {
                    ShiftType = shiftType,
                    Assignments = shiftAssignments,
                    RequiredNurses = config?.RequiredNurses ?? 0,
                    IsValid = validation.IsValid,
                    Warnings = validation.Warnings,
                    Errors = validation.Errors
                });
            }

            viewModel.DaySchedules.Add(daySchedule);
        }

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> GetAvailableNurses(int clinicId, string date, ShiftType shiftType)
    {
        var dateOnly = DateOnly.Parse(date);
        var available = await _availabilityService.GetAvailableNursesAsync(clinicId, dateOnly, shiftType, true);

        var result = available
            .OrderByDescending(a => a.IsAvailable)
            .ThenByDescending(a => a.Score)
            .Select(a => new
            {
                nurseId = a.Nurse.Id,
                name = a.Nurse.FullName,
                isAvailable = a.IsAvailable,
                isBorrowed = a.IsBorrowed,
                score = a.Score,
                reasons = a.UnavailabilityReasons,
                canHandleResponsibility = a.Nurse.CanHandleResponsibility
            });

        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> AssignNurse(int clinicId, string date, ShiftType shiftType, int nurseId, bool isResponsible)
    {
        var dateOnly = DateOnly.Parse(date);
        var assignment = await _scheduleService.AssignNurseToShiftAsync(clinicId, dateOnly, shiftType, nurseId, isResponsible);

        if (assignment == null)
        {
            return BadRequest("Could not assign nurse to this shift.");
        }

        return Ok(new { id = assignment.Id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveAssignment(int assignmentId)
    {
        var success = await _scheduleService.RemoveAssignmentAsync(assignmentId);
        return success ? Ok() : BadRequest();
    }

    [HttpPost]
    public async Task<IActionResult> Generate(int clinicId, string startDate, int days)
    {
        var start = DateOnly.Parse(startDate);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Block past date ranges
        if (start < today)
        {
            TempData["Error"] = "CannotGeneratePastDates";
            return RedirectToAction(nameof(Index), new { clinicId, startDate, days });
        }

        // Clear existing assignments for this period
        var existingAssignments = await _context.ShiftAssignments
            .Where(sa => sa.ClinicId == clinicId && sa.Date >= start && sa.Date < start.AddDays(days))
            .ToListAsync();
        _context.ShiftAssignments.RemoveRange(existingAssignments);
        await _context.SaveChangesAsync();

        // Generate new schedule
        var assignments = await _scheduleService.GenerateScheduleAsync(clinicId, start, days);

        return RedirectToAction(nameof(Index), new { clinicId, startDate = start.ToString("yyyy-MM-dd"), days });
    }

    [HttpGet]
    public async Task<IActionResult> ValidateShift(int clinicId, string date, ShiftType shiftType)
    {
        var dateOnly = DateOnly.Parse(date);
        var result = await _validationService.ValidateShiftCoverageAsync(clinicId, dateOnly, shiftType);

        return Json(new
        {
            isValid = result.IsValid,
            errors = result.Errors,
            warnings = result.Warnings
        });
    }
}

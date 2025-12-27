using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
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
    private readonly ICompositeViewEngine _viewEngine;
    private readonly IExportService _exportService;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ScheduleController(
        AppDbContext context,
        IScheduleService scheduleService,
        IValidationService validationService,
        INurseAvailabilityService availabilityService,
        ICompositeViewEngine viewEngine,
        IExportService exportService,
        IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _scheduleService = scheduleService;
        _validationService = validationService;
        _availabilityService = availabilityService;
        _viewEngine = viewEngine;
        _exportService = exportService;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index(int? clinicId, DateOnly? startDate, int weeks = 1)
    {
        var clinics = await _context.Clinics.ToListAsync();
        ViewBag.Clinics = new SelectList(clinics, "Id", "Name", clinicId);

        if (!clinicId.HasValue && clinics.Any())
        {
            clinicId = clinics.First().Id;
        }

        // Snap start date to Monday of that week
        var rawStart = startDate ?? DateOnly.FromDateTime(DateTime.Today);
        var start = GetWeekStart(rawStart);
        var end = GetWeekEnd(start, weeks);

        // Run unified scheduling algorithm for the next 2 weeks
        if (clinicId.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var autoScheduleEnd = CalculateAutoFillEndDate(today);

            // Only schedule if some of the view range is in the future
            if (end >= today)
            {
                var autoScheduleStart = start > today ? start : today;
                var scheduleResult = await _scheduleService.ScheduleAsync(clinicId.Value, autoScheduleStart, autoScheduleEnd);

                // Store result for display if any changes were made
                if (scheduleResult.AssignmentsAdded > 0 || scheduleResult.AssignmentsRemoved > 0 || scheduleResult.CompDaysGranted > 0)
                {
                    ViewBag.ScheduleResult = scheduleResult;
                }
            }
        }

        // Check for result from redirect (manual button click)
        if (TempData["ScheduleResult"] is string resultJson)
        {
            ViewBag.ScheduleResult = System.Text.Json.JsonSerializer.Deserialize<ScheduleResult>(resultJson);
        }

        var viewModel = new ScheduleViewModel
        {
            ClinicId = clinicId ?? 0,
            StartDate = start,
            Weeks = weeks,
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
    public async Task<IActionResult> AssignNurse(int clinicId, string date, ShiftType shiftType, int nurseId, bool isResponsible, bool isManual = false)
    {
        var dateOnly = DateOnly.Parse(date);
        var assignment = await _scheduleService.AssignNurseToShiftAsync(clinicId, dateOnly, shiftType, nurseId, isResponsible);

        if (assignment == null)
        {
            return BadRequest(new { success = false, error = _localizer["CouldNotAssignNurse"].Value });
        }

        // Set manual flag if requested
        if (isManual)
        {
            assignment.IsManualAssignment = true;
            await _context.SaveChangesAsync();
        }

        // Return cell HTML for AJAX updates
        var cellModel = await BuildShiftCellViewModelAsync(clinicId, dateOnly, shiftType);
        var cellHtml = await RenderPartialViewAsync("_ShiftCell", cellModel);

        return Json(new
        {
            success = true,
            id = assignment.Id,
            isManual = assignment.IsManualAssignment,
            cellHtml = cellHtml,
            validation = new
            {
                isValid = cellModel.IsValid,
                errors = cellModel.Errors,
                warnings = cellModel.Warnings
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveAssignment(int assignmentId, bool force = false)
    {
        var assignment = await _context.ShiftAssignments
            .Include(a => a.Nurse)
            .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment == null) return NotFound();

        // Prevent removing manual assignments unless forced
        if (assignment.IsManualAssignment && !force)
        {
            return BadRequest(new { success = false, error = "ManualAssignmentCannotBeRemoved", isManual = true });
        }

        var clinicId = assignment.ClinicId;
        var date = assignment.Date;
        var shiftType = assignment.ShiftType;

        var success = await _scheduleService.RemoveAssignmentAsync(assignmentId);

        if (!success)
        {
            return BadRequest(new { success = false, error = _localizer["FailedToRemoveAssignment"].Value });
        }

        // Return updated cell HTML for AJAX
        var cellModel = await BuildShiftCellViewModelAsync(clinicId, date, shiftType);
        var cellHtml = await RenderPartialViewAsync("_ShiftCell", cellModel);

        return Json(new
        {
            success = true,
            cellHtml = cellHtml,
            validation = new
            {
                isValid = cellModel.IsValid,
                errors = cellModel.Errors,
                warnings = cellModel.Warnings
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> ToggleManual(int assignmentId)
    {
        var assignment = await _context.ShiftAssignments
            .Include(a => a.Nurse)
            .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment == null) return NotFound();

        assignment.IsManualAssignment = !assignment.IsManualAssignment;
        await _context.SaveChangesAsync();

        // Return updated cell HTML for AJAX
        var cellModel = await BuildShiftCellViewModelAsync(assignment.ClinicId, assignment.Date, assignment.ShiftType);
        var cellHtml = await RenderPartialViewAsync("_ShiftCell", cellModel);

        return Json(new
        {
            success = true,
            isManual = assignment.IsManualAssignment,
            cellHtml = cellHtml,
            validation = new
            {
                isValid = cellModel.IsValid,
                errors = cellModel.Errors,
                warnings = cellModel.Warnings
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> ClearAndRebuild(int clinicId, string startDate, int weeks, bool recalculateCompDays = false)
    {
        // Snap to Monday
        var rawStart = DateOnly.Parse(startDate);
        var start = GetWeekStart(rawStart);
        var end = GetWeekEnd(start, weeks);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Block past date ranges
        if (start < today)
        {
            TempData["Error"] = "CannotClearPastDates";
            return RedirectToAction(nameof(Index), new { clinicId, startDate = start.ToString("yyyy-MM-dd"), weeks });
        }

        // Clear and rebuild schedule
        var result = await _scheduleService.ClearAndRebuildAsync(clinicId, start, end, recalculateCompDays);

        // Store result for display
        TempData["ScheduleResult"] = System.Text.Json.JsonSerializer.Serialize(result);

        return RedirectToAction(nameof(Index), new { clinicId, startDate = start.ToString("yyyy-MM-dd"), weeks });
    }

    [HttpPost]
    public async Task<IActionResult> Schedule(int clinicId, string startDate, int weeks)
    {
        var start = GetWeekStart(DateOnly.Parse(startDate));
        var end = GetWeekEnd(start, weeks);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Scheduling only works for today onwards
        var scheduleStart = start > today ? start : today;
        if (scheduleStart > end)
        {
            // Entire range is in the past
            TempData["Error"] = "CannotSchedulePastDates";
            return RedirectToAction(nameof(Index), new { clinicId, startDate = start.ToString("yyyy-MM-dd"), weeks });
        }

        var result = await _scheduleService.ScheduleAsync(clinicId, scheduleStart, end);

        // Store result for display
        TempData["ScheduleResult"] = System.Text.Json.JsonSerializer.Serialize(result);

        return RedirectToAction(nameof(Index), new { clinicId, startDate = start.ToString("yyyy-MM-dd"), weeks });
    }

    // Legacy action - redirects to new name
    [HttpPost]
    [Obsolete("Use ClearAndRebuild instead")]
    public Task<IActionResult> Generate(int clinicId, string startDate, int weeks)
        => ClearAndRebuild(clinicId, startDate, weeks, false);

    // Legacy action - redirects to new name
    [HttpPost]
    [Obsolete("Use Schedule instead")]
    public Task<IActionResult> AutoFill(int clinicId, string startDate, int weeks)
        => Schedule(clinicId, startDate, weeks);

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

    [HttpGet]
    public async Task<IActionResult> GetWorkloadSummary(int clinicId, string startDate, int weeks)
    {
        var start = GetWeekStart(DateOnly.Parse(startDate));
        var end = GetWeekEnd(start, weeks);
        var days = weeks * 7;

        // Get all assignments for the period
        var assignments = await _context.ShiftAssignments
            .Include(a => a.Nurse)
            .Where(a => a.ClinicId == clinicId && a.Date >= start && a.Date <= end)
            .ToListAsync();

        // Group by nurse and calculate hours
        var workloads = assignments
            .GroupBy(a => a.NurseId)
            .Select(g =>
            {
                var nurse = g.First().Nurse!;
                var shiftHours = g.Sum(a => GetShiftHours(a.ShiftType));
                return new NurseWorkload
                {
                    NurseId = nurse.Id,
                    NurseName = nurse.FullName,
                    TotalHours = shiftHours,
                    ContractedHours = nurse.ContractedHoursPerWeek * (days / 7m),
                    ShiftCount = g.Count()
                };
            })
            .OrderByDescending(w => w.TotalHours)
            .ToList();

        var viewModel = new WorkloadSummaryViewModel
        {
            ClinicId = clinicId,
            StartDate = start,
            EndDate = end,
            Workloads = workloads
        };

        return Json(viewModel);
    }

    private static decimal GetShiftHours(ShiftType shiftType) => shiftType switch
    {
        ShiftType.Morning => 8,
        ShiftType.Afternoon => 8,
        ShiftType.Night => 8,
        _ => 8
    };

    [HttpGet]
    public async Task<IActionResult> ExportPdf(int clinicId, string startDate, int weeks = 1)
    {
        var schedule = await BuildScheduleViewModelAsync(clinicId, DateOnly.Parse(startDate), weeks);
        var pdfBytes = _exportService.ExportScheduleToPdf(schedule);

        var fileName = $"Schedule_{schedule.Clinic?.Name ?? "Clinic"}_{schedule.StartDate:yyyy-MM-dd}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(int clinicId, string startDate, int weeks = 1)
    {
        var schedule = await BuildScheduleViewModelAsync(clinicId, DateOnly.Parse(startDate), weeks);
        var excelBytes = _exportService.ExportScheduleToExcel(schedule);

        var fileName = $"Schedule_{schedule.Clinic?.Name ?? "Clinic"}_{schedule.StartDate:yyyy-MM-dd}.xlsx";
        return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    /// <summary>
    /// Build a full ScheduleViewModel for export
    /// </summary>
    private async Task<ScheduleViewModel> BuildScheduleViewModelAsync(int clinicId, DateOnly startDate, int weeks)
    {
        // Snap to Monday
        var start = GetWeekStart(startDate);
        var end = GetWeekEnd(start, weeks);

        var viewModel = new ScheduleViewModel
        {
            ClinicId = clinicId,
            StartDate = start,
            Weeks = weeks,
            Assignments = await _scheduleService.GetScheduleAsync(clinicId, start, end),
            Clinic = await _context.Clinics.FindAsync(clinicId)
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

                var validation = await _validationService.ValidateShiftCoverageAsync(clinicId, date, shiftType);

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

        return viewModel;
    }

    /// <summary>
    /// Build a ShiftCellViewModel for the given shift
    /// </summary>
    private async Task<ShiftCellViewModel> BuildShiftCellViewModelAsync(int clinicId, DateOnly date, ShiftType shiftType)
    {
        var assignments = await _context.ShiftAssignments
            .Include(a => a.Nurse)
            .Where(a => a.ClinicId == clinicId && a.Date == date && a.ShiftType == shiftType)
            .ToListAsync();

        var config = await _context.ShiftConfigurations
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.ShiftType == shiftType);

        var validation = await _validationService.ValidateShiftCoverageAsync(clinicId, date, shiftType);

        return new ShiftCellViewModel
        {
            ClinicId = clinicId,
            Date = date,
            ShiftType = shiftType,
            Assignments = assignments,
            RequiredNurses = config?.RequiredNurses ?? 0,
            IsValid = validation.IsValid,
            Warnings = validation.Warnings,
            Errors = validation.Errors
        };
    }

    /// <summary>
    /// Render a partial view to a string for AJAX responses
    /// </summary>
    private async Task<string> RenderPartialViewAsync<TModel>(string viewName, TModel model)
    {
        ViewData.Model = model;

        using var writer = new StringWriter();
        var viewResult = _viewEngine.FindView(ControllerContext, viewName, false);

        if (!viewResult.Success)
        {
            throw new InvalidOperationException($"View '{viewName}' not found.");
        }

        var viewContext = new ViewContext(
            ControllerContext,
            viewResult.View,
            ViewData,
            TempData,
            writer,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    /// <summary>
    /// Calculate the end date for auto-fill: Sunday of the following week (2 weeks from current week's Monday)
    /// </summary>
    private static DateOnly CalculateAutoFillEndDate(DateOnly today)
    {
        var thisMonday = GetWeekStart(today);
        return GetWeekEnd(thisMonday, 2); // End of second week (following Sunday)
    }

    /// <summary>
    /// Returns the Monday of the week containing the given date
    /// </summary>
    private static DateOnly GetWeekStart(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek - 1 + 7) % 7;
        return date.AddDays(-daysFromMonday);
    }

    /// <summary>
    /// Returns the Sunday of the last week in the range (weekStart + weeks - 1)
    /// </summary>
    private static DateOnly GetWeekEnd(DateOnly weekStart, int weeks)
    {
        return weekStart.AddDays(weeks * 7 - 1);
    }
}

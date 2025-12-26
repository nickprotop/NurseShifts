using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;

namespace NurseShifts.Services;

public class ValidationService : IValidationService
{
    private readonly AppDbContext _context;
    private readonly INurseAvailabilityService _availabilityService;

    public ValidationService(AppDbContext context, INurseAvailabilityService availabilityService)
    {
        _context = context;
        _availabilityService = availabilityService;
    }

    public async Task<bool> CanNurseWorkShiftAsync(int nurseId, DateOnly date, ShiftType shiftType, int clinicId)
    {
        var result = await ValidateShiftAssignmentAsync(clinicId, date, shiftType, nurseId);
        return result.IsValid;
    }

    public async Task<ValidationResult> ValidateShiftAssignmentAsync(int clinicId, DateOnly date, ShiftType shiftType, int nurseId)
    {
        var result = new ValidationResult { IsValid = true };
        var nurse = await _context.Nurses.FindAsync(nurseId);
        var settings = await _context.SystemSettings.FirstOrDefaultAsync() ?? new SystemSettings();

        if (nurse == null)
        {
            result.IsValid = false;
            result.Errors.Add("Nurse not found.");
            return result;
        }

        if (!nurse.IsActive)
        {
            result.IsValid = false;
            result.Errors.Add("Nurse is not active.");
            return result;
        }

        // Check if nurse is on leave
        if (await _availabilityService.IsNurseOnLeaveAsync(nurseId, date))
        {
            result.IsValid = false;
            result.Errors.Add("Nurse is on approved leave.");
            return result;
        }

        // Check if nurse is on compensatory time off
        if (await _availabilityService.IsNurseOnCompTimeAsync(nurseId, date))
        {
            result.IsValid = false;
            result.Errors.Add("Nurse is on compensatory time off.");
            return result;
        }

        // Check for unavailable wish
        var unavailableWish = await _context.NurseShiftWishes
            .AnyAsync(w => w.NurseId == nurseId && w.Date == date &&
                          w.WishType == WishType.Unavailable &&
                          (w.ShiftType == null || w.ShiftType == shiftType));
        if (unavailableWish)
        {
            result.IsValid = false;
            result.Errors.Add("Nurse has marked this date/shift as unavailable.");
            return result;
        }

        // Check if shift is in avoided shifts
        var shiftFlag = shiftType switch
        {
            ShiftType.Morning => ShiftTypeFlags.Morning,
            ShiftType.Afternoon => ShiftTypeFlags.Afternoon,
            ShiftType.Night => ShiftTypeFlags.Night,
            _ => ShiftTypeFlags.None
        };
        if ((nurse.AvoidedShifts & shiftFlag) != ShiftTypeFlags.None)
        {
            result.IsValid = false;
            result.Errors.Add("This shift type is in the nurse's avoided shifts.");
            return result;
        }

        // Check double booking (same day)
        var alreadyAssigned = await _context.ShiftAssignments
            .AnyAsync(sa => sa.NurseId == nurseId && sa.Date == date && sa.Status != AssignmentStatus.Cancelled);
        if (alreadyAssigned)
        {
            result.IsValid = false;
            result.Errors.Add("Nurse is already assigned to a shift on this date.");
            return result;
        }

        // Check clinic assignment
        if (nurse.PrimaryClinicId != clinicId && !nurse.IsPoolNurse)
        {
            var canBeBorrowed = await _context.NurseClinicAssignments
                .AnyAsync(nca => nca.NurseId == nurseId && nca.ClinicId == clinicId && nca.CanBeBorrowed);
            if (!canBeBorrowed)
            {
                result.IsValid = false;
                result.Errors.Add("Nurse cannot be assigned to this clinic.");
                return result;
            }
        }

        // Check rest period
        var minRest = nurse.MinRestHoursBetweenShifts ?? settings.DefaultMinRestHoursBetweenShifts;
        // For simplicity, check if worked previous shift that would violate rest
        var previousDay = date.AddDays(-1);
        var previousNightShift = await _context.ShiftAssignments
            .AnyAsync(sa => sa.NurseId == nurseId && sa.Date == previousDay && sa.ShiftType == ShiftType.Night);
        if (previousNightShift && shiftType == ShiftType.Morning && minRest >= 8)
        {
            result.IsValid = false;
            result.Errors.Add($"Insufficient rest period ({minRest}h required) from previous night shift.");
            return result;
        }

        // Check max consecutive work days
        var maxConsecutive = nurse.MaxConsecutiveWorkDays ?? settings.DefaultMaxConsecutiveWorkDays;
        var consecutiveDays = await _availabilityService.GetConsecutiveWorkDaysAsync(nurseId, date);
        if (consecutiveDays >= maxConsecutive)
        {
            result.IsValid = false;
            result.Errors.Add($"Would exceed maximum consecutive work days ({maxConsecutive}).");
            return result;
        }

        // Check weekly hours limit
        var weekStart = date.AddDays(-(int)date.DayOfWeek + 1); // Monday
        var currentWeeklyHours = await _availabilityService.GetWeeklyHoursAsync(nurseId, weekStart);
        var shiftConfig = await _context.ShiftConfigurations
            .FirstOrDefaultAsync(sc => sc.ClinicId == clinicId && sc.ShiftType == shiftType);
        var shiftHours = shiftConfig?.ShiftDurationHours ?? 8;
        var maxWeeklyHours = nurse.ContractedHoursPerWeek + (nurse.MaxOvertimeHoursPerWeek ?? settings.DefaultMaxOvertimeHoursPerWeek);

        if (currentWeeklyHours + shiftHours > maxWeeklyHours)
        {
            result.IsValid = false;
            result.Errors.Add($"Would exceed maximum weekly hours ({maxWeeklyHours}).");
            return result;
        }

        // Soft constraints (warnings)
        if ((nurse.PreferredShifts & shiftFlag) == ShiftTypeFlags.None)
        {
            result.Warnings.Add("This is not one of the nurse's preferred shifts.");
        }

        var preferOffWish = await _context.NurseShiftWishes
            .AnyAsync(w => w.NurseId == nurseId && w.Date == date && w.WishType == WishType.PreferOff);
        if (preferOffWish)
        {
            result.Warnings.Add("Nurse has expressed a preference to be off on this date.");
        }

        if (currentWeeklyHours + shiftHours > nurse.ContractedHoursPerWeek)
        {
            result.Warnings.Add("This assignment will cause overtime.");
        }

        return result;
    }

    public async Task<ValidationResult> ValidateShiftCoverageAsync(int clinicId, DateOnly date, ShiftType shiftType)
    {
        var result = new ValidationResult { IsValid = true };

        var config = await _context.ShiftConfigurations
            .FirstOrDefaultAsync(sc => sc.ClinicId == clinicId && sc.ShiftType == shiftType &&
                                       (sc.DayOfWeek == null || sc.DayOfWeek == date.DayOfWeek));

        if (config == null)
        {
            result.Warnings.Add("No shift configuration found for this clinic/shift.");
            return result;
        }

        var assignments = await _context.ShiftAssignments
            .Include(sa => sa.Nurse)
            .Where(sa => sa.ClinicId == clinicId && sa.Date == date && sa.ShiftType == shiftType && sa.Status != AssignmentStatus.Cancelled)
            .ToListAsync();

        if (assignments.Count < config.RequiredNurses)
        {
            result.IsValid = false;
            result.Errors.Add($"Shift is understaffed: {assignments.Count}/{config.RequiredNurses} nurses.");
        }

        if (config.RequiresResponsibleNurse)
        {
            var clinic = await _context.Clinics.FindAsync(clinicId);
            var hasResponsible = assignments.Any(a =>
                a.IsResponsibleNurse ||
                (a.Nurse?.CanHandleResponsibility == true) ||
                a.NurseId == clinic?.HeadNurseId);

            if (!hasResponsible)
            {
                result.IsValid = false;
                result.Errors.Add("Shift requires a responsible nurse but none is assigned.");
            }
        }

        var seniorCount = assignments.Count(a => a.Nurse?.SkillLevel >= SkillLevel.Mid);
        if (seniorCount < config.RequiredSeniorNurses)
        {
            result.Warnings.Add($"Shift has fewer senior nurses than recommended: {seniorCount}/{config.RequiredSeniorNurses}.");
        }

        return result;
    }
}

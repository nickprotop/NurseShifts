using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;

namespace NurseShifts.Services;

public class NurseAvailabilityService : INurseAvailabilityService
{
    private readonly AppDbContext _context;

    public NurseAvailabilityService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<NurseAvailability>> GetAvailableNursesAsync(int clinicId, DateOnly date, ShiftType shiftType, bool includeBorrowable = true)
    {
        var result = new List<NurseAvailability>();
        var settings = await _context.SystemSettings.FirstOrDefaultAsync() ?? new SystemSettings();
        var clinic = await _context.Clinics.FindAsync(clinicId);

        // Get primary clinic nurses
        var primaryNurses = await _context.Nurses
            .Where(n => n.PrimaryClinicId == clinicId && n.IsActive)
            .ToListAsync();

        foreach (var nurse in primaryNurses)
        {
            var availability = await EvaluateNurseAvailabilityAsync(nurse, clinicId, date, shiftType, settings, clinic, false);
            result.Add(availability);
        }

        if (includeBorrowable)
        {
            // Get pool nurses
            var poolNurses = await _context.Nurses
                .Where(n => n.IsPoolNurse && n.PrimaryClinicId != clinicId && n.IsActive)
                .ToListAsync();

            foreach (var nurse in poolNurses)
            {
                var availability = await EvaluateNurseAvailabilityAsync(nurse, clinicId, date, shiftType, settings, clinic, true);
                result.Add(availability);
            }

            // Get borrowable nurses from other clinics
            var borrowableNurseIds = await _context.NurseClinicAssignments
                .Where(nca => nca.ClinicId == clinicId && nca.CanBeBorrowed)
                .Select(nca => nca.NurseId)
                .ToListAsync();

            var borrowableNurses = await _context.Nurses
                .Where(n => borrowableNurseIds.Contains(n.Id) && n.IsActive && n.PrimaryClinicId != clinicId && !n.IsPoolNurse)
                .ToListAsync();

            foreach (var nurse in borrowableNurses)
            {
                // Check if primary clinic shift is full
                var primaryClinicFull = await IsClinicShiftFullAsync(nurse.PrimaryClinicId, date, shiftType);
                if (primaryClinicFull)
                {
                    var availability = await EvaluateNurseAvailabilityAsync(nurse, clinicId, date, shiftType, settings, clinic, true);
                    result.Add(availability);
                }
            }
        }

        return result;
    }

    private async Task<NurseAvailability> EvaluateNurseAvailabilityAsync(
        Nurse nurse, int clinicId, DateOnly date, ShiftType shiftType,
        SystemSettings settings, Clinic? clinic, bool isBorrowed)
    {
        var availability = new NurseAvailability
        {
            Nurse = nurse,
            IsAvailable = true,
            IsBorrowed = isBorrowed,
            Score = 0
        };

        // Check unavailability reasons
        if (await IsNurseOnLeaveAsync(nurse.Id, date))
        {
            availability.IsAvailable = false;
            availability.UnavailabilityReasons.Add("On leave");
        }

        if (await IsNurseOnCompTimeAsync(nurse.Id, date))
        {
            availability.IsAvailable = false;
            availability.UnavailabilityReasons.Add("On compensatory time off");
        }

        var alreadyAssigned = await _context.ShiftAssignments
            .AnyAsync(sa => sa.NurseId == nurse.Id && sa.Date == date && sa.Status != AssignmentStatus.Cancelled);
        if (alreadyAssigned)
        {
            availability.IsAvailable = false;
            availability.UnavailabilityReasons.Add("Already assigned to a shift");
        }

        var unavailableWish = await _context.NurseShiftWishes
            .AnyAsync(w => w.NurseId == nurse.Id && w.Date == date &&
                          w.WishType == WishType.Unavailable &&
                          (w.ShiftType == null || w.ShiftType == shiftType));
        if (unavailableWish)
        {
            availability.IsAvailable = false;
            availability.UnavailabilityReasons.Add("Marked as unavailable");
        }

        var shiftFlag = shiftType switch
        {
            ShiftType.Morning => ShiftTypeFlags.Morning,
            ShiftType.Afternoon => ShiftTypeFlags.Afternoon,
            ShiftType.Night => ShiftTypeFlags.Night,
            _ => ShiftTypeFlags.None
        };
        if ((nurse.AvoidedShifts & shiftFlag) != ShiftTypeFlags.None)
        {
            availability.IsAvailable = false;
            availability.UnavailabilityReasons.Add("Shift type is avoided");
        }

        // Check rest period (simplified)
        var previousDay = date.AddDays(-1);
        var minRest = nurse.MinRestHoursBetweenShifts ?? settings.DefaultMinRestHoursBetweenShifts;
        var previousNightShift = await _context.ShiftAssignments
            .AnyAsync(sa => sa.NurseId == nurse.Id && sa.Date == previousDay && sa.ShiftType == ShiftType.Night);
        if (previousNightShift && shiftType == ShiftType.Morning && minRest >= 8)
        {
            availability.IsAvailable = false;
            availability.UnavailabilityReasons.Add("Insufficient rest from night shift");
        }

        // Check max consecutive days
        var maxConsecutive = nurse.MaxConsecutiveWorkDays ?? settings.DefaultMaxConsecutiveWorkDays;
        var consecutiveDays = await GetConsecutiveWorkDaysAsync(nurse.Id, date);
        if (consecutiveDays >= maxConsecutive)
        {
            availability.IsAvailable = false;
            availability.UnavailabilityReasons.Add($"Would exceed max consecutive days ({maxConsecutive})");
        }

        // Check weekly hours
        var weekStart = date.AddDays(-(int)date.DayOfWeek + 1);
        var weeklyHours = await GetWeeklyHoursAsync(nurse.Id, weekStart);
        var maxWeekly = nurse.ContractedHoursPerWeek + (nurse.MaxOvertimeHoursPerWeek ?? settings.DefaultMaxOvertimeHoursPerWeek);
        if (weeklyHours + 8 > maxWeekly)
        {
            availability.IsAvailable = false;
            availability.UnavailabilityReasons.Add($"Would exceed max weekly hours ({maxWeekly})");
        }

        // Calculate score if available
        if (availability.IsAvailable)
        {
            // Preferred shift
            if ((nurse.PreferredShifts & shiftFlag) != ShiftTypeFlags.None)
                availability.Score += 10;

            // Want to work wish
            var wantToWork = await _context.NurseShiftWishes
                .AnyAsync(w => w.NurseId == nurse.Id && w.Date == date && w.WishType == WishType.WantToWork);
            if (wantToWork) availability.Score += 5;

            // Below contracted hours
            if (weeklyHours < nurse.ContractedHoursPerWeek)
                availability.Score += 3;

            // Consecutive day preference
            var workedYesterday = await _context.ShiftAssignments
                .AnyAsync(sa => sa.NurseId == nurse.Id && sa.Date == previousDay);
            if (nurse.PrefersConsecutiveDays && workedYesterday)
                availability.Score += 2;

            // Prefer off wish (penalty)
            var preferOff = await _context.NurseShiftWishes
                .AnyAsync(w => w.NurseId == nurse.Id && w.Date == date && w.WishType == WishType.PreferOff);
            if (preferOff) availability.Score -= 5;

            // Overtime penalty
            if (weeklyHours + 8 > nurse.ContractedHoursPerWeek)
                availability.Score -= 4;

            // Borrowed penalty
            if (isBorrowed) availability.Score -= 2;
        }

        return availability;
    }

    private async Task<bool> IsClinicShiftFullAsync(int clinicId, DateOnly date, ShiftType shiftType)
    {
        var config = await _context.ShiftConfigurations
            .FirstOrDefaultAsync(sc => sc.ClinicId == clinicId && sc.ShiftType == shiftType);
        if (config == null) return true;

        var assigned = await _context.ShiftAssignments
            .CountAsync(sa => sa.ClinicId == clinicId && sa.Date == date && sa.ShiftType == shiftType);

        return assigned >= config.RequiredNurses;
    }

    public async Task<bool> IsNurseOnLeaveAsync(int nurseId, DateOnly date)
    {
        return await _context.NurseLeaves
            .AnyAsync(nl => nl.NurseId == nurseId &&
                           nl.Status == LeaveStatus.Approved &&
                           nl.StartDate <= date && nl.EndDate >= date);
    }

    public async Task<bool> IsNurseOnCompTimeAsync(int nurseId, DateOnly date)
    {
        return await _context.CompensatoryTimeOffs
            .AnyAsync(cto => cto.NurseId == nurseId && cto.Date == date);
    }

    public async Task<int> GetWeeklyHoursAsync(int nurseId, DateOnly weekStart)
    {
        var weekEnd = weekStart.AddDays(6);
        var assignments = await _context.ShiftAssignments
            .Where(sa => sa.NurseId == nurseId && sa.Date >= weekStart && sa.Date <= weekEnd && sa.Status != AssignmentStatus.Cancelled)
            .ToListAsync();

        // Assume 8 hours per shift for simplicity
        return assignments.Count * 8;
    }

    public async Task<int> GetConsecutiveWorkDaysAsync(int nurseId, DateOnly date)
    {
        var count = 0;
        var checkDate = date.AddDays(-1);

        while (true)
        {
            var worked = await _context.ShiftAssignments
                .AnyAsync(sa => sa.NurseId == nurseId && sa.Date == checkDate && sa.Status != AssignmentStatus.Cancelled);

            if (!worked) break;

            count++;
            checkDate = checkDate.AddDays(-1);

            if (count > 14) break; // Safety limit
        }

        return count;
    }
}

using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;

namespace NurseShifts.Services;

public class ScheduleService : IScheduleService
{
    private readonly AppDbContext _context;
    private readonly INurseAvailabilityService _availabilityService;
    private readonly IValidationService _validationService;
    private readonly IOvertimeService _overtimeService;

    public ScheduleService(
        AppDbContext context,
        INurseAvailabilityService availabilityService,
        IValidationService validationService,
        IOvertimeService overtimeService)
    {
        _context = context;
        _availabilityService = availabilityService;
        _validationService = validationService;
        _overtimeService = overtimeService;
    }

    public async Task<List<ShiftAssignment>> GetScheduleAsync(int clinicId, DateOnly startDate, DateOnly endDate)
    {
        return await _context.ShiftAssignments
            .Include(sa => sa.Nurse)
            .Include(sa => sa.Clinic)
            .Where(sa => sa.ClinicId == clinicId && sa.Date >= startDate && sa.Date <= endDate)
            .OrderBy(sa => sa.Date)
            .ThenBy(sa => sa.ShiftType)
            .ToListAsync();
    }

    public async Task<List<ShiftAssignment>> GenerateScheduleAsync(int clinicId, DateOnly startDate, int days)
    {
        var assignments = new List<ShiftAssignment>();
        var endDate = startDate.AddDays(days - 1);

        // Delete existing assignments for this period first
        var existingAssignments = await _context.ShiftAssignments
            .Where(sa => sa.ClinicId == clinicId && sa.Date >= startDate && sa.Date <= endDate)
            .ToListAsync();
        _context.ShiftAssignments.RemoveRange(existingAssignments);
        await _context.SaveChangesAsync();

        // Get clinic with head nurse info
        var clinic = await _context.Clinics
            .Include(c => c.HeadNurse)
            .FirstOrDefaultAsync(c => c.Id == clinicId);

        if (clinic == null) return assignments;

        // Get shift configurations for this clinic
        var configs = await _context.ShiftConfigurations
            .Where(sc => sc.ClinicId == clinicId)
            .ToListAsync();

        // Get system settings for defaults
        var settings = await _context.SystemSettings.FirstOrDefaultAsync() ?? new SystemSettings();

        // Track assignments during generation: nurseId -> list of (date, shiftType)
        var pendingAssignments = new Dictionary<int, List<(DateOnly Date, ShiftType Shift)>>();

        // Track weekly hours during generation: nurseId -> weekStart -> hours
        var weeklyHoursTracker = new Dictionary<int, Dictionary<DateOnly, int>>();

        // Track pending compensatory days off: nurseId -> number of 8h comp days owed
        var pendingCompDays = new Dictionary<int, int>();

        // Track which nurses got comp day on which date (to avoid duplicate records)
        var compDaysGranted = new Dictionary<int, HashSet<DateOnly>>();

        // Load accumulated overtime balances for all nurses (from previous weeks)
        var allNurses = await _context.Nurses.Where(n => n.IsActive).ToListAsync();
        foreach (var nurse in allNurses)
        {
            var overtimeBalance = await _overtimeService.GetTotalOvertimeBalanceAsync(nurse.Id);
            // Each 8 hours of overtime = 1 comp day
            var compDaysOwed = overtimeBalance / 8;
            if (compDaysOwed > 0)
            {
                pendingCompDays[nurse.Id] = compDaysOwed;
            }
            compDaysGranted[nurse.Id] = new HashSet<DateOnly>();
        }

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            foreach (ShiftType shiftType in Enum.GetValues<ShiftType>())
            {
                // Get config for this shift (specific day or general)
                var config = configs.FirstOrDefault(c => c.ShiftType == shiftType && c.DayOfWeek == date.DayOfWeek)
                    ?? configs.FirstOrDefault(c => c.ShiftType == shiftType && c.DayOfWeek == null);

                if (config == null) continue;

                var requiredNurses = config.RequiredNurses;
                var shiftDuration = config.ShiftDurationHours;
                var assigned = 0;
                var hasResponsibleNurse = false;

                // AUTO-ASSIGN HEAD NURSE TO MORNING SHIFTS ON WORKING DAYS
                if (shiftType == ShiftType.Morning &&
                    clinic.AutoAssignHeadNurseMorning &&
                    IsWorkingDay(date) &&
                    clinic.HeadNurseId.HasValue &&
                    clinic.HeadNurse != null &&
                    clinic.HeadNurse.IsActive)
                {
                    var headNurseId = clinic.HeadNurseId.Value;
                    var headNurse = clinic.HeadNurse;
                    var maxConsecutive = headNurse.MaxConsecutiveWorkDays ?? settings.DefaultMaxConsecutiveWorkDays;

                    // Check if head nurse is available (not on leave, comp time, etc.)
                    var headNurseAvailable = await IsHeadNurseAvailableAsync(headNurseId, date);

                    // Also check our pending tracking
                    var notAlreadyAssigned = !IsNurseAssignedToday(headNurseId, date, pendingAssignments);
                    var notExceedingHours = !WouldExceedWeeklyHours(headNurse, date, shiftDuration, weeklyHoursTracker, settings);
                    var notExceedingConsecutive = !await WouldExceedConsecutiveDaysAsync(headNurseId, date, pendingAssignments, maxConsecutive);

                    if (headNurseAvailable && notAlreadyAssigned && notExceedingHours && notExceedingConsecutive)
                    {
                        var assignment = new ShiftAssignment
                        {
                            Date = date,
                            ShiftType = shiftType,
                            ClinicId = clinicId,
                            NurseId = headNurseId,
                            IsResponsibleNurse = true, // Head nurse is always responsible
                            IsBorrowed = false,
                            Status = AssignmentStatus.Scheduled
                        };

                        _context.ShiftAssignments.Add(assignment);
                        assignments.Add(assignment);

                        // Track this assignment
                        TrackAssignment(headNurseId, date, shiftType, shiftDuration, pendingAssignments, weeklyHoursTracker);

                        assigned++;
                        hasResponsibleNurse = true;
                    }
                }

                // Fill remaining slots with other available nurses
                if (assigned < requiredNurses)
                {
                    // Get available nurses from the service
                    var availableNurses = await _availabilityService.GetAvailableNursesAsync(clinicId, date, shiftType, true);

                    // Filter and rank nurses (sync filters only)
                    var rankedNurses = availableNurses
                        .Where(n => n.IsAvailable)
                        .Where(n => !IsNurseAssignedToday(n.Nurse.Id, date, pendingAssignments))
                        // If AutoAssignHeadNurseMorning is enabled, head nurse only works Mon-Fri mornings
                        .Where(n => !(clinic.AutoAssignHeadNurseMorning &&
                                      n.Nurse.Id == clinic.HeadNurseId &&
                                      (!IsWorkingDay(date) || shiftType != ShiftType.Morning)))
                        .Where(n => !WouldExceedWeeklyHours(n.Nurse, date, shiftDuration, weeklyHoursTracker, settings))
                        .Where(n => !WouldViolateRestPeriod(n.Nurse.Id, date, shiftType, pendingAssignments, n.Nurse.MinRestHoursBetweenShifts ?? settings.DefaultMinRestHoursBetweenShifts))
                        .OrderByDescending(n => n.Score)
                        .ToList();

                    // Separate nurses: those needing comp days vs those who don't
                    var nursesNeedingCompDay = new List<NurseAvailability>();
                    var nursesAvailableToWork = new List<NurseAvailability>();

                    foreach (var nurseAvail in rankedNurses)
                    {
                        var nurse = nurseAvail.Nurse;

                        // Check consecutive days (async - needs database query)
                        var maxConsecutive = nurse.MaxConsecutiveWorkDays ?? settings.DefaultMaxConsecutiveWorkDays;
                        if (await WouldExceedConsecutiveDaysAsync(nurse.Id, date, pendingAssignments, maxConsecutive))
                            continue;

                        // Check if nurse has pending comp days and hasn't received one today yet
                        var hasCompDaysPending = pendingCompDays.TryGetValue(nurse.Id, out var compDaysOwed) && compDaysOwed > 0;
                        var alreadyGotCompDayToday = compDaysGranted.TryGetValue(nurse.Id, out var grantedDates) && grantedDates.Contains(date);

                        if (hasCompDaysPending && !alreadyGotCompDayToday)
                        {
                            nursesNeedingCompDay.Add(nurseAvail);
                        }
                        else
                        {
                            nursesAvailableToWork.Add(nurseAvail);
                        }
                    }

                    var slotsNeeded = requiredNurses - assigned;

                    // First, try to fill with nurses who don't need comp days
                    foreach (var nurseAvail in nursesAvailableToWork)
                    {
                        if (assigned >= requiredNurses) break;

                        var nurse = nurseAvail.Nurse;
                        var isResponsible = !hasResponsibleNurse &&
                            (nurse.CanHandleResponsibility || nurse.Id == clinic.HeadNurseId);

                        var assignment = new ShiftAssignment
                        {
                            Date = date,
                            ShiftType = shiftType,
                            ClinicId = clinicId,
                            NurseId = nurse.Id,
                            IsResponsibleNurse = isResponsible,
                            IsBorrowed = nurseAvail.IsBorrowed,
                            Status = AssignmentStatus.Scheduled
                        };

                        _context.ShiftAssignments.Add(assignment);
                        assignments.Add(assignment);
                        TrackAssignment(nurse.Id, date, shiftType, shiftDuration, pendingAssignments, weeklyHoursTracker);

                        assigned++;
                        if (isResponsible) hasResponsibleNurse = true;
                    }

                    // Grant comp days to nurses who need them (if shift is filled)
                    if (assigned >= requiredNurses)
                    {
                        foreach (var nurseAvail in nursesNeedingCompDay)
                        {
                            var nurse = nurseAvail.Nurse;
                            if (!compDaysGranted[nurse.Id].Contains(date))
                            {
                                // Create compensatory time off record
                                var compTimeOff = new CompensatoryTimeOff
                                {
                                    NurseId = nurse.Id,
                                    Date = date,
                                    HoursCompensated = 8,
                                    Notes = "Auto-generated compensatory day off",
                                    CreatedAt = DateTime.UtcNow
                                };
                                _context.CompensatoryTimeOffs.Add(compTimeOff);
                                compDaysGranted[nurse.Id].Add(date);

                                // Reduce pending comp days
                                pendingCompDays[nurse.Id]--;
                                if (pendingCompDays[nurse.Id] <= 0)
                                    pendingCompDays.Remove(nurse.Id);
                            }
                        }
                    }
                    else
                    {
                        // Shift still needs nurses - use those who need comp days
                        foreach (var nurseAvail in nursesNeedingCompDay)
                        {
                            if (assigned >= requiredNurses) break;

                            var nurse = nurseAvail.Nurse;
                            var isResponsible = !hasResponsibleNurse &&
                                (nurse.CanHandleResponsibility || nurse.Id == clinic.HeadNurseId);

                            var assignment = new ShiftAssignment
                            {
                                Date = date,
                                ShiftType = shiftType,
                                ClinicId = clinicId,
                                NurseId = nurse.Id,
                                IsResponsibleNurse = isResponsible,
                                IsBorrowed = nurseAvail.IsBorrowed,
                                Status = AssignmentStatus.Scheduled
                            };

                            _context.ShiftAssignments.Add(assignment);
                            assignments.Add(assignment);
                            TrackAssignment(nurse.Id, date, shiftType, shiftDuration, pendingAssignments, weeklyHoursTracker);

                            assigned++;
                            if (isResponsible) hasResponsibleNurse = true;
                            // Nurse works - comp day postponed to another day
                        }
                    }
                }

                // If we still need a responsible nurse and have assigned someone
                if (config.RequiresResponsibleNurse && !hasResponsibleNurse && assigned > 0)
                {
                    var firstAssignment = assignments
                        .Where(a => a.Date == date && a.ShiftType == shiftType)
                        .FirstOrDefault();
                    if (firstAssignment != null)
                    {
                        var nurseForResp = await _context.Nurses.FindAsync(firstAssignment.NurseId);
                        if (nurseForResp?.CanHandleResponsibility == true || nurseForResp?.Id == clinic.HeadNurseId)
                        {
                            firstAssignment.IsResponsibleNurse = true;
                        }
                    }
                }
            }
        }

        await _context.SaveChangesAsync();
        return assignments;
    }

    private void TrackAssignment(int nurseId, DateOnly date, ShiftType shiftType, int shiftDuration,
        Dictionary<int, List<(DateOnly Date, ShiftType Shift)>> pendingAssignments,
        Dictionary<int, Dictionary<DateOnly, int>> weeklyHoursTracker)
    {
        // Track assignment
        if (!pendingAssignments.ContainsKey(nurseId))
            pendingAssignments[nurseId] = new List<(DateOnly, ShiftType)>();
        pendingAssignments[nurseId].Add((date, shiftType));

        // Track weekly hours
        var weekStart = GetWeekStart(date);
        if (!weeklyHoursTracker.ContainsKey(nurseId))
            weeklyHoursTracker[nurseId] = new Dictionary<DateOnly, int>();
        if (!weeklyHoursTracker[nurseId].ContainsKey(weekStart))
            weeklyHoursTracker[nurseId][weekStart] = 0;
        weeklyHoursTracker[nurseId][weekStart] += shiftDuration;
    }

    private bool IsWorkingDay(DateOnly date)
    {
        return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
    }

    private async Task<bool> IsHeadNurseAvailableAsync(int nurseId, DateOnly date)
    {
        // Check if on approved leave
        var onLeave = await _context.NurseLeaves
            .AnyAsync(nl => nl.NurseId == nurseId &&
                           nl.Status == LeaveStatus.Approved &&
                           nl.StartDate <= date && nl.EndDate >= date);
        if (onLeave) return false;

        // Check if on compensatory time off
        var onCompTime = await _context.CompensatoryTimeOffs
            .AnyAsync(cto => cto.NurseId == nurseId && cto.Date == date);
        if (onCompTime) return false;

        // Check unavailable wish
        var unavailable = await _context.NurseShiftWishes
            .AnyAsync(w => w.NurseId == nurseId && w.Date == date &&
                          w.WishType == WishType.Unavailable &&
                          (w.ShiftType == null || w.ShiftType == ShiftType.Morning));
        if (unavailable) return false;

        return true;
    }

    private bool IsNurseAssignedToday(int nurseId, DateOnly date, Dictionary<int, List<(DateOnly Date, ShiftType Shift)>> pending)
    {
        if (!pending.ContainsKey(nurseId)) return false;
        return pending[nurseId].Any(p => p.Date == date);
    }

    private async Task<bool> WouldExceedConsecutiveDaysAsync(int nurseId, DateOnly date, Dictionary<int, List<(DateOnly Date, ShiftType Shift)>> pending, int maxConsecutive)
    {
        var count = 0;
        var checkDate = date.AddDays(-1);

        while (count < maxConsecutive)
        {
            // Check pending assignments first (in-memory during generation)
            var inPending = pending.ContainsKey(nurseId) && pending[nurseId].Any(p => p.Date == checkDate);

            // If not in pending, check database for existing assignments
            var inDatabase = !inPending && await _context.ShiftAssignments
                .AnyAsync(sa => sa.NurseId == nurseId && sa.Date == checkDate && sa.Status != AssignmentStatus.Cancelled);

            if (!inPending && !inDatabase) break;

            count++;
            checkDate = checkDate.AddDays(-1);
        }

        return count >= maxConsecutive;
    }

    private bool WouldExceedWeeklyHours(Nurse nurse, DateOnly date, int shiftHours,
        Dictionary<int, Dictionary<DateOnly, int>> weeklyTracker,
        SystemSettings settings)
    {
        var weekStart = GetWeekStart(date);
        var maxWeekly = nurse.ContractedHoursPerWeek + (nurse.MaxOvertimeHoursPerWeek ?? settings.DefaultMaxOvertimeHoursPerWeek);

        var currentHours = 0;
        if (weeklyTracker.ContainsKey(nurse.Id) && weeklyTracker[nurse.Id].ContainsKey(weekStart))
            currentHours = weeklyTracker[nurse.Id][weekStart];

        return (currentHours + shiftHours) > maxWeekly;
    }

    private bool WouldViolateRestPeriod(int nurseId, DateOnly date, ShiftType shiftType,
        Dictionary<int, List<(DateOnly Date, ShiftType Shift)>> pending, int minRestHours)
    {
        if (!pending.ContainsKey(nurseId)) return false;

        // Check if nurse had a night shift the previous day - can't do morning
        var previousDay = date.AddDays(-1);
        var hadNightShiftYesterday = pending[nurseId].Any(p => p.Date == previousDay && p.Shift == ShiftType.Night);

        if (hadNightShiftYesterday && shiftType == ShiftType.Morning && minRestHours >= 8)
            return true;

        return false;
    }

    private DateOnly GetWeekStart(DateOnly date)
    {
        // Monday as week start
        var daysFromMonday = ((int)date.DayOfWeek - 1 + 7) % 7;
        return date.AddDays(-daysFromMonday);
    }

    public async Task<ShiftAssignment?> AssignNurseToShiftAsync(int clinicId, DateOnly date, ShiftType shiftType, int nurseId, bool isResponsible = false)
    {
        var validation = await _validationService.ValidateShiftAssignmentAsync(clinicId, date, shiftType, nurseId);
        if (!validation.IsValid)
        {
            return null;
        }

        var nurse = await _context.Nurses.FindAsync(nurseId);
        if (nurse == null) return null;

        var assignment = new ShiftAssignment
        {
            Date = date,
            ShiftType = shiftType,
            ClinicId = clinicId,
            NurseId = nurseId,
            IsResponsibleNurse = isResponsible,
            IsBorrowed = nurse.PrimaryClinicId != clinicId,
            Status = AssignmentStatus.Scheduled
        };

        _context.ShiftAssignments.Add(assignment);
        await _context.SaveChangesAsync();

        return assignment;
    }

    public async Task<bool> RemoveAssignmentAsync(int assignmentId)
    {
        var assignment = await _context.ShiftAssignments.FindAsync(assignmentId);
        if (assignment == null) return false;

        _context.ShiftAssignments.Remove(assignment);
        await _context.SaveChangesAsync();
        return true;
    }
}

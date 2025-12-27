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

    /// <summary>
    /// Calculate a score for an already-assigned nurse to determine rebalancing priority.
    /// Lower score = more likely to be replaced by a higher-scored available nurse.
    /// Uses similar scoring logic as NurseAvailabilityService but synchronous.
    /// </summary>
    private int GetNurseScoreForRebalance(Nurse nurse, DateOnly date, ShiftType shiftType, SystemSettings settings)
    {
        var score = 0;

        var shiftFlag = shiftType switch
        {
            ShiftType.Morning => ShiftTypeFlags.Morning,
            ShiftType.Afternoon => ShiftTypeFlags.Afternoon,
            ShiftType.Night => ShiftTypeFlags.Night,
            _ => ShiftTypeFlags.None
        };

        // Preferred shift bonus
        if ((nurse.PreferredShifts & shiftFlag) != ShiftTypeFlags.None)
            score += 10;

        // Avoided shift penalty
        if ((nurse.AvoidedShifts & shiftFlag) != ShiftTypeFlags.None)
            score -= 10;

        // Primary clinic nurses have higher priority
        // (Borrowed nurses have lower priority, but we don't have that info here)

        return score;
    }

    private async Task<bool> IsNurseAvailableForShiftAsync(int nurseId, DateOnly date, ShiftType shiftType)
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

        // Check unavailable wish for this shift
        var unavailable = await _context.NurseShiftWishes
            .AnyAsync(w => w.NurseId == nurseId && w.Date == date &&
                          w.WishType == WishType.Unavailable &&
                          (w.ShiftType == null || w.ShiftType == shiftType));
        if (unavailable) return false;

        // Check if nurse is still active
        var nurse = await _context.Nurses.FindAsync(nurseId);
        if (nurse == null || !nurse.IsActive) return false;

        return true;
    }

    /// <summary>
    /// Unified scheduling algorithm - the single source of truth for schedule management.
    /// </summary>
    public async Task<ScheduleResult> ScheduleAsync(int clinicId, DateOnly startDate, DateOnly endDate)
    {
        var result = new ScheduleResult();

        var clinic = await _context.Clinics
            .Include(c => c.HeadNurse)
            .FirstOrDefaultAsync(c => c.Id == clinicId);

        if (clinic == null) return result;

        var configs = await _context.ShiftConfigurations
            .Where(sc => sc.ClinicId == clinicId)
            .ToListAsync();

        var settings = await _context.SystemSettings.FirstOrDefaultAsync() ?? new SystemSettings();

        // Track assignments during scheduling
        var pendingAssignments = new Dictionary<int, List<(DateOnly Date, ShiftType Shift)>>();
        var weeklyHoursTracker = new Dictionary<int, Dictionary<DateOnly, int>>();

        // Pre-populate trackers with existing assignments
        var allExistingAssignments = await _context.ShiftAssignments
            .Include(sa => sa.Nurse)
            .Where(sa => sa.ClinicId == clinicId && sa.Date >= startDate && sa.Date <= endDate)
            .ToListAsync();

        foreach (var existing in allExistingAssignments)
        {
            var shiftDuration = configs
                .FirstOrDefault(c => c.ShiftType == existing.ShiftType && (c.DayOfWeek == existing.Date.DayOfWeek || c.DayOfWeek == null))
                ?.ShiftDurationHours ?? 8;
            TrackAssignment(existing.NurseId, existing.Date, existing.ShiftType, shiftDuration, pendingAssignments, weeklyHoursTracker);
        }

        // === STEP 1: Calculate & Grant Comp Days ===
        await GrantPendingCompDaysAsync(clinicId, startDate, endDate, configs, settings, pendingAssignments, weeklyHoursTracker, result);

        // === STEP 2-5: Remove unavailable, fill gaps, rebalance, ensure responsible ===
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            foreach (ShiftType shiftType in Enum.GetValues<ShiftType>())
            {
                var config = configs.FirstOrDefault(c => c.ShiftType == shiftType && c.DayOfWeek == date.DayOfWeek)
                    ?? configs.FirstOrDefault(c => c.ShiftType == shiftType && c.DayOfWeek == null);

                if (config == null) continue;

                var shiftDuration = config.ShiftDurationHours;
                var requiredNurses = config.RequiredNurses;

                // Get current assignments
                var currentAssignments = await _context.ShiftAssignments
                    .Include(sa => sa.Nurse)
                    .Where(sa => sa.ClinicId == clinicId && sa.Date == date && sa.ShiftType == shiftType)
                    .ToListAsync();

                // STEP 2: Remove unavailable (auto-assignments only)
                foreach (var assignment in currentAssignments.ToList())
                {
                    if (assignment.IsManualAssignment) continue;

                    var isAvailable = await IsNurseAvailableForShiftAsync(assignment.NurseId, date, shiftType);
                    if (!isAvailable)
                    {
                        var nurseName = assignment.Nurse?.FullName ?? "Unknown";
                        _context.ShiftAssignments.Remove(assignment);
                        currentAssignments.Remove(assignment);

                        if (pendingAssignments.ContainsKey(assignment.NurseId))
                            pendingAssignments[assignment.NurseId].RemoveAll(p => p.Date == date && p.Shift == shiftType);

                        result.AssignmentsRemoved++;
                        result.Changes.Add(new ScheduleChange
                        {
                            Type = ScheduleChangeType.Removed,
                            NurseName = nurseName,
                            ShiftType = shiftType,
                            Date = date,
                            Reason = "Unavailable"
                        });
                    }
                }

                // STEP 3: Fill empty slots
                var currentCount = currentAssignments.Count;
                if (currentCount < requiredNurses)
                {
                    var hasResponsibleNurse = currentAssignments.Any(a => a.IsResponsibleNurse);
                    var needsResponsibleNurse = config.RequiresResponsibleNurse && !hasResponsibleNurse;

                    // Try to assign head nurse to morning shifts on working days
                    if (shiftType == ShiftType.Morning &&
                        clinic.AutoAssignHeadNurseMorning &&
                        IsWorkingDay(date) &&
                        clinic.HeadNurseId.HasValue &&
                        clinic.HeadNurse != null &&
                        clinic.HeadNurse.IsActive &&
                        !currentAssignments.Any(a => a.NurseId == clinic.HeadNurseId))
                    {
                        var headNurseId = clinic.HeadNurseId.Value;
                        var headNurse = clinic.HeadNurse;
                        var maxConsecutive = headNurse.MaxConsecutiveWorkDays ?? settings.DefaultMaxConsecutiveWorkDays;

                        var headNurseAvailable = await IsHeadNurseAvailableAsync(headNurseId, date);
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
                                IsResponsibleNurse = true,
                                IsBorrowed = false,
                                Status = AssignmentStatus.Scheduled
                            };

                            _context.ShiftAssignments.Add(assignment);
                            TrackAssignment(headNurseId, date, shiftType, shiftDuration, pendingAssignments, weeklyHoursTracker);

                            currentCount++;
                            hasResponsibleNurse = true;
                            result.AssignmentsAdded++;
                            result.Changes.Add(new ScheduleChange
                            {
                                Type = ScheduleChangeType.Added,
                                NurseName = headNurse.FullName,
                                ShiftType = shiftType,
                                Date = date
                            });
                        }
                    }

                    // Fill remaining slots with available nurses
                    if (currentCount < requiredNurses)
                    {
                        var existingNurseIds = currentAssignments.Select(a => a.NurseId).ToHashSet();
                        var availableNurses = await _availabilityService.GetAvailableNursesAsync(clinicId, date, shiftType, true);

                        var rankedNurses = availableNurses
                            .Where(n => n.IsAvailable)
                            .Where(n => !existingNurseIds.Contains(n.Nurse.Id))
                            .Where(n => !IsNurseAssignedToday(n.Nurse.Id, date, pendingAssignments))
                            .Where(n => !(clinic.AutoAssignHeadNurseMorning &&
                                          n.Nurse.Id == clinic.HeadNurseId &&
                                          (!IsWorkingDay(date) || shiftType != ShiftType.Morning)))
                            .Where(n => !WouldExceedWeeklyHours(n.Nurse, date, shiftDuration, weeklyHoursTracker, settings))
                            .Where(n => !WouldViolateRestPeriod(n.Nurse.Id, date, shiftType, pendingAssignments, n.Nurse.MinRestHoursBetweenShifts ?? settings.DefaultMinRestHoursBetweenShifts))
                            .OrderByDescending(n => needsResponsibleNurse && n.Nurse.CanHandleResponsibility ? 1 : 0)
                            .ThenByDescending(n => n.Score)
                            .ToList();

                        foreach (var nurseAvail in rankedNurses)
                        {
                            if (currentCount >= requiredNurses) break;

                            var nurse = nurseAvail.Nurse;
                            var maxConsecutive = nurse.MaxConsecutiveWorkDays ?? settings.DefaultMaxConsecutiveWorkDays;
                            if (await WouldExceedConsecutiveDaysAsync(nurse.Id, date, pendingAssignments, maxConsecutive))
                                continue;

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
                            TrackAssignment(nurse.Id, date, shiftType, shiftDuration, pendingAssignments, weeklyHoursTracker);

                            currentCount++;
                            if (isResponsible) hasResponsibleNurse = true;
                            result.AssignmentsAdded++;
                            result.Changes.Add(new ScheduleChange
                            {
                                Type = ScheduleChangeType.Added,
                                NurseName = nurse.FullName,
                                ShiftType = shiftType,
                                Date = date
                            });
                        }
                    }
                }

                // STEP 5: Ensure responsible nurse
                if (config.RequiresResponsibleNurse && !currentAssignments.Any(a => a.IsResponsibleNurse) && currentCount > 0)
                {
                    var shiftAssignments = await _context.ShiftAssignments
                        .Include(a => a.Nurse)
                        .Where(a => a.ClinicId == clinicId && a.Date == date && a.ShiftType == shiftType && !a.IsResponsibleNurse)
                        .ToListAsync();

                    foreach (var assignment in shiftAssignments)
                    {
                        if (assignment.Nurse?.CanHandleResponsibility == true)
                        {
                            assignment.IsResponsibleNurse = true;
                            result.Changes.Add(new ScheduleChange
                            {
                                Type = ScheduleChangeType.MarkedResponsible,
                                NurseName = assignment.Nurse.FullName,
                                ShiftType = shiftType,
                                Date = date
                            });
                            break;
                        }
                    }
                }
            }
        }

        // STEP 4: Rebalance
        await RebalanceScheduleAsync(clinicId, startDate, endDate, clinic, configs, settings, pendingAssignments, weeklyHoursTracker, result);

        await _context.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// Grant pending comp days to nurses with overtime balance
    /// </summary>
    private async Task GrantPendingCompDaysAsync(
        int clinicId, DateOnly startDate, DateOnly endDate,
        List<ShiftConfiguration> configs, SystemSettings settings,
        Dictionary<int, List<(DateOnly Date, ShiftType Shift)>> pendingAssignments,
        Dictionary<int, Dictionary<DateOnly, int>> weeklyHoursTracker,
        ScheduleResult result)
    {
        // Get nurses who work at this clinic (primary or borrowed)
        var primaryNurseIds = await _context.Nurses
            .Where(n => n.IsActive && n.PrimaryClinicId == clinicId)
            .Select(n => n.Id)
            .ToListAsync();

        var borrowedNurseIds = await _context.NurseClinicAssignments
            .Where(nca => nca.ClinicId == clinicId && nca.CanBeBorrowed)
            .Select(nca => nca.NurseId)
            .ToListAsync();

        var allNurseIds = primaryNurseIds.Union(borrowedNurseIds).ToList();
        var allNurses = await _context.Nurses
            .Where(n => n.IsActive && allNurseIds.Contains(n.Id))
            .ToListAsync();

        foreach (var nurse in allNurses)
        {
            var overtimeBalance = await _overtimeService.GetTotalOvertimeBalanceAsync(nurse.Id);
            var compDaysOwed = overtimeBalance / 8;

            if (compDaysOwed <= 0) continue;

            // Check if nurse already has a comp day this week (idempotency)
            var weekStart = GetWeekStart(startDate);
            if (await _overtimeService.HasCompDayInWeekAsync(nurse.Id, weekStart))
                continue;

            // Find best day to grant comp day (day with lowest staffing needs, not already assigned)
            DateOnly? bestDay = null;
            var bestScore = int.MaxValue;

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                // Skip if nurse already working this day
                if (IsNurseAssignedToday(nurse.Id, date, pendingAssignments))
                    continue;

                // Skip if already has comp day or leave this day
                var hasAbsence = await _context.CompensatoryTimeOffs.AnyAsync(c => c.NurseId == nurse.Id && c.Date == date)
                    || await _context.NurseLeaves.AnyAsync(l => l.NurseId == nurse.Id && l.StartDate <= date && l.EndDate >= date && l.Status == LeaveStatus.Approved);
                if (hasAbsence) continue;

                // Calculate staffing coverage for this day
                var totalAssigned = await _context.ShiftAssignments
                    .CountAsync(a => a.ClinicId == clinicId && a.Date == date);

                if (totalAssigned < bestScore)
                {
                    bestScore = totalAssigned;
                    bestDay = date;
                }
            }

            if (bestDay.HasValue)
            {
                // Grant comp day
                var compTimeOff = new CompensatoryTimeOff
                {
                    NurseId = nurse.Id,
                    Date = bestDay.Value,
                    HoursCompensated = 8,
                    Notes = "Auto-scheduled compensatory day off",
                    CreatedAt = DateTime.UtcNow
                };
                _context.CompensatoryTimeOffs.Add(compTimeOff);

                // Consume overtime balance
                await _overtimeService.ConsumeOvertimeAsync(nurse.Id, 8);

                result.CompDaysGranted++;
                result.Changes.Add(new ScheduleChange
                {
                    Type = ScheduleChangeType.CompDayGranted,
                    NurseName = nurse.FullName,
                    Date = bestDay.Value
                });
            }
        }
    }

    /// <summary>
    /// Rebalance schedule by swapping weaker assignments with better candidates
    /// </summary>
    private async Task RebalanceScheduleAsync(
        int clinicId, DateOnly startDate, DateOnly endDate,
        Clinic clinic, List<ShiftConfiguration> configs, SystemSettings settings,
        Dictionary<int, List<(DateOnly Date, ShiftType Shift)>> pendingAssignments,
        Dictionary<int, Dictionary<DateOnly, int>> weeklyHoursTracker,
        ScheduleResult result)
    {
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            foreach (ShiftType shiftType in Enum.GetValues<ShiftType>())
            {
                var config = configs.FirstOrDefault(c => c.ShiftType == shiftType && c.DayOfWeek == date.DayOfWeek)
                    ?? configs.FirstOrDefault(c => c.ShiftType == shiftType && c.DayOfWeek == null);

                if (config == null) continue;

                var shiftDuration = config.ShiftDurationHours;

                var currentAutoAssignments = await _context.ShiftAssignments
                    .Include(a => a.Nurse)
                    .Where(a => a.ClinicId == clinicId && a.Date == date &&
                                a.ShiftType == shiftType && !a.IsManualAssignment)
                    .ToListAsync();

                if (!currentAutoAssignments.Any()) continue;

                var assignedNurseIds = currentAutoAssignments.Select(a => a.NurseId).ToHashSet();

                var availableNurses = await _availabilityService.GetAvailableNursesAsync(clinicId, date, shiftType, true);
                var unassignedAvailable = availableNurses
                    .Where(n => n.IsAvailable && !assignedNurseIds.Contains(n.Nurse.Id))
                    .Where(n => !IsNurseAssignedToday(n.Nurse.Id, date, pendingAssignments))
                    .Where(n => !(clinic.AutoAssignHeadNurseMorning &&
                                  n.Nurse.Id == clinic.HeadNurseId &&
                                  (!IsWorkingDay(date) || shiftType != ShiftType.Morning)))
                    .Where(n => !WouldExceedWeeklyHours(n.Nurse, date, shiftDuration, weeklyHoursTracker, settings))
                    .Where(n => !WouldViolateRestPeriod(n.Nurse.Id, date, shiftType, pendingAssignments, n.Nurse.MinRestHoursBetweenShifts ?? settings.DefaultMinRestHoursBetweenShifts))
                    .OrderByDescending(n => n.Score)
                    .ToList();

                foreach (var available in unassignedAvailable)
                {
                    var nurse = available.Nurse;
                    var maxConsecutive = nurse.MaxConsecutiveWorkDays ?? settings.DefaultMaxConsecutiveWorkDays;
                    if (await WouldExceedConsecutiveDaysAsync(nurse.Id, date, pendingAssignments, maxConsecutive))
                        continue;

                    var weakest = currentAutoAssignments
                        .Where(a => a.Nurse != null)
                        .Where(a => !(clinic.AutoAssignHeadNurseMorning &&
                                      a.NurseId == clinic.HeadNurseId &&
                                      shiftType == ShiftType.Morning &&
                                      IsWorkingDay(date)))
                        .OrderBy(a => GetNurseScoreForRebalance(a.Nurse!, date, shiftType, settings))
                        .FirstOrDefault();

                    if (weakest?.Nurse == null) break;

                    var weakestScore = GetNurseScoreForRebalance(weakest.Nurse, date, shiftType, settings);

                    if (available.Score > weakestScore)
                    {
                        var wasResponsible = weakest.IsResponsibleNurse;

                        _context.ShiftAssignments.Remove(weakest);
                        currentAutoAssignments.Remove(weakest);
                        assignedNurseIds.Remove(weakest.NurseId);

                        if (pendingAssignments.ContainsKey(weakest.NurseId))
                            pendingAssignments[weakest.NurseId].RemoveAll(p => p.Date == date && p.Shift == shiftType);

                        var weekStart = GetWeekStart(date);
                        if (weeklyHoursTracker.ContainsKey(weakest.NurseId) &&
                            weeklyHoursTracker[weakest.NurseId].ContainsKey(weekStart))
                        {
                            weeklyHoursTracker[weakest.NurseId][weekStart] -= shiftDuration;
                        }

                        var isResponsible = wasResponsible && nurse.CanHandleResponsibility;
                        var newAssignment = new ShiftAssignment
                        {
                            Date = date,
                            ShiftType = shiftType,
                            ClinicId = clinicId,
                            NurseId = nurse.Id,
                            IsResponsibleNurse = isResponsible,
                            IsBorrowed = available.IsBorrowed,
                            Status = AssignmentStatus.Scheduled
                        };

                        _context.ShiftAssignments.Add(newAssignment);
                        currentAutoAssignments.Add(newAssignment);
                        assignedNurseIds.Add(nurse.Id);

                        TrackAssignment(nurse.Id, date, shiftType, shiftDuration, pendingAssignments, weeklyHoursTracker);

                        result.AssignmentsRemoved++;
                        result.AssignmentsAdded++;
                        result.Changes.Add(new ScheduleChange
                        {
                            Type = ScheduleChangeType.Removed,
                            NurseName = weakest.Nurse.FullName,
                            ShiftType = shiftType,
                            Date = date,
                            Reason = $"Replaced by {nurse.FullName}"
                        });
                        result.Changes.Add(new ScheduleChange
                        {
                            Type = ScheduleChangeType.Added,
                            NurseName = nurse.FullName,
                            ShiftType = shiftType,
                            Date = date
                        });

                        if (wasResponsible && !isResponsible)
                        {
                            var capableNurse = currentAutoAssignments
                                .FirstOrDefault(a => a.Nurse?.CanHandleResponsibility == true && !a.IsResponsibleNurse);
                            if (capableNurse != null)
                            {
                                capableNurse.IsResponsibleNurse = true;
                                result.Changes.Add(new ScheduleChange
                                {
                                    Type = ScheduleChangeType.MarkedResponsible,
                                    NurseName = capableNurse.Nurse!.FullName,
                                    ShiftType = shiftType,
                                    Date = date
                                });
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Clears all auto-assignments and rebuilds schedule from scratch.
    /// </summary>
    public async Task<ScheduleResult> ClearAndRebuildAsync(int clinicId, DateOnly startDate, DateOnly endDate, bool recalculateCompDays = false)
    {
        var result = new ScheduleResult();

        // Delete existing AUTO assignments (keep manual)
        var existingAutoAssignments = await _context.ShiftAssignments
            .Where(sa => sa.ClinicId == clinicId && sa.Date >= startDate && sa.Date <= endDate && !sa.IsManualAssignment)
            .ToListAsync();

        result.AssignmentsRemoved = existingAutoAssignments.Count;
        _context.ShiftAssignments.RemoveRange(existingAutoAssignments);

        // Optionally recalculate comp days
        if (recalculateCompDays)
        {
            // Revoke existing comp days in range
            var existingCompDays = await _context.CompensatoryTimeOffs
                .Where(c => c.Date >= startDate && c.Date <= endDate)
                .ToListAsync();

            foreach (var compDay in existingCompDays)
            {
                await _overtimeService.RevokeCompDayAsync(compDay.Id);
                result.CompDaysRevoked++;
                var nurse = await _context.Nurses.FindAsync(compDay.NurseId);
                result.Changes.Add(new ScheduleChange
                {
                    Type = ScheduleChangeType.CompDayRevoked,
                    NurseName = nurse?.FullName ?? "Unknown",
                    Date = compDay.Date
                });
            }

            // Recalculate overtime from historical assignments (last 12 weeks)
            var historyStart = startDate.AddDays(-84); // 12 weeks back
            var nurses = await _context.Nurses.Where(n => n.IsActive).ToListAsync();
            foreach (var nurse in nurses)
            {
                await _overtimeService.RecalculateFromAssignmentsAsync(nurse.Id, historyStart, startDate.AddDays(-1));
            }
        }

        await _context.SaveChangesAsync();

        // Now run the unified scheduling algorithm
        var scheduleResult = await ScheduleAsync(clinicId, startDate, endDate);

        // Merge results
        result.AssignmentsAdded = scheduleResult.AssignmentsAdded;
        result.CompDaysGranted = scheduleResult.CompDaysGranted;
        result.Changes.AddRange(scheduleResult.Changes);

        return result;
    }
}

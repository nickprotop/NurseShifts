using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;

namespace NurseShifts.Services;

public class OvertimeService : IOvertimeService
{
    private readonly AppDbContext _context;

    public OvertimeService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<OvertimeBalance?> GetWeeklyBalanceAsync(int nurseId, DateOnly weekStart)
    {
        return await _context.OvertimeBalances
            .FirstOrDefaultAsync(ob => ob.NurseId == nurseId && ob.WeekStartDate == weekStart);
    }

    public async Task<int> GetTotalOvertimeBalanceAsync(int nurseId)
    {
        var balances = await _context.OvertimeBalances
            .Where(ob => ob.NurseId == nurseId)
            .ToListAsync();

        return balances.Sum(b => b.BalanceHours);
    }

    public async Task UpdateWeeklyHoursAsync(int nurseId, DateOnly date, int hoursWorked)
    {
        var weekStart = GetWeekStart(date);
        var nurse = await _context.Nurses.FindAsync(nurseId);
        if (nurse == null) return;

        var balance = await _context.OvertimeBalances
            .FirstOrDefaultAsync(ob => ob.NurseId == nurseId && ob.WeekStartDate == weekStart);

        if (balance == null)
        {
            balance = new OvertimeBalance
            {
                NurseId = nurseId,
                WeekStartDate = weekStart,
                ContractedHours = nurse.ContractedHoursPerWeek,
                ActualHours = hoursWorked
            };
            _context.OvertimeBalances.Add(balance);
        }
        else
        {
            balance.ActualHours = hoursWorked;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<CompensatoryTimeOff> AddCompensatoryTimeOffAsync(int nurseId, DateOnly date, int hours, string? notes = null)
    {
        var compTime = new CompensatoryTimeOff
        {
            NurseId = nurseId,
            Date = date,
            HoursCompensated = hours,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.CompensatoryTimeOffs.Add(compTime);

        // Update the relevant week's balance
        var weekStart = GetWeekStart(date);
        var balance = await _context.OvertimeBalances
            .FirstOrDefaultAsync(ob => ob.NurseId == nurseId && ob.WeekStartDate == weekStart);

        if (balance != null)
        {
            balance.CompensatoryHoursUsed += hours;
        }

        await _context.SaveChangesAsync();
        return compTime;
    }

    public async Task RecalculateBalancesAsync(int nurseId)
    {
        var nurse = await _context.Nurses.FindAsync(nurseId);
        if (nurse == null) return;

        // Get all assignments for this nurse
        var assignments = await _context.ShiftAssignments
            .Where(sa => sa.NurseId == nurseId && sa.Status != AssignmentStatus.Cancelled)
            .OrderBy(sa => sa.Date)
            .ToListAsync();

        // Group by week
        var weeklyHours = assignments
            .GroupBy(a => GetWeekStart(a.Date))
            .ToDictionary(g => g.Key, g => g.Count() * 8); // Assume 8 hours per shift

        // Get comp time used per week
        var compTimeByWeek = await _context.CompensatoryTimeOffs
            .Where(cto => cto.NurseId == nurseId)
            .GroupBy(cto => GetWeekStart(cto.Date))
            .ToDictionaryAsync(g => g.Key, g => g.Sum(cto => cto.HoursCompensated));

        // Remove existing balances
        var existingBalances = await _context.OvertimeBalances
            .Where(ob => ob.NurseId == nurseId)
            .ToListAsync();
        _context.OvertimeBalances.RemoveRange(existingBalances);

        // Create new balances
        foreach (var week in weeklyHours)
        {
            compTimeByWeek.TryGetValue(week.Key, out var compUsed);

            var balance = new OvertimeBalance
            {
                NurseId = nurseId,
                WeekStartDate = week.Key,
                ContractedHours = nurse.ContractedHoursPerWeek,
                ActualHours = week.Value,
                CompensatoryHoursUsed = compUsed
            };
            _context.OvertimeBalances.Add(balance);
        }

        await _context.SaveChangesAsync();
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff);
    }

    public async Task<bool> HasCompDayInWeekAsync(int nurseId, DateOnly weekStart)
    {
        var weekEnd = weekStart.AddDays(6);
        return await _context.CompensatoryTimeOffs
            .AnyAsync(cto => cto.NurseId == nurseId &&
                            cto.Date >= weekStart &&
                            cto.Date <= weekEnd);
    }

    public async Task ConsumeOvertimeAsync(int nurseId, int hours)
    {
        // Find oldest week with positive balance and consume from there
        var balances = await _context.OvertimeBalances
            .Where(ob => ob.NurseId == nurseId && ob.BalanceHours > 0)
            .OrderBy(ob => ob.WeekStartDate)
            .ToListAsync();

        var remaining = hours;
        foreach (var balance in balances)
        {
            if (remaining <= 0) break;

            var toConsume = Math.Min(balance.BalanceHours, remaining);
            balance.CompensatoryHoursUsed += toConsume;
            remaining -= toConsume;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> RevokeCompDayAsync(int compTimeOffId)
    {
        var compDay = await _context.CompensatoryTimeOffs.FindAsync(compTimeOffId);
        if (compDay == null) return false;

        // Restore the hours to the relevant week's balance
        var weekStart = GetWeekStart(compDay.Date);
        var balance = await _context.OvertimeBalances
            .FirstOrDefaultAsync(ob => ob.NurseId == compDay.NurseId && ob.WeekStartDate == weekStart);

        if (balance != null)
        {
            balance.CompensatoryHoursUsed -= compDay.HoursCompensated;
            if (balance.CompensatoryHoursUsed < 0)
                balance.CompensatoryHoursUsed = 0;
        }

        _context.CompensatoryTimeOffs.Remove(compDay);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task RecalculateFromAssignmentsAsync(int nurseId, DateOnly startDate, DateOnly endDate)
    {
        var nurse = await _context.Nurses.FindAsync(nurseId);
        if (nurse == null) return;

        // Get all assignments in the range
        var assignments = await _context.ShiftAssignments
            .Where(sa => sa.NurseId == nurseId &&
                        sa.Date >= startDate &&
                        sa.Date <= endDate &&
                        sa.Status != AssignmentStatus.Cancelled)
            .ToListAsync();

        // Get shift configurations for duration info
        var configs = await _context.ShiftConfigurations.ToListAsync();

        // Group assignments by week and calculate hours
        var weeklyHours = new Dictionary<DateOnly, int>();
        foreach (var assignment in assignments)
        {
            var weekStart = GetWeekStart(assignment.Date);
            var duration = configs
                .FirstOrDefault(c => c.ShiftType == assignment.ShiftType &&
                                    (c.DayOfWeek == assignment.Date.DayOfWeek || c.DayOfWeek == null))
                ?.ShiftDurationHours ?? 8;

            if (!weeklyHours.ContainsKey(weekStart))
                weeklyHours[weekStart] = 0;
            weeklyHours[weekStart] += duration;
        }

        // Update or create balance records for each week
        foreach (var (weekStart, hours) in weeklyHours)
        {
            var balance = await _context.OvertimeBalances
                .FirstOrDefaultAsync(ob => ob.NurseId == nurseId && ob.WeekStartDate == weekStart);

            if (balance == null)
            {
                balance = new OvertimeBalance
                {
                    NurseId = nurseId,
                    WeekStartDate = weekStart,
                    ContractedHours = nurse.ContractedHoursPerWeek
                };
                _context.OvertimeBalances.Add(balance);
            }

            balance.ActualHours = hours;
        }

        await _context.SaveChangesAsync();
    }
}

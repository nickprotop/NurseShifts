using NurseShifts.Models;

namespace NurseShifts.Services;

public interface IOvertimeService
{
    Task<OvertimeBalance?> GetWeeklyBalanceAsync(int nurseId, DateOnly weekStart);
    Task<int> GetTotalOvertimeBalanceAsync(int nurseId);
    Task UpdateWeeklyHoursAsync(int nurseId, DateOnly date, int hoursWorked);
    Task<CompensatoryTimeOff> AddCompensatoryTimeOffAsync(int nurseId, DateOnly date, int hours, string? notes = null);
    Task RecalculateBalancesAsync(int nurseId);

    /// <summary>
    /// Check if nurse already has a compensatory day off in the given week
    /// </summary>
    Task<bool> HasCompDayInWeekAsync(int nurseId, DateOnly weekStart);

    /// <summary>
    /// Consume overtime hours when granting a comp day (marks hours as used)
    /// </summary>
    Task ConsumeOvertimeAsync(int nurseId, int hours);

    /// <summary>
    /// Revoke a comp day (delete absence record, restore overtime balance)
    /// </summary>
    Task<bool> RevokeCompDayAsync(int compTimeOffId);

    /// <summary>
    /// Recalculate overtime balances from historical shift assignments
    /// </summary>
    Task RecalculateFromAssignmentsAsync(int nurseId, DateOnly startDate, DateOnly endDate);
}

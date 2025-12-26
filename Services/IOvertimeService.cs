using NurseShifts.Models;

namespace NurseShifts.Services;

public interface IOvertimeService
{
    Task<OvertimeBalance?> GetWeeklyBalanceAsync(int nurseId, DateOnly weekStart);
    Task<int> GetTotalOvertimeBalanceAsync(int nurseId);
    Task UpdateWeeklyHoursAsync(int nurseId, DateOnly date, int hoursWorked);
    Task<CompensatoryTimeOff> AddCompensatoryTimeOffAsync(int nurseId, DateOnly date, int hours, string? notes = null);
    Task RecalculateBalancesAsync(int nurseId);
}

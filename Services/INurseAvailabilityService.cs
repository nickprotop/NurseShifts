using NurseShifts.Models;

namespace NurseShifts.Services;

public class NurseAvailability
{
    public Nurse Nurse { get; set; } = null!;
    public bool IsAvailable { get; set; }
    public bool IsBorrowed { get; set; }
    public int Score { get; set; }
    public List<string> UnavailabilityReasons { get; set; } = new();
}

public interface INurseAvailabilityService
{
    Task<List<NurseAvailability>> GetAvailableNursesAsync(int clinicId, DateOnly date, ShiftType shiftType, bool includeBorrowable = true);
    Task<bool> IsNurseOnLeaveAsync(int nurseId, DateOnly date);
    Task<bool> IsNurseOnCompTimeAsync(int nurseId, DateOnly date);
    Task<int> GetWeeklyHoursAsync(int nurseId, DateOnly weekStart);
    Task<int> GetConsecutiveWorkDaysAsync(int nurseId, DateOnly date);
}

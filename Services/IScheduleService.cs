using NurseShifts.Models;

namespace NurseShifts.Services;

public interface IScheduleService
{
    Task<List<ShiftAssignment>> GenerateScheduleAsync(int clinicId, DateOnly startDate, int days);
    Task<List<ShiftAssignment>> GetScheduleAsync(int clinicId, DateOnly startDate, DateOnly endDate);
    Task<ShiftAssignment?> AssignNurseToShiftAsync(int clinicId, DateOnly date, ShiftType shiftType, int nurseId, bool isResponsible = false);
    Task<bool> RemoveAssignmentAsync(int assignmentId);
}

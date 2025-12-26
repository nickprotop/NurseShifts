using NurseShifts.Models;

namespace NurseShifts.Services;

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public interface IValidationService
{
    Task<ValidationResult> ValidateShiftAssignmentAsync(int clinicId, DateOnly date, ShiftType shiftType, int nurseId);
    Task<ValidationResult> ValidateShiftCoverageAsync(int clinicId, DateOnly date, ShiftType shiftType);
    Task<bool> CanNurseWorkShiftAsync(int nurseId, DateOnly date, ShiftType shiftType, int clinicId);
}

using NurseShifts.Models;

namespace NurseShifts.Services;

public interface IScheduleService
{
    /// <summary>
    /// Unified scheduling algorithm - the single source of truth for schedule management.
    /// Steps: Calculate/grant comp days → Remove unavailable → Fill gaps → Rebalance → Ensure responsible nurse
    /// </summary>
    Task<ScheduleResult> ScheduleAsync(int clinicId, DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Clears all auto-assignments and rebuilds the schedule from scratch.
    /// Manual assignments are preserved.
    /// </summary>
    /// <param name="recalculateCompDays">If true, revokes existing comp days and recalculates from historical assignments</param>
    Task<ScheduleResult> ClearAndRebuildAsync(int clinicId, DateOnly startDate, DateOnly endDate, bool recalculateCompDays = false);

    Task<List<ShiftAssignment>> GetScheduleAsync(int clinicId, DateOnly startDate, DateOnly endDate);
    Task<ShiftAssignment?> AssignNurseToShiftAsync(int clinicId, DateOnly date, ShiftType shiftType, int nurseId, bool isResponsible = false);
    Task<bool> RemoveAssignmentAsync(int assignmentId);
}

/// <summary>
/// Result of unified scheduling operation
/// </summary>
public class ScheduleResult
{
    public int AssignmentsAdded { get; set; }
    public int AssignmentsRemoved { get; set; }
    public int CompDaysGranted { get; set; }
    public int CompDaysRevoked { get; set; }
    public List<ScheduleChange> Changes { get; set; } = new();
}

/// <summary>
/// Represents a single change made during scheduling
/// </summary>
public class ScheduleChange
{
    public ScheduleChangeType Type { get; set; }
    public string NurseName { get; set; } = string.Empty;
    public ShiftType ShiftType { get; set; }
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
}

public enum ScheduleChangeType
{
    Added,
    Removed,
    MarkedResponsible,
    CompDayGranted,
    CompDayRevoked
}

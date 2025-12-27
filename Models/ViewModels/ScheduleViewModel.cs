namespace NurseShifts.Models.ViewModels;

public class ScheduleViewModel
{
    public int ClinicId { get; set; }
    public Clinic? Clinic { get; set; }
    public DateOnly StartDate { get; set; }
    public int Weeks { get; set; }
    public int Days => Weeks * 7;  // Computed for backwards compatibility
    public List<ShiftAssignment> Assignments { get; set; } = new();
    public List<DaySchedule> DaySchedules { get; set; } = new();
}

public class DaySchedule
{
    public DateOnly Date { get; set; }
    public List<ShiftSchedule> Shifts { get; set; } = new();
}

public class ShiftSchedule
{
    public ShiftType ShiftType { get; set; }
    public List<ShiftAssignment> Assignments { get; set; } = new();
    public int RequiredNurses { get; set; }
    public bool IsValid { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class GenerateScheduleRequest
{
    public int ClinicId { get; set; }
    public DateOnly StartDate { get; set; }
    public int Weeks { get; set; } = 1;
}

public class ShiftCellViewModel
{
    public int ClinicId { get; set; }
    public DateOnly Date { get; set; }
    public ShiftType ShiftType { get; set; }
    public List<ShiftAssignment> Assignments { get; set; } = new();
    public int RequiredNurses { get; set; }
    public bool IsValid { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

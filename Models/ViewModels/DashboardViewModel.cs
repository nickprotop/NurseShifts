namespace NurseShifts.Models.ViewModels;

public class DashboardViewModel
{
    public DateOnly TodayDate { get; set; }
    public int TotalClinics { get; set; }
    public int TotalNurses { get; set; }
    public int TodayShifts { get; set; }
    public int PendingLeaveRequests { get; set; }
    public int UnderstaffedShifts { get; set; }
    public List<ShiftAssignment> TodayAssignments { get; set; } = new();
    public List<NurseLeave> UpcomingLeaves { get; set; } = new();
    public List<NurseOvertimeInfo> HighOvertimeNurses { get; set; } = new();
}

public class UnderstaffedShiftInfo
{
    public DateOnly Date { get; set; }
    public string ClinicName { get; set; } = string.Empty;
    public ShiftType ShiftType { get; set; }
    public int Assigned { get; set; }
    public int Required { get; set; }
}

public class NurseOvertimeInfo
{
    public int NurseId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int OvertimeBalance { get; set; }
}

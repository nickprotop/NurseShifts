namespace NurseShifts.Models.ViewModels;

public class WorkloadSummaryViewModel
{
    public int ClinicId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public List<NurseWorkload> Workloads { get; set; } = new();
}

public class NurseWorkload
{
    public int NurseId { get; set; }
    public string NurseName { get; set; } = string.Empty;
    public decimal TotalHours { get; set; }
    public decimal ContractedHours { get; set; }
    public decimal OvertimeHours => Math.Max(0, TotalHours - ContractedHours);
    public int ShiftCount { get; set; }

    /// <summary>
    /// Percentage of contracted hours worked (can exceed 100%)
    /// </summary>
    public int PercentageOfContract => ContractedHours > 0
        ? (int)(TotalHours * 100 / ContractedHours)
        : 0;

    /// <summary>
    /// Workload status: on-target, overtime, excessive
    /// </summary>
    public string Status => PercentageOfContract switch
    {
        <= 100 => "on-target",
        <= 120 => "overtime",
        _ => "excessive"
    };
}

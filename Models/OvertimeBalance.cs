using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NurseShifts.Models;

public class OvertimeBalance
{
    public int Id { get; set; }

    [Required]
    public int NurseId { get; set; }

    [ForeignKey(nameof(NurseId))]
    public Nurse? Nurse { get; set; }

    [Required]
    [Display(Name = "Week Start Date")]
    [DataType(DataType.Date)]
    public DateOnly WeekStartDate { get; set; }

    [Display(Name = "Contracted Hours")]
    public int ContractedHours { get; set; } = 40;

    [Display(Name = "Actual Hours")]
    public int ActualHours { get; set; } = 0;

    [NotMapped]
    [Display(Name = "Overtime Hours")]
    public int OvertimeHours => Math.Max(0, ActualHours - ContractedHours);

    [Display(Name = "Compensatory Hours Used")]
    public int CompensatoryHoursUsed { get; set; } = 0;

    [NotMapped]
    [Display(Name = "Balance Hours")]
    public int BalanceHours => OvertimeHours - CompensatoryHoursUsed;
}

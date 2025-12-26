using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NurseShifts.Models;

public class ShiftConfiguration
{
    public int Id { get; set; }

    [Required]
    public int ClinicId { get; set; }

    [ForeignKey(nameof(ClinicId))]
    public Clinic? Clinic { get; set; }

    [Required]
    [Display(Name = "Shift Type")]
    public ShiftType ShiftType { get; set; }

    [Required]
    [Display(Name = "Required Nurses")]
    [Range(1, 20)]
    public int RequiredNurses { get; set; } = 1;

    [Display(Name = "Required Senior Nurses")]
    [Range(0, 10)]
    public int RequiredSeniorNurses { get; set; } = 0;

    [Display(Name = "Requires Responsible Nurse")]
    public bool RequiresResponsibleNurse { get; set; } = true;

    [Display(Name = "Day of Week")]
    public DayOfWeek? DayOfWeek { get; set; }

    [Display(Name = "Shift Duration (Hours)")]
    [Range(4, 12)]
    public int ShiftDurationHours { get; set; } = 8;
}

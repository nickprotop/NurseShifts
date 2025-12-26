using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NurseShifts.Models;

public class ShiftAssignment
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "Date")]
    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    [Required]
    [Display(Name = "Shift Type")]
    public ShiftType ShiftType { get; set; }

    [Required]
    public int ClinicId { get; set; }

    [ForeignKey(nameof(ClinicId))]
    public Clinic? Clinic { get; set; }

    [Required]
    public int NurseId { get; set; }

    [ForeignKey(nameof(NurseId))]
    public Nurse? Nurse { get; set; }

    [Display(Name = "Responsible Nurse")]
    public bool IsResponsibleNurse { get; set; } = false;

    [Display(Name = "Borrowed")]
    public bool IsBorrowed { get; set; } = false;

    [Display(Name = "Status")]
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Scheduled;

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

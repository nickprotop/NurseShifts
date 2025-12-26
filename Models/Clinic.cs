using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NurseShifts.Models;

public class Clinic
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Clinic Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Head Nurse")]
    public int? HeadNurseId { get; set; }

    [ForeignKey(nameof(HeadNurseId))]
    public Nurse? HeadNurse { get; set; }

    /// <summary>
    /// If true, automatically assign head nurse to morning shifts on working days (Mon-Fri)
    /// </summary>
    [Display(Name = "Auto-assign Head Nurse to Morning Shifts")]
    public bool AutoAssignHeadNurseMorning { get; set; } = true;

    // Navigation properties
    public ICollection<Nurse> Nurses { get; set; } = new List<Nurse>();
    public ICollection<ShiftConfiguration> ShiftConfigurations { get; set; } = new List<ShiftConfiguration>();
    public ICollection<ShiftAssignment> ShiftAssignments { get; set; } = new List<ShiftAssignment>();
    public ICollection<NurseClinicAssignment> BorrowableNurses { get; set; } = new List<NurseClinicAssignment>();
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NurseShifts.Models;

public class NurseClinicAssignment
{
    public int Id { get; set; }

    [Required]
    public int NurseId { get; set; }

    [ForeignKey(nameof(NurseId))]
    public Nurse? Nurse { get; set; }

    [Required]
    public int ClinicId { get; set; }

    [ForeignKey(nameof(ClinicId))]
    public Clinic? Clinic { get; set; }

    [Display(Name = "Can Be Borrowed")]
    public bool CanBeBorrowed { get; set; } = true;
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NurseShifts.Models;

public class CompensatoryTimeOff
{
    public int Id { get; set; }

    [Required]
    public int NurseId { get; set; }

    [ForeignKey(nameof(NurseId))]
    public Nurse? Nurse { get; set; }

    [Required]
    [Display(Name = "Date")]
    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    [Required]
    [Display(Name = "Hours Compensated")]
    [Range(1, 12)]
    public int HoursCompensated { get; set; } = 8;

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Created At")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

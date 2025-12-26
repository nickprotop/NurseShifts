using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NurseShifts.Models;

public class NurseShiftWish
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
    [Display(Name = "Wish Type")]
    public WishType WishType { get; set; }

    [Display(Name = "Shift Type")]
    public ShiftType? ShiftType { get; set; }

    [Display(Name = "Priority")]
    [Range(1, 5)]
    public int Priority { get; set; } = 3;

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

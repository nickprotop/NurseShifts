using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NurseShifts.Models;

public class Nurse
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [NotMapped]
    public string FullName => $"{LastName} {FirstName}";

    [NotMapped]
    public string ShortName => LastName;

    [StringLength(100)]
    [EmailAddress]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(20)]
    [Phone]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Required]
    [Display(Name = "Primary Clinic")]
    public int PrimaryClinicId { get; set; }

    [ForeignKey(nameof(PrimaryClinicId))]
    public Clinic? PrimaryClinic { get; set; }

    [Display(Name = "Pool Nurse")]
    public bool IsPoolNurse { get; set; } = false;

    [Display(Name = "Employment Type")]
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;

    [Display(Name = "Contracted Hours Per Week")]
    [Range(1, 60)]
    public int ContractedHoursPerWeek { get; set; } = 40;

    [Display(Name = "Max Overtime Hours Per Week")]
    [Range(0, 40)]
    public int? MaxOvertimeHoursPerWeek { get; set; }

    [Display(Name = "Hire Date")]
    [DataType(DataType.Date)]
    public DateOnly HireDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    // Responsibility & Skills
    [Display(Name = "Can Handle Responsibility")]
    public bool CanHandleResponsibility { get; set; } = false;

    [Display(Name = "Skill Level")]
    public SkillLevel SkillLevel { get; set; } = SkillLevel.Junior;

    // Shift Preferences
    [Display(Name = "Preferred Shifts")]
    public ShiftTypeFlags PreferredShifts { get; set; } = ShiftTypeFlags.All;

    [Display(Name = "Avoided Shifts")]
    public ShiftTypeFlags AvoidedShifts { get; set; } = ShiftTypeFlags.None;

    [Display(Name = "Max Consecutive Work Days")]
    [Range(1, 14)]
    public int? MaxConsecutiveWorkDays { get; set; }

    [Display(Name = "Min Rest Hours Between Shifts")]
    [Range(8, 24)]
    public int? MinRestHoursBetweenShifts { get; set; }

    [Display(Name = "Prefers Consecutive Days")]
    public bool PrefersConsecutiveDays { get; set; } = false;

    // Status
    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<NurseClinicAssignment> ClinicAssignments { get; set; } = new List<NurseClinicAssignment>();
    public ICollection<NurseLeave> Leaves { get; set; } = new List<NurseLeave>();
    public ICollection<NurseShiftWish> ShiftWishes { get; set; } = new List<NurseShiftWish>();
    public ICollection<ShiftAssignment> ShiftAssignments { get; set; } = new List<ShiftAssignment>();
    public ICollection<OvertimeBalance> OvertimeBalances { get; set; } = new List<OvertimeBalance>();
    public ICollection<CompensatoryTimeOff> CompensatoryTimeOffs { get; set; } = new List<CompensatoryTimeOff>();
}

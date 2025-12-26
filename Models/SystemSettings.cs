using System.ComponentModel.DataAnnotations;

namespace NurseShifts.Models;

public class SystemSettings
{
    public int Id { get; set; }

    [Display(Name = "Default Contracted Hours Per Week")]
    [Range(1, 60)]
    public int DefaultContractedHoursPerWeek { get; set; } = 40;

    [Display(Name = "Default Max Overtime Hours Per Week")]
    [Range(0, 40)]
    public int DefaultMaxOvertimeHoursPerWeek { get; set; } = 8;

    [Display(Name = "Default Min Rest Hours Between Shifts")]
    [Range(8, 24)]
    public int DefaultMinRestHoursBetweenShifts { get; set; } = 11;

    [Display(Name = "Default Max Consecutive Work Days")]
    [Range(1, 14)]
    public int DefaultMaxConsecutiveWorkDays { get; set; } = 5;

    [Display(Name = "Language")]
    [StringLength(5)]
    public string Language { get; set; } = "en";

    [Display(Name = "Admin Username")]
    [StringLength(50)]
    public string AdminUsername { get; set; } = "admin";

    [Display(Name = "Admin Password Hash")]
    public string AdminPasswordHash { get; set; } = string.Empty;
}

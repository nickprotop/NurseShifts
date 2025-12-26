using NurseShifts.Models;

namespace NurseShifts.Tests.Helpers;

public static class TestDataBuilder
{
    public static SystemSettings CreateSettings(
        int maxOvertimeHours = 10,
        int minRestHours = 11,
        int maxConsecutiveDays = 6,
        int contractedHours = 40)
    {
        return new SystemSettings
        {
            Id = 1,
            DefaultMaxOvertimeHoursPerWeek = maxOvertimeHours,
            DefaultMinRestHoursBetweenShifts = minRestHours,
            DefaultMaxConsecutiveWorkDays = maxConsecutiveDays,
            DefaultContractedHoursPerWeek = contractedHours,
            Language = "en"
        };
    }

    public static Clinic CreateClinic(
        int id = 1,
        string name = "Test Clinic",
        int? headNurseId = null,
        bool autoAssignHeadNurse = false)
    {
        return new Clinic
        {
            Id = id,
            Name = name,
            HeadNurseId = headNurseId,
            AutoAssignHeadNurseMorning = autoAssignHeadNurse
        };
    }

    public static Nurse CreateNurse(
        int id = 1,
        string firstName = "Test",
        string lastName = "Nurse",
        int primaryClinicId = 1,
        bool isActive = true,
        bool isPoolNurse = false,
        bool canHandleResponsibility = true,
        int contractedHours = 40,
        EmploymentType employmentType = EmploymentType.FullTime,
        SkillLevel skillLevel = SkillLevel.Mid,
        ShiftTypeFlags preferredShifts = ShiftTypeFlags.None,
        ShiftTypeFlags avoidedShifts = ShiftTypeFlags.None,
        int? maxConsecutiveDays = null,
        int? minRestHours = null,
        int? maxOvertimeHours = null)
    {
        return new Nurse
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            PrimaryClinicId = primaryClinicId,
            IsActive = isActive,
            IsPoolNurse = isPoolNurse,
            CanHandleResponsibility = canHandleResponsibility,
            ContractedHoursPerWeek = contractedHours,
            EmploymentType = employmentType,
            SkillLevel = skillLevel,
            PreferredShifts = preferredShifts,
            AvoidedShifts = avoidedShifts,
            MaxConsecutiveWorkDays = maxConsecutiveDays,
            MinRestHoursBetweenShifts = minRestHours,
            MaxOvertimeHoursPerWeek = maxOvertimeHours,
            HireDate = DateOnly.FromDateTime(DateTime.Today.AddYears(-1))
        };
    }

    public static ShiftConfiguration CreateConfig(
        int id = 1,
        int clinicId = 1,
        ShiftType shiftType = ShiftType.Morning,
        int requiredNurses = 2,
        int shiftDurationHours = 8,
        bool requiresResponsibleNurse = true,
        DayOfWeek? dayOfWeek = null)
    {
        return new ShiftConfiguration
        {
            Id = id,
            ClinicId = clinicId,
            ShiftType = shiftType,
            RequiredNurses = requiredNurses,
            ShiftDurationHours = shiftDurationHours,
            RequiresResponsibleNurse = requiresResponsibleNurse,
            DayOfWeek = dayOfWeek
        };
    }

    public static ShiftAssignment CreateAssignment(
        int id = 1,
        int nurseId = 1,
        int clinicId = 1,
        DateOnly? date = null,
        ShiftType shiftType = ShiftType.Morning,
        bool isResponsible = false,
        bool isBorrowed = false,
        AssignmentStatus status = AssignmentStatus.Scheduled)
    {
        return new ShiftAssignment
        {
            Id = id,
            NurseId = nurseId,
            ClinicId = clinicId,
            Date = date ?? DateOnly.FromDateTime(DateTime.Today),
            ShiftType = shiftType,
            IsResponsibleNurse = isResponsible,
            IsBorrowed = isBorrowed,
            Status = status
        };
    }

    public static NurseLeave CreateLeave(
        int id = 1,
        int nurseId = 1,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        LeaveType leaveType = LeaveType.Vacation,
        LeaveStatus status = LeaveStatus.Approved)
    {
        var start = startDate ?? DateOnly.FromDateTime(DateTime.Today);
        return new NurseLeave
        {
            Id = id,
            NurseId = nurseId,
            StartDate = start,
            EndDate = endDate ?? start.AddDays(1),
            LeaveType = leaveType,
            Status = status
        };
    }

    public static NurseShiftWish CreateWish(
        int id = 1,
        int nurseId = 1,
        DateOnly? date = null,
        ShiftType? shiftType = null,
        WishType wishType = WishType.Unavailable)
    {
        return new NurseShiftWish
        {
            Id = id,
            NurseId = nurseId,
            Date = date ?? DateOnly.FromDateTime(DateTime.Today),
            ShiftType = shiftType,
            WishType = wishType
        };
    }

    public static CompensatoryTimeOff CreateCompTime(
        int id = 1,
        int nurseId = 1,
        DateOnly? date = null,
        int hoursCompensated = 8)
    {
        return new CompensatoryTimeOff
        {
            Id = id,
            NurseId = nurseId,
            Date = date ?? DateOnly.FromDateTime(DateTime.Today),
            HoursCompensated = hoursCompensated,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static OvertimeBalance CreateOvertimeBalance(
        int id = 1,
        int nurseId = 1,
        DateOnly? weekStart = null,
        int contractedHours = 40,
        int actualHours = 40,
        int compensatoryHoursUsed = 0)
    {
        return new OvertimeBalance
        {
            Id = id,
            NurseId = nurseId,
            WeekStartDate = weekStart ?? GetWeekStart(DateOnly.FromDateTime(DateTime.Today)),
            ContractedHours = contractedHours,
            ActualHours = actualHours,
            CompensatoryHoursUsed = compensatoryHoursUsed
        };
    }

    public static NurseClinicAssignment CreateClinicAssignment(
        int id = 1,
        int nurseId = 1,
        int clinicId = 1)
    {
        return new NurseClinicAssignment
        {
            Id = id,
            NurseId = nurseId,
            ClinicId = clinicId
        };
    }

    public static DateOnly GetWeekStart(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek - 1 + 7) % 7;
        return date.AddDays(-daysFromMonday);
    }

    public static DateOnly GetNextWeekday(DayOfWeek targetDay)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysUntilTarget = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilTarget == 0) daysUntilTarget = 7;
        return today.AddDays(daysUntilTarget);
    }
}

namespace NurseShifts.Models;

public enum ShiftType
{
    Morning,    // 07:00 - 15:00
    Afternoon,  // 15:00 - 23:00
    Night       // 23:00 - 07:00
}

[Flags]
public enum ShiftTypeFlags
{
    None = 0,
    Morning = 1,
    Afternoon = 2,
    Night = 4,
    All = Morning | Afternoon | Night
}

public enum EmploymentType
{
    FullTime,
    PartTime,
    PerDiem
}

public enum SkillLevel
{
    Junior,
    Mid,
    Senior
}

public enum LeaveType
{
    Sick,
    Vacation,
    Personal,
    Maternity,
    Unpaid
}

public enum LeaveStatus
{
    Pending,
    Approved,
    Rejected
}

public enum WishType
{
    WantToWork,
    PreferOff,
    Unavailable
}

public enum AssignmentStatus
{
    Scheduled,
    Confirmed,
    Swapped,
    Cancelled
}

public enum DayOfWeekFlag
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    All = Weekdays | Weekend
}

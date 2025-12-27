using FluentAssertions;
using NurseShifts.Models;
using NurseShifts.Services;
using NurseShifts.Tests.Helpers;
using Xunit;

namespace NurseShifts.Tests.Unit.Services;

public class NurseAvailabilityServiceTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task GetAvailableNurses_ReturnsClinicNurses()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning, includeBorrowable: false);

        // Assert
        result.Should().HaveCount(1);
        result[0].Nurse.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetAvailableNurses_IncludesPoolNurses()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic1 = TestDataBuilder.CreateClinic(id: 1, name: "Clinic 1");
        var clinic2 = TestDataBuilder.CreateClinic(id: 2, name: "Clinic 2");
        var regularNurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var poolNurse = TestDataBuilder.CreateNurse(id: 2, primaryClinicId: 2, isPoolNurse: true);
        context.Clinics.AddRange(clinic1, clinic2);
        context.Nurses.AddRange(regularNurse, poolNurse);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning, includeBorrowable: true);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(a => a.Nurse.Id == 1 && !a.IsBorrowed);
        result.Should().Contain(a => a.Nurse.Id == 2 && a.IsBorrowed);
    }

    [Fact]
    public async Task GetAvailableNurses_IncludesBorrowableNurses_WhenPrimaryClinicFull()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic1 = TestDataBuilder.CreateClinic(id: 1, name: "Clinic 1");
        var clinic2 = TestDataBuilder.CreateClinic(id: 2, name: "Clinic 2");
        var regularNurse1 = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var borrowableNurse = TestDataBuilder.CreateNurse(id: 2, primaryClinicId: 2);

        // Config for clinic 2 - requires 1 nurse
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 2, requiredNurses: 1);

        // Assign a nurse to clinic 2 to make it "full"
        var thirdNurse = TestDataBuilder.CreateNurse(id: 3, primaryClinicId: 2);
        var assignment = TestDataBuilder.CreateAssignment(id: 1, nurseId: 3, clinicId: 2, date: Today);

        // Make nurse 2 borrowable to clinic 1
        var clinicAssignment = TestDataBuilder.CreateClinicAssignment(id: 1, nurseId: 2, clinicId: 1);
        clinicAssignment.CanBeBorrowed = true;

        context.Clinics.AddRange(clinic1, clinic2);
        context.Nurses.AddRange(regularNurse1, borrowableNurse, thirdNurse);
        context.ShiftConfigurations.Add(config);
        context.ShiftAssignments.Add(assignment);
        context.NurseClinicAssignments.Add(clinicAssignment);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning, includeBorrowable: true);

        // Assert
        result.Should().Contain(a => a.Nurse.Id == 2 && a.IsBorrowed);
    }

    [Fact]
    public async Task EvaluateAvailability_OnLeave_NotAvailable()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var leave = TestDataBuilder.CreateLeave(nurseId: 1, startDate: Today, endDate: Today.AddDays(5));
        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        context.NurseLeaves.Add(leave);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning);

        // Assert
        result.Should().HaveCount(1);
        result[0].IsAvailable.Should().BeFalse();
        result[0].UnavailabilityReasons.Should().Contain("On leave");
    }

    [Fact]
    public async Task EvaluateAvailability_OnCompTime_NotAvailable()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var compTime = TestDataBuilder.CreateCompTime(nurseId: 1, date: Today);
        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        context.CompensatoryTimeOffs.Add(compTime);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning);

        // Assert
        result[0].IsAvailable.Should().BeFalse();
        result[0].UnavailabilityReasons.Should().Contain("On compensatory time off");
    }

    [Fact]
    public async Task EvaluateAvailability_AlreadyAssigned_NotAvailable()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var assignment = TestDataBuilder.CreateAssignment(nurseId: 1, clinicId: 1, date: Today);
        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        context.ShiftAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Afternoon);

        // Assert
        result[0].IsAvailable.Should().BeFalse();
        result[0].UnavailabilityReasons.Should().Contain("Already assigned to a shift");
    }

    [Fact]
    public async Task EvaluateAvailability_AvoidedShift_NotAvailable()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1, avoidedShifts: ShiftTypeFlags.Night);
        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Night);

        // Assert
        result[0].IsAvailable.Should().BeFalse();
        result[0].UnavailabilityReasons.Should().Contain("Shift type is avoided");
    }

    [Fact]
    public async Task EvaluateAvailability_UnavailableWish_NotAvailable()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var wish = TestDataBuilder.CreateWish(nurseId: 1, date: Today, wishType: WishType.Unavailable);
        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        context.NurseShiftWishes.Add(wish);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning);

        // Assert
        result[0].IsAvailable.Should().BeFalse();
        result[0].UnavailabilityReasons.Should().Contain("Marked as unavailable");
    }

    [Fact]
    public async Task EvaluateAvailability_RestPeriodViolation_NotAvailable()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1, minRestHours: 11);
        var previousNightShift = TestDataBuilder.CreateAssignment(
            nurseId: 1, clinicId: 1, date: Today.AddDays(-1), shiftType: ShiftType.Night);
        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        context.ShiftAssignments.Add(previousNightShift);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning);

        // Assert
        result[0].IsAvailable.Should().BeFalse();
        result[0].UnavailabilityReasons.Should().Contain("Insufficient rest from night shift");
    }

    [Fact]
    public async Task ScoreCalculation_PreferredShift_HigherScore()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var prefersMorning = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1, preferredShifts: ShiftTypeFlags.Morning);
        var noPreference = TestDataBuilder.CreateNurse(id: 2, primaryClinicId: 1);
        context.Clinics.Add(clinic);
        context.Nurses.AddRange(prefersMorning, noPreference);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning);

        // Assert
        var morningPreferred = result.First(a => a.Nurse.Id == 1);
        var noPref = result.First(a => a.Nurse.Id == 2);
        morningPreferred.Score.Should().BeGreaterThan(noPref.Score);
    }

    [Fact]
    public async Task ScoreCalculation_WantToWorkWish_HigherScore()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, primaryClinicId: 1);
        var wantToWork = TestDataBuilder.CreateWish(id: 1, nurseId: 1, date: Today, wishType: WishType.WantToWork);
        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        context.NurseShiftWishes.Add(wantToWork);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetAvailableNursesAsync(1, Today, ShiftType.Morning);

        // Assert
        var withWish = result.First(a => a.Nurse.Id == 1);
        var withoutWish = result.First(a => a.Nurse.Id == 2);
        withWish.Score.Should().BeGreaterThan(withoutWish.Score);
    }

    [Fact]
    public async Task ScoreCalculation_OvertimeHours_LowerScore()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        // Nurse 1: 40 contracted, nurse 2: also 40 contracted
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1, contractedHours: 40);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, primaryClinicId: 1, contractedHours: 40);

        // Use a fixed date for predictable week calculation
        // Wednesday Jan 8, 2025 -> week starts Monday Jan 6
        var testDate = new DateOnly(2025, 1, 8);
        var weekStart = new DateOnly(2025, 1, 6); // Monday

        // Nurse 1: 3 shifts (24 hours) - well below contracted
        for (int i = 0; i < 3; i++)
        {
            context.ShiftAssignments.Add(TestDataBuilder.CreateAssignment(
                id: i + 1, nurseId: 1, clinicId: 1, date: weekStart.AddDays(i)));
        }

        // Nurse 2: 5 shifts (40 hours) - at contracted, adding more would be overtime
        for (int i = 0; i < 5; i++)
        {
            context.ShiftAssignments.Add(TestDataBuilder.CreateAssignment(
                id: i + 10, nurseId: 2, clinicId: 1, date: weekStart.AddDays(i)));
        }

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act - get availability for Saturday (no existing assignment for either nurse)
        var saturday = weekStart.AddDays(5);
        var result = await service.GetAvailableNursesAsync(1, saturday, ShiftType.Morning);

        // Assert
        var belowHours = result.First(a => a.Nurse.Id == 1);
        var atContracted = result.First(a => a.Nurse.Id == 2);

        // Nurse 1: 24 hours, below contracted (+3), won't go overtime (no penalty)
        // Nurse 2: 40 hours, not below contracted (no +3), adding 8 = 48 > 40 (overtime penalty -4)
        // Score difference should be: 3 - (-4) = 7
        belowHours.Score.Should().BeGreaterThan(atContracted.Score);
    }

    [Fact]
    public async Task IsNurseOnLeave_WithApprovedLeave_ReturnsTrue()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var leave = TestDataBuilder.CreateLeave(nurseId: 1, startDate: Today, endDate: Today.AddDays(3), status: LeaveStatus.Approved);
        context.NurseLeaves.Add(leave);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.IsNurseOnLeaveAsync(1, Today.AddDays(1));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsNurseOnLeave_WithPendingLeave_ReturnsFalse()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var leave = TestDataBuilder.CreateLeave(nurseId: 1, startDate: Today, endDate: Today.AddDays(3), status: LeaveStatus.Pending);
        context.NurseLeaves.Add(leave);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.IsNurseOnLeaveAsync(1, Today.AddDays(1));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetConsecutiveWorkDays_ReturnsCorrectCount()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);

        // Create 4 consecutive days of assignments before today
        for (int i = 1; i <= 4; i++)
        {
            context.ShiftAssignments.Add(TestDataBuilder.CreateAssignment(
                id: i, nurseId: 1, clinicId: 1, date: Today.AddDays(-i)));
        }

        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetConsecutiveWorkDaysAsync(1, Today);

        // Assert
        result.Should().Be(4);
    }

    [Fact]
    public async Task GetWeeklyHours_CalculatesCorrectly()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var weekStart = Today.AddDays(-(int)Today.DayOfWeek + 1);

        // Add 5 shifts this week
        for (int i = 0; i < 5; i++)
        {
            context.ShiftAssignments.Add(TestDataBuilder.CreateAssignment(
                id: i + 1, nurseId: 1, clinicId: 1, date: weekStart.AddDays(i)));
        }

        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        await context.SaveChangesAsync();

        var service = new NurseAvailabilityService(context);

        // Act
        var result = await service.GetWeeklyHoursAsync(1, weekStart);

        // Assert
        result.Should().Be(40); // 5 shifts * 8 hours
    }
}

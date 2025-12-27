using FluentAssertions;
using Moq;
using NurseShifts.Models;
using NurseShifts.Services;
using NurseShifts.Tests.Helpers;
using Xunit;

namespace NurseShifts.Tests.Unit.Services;

public class ScheduleServiceTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private (ScheduleService, Mock<INurseAvailabilityService>, Mock<IValidationService>, Mock<IOvertimeService>) CreateService()
    {
        var context = TestDbContextFactory.CreateInMemory();
        var availabilityMock = new Mock<INurseAvailabilityService>();
        var validationMock = new Mock<IValidationService>();
        var overtimeMock = new Mock<IOvertimeService>();

        var service = new ScheduleService(context, availabilityMock.Object, validationMock.Object, overtimeMock.Object);
        return (service, availabilityMock, validationMock, overtimeMock);
    }

    [Fact]
    public async Task GetSchedule_ReturnsAssignmentsForDateRange()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);

        // Add assignments for different dates
        var assignment1 = TestDataBuilder.CreateAssignment(id: 1, clinicId: 1, nurseId: 1, date: Today);
        var assignment2 = TestDataBuilder.CreateAssignment(id: 2, clinicId: 1, nurseId: 1, date: Today.AddDays(1));
        var assignment3 = TestDataBuilder.CreateAssignment(id: 3, clinicId: 1, nurseId: 1, date: Today.AddDays(5)); // Outside range

        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        context.ShiftAssignments.AddRange(assignment1, assignment2, assignment3);
        await context.SaveChangesAsync();

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            Mock.Of<IValidationService>(),
            Mock.Of<IOvertimeService>());

        // Act
        var result = await service.GetScheduleAsync(1, Today, Today.AddDays(2));

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(a => a.Date == Today);
        result.Should().Contain(a => a.Date == Today.AddDays(1));
    }

    [Fact]
    public async Task GetSchedule_ReturnsOnlySpecificClinic()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic1 = TestDataBuilder.CreateClinic(id: 1, name: "Clinic 1");
        var clinic2 = TestDataBuilder.CreateClinic(id: 2, name: "Clinic 2");
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);

        var assignment1 = TestDataBuilder.CreateAssignment(id: 1, clinicId: 1, nurseId: 1, date: Today);
        var assignment2 = TestDataBuilder.CreateAssignment(id: 2, clinicId: 2, nurseId: 1, date: Today);

        context.Clinics.AddRange(clinic1, clinic2);
        context.Nurses.Add(nurse);
        context.ShiftAssignments.AddRange(assignment1, assignment2);
        await context.SaveChangesAsync();

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            Mock.Of<IValidationService>(),
            Mock.Of<IOvertimeService>());

        // Act
        var result = await service.GetScheduleAsync(1, Today, Today);

        // Assert
        result.Should().HaveCount(1);
        result[0].ClinicId.Should().Be(1);
    }

    [Fact]
    public async Task ClearAndRebuild_DeletesExistingAssignments()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var existingAssignment = TestDataBuilder.CreateAssignment(id: 1, clinicId: 1, nurseId: 1, date: Today);

        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        context.ShiftAssignments.Add(existingAssignment);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert
        var remaining = context.ShiftAssignments.Where(a => a.Id == 1).ToList();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAndRebuild_NoConfig_ReturnsEmptyResult()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        // No shift configurations added

        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        await context.SaveChangesAsync();

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            Mock.Of<IValidationService>(),
            overtimeMock.Object);

        // Act
        var result = await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert
        result.AssignmentsAdded.Should().Be(0);
    }

    [Fact]
    public async Task ClearAndRebuild_AssignsRequiredNurses()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1, canHandleResponsibility: true);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, primaryClinicId: 1);
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 2);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = nurse1, IsAvailable = true, Score = 10 },
                new() { Nurse = nurse2, IsAvailable = true, Score = 5 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        var result = await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert
        var morningAssignments = context.ShiftAssignments.Where(a => a.ShiftType == ShiftType.Morning).ToList();
        morningAssignments.Should().HaveCount(2);
    }

    [Fact]
    public async Task ClearAndRebuild_HeadNurseAutoAssign_WorkingDay()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var headNurse = TestDataBuilder.CreateNurse(id: 1, firstName: "Head", primaryClinicId: 1, canHandleResponsibility: true);
        var clinic = TestDataBuilder.CreateClinic(id: 1, headNurseId: 1, autoAssignHeadNurse: true);
        clinic.HeadNurse = headNurse;
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1);

        // Use a Monday (working day)
        var monday = TestDataBuilder.GetNextWeekday(DayOfWeek.Monday);

        context.Clinics.Add(clinic);
        context.Nurses.Add(headNurse);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            Mock.Of<IValidationService>(),
            overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, monday, monday);

        // Assert
        var morningAssignment = context.ShiftAssignments.FirstOrDefault(a => a.ShiftType == ShiftType.Morning);
        morningAssignment.Should().NotBeNull();
        morningAssignment!.NurseId.Should().Be(1);
        morningAssignment.IsResponsibleNurse.Should().BeTrue();
    }

    [Fact]
    public async Task ClearAndRebuild_HeadNurseNotAssigned_Weekend()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var headNurse = TestDataBuilder.CreateNurse(id: 1, firstName: "Head", primaryClinicId: 1, canHandleResponsibility: true);
        var otherNurse = TestDataBuilder.CreateNurse(id: 2, firstName: "Other", primaryClinicId: 1, canHandleResponsibility: true);
        var clinic = TestDataBuilder.CreateClinic(id: 1, headNurseId: 1, autoAssignHeadNurse: true);
        clinic.HeadNurse = headNurse;
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1);

        // Use a Saturday (weekend)
        var saturday = TestDataBuilder.GetNextWeekday(DayOfWeek.Saturday);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(headNurse, otherNurse);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, saturday, ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = otherNurse, IsAvailable = true, Score = 10 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, saturday, saturday);

        // Assert
        var morningAssignment = context.ShiftAssignments.FirstOrDefault(a => a.ShiftType == ShiftType.Morning);
        morningAssignment.Should().NotBeNull();
        morningAssignment!.NurseId.Should().Be(2); // Other nurse, not head nurse
    }

    [Fact]
    public async Task ClearAndRebuild_HeadNurseOnLeave_NotAssigned()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var headNurse = TestDataBuilder.CreateNurse(id: 1, firstName: "Head", primaryClinicId: 1, canHandleResponsibility: true);
        var otherNurse = TestDataBuilder.CreateNurse(id: 2, firstName: "Other", primaryClinicId: 1, canHandleResponsibility: true);
        var clinic = TestDataBuilder.CreateClinic(id: 1, headNurseId: 1, autoAssignHeadNurse: true);
        clinic.HeadNurse = headNurse;
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1);

        var monday = TestDataBuilder.GetNextWeekday(DayOfWeek.Monday);

        // Head nurse on leave
        var leave = TestDataBuilder.CreateLeave(nurseId: 1, startDate: monday, endDate: monday.AddDays(5));

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(headNurse, otherNurse);
        context.ShiftConfigurations.Add(config);
        context.NurseLeaves.Add(leave);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, monday, ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = otherNurse, IsAvailable = true, Score = 10 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, monday, monday);

        // Assert
        var morningAssignment = context.ShiftAssignments.FirstOrDefault(a => a.ShiftType == ShiftType.Morning);
        morningAssignment.Should().NotBeNull();
        morningAssignment!.NurseId.Should().Be(2); // Other nurse assigned since head is on leave
    }

    [Fact]
    public async Task ClearAndRebuild_ResponsibleNurseAssigned()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var responsibleNurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1, canHandleResponsibility: true);
        var regularNurse = TestDataBuilder.CreateNurse(id: 2, primaryClinicId: 1, canHandleResponsibility: false);
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 2, requiresResponsibleNurse: true);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(responsibleNurse, regularNurse);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = responsibleNurse, IsAvailable = true, Score = 10 },
                new() { Nurse = regularNurse, IsAvailable = true, Score = 5 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert
        var morningAssignments = context.ShiftAssignments.Where(a => a.ShiftType == ShiftType.Morning).ToList();
        morningAssignments.Should().Contain(a => a.IsResponsibleNurse);
    }

    [Fact]
    public async Task AssignNurseToShift_Valid_CreatesAssignment()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);

        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        await context.SaveChangesAsync();

        var validationMock = new Mock<IValidationService>();
        validationMock.Setup(v => v.ValidateShiftAssignmentAsync(1, Today, ShiftType.Morning, 1))
            .ReturnsAsync(new ValidationResult { IsValid = true });

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            validationMock.Object,
            Mock.Of<IOvertimeService>());

        // Act
        var result = await service.AssignNurseToShiftAsync(1, Today, ShiftType.Morning, 1, true);

        // Assert
        result.Should().NotBeNull();
        result!.NurseId.Should().Be(1);
        result.ClinicId.Should().Be(1);
        result.IsResponsibleNurse.Should().BeTrue();
        result.Status.Should().Be(AssignmentStatus.Scheduled);
    }

    [Fact]
    public async Task AssignNurseToShift_Invalid_ReturnsNull()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);

        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        await context.SaveChangesAsync();

        var validationMock = new Mock<IValidationService>();
        validationMock.Setup(v => v.ValidateShiftAssignmentAsync(1, Today, ShiftType.Morning, 1))
            .ReturnsAsync(new ValidationResult { IsValid = false, Errors = new List<string> { "Nurse on leave" } });

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            validationMock.Object,
            Mock.Of<IOvertimeService>());

        // Act
        var result = await service.AssignNurseToShiftAsync(1, Today, ShiftType.Morning, 1);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AssignNurseToShift_BorrowedNurse_SetsIsBorrowed()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic1 = TestDataBuilder.CreateClinic(id: 1, name: "Clinic 1");
        var clinic2 = TestDataBuilder.CreateClinic(id: 2, name: "Clinic 2");
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 2); // Primary is clinic 2

        context.Clinics.AddRange(clinic1, clinic2);
        context.Nurses.Add(nurse);
        await context.SaveChangesAsync();

        var validationMock = new Mock<IValidationService>();
        validationMock.Setup(v => v.ValidateShiftAssignmentAsync(1, Today, ShiftType.Morning, 1))
            .ReturnsAsync(new ValidationResult { IsValid = true });

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            validationMock.Object,
            Mock.Of<IOvertimeService>());

        // Act - Assign to clinic 1 (not primary)
        var result = await service.AssignNurseToShiftAsync(1, Today, ShiftType.Morning, 1);

        // Assert
        result.Should().NotBeNull();
        result!.IsBorrowed.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAssignment_Exists_ReturnsTrue()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1);
        var assignment = TestDataBuilder.CreateAssignment(id: 1, nurseId: 1, clinicId: 1, date: Today);

        context.Clinics.Add(clinic);
        context.Nurses.Add(nurse);
        context.ShiftAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            Mock.Of<IValidationService>(),
            Mock.Of<IOvertimeService>());

        // Act
        var result = await service.RemoveAssignmentAsync(1);

        // Assert
        result.Should().BeTrue();
        context.ShiftAssignments.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveAssignment_NotExists_ReturnsFalse()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            Mock.Of<IValidationService>(),
            Mock.Of<IOvertimeService>());

        // Act
        var result = await service.RemoveAssignmentAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ClearAndRebuild_EmptyClinic_ReturnsEmptyResult()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        // No clinic exists

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            Mock.Of<IValidationService>(),
            Mock.Of<IOvertimeService>());

        // Act
        var result = await service.ClearAndRebuildAsync(999, Today, Today);

        // Assert
        result.AssignmentsAdded.Should().Be(0);
    }

    [Fact]
    public async Task ClearAndRebuild_MultipleShiftTypes_GeneratesAll()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1, canHandleResponsibility: true);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, primaryClinicId: 1, canHandleResponsibility: true);
        var nurse3 = TestDataBuilder.CreateNurse(id: 3, primaryClinicId: 1, canHandleResponsibility: true);

        // Config for all shift types
        var morningConfig = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1);
        var afternoonConfig = TestDataBuilder.CreateConfig(id: 2, clinicId: 1, shiftType: ShiftType.Afternoon, requiredNurses: 1);
        var nightConfig = TestDataBuilder.CreateConfig(id: 3, clinicId: 1, shiftType: ShiftType.Night, requiredNurses: 1);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2, nurse3);
        context.ShiftConfigurations.AddRange(morningConfig, afternoonConfig, nightConfig);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = nurse1, IsAvailable = true, Score = 10 }
            });
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Afternoon, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = nurse2, IsAvailable = true, Score = 10 }
            });
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Night, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = nurse3, IsAvailable = true, Score = 10 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert
        var assignments = context.ShiftAssignments.ToList();
        assignments.Should().HaveCount(3);
        assignments.Should().Contain(a => a.ShiftType == ShiftType.Morning);
        assignments.Should().Contain(a => a.ShiftType == ShiftType.Afternoon);
        assignments.Should().Contain(a => a.ShiftType == ShiftType.Night);
    }

    [Fact]
    public async Task ClearAndRebuild_BorrowsNurses_WhenNeeded()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic1 = TestDataBuilder.CreateClinic(id: 1, name: "Clinic 1");
        var clinic2 = TestDataBuilder.CreateClinic(id: 2, name: "Clinic 2");
        var borrowedNurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 2, canHandleResponsibility: true);
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1);

        context.Clinics.AddRange(clinic1, clinic2);
        context.Nurses.Add(borrowedNurse);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = borrowedNurse, IsAvailable = true, IsBorrowed = true, Score = 10 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert
        var assignment = context.ShiftAssignments.FirstOrDefault();
        assignment.Should().NotBeNull();
        assignment!.IsBorrowed.Should().BeTrue();
    }

    [Fact]
    public async Task ClearAndRebuild_PreservesManualAssignments()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, firstName: "Manual", primaryClinicId: 1, canHandleResponsibility: true);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, firstName: "Auto", primaryClinicId: 1, canHandleResponsibility: true);

        // Create a manual assignment
        var manualAssignment = TestDataBuilder.CreateAssignment(id: 1, clinicId: 1, nurseId: 1, date: Today, shiftType: ShiftType.Morning);
        manualAssignment.IsManualAssignment = true;

        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 2);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        context.ShiftAssignments.Add(manualAssignment);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = nurse2, IsAvailable = true, Score = 10 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert - Manual assignment should still exist
        var allAssignments = context.ShiftAssignments.ToList();
        allAssignments.Should().Contain(a => a.IsManualAssignment && a.NurseId == 1);
    }

    [Fact]
    public async Task ClearAndRebuild_DeletesOnlyAutoAssignments()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, firstName: "Manual", primaryClinicId: 1);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, firstName: "Auto", primaryClinicId: 1);

        // Create one manual and one auto assignment
        var manualAssignment = TestDataBuilder.CreateAssignment(id: 1, clinicId: 1, nurseId: 1, date: Today, shiftType: ShiftType.Morning);
        manualAssignment.IsManualAssignment = true;

        var autoAssignment = TestDataBuilder.CreateAssignment(id: 2, clinicId: 1, nurseId: 2, date: Today, shiftType: ShiftType.Afternoon);
        autoAssignment.IsManualAssignment = false;

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        context.ShiftAssignments.AddRange(manualAssignment, autoAssignment);
        await context.SaveChangesAsync();

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context,
            Mock.Of<INurseAvailabilityService>(),
            Mock.Of<IValidationService>(),
            overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert - Manual should remain, auto removed then possibly re-added
        var remaining = context.ShiftAssignments.Where(a => a.IsManualAssignment).ToList();
        remaining.Should().HaveCount(1);
        remaining[0].NurseId.Should().Be(1);
    }

    [Fact]
    public async Task ClearAndRebuild_CountsManualAssignmentsTowardRequired()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, firstName: "Manual", primaryClinicId: 1, canHandleResponsibility: true);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, firstName: "Available", primaryClinicId: 1, canHandleResponsibility: true);

        // Create manual assignment that covers the required nurse count
        var manualAssignment = TestDataBuilder.CreateAssignment(id: 1, clinicId: 1, nurseId: 1, date: Today, shiftType: ShiftType.Morning);
        manualAssignment.IsManualAssignment = true;
        manualAssignment.IsResponsibleNurse = true;

        // Only 1 nurse required - manual assignment should cover it
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1, requiresResponsibleNurse: true);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        context.ShiftAssignments.Add(manualAssignment);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = nurse2, IsAvailable = true, Score = 10 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        var result = await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert - No new assignments should be created since manual covers the requirement
        var allAssignments = context.ShiftAssignments.Where(a => a.ShiftType == ShiftType.Morning).ToList();
        allAssignments.Should().HaveCount(1); // Only the manual one
    }

    [Fact]
    public async Task ClearAndRebuild_FillsRemainingAfterManualAssignments()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, firstName: "Manual", primaryClinicId: 1, canHandleResponsibility: true);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, firstName: "Available", primaryClinicId: 1, canHandleResponsibility: true);

        // Create manual assignment
        var manualAssignment = TestDataBuilder.CreateAssignment(id: 1, clinicId: 1, nurseId: 1, date: Today, shiftType: ShiftType.Morning);
        manualAssignment.IsManualAssignment = true;

        // 2 nurses required - 1 manual, need 1 more
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 2);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        context.ShiftAssignments.Add(manualAssignment);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityMock = new Mock<INurseAvailabilityService>();
        availabilityMock.Setup(a => a.GetAvailableNursesAsync(1, It.IsAny<DateOnly>(), ShiftType.Morning, true))
            .ReturnsAsync(new List<NurseAvailability>
            {
                new() { Nurse = nurse2, IsAvailable = true, Score = 10 }
            });

        var overtimeMock = new Mock<IOvertimeService>();
        overtimeMock.Setup(o => o.GetTotalOvertimeBalanceAsync(It.IsAny<int>())).ReturnsAsync(0);

        var service = new ScheduleService(context, availabilityMock.Object,
            Mock.Of<IValidationService>(), overtimeMock.Object);

        // Act
        await service.ClearAndRebuildAsync(1, Today, Today);

        // Assert - Total should be 2 (1 manual + 1 auto)
        var allMorningAssignments = context.ShiftAssignments.Where(a => a.ShiftType == ShiftType.Morning).ToList();
        allMorningAssignments.Should().HaveCount(2);
    }
}

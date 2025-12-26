using FluentAssertions;
using Moq;
using NurseShifts.Data;
using NurseShifts.Models;
using NurseShifts.Services;
using NurseShifts.Tests.Helpers;
using Xunit;

namespace NurseShifts.Tests.Unit.Services;

public class ValidationServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<INurseAvailabilityService> _availabilityServiceMock;
    private readonly ValidationService _sut;

    public ValidationServiceTests()
    {
        _context = TestDbContextFactory.CreateWithSeedData();
        _availabilityServiceMock = new Mock<INurseAvailabilityService>();
        _sut = new ValidationService(_context, _availabilityServiceMock.Object);

        // Default mock setup - nurse is available
        _availabilityServiceMock.Setup(x => x.IsNurseOnLeaveAsync(It.IsAny<int>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(false);
        _availabilityServiceMock.Setup(x => x.IsNurseOnCompTimeAsync(It.IsAny<int>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(false);
        _availabilityServiceMock.Setup(x => x.GetConsecutiveWorkDaysAsync(It.IsAny<int>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(0);
        _availabilityServiceMock.Setup(x => x.GetWeeklyHoursAsync(It.IsAny<int>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(0);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region ValidateShiftAssignment - Nurse Not Found

    [Fact]
    public async Task ValidateShiftAssignment_NurseNotFound_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        _context.Clinics.Add(clinic);
        await _context.SaveChangesAsync();
        var date = DateOnly.FromDateTime(DateTime.Today);

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, 999);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Nurse not found.");
    }

    #endregion

    #region ValidateShiftAssignment - Nurse Inactive

    [Fact]
    public async Task ValidateShiftAssignment_NurseInactive_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(isActive: false);
        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();
        var date = DateOnly.FromDateTime(DateTime.Today);

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Nurse is not active.");
    }

    #endregion

    #region ValidateShiftAssignment - Nurse On Leave

    [Fact]
    public async Task ValidateShiftAssignment_NurseOnLeave_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();
        var date = DateOnly.FromDateTime(DateTime.Today);

        _availabilityServiceMock.Setup(x => x.IsNurseOnLeaveAsync(nurse.Id, date))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Nurse is on approved leave.");
    }

    #endregion

    #region ValidateShiftAssignment - Nurse On Comp Time

    [Fact]
    public async Task ValidateShiftAssignment_NurseOnCompTime_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();
        var date = DateOnly.FromDateTime(DateTime.Today);

        _availabilityServiceMock.Setup(x => x.IsNurseOnCompTimeAsync(nurse.Id, date))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Nurse is on compensatory time off.");
    }

    #endregion

    #region ValidateShiftAssignment - Nurse Unavailable

    [Fact]
    public async Task ValidateShiftAssignment_NurseUnavailable_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var wish = TestDataBuilder.CreateWish(nurseId: nurse.Id, date: date, wishType: WishType.Unavailable);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.NurseShiftWishes.Add(wish);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Nurse has marked this date/shift as unavailable.");
    }

    [Fact]
    public async Task ValidateShiftAssignment_NurseUnavailableForSpecificShift_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var wish = TestDataBuilder.CreateWish(nurseId: nurse.Id, date: date, shiftType: ShiftType.Morning, wishType: WishType.Unavailable);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.NurseShiftWishes.Add(wish);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Nurse has marked this date/shift as unavailable.");
    }

    [Fact]
    public async Task ValidateShiftAssignment_NurseUnavailableForDifferentShift_ReturnsValid()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var wish = TestDataBuilder.CreateWish(nurseId: nurse.Id, date: date, shiftType: ShiftType.Night, wishType: WishType.Unavailable);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.NurseShiftWishes.Add(wish);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateShiftAssignment - Avoided Shift

    [Fact]
    public async Task ValidateShiftAssignment_AvoidedShift_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(avoidedShifts: ShiftTypeFlags.Night);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Night, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("This shift type is in the nurse's avoided shifts.");
    }

    [Fact]
    public async Task ValidateShiftAssignment_NotAvoidedShift_ReturnsValid()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(avoidedShifts: ShiftTypeFlags.Night);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateShiftAssignment - Already Assigned

    [Fact]
    public async Task ValidateShiftAssignment_AlreadyAssigned_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var existingAssignment = TestDataBuilder.CreateAssignment(nurseId: nurse.Id, clinicId: clinic.Id, date: date, shiftType: ShiftType.Afternoon);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.ShiftAssignments.Add(existingAssignment);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Nurse is already assigned to a shift on this date.");
    }

    [Fact]
    public async Task ValidateShiftAssignment_CancelledAssignment_ReturnsValid()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var cancelledAssignment = TestDataBuilder.CreateAssignment(
            nurseId: nurse.Id, clinicId: clinic.Id, date: date, status: AssignmentStatus.Cancelled);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.ShiftAssignments.Add(cancelledAssignment);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateShiftAssignment - Clinic Assignment

    [Fact]
    public async Task ValidateShiftAssignment_NotAssignedToClinic_ReturnsError()
    {
        // Arrange
        var clinic1 = TestDataBuilder.CreateClinic(id: 1, name: "Clinic 1");
        var clinic2 = TestDataBuilder.CreateClinic(id: 2, name: "Clinic 2");
        var nurse = TestDataBuilder.CreateNurse(primaryClinicId: clinic1.Id, isPoolNurse: false);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.AddRange(clinic1, clinic2);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic2.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Nurse cannot be assigned to this clinic.");
    }

    [Fact]
    public async Task ValidateShiftAssignment_PoolNurse_CanWorkAnyClinic()
    {
        // Arrange
        var clinic1 = TestDataBuilder.CreateClinic(id: 1, name: "Clinic 1");
        var clinic2 = TestDataBuilder.CreateClinic(id: 2, name: "Clinic 2");
        var nurse = TestDataBuilder.CreateNurse(primaryClinicId: clinic1.Id, isPoolNurse: true);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.AddRange(clinic1, clinic2);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic2.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateShiftAssignment_BorrowableNurse_CanWorkBorrowedClinic()
    {
        // Arrange
        var clinic1 = TestDataBuilder.CreateClinic(id: 1, name: "Clinic 1");
        var clinic2 = TestDataBuilder.CreateClinic(id: 2, name: "Clinic 2");
        var nurse = TestDataBuilder.CreateNurse(primaryClinicId: clinic1.Id, isPoolNurse: false);
        var borrowAssignment = new NurseClinicAssignment
        {
            NurseId = nurse.Id,
            ClinicId = clinic2.Id,
            CanBeBorrowed = true
        };
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.AddRange(clinic1, clinic2);
        _context.Nurses.Add(nurse);
        _context.NurseClinicAssignments.Add(borrowAssignment);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic2.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateShiftAssignment - Rest Period

    [Fact]
    public async Task ValidateShiftAssignment_RestPeriodViolation_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(minRestHours: 11);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);

        // Nurse worked night shift yesterday
        var nightShift = TestDataBuilder.CreateAssignment(
            nurseId: nurse.Id, clinicId: clinic.Id, date: yesterday, shiftType: ShiftType.Night);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.ShiftAssignments.Add(nightShift);
        await _context.SaveChangesAsync();

        // Act - try to assign morning shift today (would violate 11h rest)
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, today, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Insufficient rest period"));
    }

    [Fact]
    public async Task ValidateShiftAssignment_AfternoonAfterNightShift_ReturnsValid()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(minRestHours: 11);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);

        // Nurse worked night shift yesterday
        var nightShift = TestDataBuilder.CreateAssignment(
            nurseId: nurse.Id, clinicId: clinic.Id, date: yesterday, shiftType: ShiftType.Night);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.ShiftAssignments.Add(nightShift);
        await _context.SaveChangesAsync();

        // Act - afternoon shift today should be OK (enough rest)
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, today, ShiftType.Afternoon, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateShiftAssignment - Consecutive Days

    [Fact]
    public async Task ValidateShiftAssignment_ConsecutiveDaysExceeded_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(maxConsecutiveDays: 5);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        _availabilityServiceMock.Setup(x => x.GetConsecutiveWorkDaysAsync(nurse.Id, date))
            .ReturnsAsync(5); // Already at max

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("maximum consecutive work days"));
    }

    [Fact]
    public async Task ValidateShiftAssignment_BelowConsecutiveDaysLimit_ReturnsValid()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(maxConsecutiveDays: 5);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        _availabilityServiceMock.Setup(x => x.GetConsecutiveWorkDaysAsync(nurse.Id, date))
            .ReturnsAsync(4); // Below max

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateShiftAssignment - Weekly Hours

    [Fact]
    public async Task ValidateShiftAssignment_WeeklyHoursExceeded_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(contractedHours: 40, maxOvertimeHours: 10); // Max 50h
        var config = TestDataBuilder.CreateConfig(clinicId: clinic.Id, shiftDurationHours: 8);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.ShiftConfigurations.Add(config);
        await _context.SaveChangesAsync();

        _availabilityServiceMock.Setup(x => x.GetWeeklyHoursAsync(nurse.Id, It.IsAny<DateOnly>()))
            .ReturnsAsync(48); // 48 + 8 = 56 > 50

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("maximum weekly hours"));
    }

    #endregion

    #region ValidateShiftAssignment - Warnings

    [Fact]
    public async Task ValidateShiftAssignment_NursePreferOff_ReturnsWarning()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var wish = TestDataBuilder.CreateWish(nurseId: nurse.Id, date: date, wishType: WishType.PreferOff);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.NurseShiftWishes.Add(wish);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain("Nurse has expressed a preference to be off on this date.");
    }

    [Fact]
    public async Task ValidateShiftAssignment_CausesOvertime_ReturnsWarning()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(contractedHours: 40, maxOvertimeHours: 10);
        var config = TestDataBuilder.CreateConfig(clinicId: clinic.Id, shiftDurationHours: 8);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        _context.ShiftConfigurations.Add(config);
        await _context.SaveChangesAsync();

        _availabilityServiceMock.Setup(x => x.GetWeeklyHoursAsync(nurse.Id, It.IsAny<DateOnly>()))
            .ReturnsAsync(36); // 36 + 8 = 44 > 40 contracted

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain("This assignment will cause overtime.");
    }

    #endregion

    #region ValidateShiftAssignment - Valid Case

    [Fact]
    public async Task ValidateShiftAssignment_Valid_ReturnsSuccess()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(preferredShifts: ShiftTypeFlags.Morning);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftAssignmentAsync(clinic.Id, date, ShiftType.Morning, nurse.Id);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region ValidateShiftCoverage

    [Fact]
    public async Task ValidateShiftCoverage_Understaffed_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var config = TestDataBuilder.CreateConfig(clinicId: clinic.Id, requiredNurses: 3);
        var nurse = TestDataBuilder.CreateNurse();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var assignment = TestDataBuilder.CreateAssignment(nurseId: nurse.Id, clinicId: clinic.Id, date: date);

        _context.Clinics.Add(clinic);
        _context.ShiftConfigurations.Add(config);
        _context.Nurses.Add(nurse);
        _context.ShiftAssignments.Add(assignment);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftCoverageAsync(clinic.Id, date, ShiftType.Morning);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("understaffed"));
    }

    [Fact]
    public async Task ValidateShiftCoverage_NoResponsibleNurse_ReturnsError()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var config = TestDataBuilder.CreateConfig(clinicId: clinic.Id, requiredNurses: 1, requiresResponsibleNurse: true);
        var nurse = TestDataBuilder.CreateNurse(canHandleResponsibility: false);
        var date = DateOnly.FromDateTime(DateTime.Today);
        var assignment = TestDataBuilder.CreateAssignment(
            nurseId: nurse.Id, clinicId: clinic.Id, date: date, isResponsible: false);

        _context.Clinics.Add(clinic);
        _context.ShiftConfigurations.Add(config);
        _context.Nurses.Add(nurse);
        _context.ShiftAssignments.Add(assignment);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftCoverageAsync(clinic.Id, date, ShiftType.Morning);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Shift requires a responsible nurse but none is assigned.");
    }

    [Fact]
    public async Task ValidateShiftCoverage_FullyStaffed_ReturnsValid()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var config = TestDataBuilder.CreateConfig(clinicId: clinic.Id, requiredNurses: 2, requiresResponsibleNurse: true);
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, firstName: "Nurse1", canHandleResponsibility: true);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, firstName: "Nurse2", canHandleResponsibility: false);
        var date = DateOnly.FromDateTime(DateTime.Today);
        var assignment1 = TestDataBuilder.CreateAssignment(id: 1, nurseId: nurse1.Id, clinicId: clinic.Id, date: date, isResponsible: true);
        var assignment2 = TestDataBuilder.CreateAssignment(id: 2, nurseId: nurse2.Id, clinicId: clinic.Id, date: date);

        _context.Clinics.Add(clinic);
        _context.ShiftConfigurations.Add(config);
        _context.Nurses.AddRange(nurse1, nurse2);
        _context.ShiftAssignments.AddRange(assignment1, assignment2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftCoverageAsync(clinic.Id, date, ShiftType.Morning);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateShiftCoverage_NoConfig_ReturnsWarning()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateShiftCoverageAsync(clinic.Id, date, ShiftType.Morning);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain("No shift configuration found for this clinic/shift.");
    }

    #endregion

    #region CanNurseWorkShift

    [Fact]
    public async Task CanNurseWorkShift_ValidAssignment_ReturnsTrue()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse();
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.CanNurseWorkShiftAsync(nurse.Id, date, ShiftType.Morning, clinic.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanNurseWorkShift_InvalidAssignment_ReturnsFalse()
    {
        // Arrange
        var clinic = TestDataBuilder.CreateClinic();
        var nurse = TestDataBuilder.CreateNurse(isActive: false);
        var date = DateOnly.FromDateTime(DateTime.Today);

        _context.Clinics.Add(clinic);
        _context.Nurses.Add(nurse);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.CanNurseWorkShiftAsync(nurse.Id, date, ShiftType.Morning, clinic.Id);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}

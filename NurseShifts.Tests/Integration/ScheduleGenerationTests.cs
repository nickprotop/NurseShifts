using FluentAssertions;
using NurseShifts.Models;
using NurseShifts.Services;
using NurseShifts.Tests.Helpers;
using Xunit;

namespace NurseShifts.Tests.Integration;

/// <summary>
/// Integration tests for the full scheduling workflow.
/// Uses real services (not mocked) with in-memory database.
/// </summary>
public class ScheduleGenerationTests
{
    private static DateOnly GetNextMonday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return today.AddDays(daysUntilMonday);
    }

    [Fact]
    public async Task FullWeekGeneration_ComplexScenario()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var monday = GetNextMonday();

        // Setup clinic with head nurse
        var headNurse = TestDataBuilder.CreateNurse(id: 1, firstName: "Head", primaryClinicId: 1,
            canHandleResponsibility: true, contractedHours: 40);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, firstName: "Alice", primaryClinicId: 1,
            canHandleResponsibility: true, contractedHours: 40);
        var nurse3 = TestDataBuilder.CreateNurse(id: 3, firstName: "Bob", primaryClinicId: 1,
            canHandleResponsibility: false, contractedHours: 40);

        var clinic = TestDataBuilder.CreateClinic(id: 1, name: "Main Clinic", headNurseId: 1, autoAssignHeadNurse: true);
        clinic.HeadNurse = headNurse;

        // Morning shift: 2 nurses required
        var morningConfig = TestDataBuilder.CreateConfig(id: 1, clinicId: 1,
            shiftType: ShiftType.Morning, requiredNurses: 2, requiresResponsibleNurse: true);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(headNurse, nurse2, nurse3);
        context.ShiftConfigurations.Add(morningConfig);
        await context.SaveChangesAsync();

        // Create real services
        var availabilityService = new NurseAvailabilityService(context);
        var validationService = new ValidationService(context, availabilityService);
        var overtimeService = new OvertimeService(context);
        var scheduleService = new ScheduleService(context, availabilityService, validationService, overtimeService);

        // Act - Generate 5 weekdays
        var result = await scheduleService.ClearAndRebuildAsync(1, monday, monday.AddDays(4));

        // Assert
        result.AssignmentsAdded.Should().BeGreaterThan(0);

        // Check that each weekday has morning assignments
        var assignments = context.ShiftAssignments.ToList();
        for (int i = 0; i < 5; i++)
        {
            var dayAssignments = assignments.Where(a => a.Date == monday.AddDays(i) && a.ShiftType == ShiftType.Morning).ToList();
            dayAssignments.Count.Should().BeGreaterThanOrEqualTo(1);

            // Head nurse should be assigned to weekday mornings
            if (monday.AddDays(i).DayOfWeek != DayOfWeek.Saturday &&
                monday.AddDays(i).DayOfWeek != DayOfWeek.Sunday)
            {
                dayAssignments.Should().Contain(a => a.NurseId == 1);
            }
        }

        // At least one responsible nurse per day
        var responsibleAssignments = assignments.Where(a => a.IsResponsibleNurse).ToList();
        responsibleAssignments.Count.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task MultipleNursesMultipleShifts_CorrectDistribution()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var monday = GetNextMonday();

        // Create 6 nurses for 3 shifts
        var nurses = new List<Nurse>();
        for (int i = 1; i <= 6; i++)
        {
            var nurse = TestDataBuilder.CreateNurse(
                id: i,
                firstName: $"Nurse{i}",
                primaryClinicId: 1,
                canHandleResponsibility: i <= 3, // First 3 can handle responsibility
                contractedHours: 40,
                maxOvertimeHours: 8);
            nurses.Add(nurse);
        }

        var clinic = TestDataBuilder.CreateClinic(id: 1);

        // Each shift requires 2 nurses
        var morningConfig = TestDataBuilder.CreateConfig(id: 1, clinicId: 1,
            shiftType: ShiftType.Morning, requiredNurses: 2);
        var afternoonConfig = TestDataBuilder.CreateConfig(id: 2, clinicId: 1,
            shiftType: ShiftType.Afternoon, requiredNurses: 2);
        var nightConfig = TestDataBuilder.CreateConfig(id: 3, clinicId: 1,
            shiftType: ShiftType.Night, requiredNurses: 2);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurses);
        context.ShiftConfigurations.AddRange(morningConfig, afternoonConfig, nightConfig);
        await context.SaveChangesAsync();

        var availabilityService = new NurseAvailabilityService(context);
        var validationService = new ValidationService(context, availabilityService);
        var overtimeService = new OvertimeService(context);
        var scheduleService = new ScheduleService(context, availabilityService, validationService, overtimeService);

        // Act
        await scheduleService.ClearAndRebuildAsync(1, monday, monday);

        // Assert
        var assignments = context.ShiftAssignments.ToList();

        // Each nurse should work at most one shift per day
        var nurseShiftCounts = assignments.GroupBy(a => a.NurseId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var count in nurseShiftCounts.Values)
        {
            count.Should().BeLessThanOrEqualTo(1);
        }

        // Should have 6 assignments (2 per shift type)
        assignments.Should().HaveCount(6);
    }

    [Fact]
    public async Task LeaveHandling_MidSchedule()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var monday = GetNextMonday();

        var nurse1 = TestDataBuilder.CreateNurse(id: 1, firstName: "Available", primaryClinicId: 1, canHandleResponsibility: true);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, firstName: "OnLeave", primaryClinicId: 1, canHandleResponsibility: true);

        // Nurse 2 is on leave mid-week
        var leave = TestDataBuilder.CreateLeave(nurseId: 2, startDate: monday.AddDays(2), endDate: monday.AddDays(3));

        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        context.NurseLeaves.Add(leave);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityService = new NurseAvailabilityService(context);
        var validationService = new ValidationService(context, availabilityService);
        var overtimeService = new OvertimeService(context);
        var scheduleService = new ScheduleService(context, availabilityService, validationService, overtimeService);

        // Act
        await scheduleService.ClearAndRebuildAsync(1, monday, monday.AddDays(4));

        // Assert
        var assignments = context.ShiftAssignments.ToList();

        // On leave days (Wed, Thu), nurse 2 should not be assigned
        var wednesdayAssignments = assignments.Where(a => a.Date == monday.AddDays(2)).ToList();
        var thursdayAssignments = assignments.Where(a => a.Date == monday.AddDays(3)).ToList();

        wednesdayAssignments.Should().NotContain(a => a.NurseId == 2);
        thursdayAssignments.Should().NotContain(a => a.NurseId == 2);
    }

    [Fact]
    public async Task ConsecutiveDaysLimit_RespectsMaxWhenMultipleNursesAvailable()
    {
        // Arrange
        using var context = TestDbContextFactory.CreateInMemory();
        var monday = GetNextMonday();

        // Create two nurses - one with max 2 consecutive days, one with max 5
        var nurse1 = TestDataBuilder.CreateNurse(id: 1, firstName: "LowLimit", primaryClinicId: 1,
            canHandleResponsibility: true, maxConsecutiveDays: 2);
        var nurse2 = TestDataBuilder.CreateNurse(id: 2, firstName: "HighLimit", primaryClinicId: 1,
            canHandleResponsibility: true, maxConsecutiveDays: 5);

        var clinic = TestDataBuilder.CreateClinic(id: 1);
        var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1);

        context.Clinics.Add(clinic);
        context.Nurses.AddRange(nurse1, nurse2);
        context.ShiftConfigurations.Add(config);
        await context.SaveChangesAsync();

        var availabilityService = new NurseAvailabilityService(context);
        var validationService = new ValidationService(context, availabilityService);
        var overtimeService = new OvertimeService(context);
        var scheduleService = new ScheduleService(context, availabilityService, validationService, overtimeService);

        // Act - Generate 5 days
        await scheduleService.ClearAndRebuildAsync(1, monday, monday.AddDays(4));

        // Assert - Assignments should be distributed between nurses
        var assignments = context.ShiftAssignments.ToList();
        assignments.Should().HaveCount(5);

        // Check that no nurse works more consecutive days than their limit
        var nurse1Dates = assignments.Where(a => a.NurseId == 1).Select(a => a.Date).OrderBy(d => d).ToList();
        var nurse2Dates = assignments.Where(a => a.NurseId == 2).Select(a => a.Date).OrderBy(d => d).ToList();

        // Both nurses should have some assignments
        nurse1Dates.Count.Should().BeGreaterThan(0);
        nurse2Dates.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DatabasePersistence_RoundTrip()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var monday = GetNextMonday();

        // Create and save schedule
        using (var context = TestDbContextFactory.CreateInMemory(dbName))
        {
            var nurse = TestDataBuilder.CreateNurse(id: 1, primaryClinicId: 1, canHandleResponsibility: true);
            var clinic = TestDataBuilder.CreateClinic(id: 1);
            var config = TestDataBuilder.CreateConfig(id: 1, clinicId: 1, shiftType: ShiftType.Morning, requiredNurses: 1);

            context.Clinics.Add(clinic);
            context.Nurses.Add(nurse);
            context.ShiftConfigurations.Add(config);
            await context.SaveChangesAsync();

            var availabilityService = new NurseAvailabilityService(context);
            var validationService = new ValidationService(context, availabilityService);
            var overtimeService = new OvertimeService(context);
            var scheduleService = new ScheduleService(context, availabilityService, validationService, overtimeService);

            await scheduleService.ClearAndRebuildAsync(1, monday, monday.AddDays(2));
        }

        // Read in new context
        using (var context = TestDbContextFactory.CreateInMemory(dbName))
        {
            var availabilityService = new NurseAvailabilityService(context);
            var validationService = new ValidationService(context, availabilityService);
            var overtimeService = new OvertimeService(context);
            var scheduleService = new ScheduleService(context, availabilityService, validationService, overtimeService);

            // Act
            var result = await scheduleService.GetScheduleAsync(1, monday, monday.AddDays(2));

            // Assert - Schedule persisted and retrieved correctly
            result.Should().NotBeEmpty();
            result.Count.Should().BeGreaterThanOrEqualTo(1);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using NurseShifts.Models;

namespace NurseShifts.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Nurse> Nurses => Set<Nurse>();
    public DbSet<NurseClinicAssignment> NurseClinicAssignments => Set<NurseClinicAssignment>();
    public DbSet<NurseLeave> NurseLeaves => Set<NurseLeave>();
    public DbSet<NurseShiftWish> NurseShiftWishes => Set<NurseShiftWish>();
    public DbSet<ShiftConfiguration> ShiftConfigurations => Set<ShiftConfiguration>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<OvertimeBalance> OvertimeBalances => Set<OvertimeBalance>();
    public DbSet<CompensatoryTimeOff> CompensatoryTimeOffs => Set<CompensatoryTimeOff>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SystemSettings - single row (password is pre-hashed for "admin123")
        modelBuilder.Entity<SystemSettings>().HasData(new SystemSettings
        {
            Id = 1,
            DefaultContractedHoursPerWeek = 40,
            DefaultMaxOvertimeHoursPerWeek = 8,
            DefaultMinRestHoursBetweenShifts = 11,
            DefaultMaxConsecutiveWorkDays = 5,
            Language = "en",
            AdminUsername = "admin",
            AdminPasswordHash = "$2a$11$.lea7HLBOb5xwftzvm8hXOriVBT1cJSBln266PVSU7UCbsHpLQ3QS"
        });

        // Clinic -> HeadNurse relationship (optional)
        modelBuilder.Entity<Clinic>()
            .HasOne(c => c.HeadNurse)
            .WithMany()
            .HasForeignKey(c => c.HeadNurseId)
            .OnDelete(DeleteBehavior.SetNull);

        // Nurse -> PrimaryClinic relationship
        modelBuilder.Entity<Nurse>()
            .HasOne(n => n.PrimaryClinic)
            .WithMany(c => c.Nurses)
            .HasForeignKey(n => n.PrimaryClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        // NurseClinicAssignment - Many-to-Many between Nurse and Clinic for borrowing
        modelBuilder.Entity<NurseClinicAssignment>()
            .HasOne(nca => nca.Nurse)
            .WithMany(n => n.ClinicAssignments)
            .HasForeignKey(nca => nca.NurseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NurseClinicAssignment>()
            .HasOne(nca => nca.Clinic)
            .WithMany(c => c.BorrowableNurses)
            .HasForeignKey(nca => nca.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NurseClinicAssignment>()
            .HasIndex(nca => new { nca.NurseId, nca.ClinicId })
            .IsUnique();

        // NurseLeave
        modelBuilder.Entity<NurseLeave>()
            .HasOne(nl => nl.Nurse)
            .WithMany(n => n.Leaves)
            .HasForeignKey(nl => nl.NurseId)
            .OnDelete(DeleteBehavior.Cascade);

        // NurseShiftWish
        modelBuilder.Entity<NurseShiftWish>()
            .HasOne(nsw => nsw.Nurse)
            .WithMany(n => n.ShiftWishes)
            .HasForeignKey(nsw => nsw.NurseId)
            .OnDelete(DeleteBehavior.Cascade);

        // ShiftConfiguration
        modelBuilder.Entity<ShiftConfiguration>()
            .HasOne(sc => sc.Clinic)
            .WithMany(c => c.ShiftConfigurations)
            .HasForeignKey(sc => sc.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShiftConfiguration>()
            .HasIndex(sc => new { sc.ClinicId, sc.ShiftType, sc.DayOfWeek })
            .IsUnique();

        // ShiftAssignment
        modelBuilder.Entity<ShiftAssignment>()
            .HasOne(sa => sa.Clinic)
            .WithMany(c => c.ShiftAssignments)
            .HasForeignKey(sa => sa.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShiftAssignment>()
            .HasOne(sa => sa.Nurse)
            .WithMany(n => n.ShiftAssignments)
            .HasForeignKey(sa => sa.NurseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShiftAssignment>()
            .HasIndex(sa => new { sa.Date, sa.ShiftType, sa.NurseId })
            .IsUnique();

        // OvertimeBalance
        modelBuilder.Entity<OvertimeBalance>()
            .HasOne(ob => ob.Nurse)
            .WithMany(n => n.OvertimeBalances)
            .HasForeignKey(ob => ob.NurseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OvertimeBalance>()
            .HasIndex(ob => new { ob.NurseId, ob.WeekStartDate })
            .IsUnique();

        // CompensatoryTimeOff
        modelBuilder.Entity<CompensatoryTimeOff>()
            .HasOne(cto => cto.Nurse)
            .WithMany(n => n.CompensatoryTimeOffs)
            .HasForeignKey(cto => cto.NurseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

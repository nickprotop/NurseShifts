using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;

namespace NurseShifts.Tests.Helpers;

public static class TestDbContextFactory
{
    public static AppDbContext CreateInMemory(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static AppDbContext CreateWithSeedData(string? dbName = null)
    {
        // Note: EnsureCreated() already seeds SystemSettings via OnModelCreating
        return CreateInMemory(dbName);
    }
}

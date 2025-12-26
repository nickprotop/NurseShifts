using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NurseShifts.Data;
using NurseShifts.Models;
using NurseShifts.Models.ViewModels;

namespace NurseShifts.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _context;

    public HomeController(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = today.AddDays(-(int)today.DayOfWeek + 1);
        var weekEnd = weekStart.AddDays(6);

        var viewModel = new DashboardViewModel
        {
            TodayDate = today,
            TotalClinics = await _context.Clinics.CountAsync(),
            TotalNurses = await _context.Nurses.CountAsync(n => n.IsActive),
            TodayShifts = await _context.ShiftAssignments
                .Where(sa => sa.Date == today && sa.Status != AssignmentStatus.Cancelled)
                .CountAsync(),
            PendingLeaveRequests = await _context.NurseLeaves
                .Where(l => l.Status == LeaveStatus.Pending)
                .CountAsync(),
            TodayAssignments = await _context.ShiftAssignments
                .Include(sa => sa.Nurse)
                .Include(sa => sa.Clinic)
                .Where(sa => sa.Date == today && sa.Status != AssignmentStatus.Cancelled)
                .OrderBy(sa => sa.ShiftType)
                .ThenBy(sa => sa.ClinicId)
                .Take(20)
                .ToListAsync(),
            UnderstaffedShifts = await GetUnderstaffedShiftCountAsync(today, today.AddDays(7)),
            UpcomingLeaves = await _context.NurseLeaves
                .Include(l => l.Nurse)
                .Where(l => l.Status == LeaveStatus.Approved && l.EndDate >= today)
                .OrderBy(l => l.StartDate)
                .Take(5)
                .ToListAsync(),
            HighOvertimeNurses = await GetHighOvertimeNursesAsync()
        };

        return View(viewModel);
    }

    private async Task<int> GetUnderstaffedShiftCountAsync(DateOnly start, DateOnly end)
    {
        var count = 0;
        var configs = await _context.ShiftConfigurations.ToListAsync();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            foreach (var config in configs)
            {
                var assigned = await _context.ShiftAssignments
                    .CountAsync(sa => sa.ClinicId == config.ClinicId &&
                                     sa.Date == date &&
                                     sa.ShiftType == config.ShiftType &&
                                     sa.Status != AssignmentStatus.Cancelled);

                if (assigned < config.RequiredNurses)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private async Task<List<NurseOvertimeInfo>> GetHighOvertimeNursesAsync()
    {
        var nurses = await _context.Nurses
            .Where(n => n.IsActive)
            .Include(n => n.OvertimeBalances)
            .ToListAsync();

        return nurses
            .Select(n => new NurseOvertimeInfo
            {
                NurseId = n.Id,
                FullName = n.FullName,
                OvertimeBalance = n.OvertimeBalances.Sum(b => b.BalanceHours)
            })
            .Where(n => n.OvertimeBalance > 0)
            .OrderByDescending(n => n.OvertimeBalance)
            .Take(10)
            .ToList();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

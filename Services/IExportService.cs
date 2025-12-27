using NurseShifts.Models.ViewModels;

namespace NurseShifts.Services;

public interface IExportService
{
    byte[] ExportScheduleToPdf(ScheduleViewModel schedule);
    byte[] ExportScheduleToExcel(ScheduleViewModel schedule);
}

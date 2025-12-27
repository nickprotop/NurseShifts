using ClosedXML.Excel;
using NurseShifts.Models;
using NurseShifts.Models.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NurseShifts.Services;

public class ExportService : IExportService
{
    public byte[] ExportScheduleToPdf(ScheduleViewModel schedule)
    {
        // Set QuestPDF license (Community license for open source)
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Schedule - {schedule.Clinic?.Name ?? "Clinic"}")
                        .FontSize(16).Bold();
                    col.Item().Text($"{schedule.StartDate:MMM d} - {schedule.StartDate.AddDays(schedule.Days - 1):MMM d, yyyy}")
                        .FontSize(10).FontColor(Colors.Grey.Medium);
                });

                page.Content().PaddingVertical(0.5f, Unit.Centimetre).Table(table =>
                {
                    // Define columns: first for shift type, then one per day
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(60); // Shift type column
                        foreach (var _ in schedule.DaySchedules)
                        {
                            cols.RelativeColumn(); // Day columns
                        }
                    });

                    // Header row
                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten2)
                            .Padding(4).Text("Shift").Bold();

                        foreach (var day in schedule.DaySchedules)
                        {
                            var isWeekend = day.Date.DayOfWeek == DayOfWeek.Saturday ||
                                          day.Date.DayOfWeek == DayOfWeek.Sunday;
                            var bgColor = isWeekend ? Colors.Grey.Lighten3 : Colors.Grey.Lighten2;

                            header.Cell().Background(bgColor).Padding(4).AlignCenter()
                                .Column(col =>
                                {
                                    col.Item().Text(day.Date.DayOfWeek.ToString()[..3]).FontSize(8);
                                    col.Item().Text(day.Date.ToString("MMM d")).Bold();
                                });
                        }
                    });

                    // Data rows
                    foreach (var shiftType in Enum.GetValues<ShiftType>())
                    {
                        var shiftName = shiftType switch
                        {
                            ShiftType.Morning => "Morning",
                            ShiftType.Afternoon => "Afternoon",
                            ShiftType.Night => "Night",
                            _ => shiftType.ToString()
                        };

                        table.Cell().Border(0.5f).Padding(4)
                            .Text(shiftName).Bold();

                        foreach (var day in schedule.DaySchedules)
                        {
                            var shift = day.Shifts.FirstOrDefault(s => s.ShiftType == shiftType);
                            var isWeekend = day.Date.DayOfWeek == DayOfWeek.Saturday ||
                                          day.Date.DayOfWeek == DayOfWeek.Sunday;

                            var cellBgColor = Colors.White;
                            if (shift != null && !shift.IsValid)
                            {
                                cellBgColor = shift.Errors.Any() ? Colors.Red.Lighten4 : Colors.Yellow.Lighten4;
                            }
                            else if (isWeekend)
                            {
                                cellBgColor = Colors.Grey.Lighten4;
                            }

                            table.Cell().Border(0.5f).Background(cellBgColor).Padding(4)
                                .Column(col =>
                                {
                                    if (shift?.Assignments.Any() == true)
                                    {
                                        foreach (var assignment in shift.Assignments)
                                        {
                                            col.Item().Text(text =>
                                            {
                                                if (assignment.IsManualAssignment)
                                                    text.Span("[M] ").FontSize(7);
                                                if (assignment.IsResponsibleNurse)
                                                    text.Span("* ").FontColor(Colors.Orange.Medium);
                                                if (assignment.IsBorrowed)
                                                    text.Span("(B) ").FontSize(7);
                                                text.Span(assignment.Nurse?.ShortName ?? "?");
                                            });
                                        }
                                    }

                                    col.Item().Text($"{shift?.Assignments.Count ?? 0}/{shift?.RequiredNurses ?? 0}")
                                        .FontSize(7).FontColor(Colors.Grey.Medium);
                                });
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    public byte[] ExportScheduleToExcel(ScheduleViewModel schedule)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Schedule");

        // Title
        worksheet.Cell(1, 1).Value = $"Schedule - {schedule.Clinic?.Name ?? "Clinic"}";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 14;
        worksheet.Range(1, 1, 1, schedule.Days + 1).Merge();

        worksheet.Cell(2, 1).Value = $"{schedule.StartDate:MMM d} - {schedule.StartDate.AddDays(schedule.Days - 1):MMM d, yyyy}";
        worksheet.Range(2, 1, 2, schedule.Days + 1).Merge();

        // Headers
        var headerRow = 4;
        worksheet.Cell(headerRow, 1).Value = "Shift";
        worksheet.Cell(headerRow, 1).Style.Font.Bold = true;
        worksheet.Cell(headerRow, 1).Style.Fill.BackgroundColor = XLColor.LightGray;

        for (int i = 0; i < schedule.DaySchedules.Count; i++)
        {
            var day = schedule.DaySchedules[i];
            var col = i + 2;
            worksheet.Cell(headerRow, col).Value = $"{day.Date:ddd}\n{day.Date:MMM d}";
            worksheet.Cell(headerRow, col).Style.Font.Bold = true;
            worksheet.Cell(headerRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(headerRow, col).Style.Alignment.WrapText = true;

            var isWeekend = day.Date.DayOfWeek == DayOfWeek.Saturday ||
                          day.Date.DayOfWeek == DayOfWeek.Sunday;
            if (isWeekend)
            {
                worksheet.Cell(headerRow, col).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
        }

        // Data rows
        var dataRow = headerRow + 1;
        foreach (var shiftType in Enum.GetValues<ShiftType>())
        {
            var shiftName = shiftType switch
            {
                ShiftType.Morning => "Morning (07-15)",
                ShiftType.Afternoon => "Afternoon (15-23)",
                ShiftType.Night => "Night (23-07)",
                _ => shiftType.ToString()
            };

            worksheet.Cell(dataRow, 1).Value = shiftName;
            worksheet.Cell(dataRow, 1).Style.Font.Bold = true;

            for (int i = 0; i < schedule.DaySchedules.Count; i++)
            {
                var day = schedule.DaySchedules[i];
                var col = i + 2;
                var shift = day.Shifts.FirstOrDefault(s => s.ShiftType == shiftType);

                if (shift != null)
                {
                    var nurses = shift.Assignments.Select(a =>
                    {
                        var prefix = "";
                        if (a.IsManualAssignment) prefix += "[M]";
                        if (a.IsResponsibleNurse) prefix += "*";
                        if (a.IsBorrowed) prefix += "(B)";
                        return $"{prefix}{a.Nurse?.ShortName ?? "?"}";
                    });

                    var cellValue = string.Join("\n", nurses);
                    cellValue += $"\n({shift.Assignments.Count}/{shift.RequiredNurses})";

                    worksheet.Cell(dataRow, col).Value = cellValue;
                    worksheet.Cell(dataRow, col).Style.Alignment.WrapText = true;

                    // Color coding
                    if (!shift.IsValid)
                    {
                        worksheet.Cell(dataRow, col).Style.Fill.BackgroundColor =
                            shift.Errors.Any() ? XLColor.LightCoral : XLColor.LightYellow;
                    }
                }

                var isWeekend = day.Date.DayOfWeek == DayOfWeek.Saturday ||
                              day.Date.DayOfWeek == DayOfWeek.Sunday;
                if (isWeekend && (shift == null || shift.IsValid))
                {
                    worksheet.Cell(dataRow, col).Style.Fill.BackgroundColor = XLColor.WhiteSmoke;
                }
            }

            dataRow++;
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();
        worksheet.Column(1).Width = 18;
        for (int i = 2; i <= schedule.Days + 1; i++)
        {
            worksheet.Column(i).Width = 15;
        }

        // Add borders
        var tableRange = worksheet.Range(headerRow, 1, dataRow - 1, schedule.Days + 1);
        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

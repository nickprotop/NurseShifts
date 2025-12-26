namespace NurseShifts.Models.ViewModels;

public class NurseFormViewModel
{
    public Nurse Nurse { get; set; } = new();
    public List<Clinic> AvailableClinics { get; set; } = new();
    public List<int> SelectedBorrowableClinicIds { get; set; } = new();
}

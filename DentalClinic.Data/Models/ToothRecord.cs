namespace DentalClinic.Data.Models;

// سجل سن واحد ضمن المخطط السني (Odontogram) لجلسة معينة
public class ToothRecord
{
    public int ToothRecordID { get; set; }
    public int SessionID { get; set; }
    public string ToothNumber { get; set; } = string.Empty; // ترقيم FDI مثل 11 أو 36
    public string ToothCondition { get; set; } = string.Empty; // Decayed / Filled / Extracted ...
    public string? ProcedureNotes { get; set; }
}

namespace DentalClinic.Data.Models;

// جلسة عمل ضمن حالة ترميم واحدة - جدول مستقل تمامًا عن MedicalSessions الخاصة بجلسات
// العيادة العادية (تفاديًا لأي تعارض مع Finance/Odontogram/PDF الخاصة بها)
public class ProstheticSession
{
    public int ProstheticSessionID { get; set; }
    public int CaseID { get; set; }
    public DateTime SessionDateTime { get; set; }
    public int PerformedByUserID { get; set; }
    public string? PerformedByUserName { get; set; } // JOIN فقط
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

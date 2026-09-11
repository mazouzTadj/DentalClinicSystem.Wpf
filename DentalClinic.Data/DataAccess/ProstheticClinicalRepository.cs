using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// Patch 9: قراءة "الملخص السريري" (Diagnosis / TreatmentPerformed / Medication) من MedicalSessions
// حصراً للعرض داخل ProstheticCaseEditWindow في تطبيق المرمم. مستودع منفصل تماماً عن SessionRepository
// عمداً - ذاك المستودع مُعلَّق صراحة بأنه "خاص بتطبيق الطبيب فقط" ويُرجع أيضاً TotalPrice/PaidAmount/
// WriteOffAmount (فاينانس العيادة العام)، وهذا بالضبط ما يجب ألا يتسرَّب لتطبيق المرمم مطلقاً (راجع
// القسم 3 من وثيقة التصميم: Prosthetic Finance منفصل تماماً عن Finance العيادة). لذلك هذا الـSELECT
// لا يحتوي، ولن يحتوي مستقبلاً، أي حقل مالي - فقط الحقول الثلاثة المشمولة فعلياً بصلاحيات
// Prosthetics.ViewTreatment/ViewDiagnosis/ViewMedications + معلومات تعريف الجلسة غير الحساسة
// (التاريخ + اسم الطبيب). القراءة فقط - لا توجد أي عملية Add/Update/Delete هنا، فتعديل هذه البيانات
// يبقى حصراً من تطبيق الطبيب عبر TreatmentManagementWindow/SessionRepository كما هو الحال دائماً.
public class ProstheticClinicalRepository
{
    private readonly DatabaseHelper _db;

    public ProstheticClinicalRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // Patch 15: تنقيح حقلي بدل رفض كامل - كل حقل من الثلاثة مستقل تماماً بصلاحيته الخاصة (نفس
    // فلسفة ProstheticClinicalSummaryRowViewModel في الواجهة تماماً منذ Patch 9)، فقد يملك المستخدم
    // ViewTreatment بلا ViewDiagnosis مثلاً. سابقاً كان هذا التنقيح يحدث فقط عند بناء صف العرض في
    // الواجهة؛ استدعاء هذه الدالة مباشرة (متجاوزاً الواجهة) كان يُعيد الحقول الثلاثة كاملة بلا قيد.
    // القيمة المخفية تُصبح الآن null من مصدر البيانات نفسه، وليست فقط مخفية بصرياً فوق نص حقيقي.
    public List<ProstheticClinicalSummaryEntry> GetByPatient(int patientId, UserAccount actingUser)
    {
        var canViewDiagnosis = actingUser.Role == UserRole.Doctor || actingUser.HasProstheticPermission(ProstheticPermissionKeys.ViewDiagnosis);
        var canViewTreatment = actingUser.Role == UserRole.Doctor || actingUser.HasProstheticPermission(ProstheticPermissionKeys.ViewTreatment);
        var canViewMedications = actingUser.Role == UserRole.Doctor || actingUser.HasProstheticPermission(ProstheticPermissionKeys.ViewMedications);

        // ⚠️ متعمَّد: لا TotalPrice ولا PaidAmount ولا WriteOffAmount ولا RemainingAmount هنا إطلاقاً
        const string sql = @"
            SELECT ms.SessionDateTime, u.FullName AS DoctorName,
                   ms.Diagnosis, ms.TreatmentPerformed, ms.Medication
            FROM dbo.MedicalSessions ms
            INNER JOIN dbo.Users u ON u.UserID = ms.DoctorID
            WHERE ms.PatientID = @PatientID
            ORDER BY ms.SessionDateTime DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId));
        var result = new List<ProstheticClinicalSummaryEntry>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticClinicalSummaryEntry
            {
                SessionDateTime = (DateTime)row["SessionDateTime"],
                DoctorName = row["DoctorName"] as string ?? "-",
                Diagnosis = canViewDiagnosis ? row["Diagnosis"] as string : null,
                TreatmentPerformed = canViewTreatment ? row["TreatmentPerformed"] as string : null,
                Medication = canViewMedications ? row["Medication"] as string : null
            });
        }
        return result;
    }
}

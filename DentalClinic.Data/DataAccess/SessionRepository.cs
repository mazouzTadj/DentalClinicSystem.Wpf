using Microsoft.Data.SqlClient;
using System.Data;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// كل ما يخص الملف الطبي السري - يُستخدم من تطبيق الطبيب فقط
public class SessionRepository
{
    private readonly DatabaseHelper _db;

    public SessionRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // تسجيل جلسة طبية جديدة (تشخيص / معالجة / دواء / سعر)
    public int Add(MedicalSession session)
    {
        const string sql = @"
            INSERT INTO MedicalSessions
                (VisitID, PatientID, DoctorID, ChiefComplaint, Diagnosis, TreatmentPerformed, Medication, TotalPrice, PaidAmount, Notes)
            VALUES
                (@VisitID, @PatientID, @DoctorID, @ChiefComplaint, @Diagnosis, @TreatmentPerformed, @Medication, @TotalPrice, @PaidAmount, @Notes)";

        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@VisitID", (object?)session.VisitID ?? DBNull.Value),
            new SqlParameter("@PatientID", session.PatientID),
            new SqlParameter("@DoctorID", session.DoctorID),
            new SqlParameter("@ChiefComplaint", (object?)session.ChiefComplaint ?? DBNull.Value),
            new SqlParameter("@Diagnosis", (object?)session.Diagnosis ?? DBNull.Value),
            new SqlParameter("@TreatmentPerformed", (object?)session.TreatmentPerformed ?? DBNull.Value),
            new SqlParameter("@Medication", (object?)session.Medication ?? DBNull.Value),
            new SqlParameter("@TotalPrice", session.TotalPrice),
            new SqlParameter("@PaidAmount", session.PaidAmount),
            new SqlParameter("@Notes", (object?)session.Notes ?? DBNull.Value));
    }

    // السجل الطبي الكامل لكل زيارات مريض معيّن (الأحدث أولاً)
    public List<MedicalSession> GetByPatient(int patientId)
    {
        const string sql = @"
            SELECT SessionID, VisitID, PatientID, DoctorID, SessionDateTime, ChiefComplaint,
                   Diagnosis, TreatmentPerformed, Medication, TotalPrice, PaidAmount, Notes
            FROM MedicalSessions
            WHERE PatientID = @PatientID
            ORDER BY SessionDateTime DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId));
        var result = new List<MedicalSession>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new MedicalSession
            {
                SessionID = (int)row["SessionID"],
                VisitID = row["VisitID"] as int?,
                PatientID = (int)row["PatientID"],
                DoctorID = (int)row["DoctorID"],
                SessionDateTime = (DateTime)row["SessionDateTime"],
                ChiefComplaint = row["ChiefComplaint"] as string,
                Diagnosis = row["Diagnosis"] as string,
                TreatmentPerformed = row["TreatmentPerformed"] as string,
                Medication = row["Medication"] as string,
                TotalPrice = (decimal)row["TotalPrice"],
                PaidAmount = (decimal)row["PaidAmount"],
                Notes = row["Notes"] as string
            });
        }
        return result;
    }

    // تسجيل مبسّط لسن ضمن جلسة - تمهيداً لمخطط الأسنان التفاعلي (Odontogram) في خطوة لاحقة
    public void AddToothRecord(int sessionId, string toothNumber, string condition, string? notes)
    {
        const string sql = @"
            INSERT INTO ToothRecords (SessionID, ToothNumber, ToothCondition, ProcedureNotes)
            VALUES (@SessionID, @ToothNumber, @ToothCondition, @ProcedureNotes)";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@SessionID", sessionId),
            new SqlParameter("@ToothNumber", toothNumber),
            new SqlParameter("@ToothCondition", condition),
            new SqlParameter("@ProcedureNotes", (object?)notes ?? DBNull.Value));
    }

    // تحديث دفعة/ملاحظات جلسة موجودة بدل إنشاء جلسة جديدة - يُستخدم عندما يكون السعر الإجمالي لم يتغيّر
    // (أي أن الطبيب يسجّل دفعة إضافية لنفس الرصيد القائم، وليس علاجاً/فاتورة جديدة)
    public void UpdateSessionPayment(int sessionId, decimal newPaidAmount, string? chiefComplaint,
        string? diagnosis, string? treatment, string? medication)
    {
        const string sql = @"
            UPDATE MedicalSessions
            SET PaidAmount = @PaidAmount,
                ChiefComplaint = @ChiefComplaint,
                Diagnosis = @Diagnosis,
                TreatmentPerformed = @Treatment,
                Medication = @Medication
            WHERE SessionID = @SessionID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@PaidAmount", newPaidAmount),
            new SqlParameter("@ChiefComplaint", (object?)chiefComplaint ?? DBNull.Value),
            new SqlParameter("@Diagnosis", (object?)diagnosis ?? DBNull.Value),
            new SqlParameter("@Treatment", (object?)treatment ?? DBNull.Value),
            new SqlParameter("@Medication", (object?)medication ?? DBNull.Value),
            new SqlParameter("@SessionID", sessionId));
    }

    // أرقام الأسنان المرتبطة بجلسة معيّنة - تُستخدم لإعادة تعبئة المخطط السني عند التعبئة التلقائية من آخر زيارة
    public List<string> GetToothNumbersForSession(int sessionId)
    {
        const string sql = "SELECT ToothNumber FROM ToothRecords WHERE SessionID = @SessionID";
        var table = _db.ExecuteQuery(sql, new SqlParameter("@SessionID", sessionId));

        var result = new List<string>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(row["ToothNumber"].ToString()!);
        }
        return result;
    }

    // بحث متقدم متعدد المعايير عبر كل الجلسات (اسم/هاتف، تشخيص، مدى تاريخي، رصيد متبقٍ، رقم سن)
    // كل معيار اختياري تماماً؛ يُبنى شرط WHERE ديناميكياً لكن بمعاملات SQL آمنة دائماً (لا دمج نصي لقيم المستخدم)
    public List<SessionSearchResult> AdvancedSearch(SessionSearchCriteria criteria)
    {
        var sql = @"
            SELECT DISTINCT s.SessionID, s.PatientID, p.FullName AS PatientFullName, p.PhoneNumber AS PatientPhone,
                   s.SessionDateTime, s.Diagnosis, s.TreatmentPerformed, s.TotalPrice, s.PaidAmount
            FROM MedicalSessions s
            INNER JOIN Patients p ON p.PatientID = s.PatientID
            LEFT JOIN ToothRecords t ON t.SessionID = s.SessionID
            WHERE 1 = 1";

        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(criteria.PatientNameOrPhone))
        {
            sql += " AND (p.FullName LIKE @NameOrPhone OR p.PhoneNumber LIKE @NameOrPhone)";
            parameters.Add(new SqlParameter("@NameOrPhone", $"%{criteria.PatientNameOrPhone}%"));
        }

        if (!string.IsNullOrWhiteSpace(criteria.DiagnosisContains))
        {
            sql += " AND s.Diagnosis LIKE @Diagnosis";
            parameters.Add(new SqlParameter("@Diagnosis", $"%{criteria.DiagnosisContains}%"));
        }

        if (criteria.FromDate.HasValue)
        {
            sql += " AND s.SessionDateTime >= @FromDate";
            parameters.Add(new SqlParameter("@FromDate", criteria.FromDate.Value.Date));
        }

        if (criteria.ToDate.HasValue)
        {
            sql += " AND s.SessionDateTime < @ToDate";
            parameters.Add(new SqlParameter("@ToDate", criteria.ToDate.Value.Date.AddDays(1)));
        }

        if (criteria.OnlyWithOutstandingBalance)
        {
            sql += " AND (s.TotalPrice - s.PaidAmount) > 0";
        }

        if (!string.IsNullOrWhiteSpace(criteria.ToothNumber))
        {
            sql += " AND t.ToothNumber = @ToothNumber";
            parameters.Add(new SqlParameter("@ToothNumber", criteria.ToothNumber.Trim()));
        }

        sql += " ORDER BY s.SessionDateTime DESC";

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        var results = new List<SessionSearchResult>();

        foreach (DataRow row in table.Rows)
        {
            results.Add(new SessionSearchResult
            {
                SessionID = (int)row["SessionID"],
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                PatientPhone = row["PatientPhone"].ToString()!,
                SessionDateTime = (DateTime)row["SessionDateTime"],
                Diagnosis = row["Diagnosis"] as string,
                TreatmentPerformed = row["TreatmentPerformed"] as string,
                TotalPrice = (decimal)row["TotalPrice"],
                PaidAmount = (decimal)row["PaidAmount"]
            });
        }
        return results;
    }
}

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
}

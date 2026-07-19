using Microsoft.Data.SqlClient;
using System.Data;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

public class PatientRepository
{
    private readonly DatabaseHelper _db;

    public PatientRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // تسجيل مريض جديد - يُستخدم من تطبيق الممرضة فقط
    public int Add(Patient patient)
    {
        const string sql = @"
            INSERT INTO Patients (FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes, RegisteredByUserID)
            VALUES (@FullName, @Age, @Gender, @PhoneNumber, @Address, @BasicMedicalNotes, @RegisteredByUserID)";

        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@FullName", patient.FullName),
            new SqlParameter("@Age", (object?)patient.Age ?? DBNull.Value),
            new SqlParameter("@Gender", (object?)patient.Gender ?? DBNull.Value),
            new SqlParameter("@PhoneNumber", patient.PhoneNumber),
            new SqlParameter("@Address", (object?)patient.Address ?? DBNull.Value),
            new SqlParameter("@BasicMedicalNotes", (object?)patient.BasicMedicalNotes ?? DBNull.Value),
            new SqlParameter("@RegisteredByUserID", patient.RegisteredByUserID));
    }

    // بحث عن مريض بالاسم أو رقم الهاتف - يُستخدم عند إضافة مريض قديم لقائمة الانتظار
    public List<Patient> Search(string term)
    {
        const string sql = @"
            SELECT PatientID, FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes,
                   RegisteredByUserID, RegisteredAt, IsActive
            FROM Patients
            WHERE IsActive = 1
              AND (FullName LIKE @Term OR PhoneNumber LIKE @Term)
            ORDER BY FullName";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Term", $"%{term}%"));
        var result = new List<Patient>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(MapRow(row));
        }
        return result;
    }

    public Patient? GetById(int patientId)
    {
        const string sql = @"
            SELECT PatientID, FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes,
                   RegisteredByUserID, RegisteredAt, IsActive
            FROM Patients
            WHERE PatientID = @PatientID";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@PatientID", patientId));
        return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
    }

    // البحث عن مريض مطابق تماماً بالاسم ورقم الهاتف - يُستخدم لمنع تسجيل نفس المريض أكثر من مرة بالخطأ
    public Patient? FindDuplicate(string fullName, string phoneNumber)
    {
        const string sql = @"
            SELECT TOP 1 PatientID, FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes,
                   RegisteredByUserID, RegisteredAt, IsActive
            FROM Patients
            WHERE IsActive = 1
              AND LTRIM(RTRIM(PhoneNumber)) = LTRIM(RTRIM(@PhoneNumber))
              AND LOWER(LTRIM(RTRIM(FullName))) = LOWER(LTRIM(RTRIM(@FullName)))
            ORDER BY RegisteredAt DESC";

        var table = _db.ExecuteQuery(sql,
            new SqlParameter("@PhoneNumber", phoneNumber),
            new SqlParameter("@FullName", fullName));

        return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
    }

    private static Patient MapRow(DataRow row) => new Patient
    {
        PatientID = (int)row["PatientID"],
        FullName = row["FullName"].ToString()!,
        Age = row["Age"] as int?,
        Gender = row["Gender"] as string,
        PhoneNumber = row["PhoneNumber"].ToString()!,
        Address = row["Address"] as string,
        BasicMedicalNotes = row["BasicMedicalNotes"] as string,
        RegisteredByUserID = (int)row["RegisteredByUserID"],
        RegisteredAt = (DateTime)row["RegisteredAt"],
        IsActive = (bool)row["IsActive"]
    };
}

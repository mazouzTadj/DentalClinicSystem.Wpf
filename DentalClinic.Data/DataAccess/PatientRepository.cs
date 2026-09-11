using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

public class PatientRepository
{
    private readonly DatabaseHelper _db;

    public PatientRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureAssignedDoctorColumnExists();
    }

    // عمود "الطبيب المسؤول عن المريض" - migration آمنة لقواعد البيانات القديمة (نفس نمط
    // EnsureCommissionColumnExists في UserRepository)
    private void EnsureAssignedDoctorColumnExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'AssignedDoctorUserID' AND Object_ID = Object_ID(N'Patients'))
            BEGIN
                ALTER TABLE Patients ADD AssignedDoctorUserID INT NULL;
            END";
        _db.ExecuteNonQuery(sql);
    }

    // تسجيل مريض جديد - يُستخدم من تطبيق الممرضة فقط
    public int Add(Patient patient)
    {
        const string sql = @"
            INSERT INTO Patients (FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes, RegisteredByUserID, AssignedDoctorUserID)
            VALUES (@FullName, @Age, @Gender, @PhoneNumber, @Address, @BasicMedicalNotes, @RegisteredByUserID, @AssignedDoctorUserID)";

        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@FullName", patient.FullName),
            new SqlParameter("@Age", (object?)patient.Age ?? DBNull.Value),
            new SqlParameter("@Gender", (object?)patient.Gender ?? DBNull.Value),
            new SqlParameter("@PhoneNumber", patient.PhoneNumber),
            new SqlParameter("@Address", (object?)patient.Address ?? DBNull.Value),
            new SqlParameter("@BasicMedicalNotes", (object?)patient.BasicMedicalNotes ?? DBNull.Value),
            new SqlParameter("@RegisteredByUserID", patient.RegisteredByUserID),
            new SqlParameter("@AssignedDoctorUserID", (object?)patient.AssignedDoctorUserID ?? DBNull.Value));
    }

    // تعديل البيانات الأساسية لمريض موجود مسبقاً (تصحيح خطأ إدخال، مثلاً اسم أو رقم هاتف خاطئ).
    // لا يُغيَّر RegisteredByUserID ولا RegisteredAt عمداً - تبقى بصمة أول من سجَّل المريض ومتى كما هي،
    // حتى لو عدَّل بياناته لاحقاً مستخدم آخر يملك صلاحية EditPatients.
    public void Update(Patient patient)
    {
        const string sql = @"
            UPDATE Patients
            SET FullName = @FullName, Age = @Age, Gender = @Gender, PhoneNumber = @PhoneNumber,
                Address = @Address, BasicMedicalNotes = @BasicMedicalNotes, AssignedDoctorUserID = @AssignedDoctorUserID
            WHERE PatientID = @PatientID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@FullName", patient.FullName),
            new SqlParameter("@Age", (object?)patient.Age ?? DBNull.Value),
            new SqlParameter("@Gender", (object?)patient.Gender ?? DBNull.Value),
            new SqlParameter("@PhoneNumber", patient.PhoneNumber),
            new SqlParameter("@Address", (object?)patient.Address ?? DBNull.Value),
            new SqlParameter("@BasicMedicalNotes", (object?)patient.BasicMedicalNotes ?? DBNull.Value),
            new SqlParameter("@AssignedDoctorUserID", (object?)patient.AssignedDoctorUserID ?? DBNull.Value),
            new SqlParameter("@PatientID", patient.PatientID));
    }

    // بحث عن مريض بالاسم أو رقم الهاتف - يُستخدم عند إضافة مريض قديم لقائمة الانتظار، ومن شاشة البحث
    // term قد تكون فارغة إن استُخدم فلتر الجنس/العمر وحدهما بدون اسم أو هاتف
    //
    // allowedDoctorUserIds: قيود الرؤية حسب الطبيب المسؤول عن كل مريض -
    //   null  => بلا أي تقييد إطلاقاً (يُستخدم فقط للطبيب الرئيسي أو الممرضة وقت تسجيل مريض جديد)
    //   قائمة (حتى لو فارغة) => يظهر فقط المرضى المُسنَدين لأحد هذي المعرّفات
    // includeUnassigned: هل تُضاف أيضاً المرضى غير المُسنَدين لأي طبيب بعد (AssignedDoctorUserID = NULL)؟
    //   (تُهمَل هذي القيمة تماماً إن كان allowedDoctorUserIds = null)
    public List<Patient> Search(string term, string? gender = null, int? minAge = null, int? maxAge = null,
        List<int>? allowedDoctorUserIds = null, bool includeUnassigned = true)
    {
        var sql = @"
            SELECT PatientID, FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes,
                   RegisteredByUserID, RegisteredAt, IsActive, AssignedDoctorUserID
            FROM Patients
            WHERE IsActive = 1";

        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(term))
        {
            sql += " AND (FullName LIKE @Term OR PhoneNumber LIKE @Term)";
            parameters.Add(new SqlParameter("@Term", $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(gender))
        {
            sql += " AND Gender = @Gender";
            parameters.Add(new SqlParameter("@Gender", gender));
        }

        if (minAge.HasValue)
        {
            sql += " AND Age >= @MinAge";
            parameters.Add(new SqlParameter("@MinAge", minAge.Value));
        }

        if (maxAge.HasValue)
        {
            sql += " AND Age <= @MaxAge";
            parameters.Add(new SqlParameter("@MaxAge", maxAge.Value));
        }

        if (allowedDoctorUserIds != null)
        {
            if (allowedDoctorUserIds.Count == 0)
            {
                // ما فيه ولا طبيب مسموح - يظهر فقط الغير مُسنَدين (إن سُمح بهم)، وإلا لا يظهر شيء إطلاقاً
                sql += includeUnassigned ? " AND AssignedDoctorUserID IS NULL" : " AND 1 = 0";
            }
            else
            {
                var placeholders = allowedDoctorUserIds.Select((id, i) => $"@Doc{i}").ToList();
                for (var i = 0; i < allowedDoctorUserIds.Count; i++)
                {
                    parameters.Add(new SqlParameter($"@Doc{i}", allowedDoctorUserIds[i]));
                }

                sql += includeUnassigned
                    ? $" AND (AssignedDoctorUserID IN ({string.Join(",", placeholders)}) OR AssignedDoctorUserID IS NULL)"
                    : $" AND AssignedDoctorUserID IN ({string.Join(",", placeholders)})";
            }
        }

        sql += " ORDER BY FullName";

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        var result = new List<Patient>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(MapRow(row));
        }
        return result;
    }

    // allowedDoctorUserIds بنفس معنى ومنطق Search أعلاه تماماً - يُستخدم كطبقة حماية إضافية عند
    // فتح الملف الطبي الكامل، حتى لو حاول طبيب ثانوي الوصول لمريض ليس له بطريقة غير مباشرة (مثلاً
    // معرّف مريض مُدخَل يدوياً) لن يُرجع GetById شيئاً له أصلاً.
    public Patient? GetById(int patientId, List<int>? allowedDoctorUserIds = null, bool includeUnassigned = true)
    {
        var sql = @"
            SELECT PatientID, FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes,
                   RegisteredByUserID, RegisteredAt, IsActive, AssignedDoctorUserID
            FROM Patients
            WHERE PatientID = @PatientID";

        var parameters = new List<SqlParameter> { new SqlParameter("@PatientID", patientId) };

        if (allowedDoctorUserIds != null)
        {
            if (allowedDoctorUserIds.Count == 0)
            {
                sql += includeUnassigned ? " AND AssignedDoctorUserID IS NULL" : " AND 1 = 0";
            }
            else
            {
                var placeholders = allowedDoctorUserIds.Select((id, i) => $"@Doc{i}").ToList();
                for (var i = 0; i < allowedDoctorUserIds.Count; i++)
                {
                    parameters.Add(new SqlParameter($"@Doc{i}", allowedDoctorUserIds[i]));
                }

                sql += includeUnassigned
                    ? $" AND (AssignedDoctorUserID IN ({string.Join(",", placeholders)}) OR AssignedDoctorUserID IS NULL)"
                    : $" AND AssignedDoctorUserID IN ({string.Join(",", placeholders)})";
            }
        }

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
    }

    // البحث عن مريض مطابق تماماً بالاسم ورقم الهاتف - يُستخدم لمنع تسجيل نفس المريض أكثر من مرة بالخطأ
    // بحث عن مرضى بنفس الاسم بالضبط (بغض النظر عن رقم الهاتف) - يُستخدم للتنبيه عند تسجيل مريض جديد
    // باسم مطابق لمريض موجود مسبقاً، حتى لو اختلف رقم الهاتف أو كان فارغاً، لمنع تسجيل مرضى مكرَّرين
    // بصمت دون أي تنبيه للممرضة
    public List<Patient> FindByNameOnly(string fullName)
    {
        const string sql = @"
            SELECT PatientID, FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes,
                   RegisteredByUserID, RegisteredAt, IsActive, AssignedDoctorUserID
            FROM Patients
            WHERE IsActive = 1
              AND LOWER(LTRIM(RTRIM(FullName))) = LOWER(LTRIM(RTRIM(@FullName)))
            ORDER BY RegisteredAt DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@FullName", fullName));
        var result = new List<Patient>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(MapRow(row));
        }
        return result;
    }

    public Patient? FindDuplicate(string fullName, string phoneNumber)
    {
        const string sql = @"
            SELECT TOP 1 PatientID, FullName, Age, Gender, PhoneNumber, Address, BasicMedicalNotes,
                   RegisteredByUserID, RegisteredAt, IsActive, AssignedDoctorUserID
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
        IsActive = (bool)row["IsActive"],
        AssignedDoctorUserID = row.Table.Columns.Contains("AssignedDoctorUserID") && row["AssignedDoctorUserID"] != DBNull.Value
            ? Convert.ToInt32(row["AssignedDoctorUserID"])
            : (int?)null
    };

    // حذف مريض نهائياً من قاعدة البيانات (وليس مجرد تعطيله عبر IsActive) - عملية لا يمكن التراجع
    // عنها، لذا يجب أن تُستدعى فقط بعد تأكيد صريح من المستخدم في الواجهة، ومن مستخدم يملك صلاحية
    // كافية (راجع UserPermission.DeletePatients).
    //
    // ترتيب الحذف يتبع سلسلة المفاتيح الأجنبية بدقة (لا يوجد ON DELETE CASCADE في المخطط عمداً،
    // لتفادي حذف تاريخ طبي بالخطأ من عملية أخرى): ToothRecords -> Payments -> MedicalSessions
    // -> VisitQueue -> Patients. كل هذا داخل معاملة (Transaction) واحدة: إما تُحذف كل السجلات
    // المرتبطة بنجاح أو لا يتغيّر شيء إطلاقاً.
    //
    // ⚠️ Patch 12: عمداً لا تُحذَف حالات الترميم (ProstheticCases) هنا - لا حذف تلقائي عبر نظام
    // منفصل بالكامل (راجع الفصل الكامل بين Finance/بيانات العيادة والترميم منذ Patch 1). إن كان
    // للمريض حالة ترميم، تفشل هذه الدالة الآن برسالة واضحة (بدل خطأ SQL خام) تطلب من المستخدم
    // معالجة حالات الترميم أولاً (حذفها عبر ProstheticCaseRepository.PermanentlyDelete، أو نقلها -
    // لا مسار نقل مريض حالياً)، بدل حذفها تلقائياً بصمت من هنا.
    public void PermanentlyDelete(int patientId)
    {
        using var conn = _db.GetConnection();
        conn.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            void Exec(string sql)
            {
                using var cmd = new SqlCommand(sql, conn, transaction) { CommandTimeout = DatabaseHelper.CommandTimeoutSeconds };
                cmd.Parameters.AddWithValue("@PatientID", patientId);
                cmd.ExecuteNonQuery();
            }

            Exec(@"DELETE FROM ToothRecords
                    WHERE SessionID IN (SELECT SessionID FROM MedicalSessions WHERE PatientID = @PatientID)");

            Exec(@"DELETE FROM Payments
                    WHERE SessionID IN (SELECT SessionID FROM MedicalSessions WHERE PatientID = @PatientID)");

            Exec("DELETE FROM MedicalSessions WHERE PatientID = @PatientID");

            Exec("DELETE FROM VisitQueue WHERE PatientID = @PatientID");

            Exec("DELETE FROM Patients WHERE PatientID = @PatientID");

            transaction.Commit();
        }
        // Patch 12: قبل هذا الإصلاح، محاولة حذف مريض له حالة ترميم واحدة على الأقل كانت تفشل بخطأ
        // SQL خام غير مفهوم (FK_ProstheticCases_Patient) بدل رسالة واضحة - نتعمَّد عدم حذف حالات
        // الترميم تلقائياً هنا (قرار "لا حذف تلقائي عبر جدول لا علاقة مباشرة له" نفسه المطبَّق أصلاً
        // على بقية الجداول في هذه الدالة)، ونطلب من المستخدم معالجة حالات الترميم أولاً بدل ذلك.
        catch (SqlException sqlEx) when (sqlEx.Number == 547 && sqlEx.Message.Contains("FK_ProstheticCases_Patient"))
        {
            transaction.Rollback();
            throw new InvalidOperationException(
                "This patient can't be deleted because they have one or more prosthetic cases on record. Please delete or reassign those prosthetic cases first, then try again.", sqlEx);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}

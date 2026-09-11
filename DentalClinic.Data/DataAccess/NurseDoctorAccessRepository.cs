using Microsoft.Data.SqlClient;
using System.Data;

namespace DentalClinic.Data.DataAccess;

// يدير جدول "من يقدر يشوف قائمة انتظار مين": الطبيب الرئيسي هو من يمنح كل ممرضة (بشكل مستقل عن
// الممرضات الأخريات) إمكانية رؤية قائمة انتظار طبيب معيَّن أو أكثر. لا علاقة لهذا بصلاحية
// EditPatients/RegisterPatients العامة - هذا تحكّم دقيق لكل زوج (ممرضة، طبيب) على حدة.
public class NurseDoctorAccessRepository
{
    private readonly DatabaseHelper _db;

    public NurseDoctorAccessRepository(DatabaseHelper db)
    {
        _db = db;
        EnsureTableExists();
    }

    private void EnsureTableExists()
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'NurseDoctorAccess')
            BEGIN
                CREATE TABLE NurseDoctorAccess (
                    NurseUserID INT NOT NULL,
                    DoctorUserID INT NOT NULL,
                    CONSTRAINT PK_NurseDoctorAccess PRIMARY KEY (NurseUserID, DoctorUserID),
                    CONSTRAINT FK_NurseDoctorAccess_Nurse FOREIGN KEY (NurseUserID) REFERENCES Users(UserID),
                    CONSTRAINT FK_NurseDoctorAccess_Doctor FOREIGN KEY (DoctorUserID) REFERENCES Users(UserID)
                );
            END";
        _db.ExecuteNonQuery(sql);
    }

    // قائمة معرّفات الأطباء المسموح لهذه الممرضة رؤية قوائمهم - فارغة افتراضياً لكل ممرضة جديدة
    // (الوضع الافتراضي بعد هذه الميزة: بلا صلاحية على أي طبيب، إلى أن يمنحها الطبيب الرئيسي صراحةً)
    public List<int> GetAllowedDoctorIds(int nurseUserId)
    {
        const string sql = "SELECT DoctorUserID FROM NurseDoctorAccess WHERE NurseUserID = @NurseUserID";
        var table = _db.ExecuteQuery(sql, new SqlParameter("@NurseUserID", nurseUserId));

        var result = new List<int>();
        foreach (DataRow row in table.Rows)
        {
            result.Add((int)row["DoctorUserID"]);
        }
        return result;
    }

    // يستبدل كامل قائمة الأطباء المسموحين لهذه الممرضة بالقائمة الجديدة دفعة واحدة - تُستدعى من
    // شاشة تعديل المستخدم عند حفظ التغييرات على ممرضة
    public void SetAllowedDoctorIds(int nurseUserId, IEnumerable<int> doctorUserIds)
    {
        _db.ExecuteNonQuery("DELETE FROM NurseDoctorAccess WHERE NurseUserID = @NurseUserID",
            new SqlParameter("@NurseUserID", nurseUserId));

        const string insertSql = "INSERT INTO NurseDoctorAccess (NurseUserID, DoctorUserID) VALUES (@NurseUserID, @DoctorUserID)";
        foreach (var doctorId in doctorUserIds.Distinct())
        {
            _db.ExecuteNonQuery(insertSql,
                new SqlParameter("@NurseUserID", nurseUserId),
                new SqlParameter("@DoctorUserID", doctorId));
        }
    }
}

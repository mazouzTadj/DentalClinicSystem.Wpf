using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// كل استعلامات اللوحة المالية - يُستخدم من تطبيق الطبيب فقط (صلاحية المدير العام تحديداً)
public class FinancialRepository
{
    private readonly DatabaseHelper _db;

    public FinancialRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // إجمالي المداخيل (المبالغ المُحصَّلة فعلياً PaidAmount، وليس TotalPrice) - يومي/شهري/سنوي + إجمالي المتبقي
    public RevenueSummary GetRevenueSummary()
    {
        const string sql = @"
            SELECT
                ISNULL(SUM(CASE WHEN CAST(SessionDateTime AS DATE) = CAST(GETDATE() AS DATE) THEN PaidAmount ELSE 0 END), 0) AS TodayRevenue,
                ISNULL(SUM(CASE WHEN YEAR(SessionDateTime) = YEAR(GETDATE()) AND MONTH(SessionDateTime) = MONTH(GETDATE()) THEN PaidAmount ELSE 0 END), 0) AS MonthRevenue,
                ISNULL(SUM(CASE WHEN YEAR(SessionDateTime) = YEAR(GETDATE()) THEN PaidAmount ELSE 0 END), 0) AS YearRevenue,
                ISNULL(SUM(TotalPrice - PaidAmount), 0) AS TotalOutstanding
            FROM MedicalSessions";

        var table = _db.ExecuteQuery(sql);
        var row = table.Rows[0];

        return new RevenueSummary
        {
            TodayRevenue = (decimal)row["TodayRevenue"],
            MonthRevenue = (decimal)row["MonthRevenue"],
            YearRevenue = (decimal)row["YearRevenue"],
            TotalOutstanding = (decimal)row["TotalOutstanding"]
        };
    }

    // كل المرضى الذين لديهم مبلغ متبقٍ غير مسدَّد، مرتّبين تنازلياً حسب الأكبر ديناً
    public List<OutstandingBalanceRow> GetOutstandingBalances()
    {
        const string sql = @"
            SELECT p.PatientID, p.FullName AS PatientFullName, p.PhoneNumber,
                   SUM(s.TotalPrice - s.PaidAmount) AS TotalOwed,
                   MAX(s.SessionDateTime) AS LastVisit
            FROM MedicalSessions s
            INNER JOIN Patients p ON p.PatientID = s.PatientID
            GROUP BY p.PatientID, p.FullName, p.PhoneNumber
            HAVING SUM(s.TotalPrice - s.PaidAmount) > 0
            ORDER BY TotalOwed DESC";

        var table = _db.ExecuteQuery(sql);
        var result = new List<OutstandingBalanceRow>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new OutstandingBalanceRow
            {
                PatientID = (int)row["PatientID"],
                PatientFullName = row["PatientFullName"].ToString()!,
                PhoneNumber = row["PhoneNumber"].ToString()!,
                TotalOwed = (decimal)row["TotalOwed"],
                LastVisit = (DateTime)row["LastVisit"]
            });
        }
        return result;
    }

    // عدد المرضى (الفريدين) لكل يوم خلال آخر N يوماً - أيام بلا زيارات لا تظهر في النتيجة (تُملأ لاحقاً في الواجهة)
    public List<DailyPatientCount> GetDailyPatientCounts(int days = 14)
    {
        const string sql = @"
            SELECT CAST(SessionDateTime AS DATE) AS VisitDay, COUNT(DISTINCT PatientID) AS PatientCount
            FROM MedicalSessions
            WHERE SessionDateTime >= @FromDate
            GROUP BY CAST(SessionDateTime AS DATE)
            ORDER BY VisitDay";

        var fromDate = DateTime.Now.Date.AddDays(-(days - 1));
        var table = _db.ExecuteQuery(sql, new SqlParameter("@FromDate", fromDate));

        var result = new List<DailyPatientCount>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new DailyPatientCount
            {
                Date = (DateTime)row["VisitDay"],
                Count = (int)row["PatientCount"]
            });
        }
        return result;
    }

    // أكثر النصوص تكراراً في حقل "المعالجة" - تقريب بسيط لأكثر الخدمات طلباً
    // (يعتمد على تطابق نصي حرفي، فيُنصح لاحقاً بحقل نوع علاج منظّم لدقة أعلى - هذا كافٍ كبداية عملية)
    public List<TreatmentFrequency> GetTopTreatments(int top = 5)
    {
        const string sql = @"
            SELECT TOP (@Top) TreatmentPerformed, COUNT(*) AS Cnt
            FROM MedicalSessions
            WHERE TreatmentPerformed IS NOT NULL AND LTRIM(RTRIM(TreatmentPerformed)) <> ''
            GROUP BY TreatmentPerformed
            ORDER BY COUNT(*) DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@Top", top));
        var result = new List<TreatmentFrequency>();

        foreach (DataRow row in table.Rows)
        {
            result.Add(new TreatmentFrequency
            {
                TreatmentText = row["TreatmentPerformed"].ToString()!,
                Count = (int)row["Cnt"]
            });
        }
        return result;
    }
}

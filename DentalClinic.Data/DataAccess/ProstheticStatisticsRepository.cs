using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// إحصائيات الترميم (Patch 11.1 + 11.2 + 12) - طبقة قراءة فقط بالكامل (لا Insert/Update/Delete
// مباشرة هنا، باستثناء الاستعانة بـProstheticExpenseRepository للقراءة فقط أيضاً - راجع أدناه).
// ⚠️ نفس قاعدة الفصل المالي المطبَّقة في ProstheticPaymentRepository بالحرف: كل استعلام هنا يقرأ
// حصراً من dbo.ProstheticCases / dbo.ProstheticSessions / dbo.ProstheticPayments / dbo.ProstheticStages
// / dbo.ProstheticCaseHistory (للمشتقات الزمنية) / dbo.ProstheticExpenses (Patch 12، لحساب
// TotalExpenses/NetIncome حصراً) - لا يُستخدم إطلاقاً dbo.Payments، ولا الحقول المالية في
// dbo.MedicalSessions، ولا dbo.ClinicExpenses، ولا أي استعلام من FinancialRepository (فاينانس
// العيادة العام). الفصل الكامل المتَّفق عليه منذ Patch 1 محفوظ هنا حرفياً - ProstheticExpenses
// جدول جديد مستقل بذاته، وليس استثناءً لهذا الفصل.
public class ProstheticStatisticsRepository
{
    private readonly DatabaseHelper _db;
    // Patch 12: مصدر TotalExpenses/NetIncome أدناه - dbo.ProstheticExpenses حصراً (جدول منفصل تماماً
    // عن ClinicExpenses العام، راجع تعليق ProstheticExpenseRepository). استخدام الـRepository نفسه
    // هنا (بدل تكرار SQL) يضمن أيضاً أن EnsureTableExists يعمل من أول فتح لشاشة الإحصائيات، حتى قبل
    // فتح شاشة المصاريف ولو مرة واحدة.
    private readonly ProstheticExpenseRepository _expenseRepo;

    public ProstheticStatisticsRepository(DatabaseHelper db)
    {
        _db = db;
        _expenseRepo = new ProstheticExpenseRepository(db);
    }

    // fromDate/toDateExclusive: نطاق نصف مفتوح [from, to) - المستدعي (نافذة الإحصائيات) يحلّ
    // الفترة المختارة (اليوم/الأسبوع/الشهر/مخصص) لتاريخين ملموسين قبل استدعاء هذه الدالة، فتبقى
    // طبقة القراءة نفسها بسيطة ولا تعرف شيئاً عن مفهوم "الفترة" كتسمية.
    //
    // ⚠️ ملاحظة تصميم مهمة حول "الأساس الزمني" لكل مقياس - ليست كلها بنفس العمود:
    //   - عدّادات الحالات + قيمة الحالات + Upper/Lower: على ProstheticCases.CreatedAt (حالات
    //     أُنشئت ضمن الفترة).
    //   - المدفوعات (TotalPayments): على ProstheticPayments.PaymentDate (مبالغ ورَدت فعلياً ضمن
    //     الفترة، بصرف النظر عن تاريخ إنشاء حالتها - نفس منطق ProstheticPaymentRepository.GetFinanceSummary
    //     تماماً لحقل الإيراد).
    //   - المتبقي (TotalOutstanding): يُحسَب حياً (الآن) على حالات الفترة نفسها (CreatedAt)، وليس
    //     "المتبقي وقت انتهاء الفترة" - نفس أسلوب GetFinanceSummary (لا قيمة مخزَّنة يمكن أن تُصبح
    //     قديمة، فحذف/تعديل أي دفعة يُصحِّح الرقم تلقائياً في المرة القادمة التي تُفتح فيها الشاشة).
    //   - عدد الجلسات: على ProstheticSessions.SessionDateTime (جلسات جرت فعلياً ضمن الفترة).
    // هذا التمييز مقصود ومطابق لما هو معتمد فعلياً في بقية المشروع، وليس تناقضاً.
    public ProstheticStatisticsSummary GetSummary(int? prosthetistUserId, DateTime fromDate, DateTime toDateExclusive)
    {
        var caseFilter = prosthetistUserId.HasValue ? " AND c.AssignedProsthetistUserID = @ProsthetistID" : "";

        SqlParameter[] CaseParams() => prosthetistUserId.HasValue
            ? new[] { new SqlParameter("@FromDate", fromDate), new SqlParameter("@ToDate", toDateExclusive), new SqlParameter("@ProsthetistID", prosthetistUserId.Value) }
            : new[] { new SqlParameter("@FromDate", fromDate), new SqlParameter("@ToDate", toDateExclusive) };

        var summary = new ProstheticStatisticsSummary();

        // 1) عدّادات الحالات + Upper/Lower + قيمة الحالات - كلها من استعلام واحد على ProstheticCases
        var caseSql = $@"
            SELECT
                COUNT(*) AS TotalCases,
                SUM(CASE WHEN c.CaseStatus = 'Completed' THEN 1 ELSE 0 END) AS CompletedCases,
                SUM(CASE WHEN c.CaseStatus = 'Open' THEN 1 ELSE 0 END) AS ActiveCases,
                SUM(CASE WHEN c.CaseStatus = 'Cancelled' THEN 1 ELSE 0 END) AS CancelledCases,
                SUM(CASE WHEN c.IncludesUpper = 1 AND c.IncludesLower = 0 THEN 1 ELSE 0 END) AS UpperOnlyCases,
                SUM(CASE WHEN c.IncludesUpper = 0 AND c.IncludesLower = 1 THEN 1 ELSE 0 END) AS LowerOnlyCases,
                SUM(CASE WHEN c.IncludesUpper = 1 AND c.IncludesLower = 1 THEN 1 ELSE 0 END) AS BothArchCases,
                ISNULL(SUM(c.TotalAgreedPrice), 0) AS TotalCaseValue
            FROM dbo.ProstheticCases c
            WHERE c.CreatedAt >= @FromDate AND c.CreatedAt < @ToDate{caseFilter}";

        var caseTable = _db.ExecuteQuery(caseSql, CaseParams());
        var caseRow = caseTable.Rows[0];
        summary.TotalCases = Convert.ToInt32(caseRow["TotalCases"]);
        summary.CompletedCases = caseRow["CompletedCases"] is DBNull ? 0 : Convert.ToInt32(caseRow["CompletedCases"]);
        summary.ActiveCases = caseRow["ActiveCases"] is DBNull ? 0 : Convert.ToInt32(caseRow["ActiveCases"]);
        summary.CancelledCases = caseRow["CancelledCases"] is DBNull ? 0 : Convert.ToInt32(caseRow["CancelledCases"]);
        summary.UpperOnlyCases = caseRow["UpperOnlyCases"] is DBNull ? 0 : Convert.ToInt32(caseRow["UpperOnlyCases"]);
        summary.LowerOnlyCases = caseRow["LowerOnlyCases"] is DBNull ? 0 : Convert.ToInt32(caseRow["LowerOnlyCases"]);
        summary.BothArchCases = caseRow["BothArchCases"] is DBNull ? 0 : Convert.ToInt32(caseRow["BothArchCases"]);
        summary.TotalCaseValue = Convert.ToDecimal(caseRow["TotalCaseValue"]);

        // 2) المتبقي الحي على حالات الفترة نفسها (راجع الملاحظة أعلاه) - نفس أسلوب GetFinanceSummary بالحرف
        var outstandingSql = $@"
            SELECT ISNULL(SUM(CASE WHEN (c.TotalAgreedPrice - ISNULL(paid.Amount, 0)) > 0
                                    THEN (c.TotalAgreedPrice - ISNULL(paid.Amount, 0)) ELSE 0 END), 0) AS TotalOutstanding
            FROM dbo.ProstheticCases c
            OUTER APPLY (SELECT SUM(pp.Amount) AS Amount FROM dbo.ProstheticPayments pp WHERE pp.CaseID = c.CaseID) paid
            WHERE c.CreatedAt >= @FromDate AND c.CreatedAt < @ToDate AND c.CaseStatus <> 'Cancelled'{caseFilter}";
        summary.TotalOutstanding = Convert.ToDecimal(_db.ExecuteScalar(outstandingSql, CaseParams()));

        // 3) المدفوعات الواردة فعلياً ضمن الفترة (PaymentDate) - راجع الملاحظة أعلاه لسبب اختلاف العمود الزمني
        var paymentsSql = $@"
            SELECT ISNULL(SUM(pp.Amount), 0) AS TotalPayments
            FROM dbo.ProstheticPayments pp
            INNER JOIN dbo.ProstheticCases c ON c.CaseID = pp.CaseID
            WHERE pp.PaymentDate >= @FromDate AND pp.PaymentDate < @ToDate{caseFilter}";
        summary.TotalPayments = Convert.ToDecimal(_db.ExecuteScalar(paymentsSql, CaseParams()));

        // 3.5) مصاريف المرمم ضمن نفس الفترة/الفلتر (Patch 12) - ExpenseDate، من ProstheticExpenses
        //      حصراً. يُستخدَم مع TotalPayments أعلاه لحساب NetIncome (خاصية مشتقة في الموديل).
        summary.TotalExpenses = _expenseRepo.GetTotalExpenses(prosthetistUserId, fromDate, toDateExclusive);

        // 4) عدد الجلسات التي جرت فعلياً ضمن الفترة (SessionDateTime)
        var sessionsSql = $@"
            SELECT COUNT(*) AS SessionCount
            FROM dbo.ProstheticSessions s
            INNER JOIN dbo.ProstheticCases c ON c.CaseID = s.CaseID
            WHERE s.SessionDateTime >= @FromDate AND s.SessionDateTime < @ToDate{caseFilter}";
        summary.SessionCount = Convert.ToInt32(_db.ExecuteScalar(sessionsSql, CaseParams()));

        // 5) الحالات حسب المرحلة (لنفس حالات الفترة/المرمم) - بترتيب SortOrder المعتمد أصلاً في القوائم
        //    + Patch 11.2: متوسط عدد الأيام في المرحلة الحالية لكل مجموعة (منذ آخر StageChanged
        //    نحوها في History، أو منذ CreatedAt إن لم تتغيّر المرحلة إطلاقاً) - مؤشر "ازدحام زمني"
        //    إضافي فوق العدد المجرَّد، مُشتَق بالكامل من ProstheticCaseHistory الموجود بالفعل
        //    (بلا أي عمود جديد في ProstheticCases، وبلا أي صلاحية جديدة).
        var stageSql = $@"
            SELECT ISNULL(st.StageName, N'-') AS StageName, COUNT(*) AS StageCount, ISNULL(st.SortOrder, 999999) AS SortOrder,
                   AVG(CAST(DATEDIFF(DAY, ISNULL(lastChange.ChangedAt, c.CreatedAt), GETDATE()) AS FLOAT)) AS AvgDaysInStage
            FROM dbo.ProstheticCases c
            LEFT JOIN dbo.ProstheticStages st ON st.StageID = c.StageID
            OUTER APPLY (
                SELECT TOP 1 h.ChangedAt
                FROM dbo.ProstheticCaseHistory h
                WHERE h.CaseID = c.CaseID AND h.ActionType = 'StageChanged'
                ORDER BY h.ChangedAt DESC
            ) lastChange
            WHERE c.CreatedAt >= @FromDate AND c.CreatedAt < @ToDate{caseFilter}
            GROUP BY ISNULL(st.StageName, N'-'), ISNULL(st.SortOrder, 999999)
            ORDER BY SortOrder";
        var stageTable = _db.ExecuteQuery(stageSql, CaseParams());
        foreach (DataRow row in stageTable.Rows)
        {
            summary.CasesByStage.Add(new ProstheticStageCount
            {
                StageName = row["StageName"].ToString()!,
                Count = Convert.ToInt32(row["StageCount"]),
                AverageDaysInStage = row["AvgDaysInStage"] is DBNull ? null : Convert.ToDouble(row["AvgDaysInStage"])
            });
        }

        // 6) متوسط مدة إنجاز الحالة (Patch 11.2) - من الإنشاء إلى آخر حركة History من نوع
        //    StatusChanged→Completed لنفس الحالة. CROSS APPLY يستبعد تلقائياً أي حالة "مكتملة"
        //    بلا سجل History متّسق (لا يجب أن يحدث عملياً، لكن دفاعياً بدل قسمة على بيانات ناقصة)
        var avgCompletionSql = $@"
            SELECT AVG(CAST(DATEDIFF(DAY, c.CreatedAt, comp.CompletedAt) AS FLOAT)) AS AvgDays
            FROM dbo.ProstheticCases c
            CROSS APPLY (
                SELECT TOP 1 h.ChangedAt AS CompletedAt
                FROM dbo.ProstheticCaseHistory h
                WHERE h.CaseID = c.CaseID AND h.ActionType = 'StatusChanged' AND h.NewValue = 'Completed'
                ORDER BY h.ChangedAt DESC
            ) comp
            WHERE c.CaseStatus = 'Completed' AND c.CreatedAt >= @FromDate AND c.CreatedAt < @ToDate{caseFilter}";
        var avgCompletionResult = _db.ExecuteScalar(avgCompletionSql, CaseParams());
        summary.AverageCompletionDays = avgCompletionResult is null or DBNull ? null : Convert.ToDouble(avgCompletionResult);

        return summary;
    }

    // مقارنة كل المرممين جنباً إلى جنب ضمن نفس الفترة (Patch 11.2) - تُستدعى فقط عند اختيار "كل
    // المرممين" في الفلتر (راجع تعليق التبويب في ProstheticStatisticsWindow). كل عمود subquery
    // مستقل عمداً بدل JOIN واحد مُجمَّع - تفادياً لتضخُّم الصفوف (Fan-out) عند دمج
    // ProstheticSessions وProstheticPayments في نفس الاستعلام لكل حالة.
    public List<ProstheticComparisonRow> GetProsthetistComparison(DateTime fromDate, DateTime toDateExclusive)
    {
        const string sql = @"
            SELECT u.UserID AS ProsthetistUserID, u.FullName AS ProsthetistName,
                (SELECT COUNT(*) FROM dbo.ProstheticCases c
                 WHERE c.AssignedProsthetistUserID = u.UserID AND c.CreatedAt >= @FromDate AND c.CreatedAt < @ToDate) AS TotalCases,
                (SELECT COUNT(*) FROM dbo.ProstheticCases c
                 WHERE c.AssignedProsthetistUserID = u.UserID AND c.CreatedAt >= @FromDate AND c.CreatedAt < @ToDate AND c.CaseStatus = 'Completed') AS CompletedCases,
                (SELECT COUNT(*) FROM dbo.ProstheticCases c
                 WHERE c.AssignedProsthetistUserID = u.UserID AND c.CreatedAt >= @FromDate AND c.CreatedAt < @ToDate AND c.CaseStatus = 'Open') AS ActiveCases,
                (SELECT COUNT(*) FROM dbo.ProstheticSessions s INNER JOIN dbo.ProstheticCases c2 ON c2.CaseID = s.CaseID
                 WHERE c2.AssignedProsthetistUserID = u.UserID AND s.SessionDateTime >= @FromDate AND s.SessionDateTime < @ToDate) AS SessionCount,
                (SELECT ISNULL(SUM(c.TotalAgreedPrice), 0) FROM dbo.ProstheticCases c
                 WHERE c.AssignedProsthetistUserID = u.UserID AND c.CreatedAt >= @FromDate AND c.CreatedAt < @ToDate) AS TotalCaseValue,
                (SELECT ISNULL(SUM(pp.Amount), 0) FROM dbo.ProstheticPayments pp INNER JOIN dbo.ProstheticCases c3 ON c3.CaseID = pp.CaseID
                 WHERE c3.AssignedProsthetistUserID = u.UserID AND pp.PaymentDate >= @FromDate AND pp.PaymentDate < @ToDate) AS TotalPayments,
                (SELECT ISNULL(SUM(CASE WHEN (c4.TotalAgreedPrice - ISNULL(paid.Amount, 0)) > 0
                                        THEN (c4.TotalAgreedPrice - ISNULL(paid.Amount, 0)) ELSE 0 END), 0)
                 FROM dbo.ProstheticCases c4
                 OUTER APPLY (SELECT SUM(pp2.Amount) AS Amount FROM dbo.ProstheticPayments pp2 WHERE pp2.CaseID = c4.CaseID) paid
                 WHERE c4.AssignedProsthetistUserID = u.UserID AND c4.CreatedAt >= @FromDate AND c4.CreatedAt < @ToDate AND c4.CaseStatus <> 'Cancelled') AS TotalOutstanding,
                -- Patch 12: مصاريف هذا المرمم ضمن الفترة (ExpenseDate) - من ProstheticExpenses حصراً،
                -- subquery مستقل بنفس أسلوب باقي أعمدة هذا الاستعلام أعلاه
                (SELECT ISNULL(SUM(pe.Amount), 0) FROM dbo.ProstheticExpenses pe
                 WHERE pe.ProsthetistUserID = u.UserID AND pe.ExpenseDate >= @FromDate AND pe.ExpenseDate < @ToDate) AS TotalExpenses
            FROM dbo.Users u
            WHERE u.RoleID = (SELECT RoleID FROM dbo.Roles WHERE RoleName = N'Prosthetist')
            ORDER BY u.FullName";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@FromDate", fromDate), new SqlParameter("@ToDate", toDateExclusive));
        var result = new List<ProstheticComparisonRow>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticComparisonRow
            {
                ProsthetistUserID = Convert.ToInt32(row["ProsthetistUserID"]),
                ProsthetistName = row["ProsthetistName"].ToString()!,
                TotalCases = Convert.ToInt32(row["TotalCases"]),
                CompletedCases = Convert.ToInt32(row["CompletedCases"]),
                ActiveCases = Convert.ToInt32(row["ActiveCases"]),
                SessionCount = Convert.ToInt32(row["SessionCount"]),
                TotalCaseValue = Convert.ToDecimal(row["TotalCaseValue"]),
                TotalPayments = Convert.ToDecimal(row["TotalPayments"]),
                TotalOutstanding = Convert.ToDecimal(row["TotalOutstanding"]),
                TotalExpenses = Convert.ToDecimal(row["TotalExpenses"])
            });
        }
        return result;
    }

    // الحالات المفتوحة منذ أطول مدة (Patch 11.2) - عمداً بلا أي فلتر فترة/CreatedAt: حالة فُتحت
    // قبل أشهر تبقى "متأخرة" بصرف النظر عن فلتر الفترة المختار في بقية النافذة (فترة "اليوم" مثلاً
    // كانت ستُخفيها تماماً وتُفرغ هذا التبويب من فائدته). الفلتر الوحيد المطبَّق هنا هو المرمم -
    // إن حُدِّد. لا بيانات مريض في هذا الاستعلام إطلاقاً (نفس قرار عدم إظهار اسم/هاتف المريض في
    // الجداول التجميعية - راجع تعليق ProstheticOverdueCaseRow).
    public List<ProstheticOverdueCaseRow> GetOverdueCases(int? prosthetistUserId, int maxResults)
    {
        var filter = prosthetistUserId.HasValue ? " AND c.AssignedProsthetistUserID = @ProsthetistID" : "";
        var sql = $@"
            SELECT TOP (@MaxResults) c.CaseID, c.CaseNumber, ISNULL(u.FullName, N'-') AS ProsthetistName,
                   ISNULL(st.StageName, N'-') AS StageName, c.CreatedAt,
                   DATEDIFF(DAY, c.CreatedAt, GETDATE()) AS DaysOpen
            FROM dbo.ProstheticCases c
            LEFT JOIN dbo.Users u ON u.UserID = c.AssignedProsthetistUserID
            LEFT JOIN dbo.ProstheticStages st ON st.StageID = c.StageID
            WHERE c.CaseStatus = 'Open'{filter}
            ORDER BY DaysOpen DESC";

        var parameters = new List<SqlParameter> { new("@MaxResults", maxResults) };
        if (prosthetistUserId.HasValue) parameters.Add(new SqlParameter("@ProsthetistID", prosthetistUserId.Value));

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        var result = new List<ProstheticOverdueCaseRow>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticOverdueCaseRow
            {
                CaseID = Convert.ToInt32(row["CaseID"]),
                CaseNumber = Convert.ToInt32(row["CaseNumber"]),
                ProsthetistName = row["ProsthetistName"].ToString()!,
                StageName = row["StageName"].ToString()!,
                CreatedAt = Convert.ToDateTime(row["CreatedAt"]),
                DaysOpen = Convert.ToInt32(row["DaysOpen"])
            });
        }
        return result;
    }
}

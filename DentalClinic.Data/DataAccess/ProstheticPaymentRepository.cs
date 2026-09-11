using System.Data;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// طبقة وصول مالية الترميم - تستعلم فقط من ProstheticPayments/ProstheticCases، ولا تلمس
// Payments/MedicalSessions/ClinicExpenses (Finance العيادة) إطلاقًا في أي استعلام هنا.
// ⚠️ ملاحظة تصميم مهمة: الرصيد المستحق (Outstanding) هنا يُحسَب دائمًا حيًا كـ
// (TotalAgreedPrice - SUM(Payments)) ولا يُخزَّن كعمود منفصل قابل لعدم التزامن - هذا يتفادى
// بالكامل فئة الخطأ التي كانت موجودة سابقًا في Finance العيادة (حذف الدفعة يُنشئ دَينًا وهميًا)،
// لأنه لا يوجد أي قيمة "مخزَّنة" يمكن أن تبقى قديمة بعد حذف دفعة - حذف الصف كافٍ وحده لتصحيح كل شيء.
public class ProstheticPaymentRepository
{
    private readonly DatabaseHelper _db;

    public ProstheticPaymentRepository(DatabaseHelper db)
    {
        _db = db;
    }

    // Patch 15: يتطلب Prosthetics.ViewPayment (أو الطبيب) - نفس الصلاحية التي تحكم أصلاً ظهور
    // تبويب "المدفوعات" بالكامل في الواجهة (راجع _canViewPayment في ProstheticCaseEditWindow)،
    // منقولة الآن أيضاً لمصدر البيانات نفسه بدل الاعتماد فقط على أن الواجهة لن تستدعي هذه الدالة.
    public List<ProstheticPayment> GetByCase(int caseId, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.ViewPayment);

        const string sql = @"
            SELECT pp.ProstheticPaymentID, pp.CaseID, pp.Amount, pp.PaymentDate, pp.ReceivedByUserID,
                   u.FullName AS ReceivedByUserName, pp.Notes
            FROM dbo.ProstheticPayments pp
            LEFT JOIN dbo.Users u ON u.UserID = pp.ReceivedByUserID
            WHERE pp.CaseID = @CaseID
            ORDER BY pp.PaymentDate DESC";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@CaseID", caseId));
        var result = new List<ProstheticPayment>();
        foreach (DataRow row in table.Rows)
        {
            result.Add(new ProstheticPayment
            {
                ProstheticPaymentID = (int)row["ProstheticPaymentID"],
                CaseID = (int)row["CaseID"],
                Amount = Convert.ToDecimal(row["Amount"]),
                PaymentDate = (DateTime)row["PaymentDate"],
                ReceivedByUserID = (int)row["ReceivedByUserID"],
                ReceivedByUserName = row["ReceivedByUserName"] as string,
                Notes = row["Notes"] as string
            });
        }
        return result;
    }

    // Patch 14: كل دالة كتابة هنا تتحقق بذاتها من الصلاحية الدقيقة المطابقة (دفاع في العمق) -
    // AddPayment/EditPayment/DeletePayment صلاحيات منفصلة أصلًا، وليست ViewPayment أو EditCase
    public int AddPayment(ProstheticPayment payment, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.AddPayment);

        const string sql = @"
            INSERT INTO dbo.ProstheticPayments (CaseID, Amount, PaymentDate, ReceivedByUserID, Notes)
            VALUES (@CaseID, @Amount, @PaymentDate, @ReceivedByUserID, @Notes)";

        return _db.ExecuteInsertAndGetId(sql,
            new SqlParameter("@CaseID", payment.CaseID),
            new SqlParameter("@Amount", payment.Amount),
            new SqlParameter("@PaymentDate", payment.PaymentDate),
            new SqlParameter("@ReceivedByUserID", payment.ReceivedByUserID),
            new SqlParameter("@Notes", (object?)payment.Notes ?? DBNull.Value));
    }

    public void UpdatePayment(ProstheticPayment payment, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.EditPayment);

        const string sql = @"
            UPDATE dbo.ProstheticPayments
            SET Amount = @Amount, PaymentDate = @PaymentDate, Notes = @Notes
            WHERE ProstheticPaymentID = @ID";

        _db.ExecuteNonQuery(sql,
            new SqlParameter("@Amount", payment.Amount),
            new SqlParameter("@PaymentDate", payment.PaymentDate),
            new SqlParameter("@Notes", (object?)payment.Notes ?? DBNull.Value),
            new SqlParameter("@ID", payment.ProstheticPaymentID));
    }

    // حذف دفعة - كافٍ وحده لتحديث دخل اليوم/الشهر/السنة والرصيد المستحق تلقائيًا (محسوبة حيًا، راجع الملاحظة أعلاه)
    public void DeletePayment(int prostheticPaymentId, UserAccount actingUser)
    {
        ProstheticPermissionGuard.Ensure(actingUser, ProstheticPermissionKeys.DeletePayment);

        _db.ExecuteNonQuery(
            "DELETE FROM dbo.ProstheticPayments WHERE ProstheticPaymentID = @ID",
            new SqlParameter("@ID", prostheticPaymentId));
    }

    // ملخص مالي للترميم فقط (اليوم/الشهر/السنة + إجمالي المستحق على كل الحالات المفتوحة) -
    // يعادل بطاقات Finance العيادة لكن من جداول الترميم حصرًا. prosthetistUserId: فلترة اختيارية
    // (null = كل المرممين، للطبيب الرئيسي فقط).
    public ProstheticFinanceSummary GetFinanceSummary(int? prosthetistUserId)
    {
        var filter = prosthetistUserId.HasValue ? " AND c.AssignedProsthetistUserID = @ProsthetistID" : "";

        var sql = $@"
            SELECT
                ISNULL((SELECT SUM(pp.Amount) FROM dbo.ProstheticPayments pp
                        INNER JOIN dbo.ProstheticCases c ON c.CaseID = pp.CaseID
                        WHERE CAST(pp.PaymentDate AS DATE) = CAST(GETDATE() AS DATE){filter}), 0) AS TodayRevenue,
                ISNULL((SELECT SUM(pp.Amount) FROM dbo.ProstheticPayments pp
                        INNER JOIN dbo.ProstheticCases c ON c.CaseID = pp.CaseID
                        WHERE YEAR(pp.PaymentDate) = YEAR(GETDATE()) AND MONTH(pp.PaymentDate) = MONTH(GETDATE()){filter}), 0) AS MonthRevenue,
                ISNULL((SELECT SUM(pp.Amount) FROM dbo.ProstheticPayments pp
                        INNER JOIN dbo.ProstheticCases c ON c.CaseID = pp.CaseID
                        WHERE YEAR(pp.PaymentDate) = YEAR(GETDATE()){filter}), 0) AS YearRevenue,
                -- ⚠️ يُحسَب لكل حالة على حدة (بحد أدنى صفر) ثم يُجمَع - وليس فرق مجموعين خام،
                -- حتى لا تُلغي حالة مدفوعة زيادة نقصان حالة أخرى في المجموع النهائي (مضلِّل ماليًا)
                ISNULL((SELECT SUM(CASE WHEN (c.TotalAgreedPrice - ISNULL(paid.Amount, 0)) > 0
                                         THEN (c.TotalAgreedPrice - ISNULL(paid.Amount, 0)) ELSE 0 END)
                        FROM dbo.ProstheticCases c
                        OUTER APPLY (SELECT SUM(pp2.Amount) AS Amount FROM dbo.ProstheticPayments pp2 WHERE pp2.CaseID = c.CaseID) paid
                        WHERE c.CaseStatus <> 'Cancelled'{filter}), 0) AS TotalOutstandingBalance,
                ISNULL((SELECT SUM(CASE WHEN (ISNULL(paid.Amount, 0) - c.TotalAgreedPrice) > 0
                                         THEN (ISNULL(paid.Amount, 0) - c.TotalAgreedPrice) ELSE 0 END)
                        FROM dbo.ProstheticCases c
                        OUTER APPLY (SELECT SUM(pp2.Amount) AS Amount FROM dbo.ProstheticPayments pp2 WHERE pp2.CaseID = c.CaseID) paid
                        WHERE c.CaseStatus <> 'Cancelled'{filter}), 0) AS TotalCreditBalance";

        var parameters = new List<SqlParameter>();
        if (prosthetistUserId.HasValue)
            parameters.Add(new SqlParameter("@ProsthetistID", prosthetistUserId.Value));

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        var row = table.Rows[0];
        return new ProstheticFinanceSummary
        {
            TodayRevenue = Convert.ToDecimal(row["TodayRevenue"]),
            MonthRevenue = Convert.ToDecimal(row["MonthRevenue"]),
            YearRevenue = Convert.ToDecimal(row["YearRevenue"]),
            TotalOutstandingBalance = Convert.ToDecimal(row["TotalOutstandingBalance"]),
            TotalCreditBalance = Convert.ToDecimal(row["TotalCreditBalance"])
        };
    }

    // فحص سريع قبل تسجيل دفعة جديدة من الواجهة (تحذير وليس منعًا صارمًا - قد يكون تسبيقًا
    // مشروعًا أو سيُعدَّل السعر لاحقًا) - يُرجع المبلغ الذي سيتجاوز به السعر المتفق عليه، أو صفر
    // فحص سريع قبل تسجيل دفعة جديدة أو تعديل دفعة موجودة من الواجهة (تحذير وليس منعًا صارمًا -
    // قد يكون تسبيقًا مشروعًا أو سيُعدَّل السعر لاحقًا) - يُرجع المبلغ الذي سيتجاوز به السعر
    // المتفق عليه، أو صفر. excludePaymentId: عند تعديل دفعة موجودة، مرِّر مُعرِّفها هنا لاستبعاد
    // قيمتها القديمة من "المدفوع حالياً" قبل إضافة القيمة الجديدة - وإلا تُحتسَب الدفعة نفسها
    // مرتين (مرة ضمن المجموع القديم المخزَّن، ومرة كـ newPaymentAmount الجديد)، فيُصبح الفحص
    // خاطئاً دائماً في وضع التعديل.
    public decimal GetProjectedOverpayment(int caseId, decimal newPaymentAmount, int? excludePaymentId = null)
    {
        var sql = @"
            SELECT c.TotalAgreedPrice, ISNULL(SUM(pp.Amount), 0) AS TotalPaid
            FROM dbo.ProstheticCases c
            LEFT JOIN dbo.ProstheticPayments pp ON pp.CaseID = c.CaseID"
            + (excludePaymentId.HasValue ? " AND pp.ProstheticPaymentID <> @ExcludePaymentID" : "") + @"
            WHERE c.CaseID = @CaseID
            GROUP BY c.TotalAgreedPrice";

        var parameters = new List<SqlParameter> { new SqlParameter("@CaseID", caseId) };
        if (excludePaymentId.HasValue) parameters.Add(new SqlParameter("@ExcludePaymentID", excludePaymentId.Value));

        var table = _db.ExecuteQuery(sql, parameters.ToArray());
        if (table.Rows.Count == 0) return 0;

        var agreedPrice = Convert.ToDecimal(table.Rows[0]["TotalAgreedPrice"]);
        var totalPaid = Convert.ToDecimal(table.Rows[0]["TotalPaid"]);
        var projected = totalPaid + newPaymentAmount - agreedPrice;
        return projected > 0 ? projected : 0;
    }
}

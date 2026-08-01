using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using DentalClinic.Data.Models;

namespace DentalClinic.Data.DataAccess;

// الخدمة المسؤولة عن نظام "تقسيم إيرادات الأطباء": عندما يُحصَّل طبيب غير رئيسي دفعة، يبقى المبلغ
// كاملاً (100%) في إجمالي واردات العيادة كما هو، لكن يُنشأ تلقائياً مصروف بنسبة عمولته (افتراضياً
// 50%، قابلة للتعديل) يُخصَم من صافي الربح دون أي تدخل يدوي من الطبيب الرئيسي.
public class DoctorCommissionService
{
    private readonly DatabaseHelper _db;
    private readonly UserRepository _userRepo;
    private readonly ClinicSettingsRepository _settingsRepo;
    private readonly FinancialRepository _financialRepo;

    public DoctorCommissionService(DatabaseHelper db)
    {
        _db = db;
        _userRepo = new UserRepository(db);
        _settingsRepo = new ClinicSettingsRepository(db);
        _financialRepo = new FinancialRepository(db);
    }

    // النسبة الفعلية المطبَّقة على طبيب معيَّن: 0 إن كان الطبيب الرئيسي، وإلا نسبته المخصَّصة
    // إن وُجدت، وإلا النسبة العامة الافتراضية من الإعدادات
    public decimal GetEffectiveCommissionPercent(int doctorUserId)
    {
        var primaryId = GetPrimaryDoctorUserId();
        if (primaryId.HasValue && primaryId.Value == doctorUserId)
        {
            return 0m;
        }

        var doctor = _userRepo.GetAllDoctors().FirstOrDefault(d => d.UserID == doctorUserId);
        if (doctor?.CommissionPercent is decimal custom)
        {
            return custom;
        }

        return _settingsRepo.GetDefaultDoctorCommissionPercent();
    }

    // نُستدعى مباشرة بعد كل عملية تحصيل دفعة فعلية. إن كان الطبيب صاحب الجلسة ليس الطبيب الرئيسي،
    // تُنشأ تلقائياً حصة عمولته كمصروف عيادة يُخصَم من صافي الربح - دون أي إدخال يدوي.
    public void RecordCommissionForPayment(int sessionId, int paymentId, decimal paidAmount)
    {
        if (paidAmount <= 0) return;

        const string sql = @"
            SELECT s.DoctorID, u.FullName
            FROM MedicalSessions s
            INNER JOIN Users u ON u.UserID = s.DoctorID
            WHERE s.SessionID = @SessionID";

        var table = _db.ExecuteQuery(sql, new SqlParameter("@SessionID", sessionId));
        if (table.Rows.Count == 0) return;

        var doctorId = (int)table.Rows[0]["DoctorID"];
        var doctorName = table.Rows[0]["FullName"].ToString()!;

        var primaryId = GetPrimaryDoctorUserId();
        if (primaryId.HasValue && primaryId.Value == doctorId)
        {
            // الطبيب الرئيسي يأخذ 100% من الدفعة - لا عمولة تُخصَم
            return;
        }

        var percent = GetEffectiveCommissionPercent(doctorId);
        if (percent <= 0) return;

        var commissionAmount = Math.Round(paidAmount * percent / 100m, 2);
        if (commissionAmount <= 0) return;

        var description = $"Doctor commission - Dr. {doctorName} ({percent:0.##}% of {paidAmount:N2}) - Session #{sessionId}";
        _financialRepo.AddDoctorCommissionExpense(commissionAmount, description, sessionId, paymentId, doctorId);
    }

    // إحصائيات كل طبيب جاهزة للعرض مباشرة (تدمج بيانات النشاط مع نظام العمولة)
    public List<DoctorStatRow> GetDoctorStatisticsWithCommission()
    {
        var stats = _financialRepo.GetDoctorStatistics();
        var primaryId = GetPrimaryDoctorUserId();

        foreach (var stat in stats)
        {
            stat.IsPrimary = primaryId.HasValue && primaryId.Value == stat.DoctorUserId;
            stat.CommissionPercent = stat.IsPrimary ? 0m : GetEffectiveCommissionPercent(stat.DoctorUserId);
            stat.DoctorShare = stat.IsPrimary ? 0m : Math.Round(stat.GrossIncome * stat.CommissionPercent / 100m, 2);
        }

        return stats.OrderByDescending(s => s.GrossIncome).ToList();
    }

    // "الطبيب الرئيسي" = من عُيِّن صراحة من شاشة الإعدادات إن وُجد تعيين، وإلا أول طبيب مسجَّل في النظام
    // (المرجع القديم) - هذا يمنع اعتبار حساب "admin" الأول تلقائياً كطبيب رئيسي إن لم يكن هو المقصود فعلاً
    public int? GetPrimaryDoctorUserId()
    {
        var explicitId = _settingsRepo.GetExplicitPrimaryDoctorId();
        if (explicitId.HasValue)
        {
            // نتأكد أن الحساب المعيَّن ما زال طبيباً نشطاً - وإلا نتجاهله ونعود للقاعدة التلقائية
            var stillValidDoctor = _userRepo.GetAllDoctors().Any(d => d.UserID == explicitId.Value);
            if (stillValidDoctor) return explicitId;
        }

        return _userRepo.GetPrimaryDoctorUserId();
    }

    // يُستخدَم من شاشة إعدادات العمولات لتعيين/إلغاء تعيين الطبيب الرئيسي يدوياً
    public void SetPrimaryDoctor(int? doctorUserId) => _settingsRepo.SetExplicitPrimaryDoctorId(doctorUserId);
    public List<UserAccount> GetAllDoctors() => _userRepo.GetAllDoctors();
    public decimal GetDefaultCommissionPercent() => _settingsRepo.GetDefaultDoctorCommissionPercent();
    public void SetDefaultCommissionPercent(decimal percent) => _settingsRepo.SetDefaultDoctorCommissionPercent(percent);
    public void SetDoctorCommissionPercent(int doctorUserId, decimal? percent) => _userRepo.UpdateDoctorCommissionPercent(doctorUserId, percent);
}

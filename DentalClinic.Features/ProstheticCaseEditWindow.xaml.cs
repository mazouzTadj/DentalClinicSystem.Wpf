using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// صف عرض واحد لجلسة ترميم في تبويب "الجلسات"
public class ProstheticSessionRowViewModel
{
    public ProstheticSession Session { get; }
    public string DateText => Session.SessionDateTime.ToString("yyyy-MM-dd");
    public string PerformedByText => Session.PerformedByUserName ?? "-";
    public string DescriptionText => string.IsNullOrWhiteSpace(Session.Description) ? "-" : Session.Description;

    public ProstheticSessionRowViewModel(ProstheticSession session) => Session = session;
}

// صف عرض واحد لدفعة ترميم في تبويب "المدفوعات"
public class ProstheticPaymentRowViewModel
{
    public ProstheticPayment Payment { get; }
    public string DateText => Payment.PaymentDate.ToString("yyyy-MM-dd");
    public string AmountText => Payment.Amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    public string ReceivedByText => Payment.ReceivedByUserName ?? "-";
    public string NotesText => string.IsNullOrWhiteSpace(Payment.Notes) ? "-" : Payment.Notes;

    public ProstheticPaymentRowViewModel(ProstheticPayment payment) => Payment = payment;
}

// صف عرض واحد لجلسة سريرية في تبويب "الملخص السريري" (Patch 9) - للقراءة فقط. كل حقل من الثلاثة
// (تشخيص/علاج/دواء) يُقرَّر نصه هنا وقت الإنشاء حسب صلاحية العارض تحديداً، بحيث لا يُخزَّن أو يُمرَّر
// النص الحقيقي المخفي إلى الواجهة إطلاقاً حتى لو حاول أحد قراءته عبر أدوات فحص الواجهة - القيمة
// المخفية غير موجودة أصلاً في هذا الكائن، وليست فقط "مخفية بصرياً" فوق نص حقيقي.
public class ProstheticClinicalSummaryRowViewModel
{
    public string HeaderText { get; }
    public string DiagnosisText { get; }
    public string TreatmentText { get; }
    public string MedicationText { get; }

    public ProstheticClinicalSummaryRowViewModel(
        ProstheticClinicalSummaryEntry entry, bool canViewDiagnosis, bool canViewTreatment, bool canViewMedications)
    {
        HeaderText = $"{entry.SessionDateTime:yyyy-MM-dd} - {entry.DoctorName}";
        var restricted = LocalizationManager.T("ProsthCase_ClinicalFieldRestricted");

        DiagnosisText = canViewDiagnosis
            ? (string.IsNullOrWhiteSpace(entry.Diagnosis) ? "-" : entry.Diagnosis)
            : restricted;
        TreatmentText = canViewTreatment
            ? (string.IsNullOrWhiteSpace(entry.TreatmentPerformed) ? "-" : entry.TreatmentPerformed)
            : restricted;
        MedicationText = canViewMedications
            ? (string.IsNullOrWhiteSpace(entry.Medication) ? "-" : entry.Medication)
            : restricted;
    }
}

// صف عرض واحد لحركة واحدة من سجل تدقيق الحالة في تبويب "History" (Patch 10) - للقراءة فقط بالكامل،
// لا يوجد أي زر تعديل/حذف على هذا التبويب إطلاقاً (السجل نفسه Append-Only في قاعدة البيانات أصلاً -
// راجع ProstheticCaseRepository.InsertHistory، لا توجد فيه أي دالة Update أو Delete).
// يُترجم ActionType الداخلي (PriceChanged, StageChanged, ...) إلى تسمية حقل مفهومة + قيمتين قبل/بعد
// بصيغة عرض جاهزة، مع إبقاء القيمة الخام كما هي دائماً في قاعدة البيانات (هذا الكائن للعرض فقط،
// لا يُعاد كتابة أي شيء بناءً عليه).
public class ProstheticHistoryRowViewModel
{
    public string DateText { get; }
    public string UserText { get; }
    public string FieldLabelText { get; }
    // نص واحد جاهز للعرض: "قديم ← جديد" لمعظم الحركات، أو جملة عامة واحدة لحركة "إنشاء الحالة"
    // التي لا قبل/بعد فعلي لها - تبسيط متعمَّد لتفادي أي Converter جديد في XAML (لا يوجد أي
    // IValueConverter في المشروع حالياً، فلا داعي لإدخال نمط جديد لحالة واحدة بسيطة).
    public string DetailText { get; }

    public ProstheticHistoryRowViewModel(
        ProstheticCaseHistoryEntry entry, IReadOnlyDictionary<int, string> workTypeNamesById)
    {
        DateText = entry.ChangedAt.ToString("yyyy-MM-dd HH:mm");
        UserText = entry.ChangedByUserName ?? "-";

        string FormatWorkType(string? raw) =>
            raw == null ? LocalizationManager.T("ProsthCase_NoWorkType")
            : int.TryParse(raw, out var id) && workTypeNamesById.TryGetValue(id, out var name) ? name
            : raw; // نوع عمل حُذف نهائياً من القوائم ولم يعد قابلاً للحل - نعرض قيمته الخام بدل إخفائها

        string FormatArch(string? raw) => raw switch
        {
            "Upper+Lower" => LocalizationManager.T("ProsthCase_ArchBoth"),
            "Upper" => LocalizationManager.T("ProsthCase_Upper"),
            "Lower" => LocalizationManager.T("ProsthCase_Lower"),
            _ => "-"
        };

        string FormatStatus(string? raw) => raw switch
        {
            ProstheticCaseStatus.Completed => LocalizationManager.T("ProsthCase_StatusCompleted"),
            ProstheticCaseStatus.Cancelled => LocalizationManager.T("ProsthCase_StatusCancelled"),
            ProstheticCaseStatus.Open => LocalizationManager.T("ProsthCase_StatusOpen"),
            _ => raw ?? "-"
        };

        string FormatPrice(string? raw) =>
            decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)
                : (raw ?? "-");

        string FormatPlain(string? raw) => string.IsNullOrWhiteSpace(raw) ? "-" : raw;

        string ChangeText(string oldText, string newText) => $"{oldText}  \u2192  {newText}";

        switch (entry.ActionType)
        {
            case "Created":
                FieldLabelText = LocalizationManager.T("ProsthHistory_ActionCreated");
                DetailText = LocalizationManager.T("ProsthHistory_ActionCreatedDetail");
                break;
            case "ProsthetistReassigned":
                FieldLabelText = LocalizationManager.T("ProsthHistory_FieldProsthetist");
                DetailText = ChangeText(FormatPlain(entry.OldValue), FormatPlain(entry.NewValue));
                break;
            case "StageChanged":
                FieldLabelText = LocalizationManager.T("ProsthHistory_FieldStage");
                DetailText = ChangeText(FormatPlain(entry.OldValue), FormatPlain(entry.NewValue));
                break;
            case "StatusChanged":
                FieldLabelText = LocalizationManager.T("ProsthHistory_FieldStatus");
                DetailText = ChangeText(FormatStatus(entry.OldValue), FormatStatus(entry.NewValue));
                break;
            case "WorkTypeChanged":
                FieldLabelText = LocalizationManager.T("ProsthHistory_FieldWorkType");
                DetailText = ChangeText(FormatWorkType(entry.OldValue), FormatWorkType(entry.NewValue));
                break;
            case "ArchChanged":
                FieldLabelText = LocalizationManager.T("ProsthHistory_FieldArch");
                DetailText = ChangeText(FormatArch(entry.OldValue), FormatArch(entry.NewValue));
                break;
            case "ToothScopeChanged":
                FieldLabelText = LocalizationManager.T("ProsthHistory_FieldToothScope");
                DetailText = ChangeText(FormatPlain(entry.OldValue), FormatPlain(entry.NewValue));
                break;
            case "PriceChanged":
                FieldLabelText = LocalizationManager.T("ProsthHistory_FieldPrice");
                DetailText = ChangeText(FormatPrice(entry.OldValue), FormatPrice(entry.NewValue));
                break;
            case "NotesChanged":
                FieldLabelText = LocalizationManager.T("ProsthHistory_FieldNotes");
                DetailText = ChangeText(FormatPlain(entry.OldValue), FormatPlain(entry.NewValue));
                break;
            default:
                // احتياط لأي ActionType مستقبلي لم يُضَف له تنسيق بعد - نعرض القيم الخام بدل إخفاء السطر بصمت
                FieldLabelText = entry.ActionType;
                DetailText = ChangeText(FormatPlain(entry.OldValue), FormatPlain(entry.NewValue));
                break;
        }
    }
}

// نافذة إنشاء/تعديل حالة ترميم - وضعان:
// - الإنشاء: باني (patientId, currentUser) - لا تُنشئ Patient جديداً أبداً، ترتبط بمريض موجود.
//   CaseNumber يُولَّد تلقائياً من القاعدة (SQL SEQUENCE) - لا حقل يدوي له عمداً.
// - التعديل: باني (existingCase, currentUser) - تُفتَح بالنقر المزدوج من ProstheticCaseWindow.
//   تعرض معلومات القراءة فقط (المريض، رقم الحالة، الحالة العامة) + كل الحقول القابلة للتعديل
//   (المرمم، نوع العمل، الفك، المرحلة، السعر، الملاحظات) معبَّأة بالقيم الحالية.
public partial class ProstheticCaseEditWindow : Window
{
    // مجموعة أرقام الأسنان الصحيحة بترقيم FDI (32 سناً) - تُستخدم فقط لتفسير القيم القديمة المخزَّنة
    // كنص حر في ToothScope (قبل تحويل الحقل إلى مخطط أسنان تفاعلي) وتحديدها تلقائياً عند فتح حالة قديمة.
    private static readonly HashSet<string> ValidToothNumbers = new(new[]
    {
        "18","17","16","15","14","13","12","11","21","22","23","24","25","26","27","28",
        "48","47","46","45","44","43","42","41","31","32","33","34","35","36","37","38"
    });

    // يقرأ القيمة القديمة النصية لـToothScope (كانت حقل وصف حر) ويستخرج منها أي أرقام أسنان صحيحة
    // ليُعاد تحديدها تلقائياً في مخطط الأسنان - أي نص آخر غير رقم سن صحيح يُتجاهَل بصمت.
    private static List<string> ParseToothNumbers(string? raw)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        var parts = raw.Split(new[] { ',', ';', '/', '\\', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (ValidToothNumbers.Contains(trimmed) && !result.Contains(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    private readonly int _patientId;
    private readonly UserAccount _currentUser;
    private readonly ProstheticCase? _existingCase; // null = وضع الإنشاء
    private readonly ProstheticCaseRepository _caseRepo;
    private readonly ProstheticLookupRepository _lookupRepo;
    private readonly UserRepository _userRepo;
    private readonly ProstheticSessionRepository _sessionRepo;
    private readonly ProstheticPaymentRepository _paymentRepo;
    private readonly ProstheticClinicalRepository _clinicalRepo;

    // ⚠️ هذه النافذة مشتركة بين تطبيق الطبيب (دائماً صلاحية كاملة، لم يتغيَّر شيء بالنسبة له) وتطبيق
    // المرمم (منذ الباتش 3) حيث تُطبَّق الصلاحيات الدقيقة فعلياً - لا يكفي إخفاء/تعطيل الحقول في
    // الواجهة وحده (راجع بند الأمان في المتطلبات)، لذلك تُفحَص هذه الحقول مجدداً في SaveButton_Click
    // قبل استدعاء أي عملية كتابة فعلية بالـRepository.
    private readonly bool _canEditCase;
    private readonly bool _canTransferCase;
    // Patch 12: صلاحية حذف الحالة نهائياً - جديدة كلياً (كانت Prosthetics.DeleteCase معرَّفة في
    // القاعدة منذ Patch 1.1 بلا أي أثر فعلي في أي شاشة، راجع ProstheticCaseRepository.PermanentlyDelete)
    private readonly bool _canDeleteCase;
    private readonly bool _canViewStage;
    private readonly bool _canEditStage;
    private readonly bool _canViewPatient;
    private readonly bool _canAddSession;
    private readonly bool _canEditSession;
    private readonly bool _canDeleteSession;
    private readonly bool _canViewPayment;
    private readonly bool _canAddPayment;
    private readonly bool _canEditPayment;
    private readonly bool _canDeletePayment;
    // Patch 9: كل صلاحية مستقلة تماماً عن الأخرى - قد يملك المرمم ViewTreatment بلا ViewDiagnosis
    // مثلاً، فيظهر حقل العلاج بقيمته الفعلية وحقل التشخيص بـ"مقيَّد" في نفس بطاقة الجلسة بالضبط
    private readonly bool _canViewTreatment;
    private readonly bool _canViewDiagnosis;
    private readonly bool _canViewMedications;
    private bool CanViewAnyClinicalField => _canViewTreatment || _canViewDiagnosis || _canViewMedications;
    // Patch 10: لا صلاحية "ViewHistory" مستقلة في الـ21 صلاحية عمداً (تعليمات صريحة بعدم إضافة
    // صلاحية جديدة إن أمكن الربط بصلاحية موجودة) - نربط رؤية تبويب History بأكمله بـEditCase، لأن
    // أغلب حركاته (WorkTypeChanged/ArchChanged/ToothScopeChanged/PriceChanged/NotesChanged) هي
    // بالضبط نفس الحقول التي تحكمها هذه الصلاحية أصلاً في تبويب "معلومات الحالة". حركة StageChanged
    // تحديداً تُصفَّى بشكل إضافي ومستقل بصلاحية _canViewStage عند التحميل (راجع LoadHistory) - فلا
    // معنى لإظهار "تغيّرت المرحلة من كذا لكذا" لمن لا يملك أصلاً حق رؤية المرحلة الحالية.
    private bool CanViewHistory => _canEditCase;

    // يُستخدَم لتعبئة حقل السعر تلقائياً عند اختيار نوع عمل - نفس فكرة تعبئة سعر العلاج تلقائياً
    // في ملف المريض (PatientFileWindow.RecomputeTotalPriceFromTreatments)، لكن هنا اختيار مفرد
    // (ComboBox واحد) بدل مجموع علاجات متعددة، فنملأ السعر مباشرة بسعر النوع المختار بدل جمع.
    private Dictionary<int, decimal> _workTypePrices = new();
    // true فقط أثناء تعبئة WorkTypeBox برمجياً في LoadLookups (تحميل أولي/استعادة القيمة الحالية
    // لحالة قيد التعديل) - نمنع التعبئة التلقائية للسعر أثناء ذلك حتى لا نستبدل بصمت السعر المتفَق
    // عليه فعلياً والمحفوظ مسبقاً في الحالة بسعر نوع العمل الافتراضي الحالي.
    private bool _isLoadingLookups;

    // الجلسات/المدفوعات تُحفَظ فوراً عند كل عملية (وليست جزءاً من "حفظ" النافذة الرئيسي) - هذه
    // العلامة تجعل النافذة تُرجِع DialogResult=true عند الإغلاق إن حدث أي تغيير كهذا، حتى يُحدِّث
    // المستدعي (ProstheticCaseWindow/MainWindow) قائمته حتى لو لم يضغط المستخدم "حفظ" الرئيسي إطلاقاً
    private bool _sessionsOrPaymentsChanged;

    private bool IsEditMode => _existingCase != null;

    public ObservableCollection<ProstheticSessionRowViewModel> SessionRows { get; } = new();
    public ObservableCollection<ProstheticPaymentRowViewModel> PaymentRows { get; } = new();
    public ObservableCollection<ProstheticClinicalSummaryRowViewModel> ClinicalSummaryRows { get; } = new();
    public ObservableCollection<ProstheticHistoryRowViewModel> HistoryRows { get; } = new();

    public bool CaseCreated { get; private set; }

    // الطبيب دائماً بصلاحية كاملة (كما كان الوضع قبل الباتش 3)؛ المرمم يُحدَّد حسب صلاحياته الدقيقة تحديداً
    private bool HasEditRight(string permissionKey) =>
        _currentUser.Role == UserRole.Doctor || _currentUser.HasProstheticPermission(permissionKey);

    // وضع الإنشاء
    public ProstheticCaseEditWindow(int patientId, UserAccount currentUser)
    {
        _patientId = patientId;
        _existingCase = null;
        _currentUser = currentUser;
        InitializeComponent();

        // الإنشاء متاح حالياً فقط عبر PatientFileWindow (الطبيب)، فالصلاحيات هنا دائماً كاملة
        _canEditCase = true;
        _canTransferCase = true;
        _canViewStage = true;
        _canEditStage = true;
        _canViewPatient = true;
        _canViewTreatment = true;
        _canViewDiagnosis = true;
        _canViewMedications = true;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _caseRepo = new ProstheticCaseRepository(db);
        _lookupRepo = new ProstheticLookupRepository(db);
        _userRepo = new UserRepository(db);
        _sessionRepo = new ProstheticSessionRepository(db);
        _paymentRepo = new ProstheticPaymentRepository(db);
        _clinicalRepo = new ProstheticClinicalRepository(db);

        TitleText.Text = LocalizationManager.T("ProsthCase_NewTitle");
        ReadOnlyInfoSection.Visibility = Visibility.Collapsed;

        // لا معنى لتبويبَي الجلسات/المدفوعات/الملخص السريري/History قبل وجود حالة فعلية بـCaseID (وضع الإنشاء فقط)
        SessionsTabItem.Visibility = Visibility.Collapsed;
        PaymentsTabItem.Visibility = Visibility.Collapsed;
        ClinicalSummaryTabItem.Visibility = Visibility.Collapsed;
        HistoryTabItem.Visibility = Visibility.Collapsed;
        // لا معنى لزر "حذف الحالة" أيضاً - لا توجد حالة بعد لحذفها في وضع الإنشاء
        DeleteCaseButton.Visibility = Visibility.Collapsed;

        Loaded += (s, e) => LoadLookups();
    }

    // وضع التعديل
    public ProstheticCaseEditWindow(ProstheticCase existingCase, UserAccount currentUser)
    {
        _patientId = existingCase.PatientID;
        _existingCase = existingCase;
        _currentUser = currentUser;
        InitializeComponent();

        _canEditCase = HasEditRight(ProstheticPermissionKeys.EditCase);
        _canTransferCase = HasEditRight(ProstheticPermissionKeys.TransferCase);
        _canDeleteCase = HasEditRight(ProstheticPermissionKeys.DeleteCase);
        // تعديل المرحلة يستلزم منطقياً القدرة على رؤيتها، حتى لو مُنح EditStage وحدها دون ViewStage
        // بالخطأ من الواجهة الإدارية - الاثنان صلاحيتان ذريّتان منفصلتان عمداً في التصميم، لكن هذا
        // الاستثناء الوحيد المعقول لتفادي زر "تعديل" على حقل مخفي لا معنى له
        _canEditStage = HasEditRight(ProstheticPermissionKeys.EditStage);
        _canViewStage = _canEditStage || HasEditRight(ProstheticPermissionKeys.ViewStage);
        // Prosthetics.ViewPatient (Patch 8): يتحكم فعلياً - وليس فقط بالاسم في نظرياً - بإظهار
        // هوية المريض (الاسم/الهاتف) داخل نافذة تفاصيل الحالة نفسها. هذا امتداد طبيعي لقرار إخفاء
        // اسم المريض من جدول قائمة الحالات (راجع النقطة 7 في وثيقة التصميم) - كان الإخفاء هناك
        // مقصوداً، لكن فتح الحالة كان يُظهر الاسم دائماً بلا أي فحص صلاحية حتى الآن.
        _canViewPatient = HasEditRight(ProstheticPermissionKeys.ViewPatient);
        _canAddSession = HasEditRight(ProstheticPermissionKeys.AddSession);
        _canEditSession = HasEditRight(ProstheticPermissionKeys.EditSession);
        _canDeleteSession = HasEditRight(ProstheticPermissionKeys.DeleteSession);
        _canViewPayment = HasEditRight(ProstheticPermissionKeys.ViewPayment);
        _canAddPayment = HasEditRight(ProstheticPermissionKeys.AddPayment);
        _canEditPayment = HasEditRight(ProstheticPermissionKeys.EditPayment);
        _canDeletePayment = HasEditRight(ProstheticPermissionKeys.DeletePayment);
        // Patch 9: تفعيل فعلي - كانت هذه الصلاحيات الثلاث موجودة في القاعدة منذ البداية بلا أي أثر
        _canViewTreatment = HasEditRight(ProstheticPermissionKeys.ViewTreatment);
        _canViewDiagnosis = HasEditRight(ProstheticPermissionKeys.ViewDiagnosis);
        _canViewMedications = HasEditRight(ProstheticPermissionKeys.ViewMedications);

        WorkTypeBox.IsEnabled = _canEditCase;
        IncludesUpperCheck.IsEnabled = _canEditCase;
        IncludesLowerCheck.IsEnabled = _canEditCase;
        ToothScopeOdontogram.IsEnabled = _canEditCase;
        TotalPriceBox.IsEnabled = _canEditCase;
        NotesBox.IsEnabled = _canEditCase;
        ProsthetistBox.IsEnabled = _canTransferCase;
        // Patch 12: زر الحذف النهائي - مقيَّد بصلاحيته الخاصة تماماً كبقية أزرار الحذف (الجلسات/المدفوعات)
        DeleteCaseButton.Visibility = _canDeleteCase ? Visibility.Visible : Visibility.Collapsed;

        // Prosthetics.ViewStage/EditStage (Patch 8): سابقاً كان حقل المرحلة يظهر دائماً وفقط
        // "التعديل" هو المقيَّد؛ أصبح الآن الحقل بأكمله (تسمية + قائمة) يُخفى تماماً عن مرمم لا
        // يملك حتى حق الرؤية، بدل قائمة معطَّلة تكشف قيمة المرحلة الحالية بلا داعٍ
        StageFieldPanel.Visibility = _canViewStage ? Visibility.Visible : Visibility.Collapsed;
        StageBox.IsEnabled = _canEditStage;

        // تبويب "الجلسات" مرئي دائماً في وضع التعديل (رؤية السجل متاحة لأي فاتح للنافذة)؛
        // الإضافة/التعديل/الحذف هي المُقيَّدة فعلياً بالصلاحيات الثلاث أعلاه
        AddSessionButton.Visibility = _canAddSession ? Visibility.Visible : Visibility.Collapsed;

        // تبويب "المدفوعات" مقيَّد بالكامل بصلاحية ViewPayment (بيانات مالية أكثر حساسية من الجلسات)
        PaymentsTabItem.Visibility = _canViewPayment ? Visibility.Visible : Visibility.Collapsed;
        AddPaymentButton.Visibility = _canAddPayment ? Visibility.Visible : Visibility.Collapsed;

        // تبويب "الملخص السريري" (Patch 9) - يُخفى بالكامل إن لم يملك المستخدم ولو صلاحية واحدة من
        // الثلاث (لا فائدة من تبويب فارغ تماماً من "مقيَّد" في كل حقل). عند ظهوره، كل حقل داخله
        // مستقل تماماً بصلاحيته الخاصة - راجع ProstheticClinicalSummaryRowViewModel
        ClinicalSummaryTabItem.Visibility = CanViewAnyClinicalField ? Visibility.Visible : Visibility.Collapsed;

        // تبويب "History" (Patch 10) - مرتبط بـEditCase (راجع تعليق CanViewHistory أعلاه)؛ للقراءة
        // فقط بالكامل ولا يظهر فيه أي زر تعديل/حذف إطلاقاً
        HistoryTabItem.Visibility = CanViewHistory ? Visibility.Visible : Visibility.Collapsed;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _caseRepo = new ProstheticCaseRepository(db);
        _lookupRepo = new ProstheticLookupRepository(db);
        _userRepo = new UserRepository(db);
        _sessionRepo = new ProstheticSessionRepository(db);
        _paymentRepo = new ProstheticPaymentRepository(db);
        _clinicalRepo = new ProstheticClinicalRepository(db);

        SessionsGrid.ItemsSource = SessionRows;
        PaymentsGrid.ItemsSource = PaymentRows;
        ClinicalSummaryList.ItemsSource = ClinicalSummaryRows;
        HistoryList.ItemsSource = HistoryRows;

        TitleText.Text = LocalizationManager.T("ProsthCase_EditTitleFormat", existingCase.CaseNumber);
        ReadOnlyInfoSection.Visibility = Visibility.Visible;
        if (_canViewPatient)
        {
            PatientNameText.Text = existingCase.PatientFullName;
            PatientPhoneText.Text = string.IsNullOrWhiteSpace(existingCase.PatientPhoneNumber) ? "-" : existingCase.PatientPhoneNumber;
        }
        else
        {
            // بلا Prosthetics.ViewPatient: نُبقي القسم ظاهراً (رقم الحالة والحالة العامة معلومات
            // تشغيلية غير حساسة، لا داعي لإخفائها)، لكن نستبدل هوية المريض بنص واضح يشرح السبب،
            // بدل حقل فارغ يبدو كخطأ أو نقص بيانات
            PatientNameText.Text = LocalizationManager.T("ProsthCase_PatientInfoRestricted");
            PatientPhoneText.Text = LocalizationManager.T("ProsthCase_PatientInfoRestricted");
        }
        CaseNumberText.Text = existingCase.CaseNumber.ToString();
        StatusText.Text = LocalizationManager.T(existingCase.CaseStatus switch
        {
            ProstheticCaseStatus.Completed => "ProsthCase_StatusCompleted",
            ProstheticCaseStatus.Cancelled => "ProsthCase_StatusCancelled",
            _ => "ProsthCase_StatusOpen"
        });

        ToothScopeOdontogram.SetSelectedTeeth(ParseToothNumbers(existingCase.ToothScope));
        IncludesUpperCheck.IsChecked = existingCase.IncludesUpper;
        IncludesLowerCheck.IsChecked = existingCase.IncludesLower;
        TotalPriceBox.Text = existingCase.TotalAgreedPrice.ToString(System.Globalization.CultureInfo.InvariantCulture);
        NotesBox.Text = existingCase.Notes ?? string.Empty;
        RefreshFinanceSummary(existingCase.TotalAgreedPrice, existingCase.TotalPaid);

        Loaded += (s, e) =>
        {
            LoadLookups();
            LoadSessions();
            if (_canViewPayment) LoadPayments();
            if (CanViewAnyClinicalField) LoadClinicalSummary();
            if (CanViewHistory) LoadHistory();
        };
    }

    private void LoadLookups()
    {
        try
        {
            var prosthetists = _userRepo.GetAllProsthetists();
            ProsthetistBox.Items.Clear();
            foreach (var p in prosthetists)
            {
                var item = new ComboBoxItem { Content = p.FullName, Tag = p.UserID };
                ProsthetistBox.Items.Add(item);
                if (IsEditMode && _existingCase!.AssignedProsthetistUserID == p.UserID)
                    item.IsSelected = true;
            }
            NoProsthetistsHintText.Visibility = prosthetists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SaveButton.IsEnabled = prosthetists.Count > 0;

            // Patch 13: نجلب الكل (نشط وغير نشط) بدل activeOnly الافتراضي، لأن الحالة الحالية قيد
            // التعديل قد تستخدم فعلياً نوع عمل عُطِّل لاحقاً - سابقاً كانت هذه الحالات "تُفقَد" من
            // القائمة فيظهر ComboBox وكأن لا نوع عمل مُختار إطلاقاً رغم أن الحالة تحمل قيمة صالحة
            // في القاعدة (خلل حقيقي اكتُشف عند اختبار "تعطيل نوع عمل مستخدم في حالة قديمة").
            // القاعدة: نعرض العنصر إن كان نشطاً، أو كان هو تحديداً القيمة الحالية المحفوظة لهذه
            // الحالة (Grandfathering) - أي عنصر آخر غير نشط يبقى مخفياً تماماً عن حالات جديدة/أخرى.
            var workTypes = _lookupRepo.GetWorkTypes(activeOnly: false);
            _workTypePrices = workTypes.ToDictionary(wt => wt.WorkTypeID, wt => wt.Price);

            _isLoadingLookups = true;
            try
            {
                WorkTypeBox.Items.Clear();
                var noWorkTypeItem = new ComboBoxItem { Content = LocalizationManager.T("ProsthCase_NoWorkType"), Tag = null };
                WorkTypeBox.Items.Add(noWorkTypeItem);
                foreach (var wt in workTypes)
                {
                    var isCurrentValue = IsEditMode && _existingCase!.WorkTypeID == wt.WorkTypeID;
                    if (!wt.IsActive && !isCurrentValue) continue;
                    var item = new ComboBoxItem { Content = wt.WorkTypeName, Tag = wt.WorkTypeID };
                    WorkTypeBox.Items.Add(item);
                    if (isCurrentValue) item.IsSelected = true;
                }
                if (WorkTypeBox.SelectedItem == null) noWorkTypeItem.IsSelected = true;
            }
            finally
            {
                _isLoadingLookups = false;
            }

            var stages = _lookupRepo.GetStages(activeOnly: false);
            StageBox.Items.Clear();
            var noStageItem = new ComboBoxItem { Content = LocalizationManager.T("ProsthCase_NoStage"), Tag = null };
            StageBox.Items.Add(noStageItem);
            foreach (var st in stages)
            {
                var isCurrentValue = IsEditMode && _existingCase!.StageID == st.StageID;
                if (!st.IsActive && !isCurrentValue) continue;
                var item = new ComboBoxItem { Content = st.StageName, Tag = st.StageID };
                StageBox.Items.Add(item);
                if (isCurrentValue) item.IsSelected = true;
            }
            if (StageBox.SelectedItem == null) noStageItem.IsSelected = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    // يملأ حقل السعر تلقائياً بسعر نوع العمل المختار - بنفس فكرة تعبئة سعر العلاج تلقائياً في ملف
    // المريض عند اختيار علاج (PatientFileWindow.RecomputeTotalPriceFromTreatments)، هنا لاختيار
    // مفرد بدل مجموع. لا يعمل أثناء التحميل الأولي/استعادة قيمة حالة موجودة (_isLoadingLookups)
    // حتى لا يُستبدَل السعر المتفَق عليه فعلياً والمحفوظ مسبقاً بصمت. يملأ السعر مباشرة (استبدال
    // كامل، وليس فقط عند كون الحقل فارغاً) تماماً كسلوك حقل سعر العلاجات - المستخدم يستطيع تعديل
    // القيمة يدوياً بعد التعبئة إن أراد سعراً مختلفاً لحالة بعينها.
    private void WorkTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingLookups) return;
        if (TotalPriceBox == null) return; // قد يُطلَق الحدث قبل اكتمال إنشاء عناصر النافذة

        var workTypeId = (WorkTypeBox.SelectedItem as ComboBoxItem)?.Tag as int?;
        if (workTypeId.HasValue && _workTypePrices.TryGetValue(workTypeId.Value, out var price))
        {
            TotalPriceBox.Text = price > 0 ? price.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        }
    }

    private void LoadSessions()
    {
        if (_existingCase == null) return;
        try
        {
            var sessions = _sessionRepo.GetByCase(_existingCase.CaseID);
            SessionRows.Clear();
            foreach (var s in sessions) SessionRows.Add(new ProstheticSessionRowViewModel(s));
            UpdateSessionButtonsState();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    private void LoadPayments()
    {
        if (_existingCase == null) return;
        try
        {
            var payments = _paymentRepo.GetByCase(_existingCase.CaseID, _currentUser);
            PaymentRows.Clear();
            foreach (var p in payments) PaymentRows.Add(new ProstheticPaymentRowViewModel(p));
            UpdatePaymentButtonsState();

            // نعيد جلب الحالة لأخذ TotalPaid المحدَّث فعلياً من القاعدة (وليس القيمة القديمة وقت فتح النافذة)
            var refreshedCase = _caseRepo.GetById(_existingCase.CaseID, _currentUser);
            if (refreshedCase != null) RefreshFinanceSummary(refreshedCase.TotalAgreedPrice, refreshedCase.TotalPaid);
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    // Patch 9: قراءة فقط - لا يوجد أي زر إضافة/تعديل/حذف في هذا التبويب، فتعديل التشخيص/العلاج/
    // الدواء يبقى حصراً من تطبيق الطبيب كما هو الحال دائماً. تُجلَب كل جلسات المريض (وليس فقط
    // جلسات هذه الحالة تحديداً - لا يوجد ربط مباشر بين ProstheticCases وMedicalSessions أصلاً)،
    // ويُطبَّق التقييد لكل حقل بشكل مستقل تماماً عند بناء كل صف عرض.
    private void LoadClinicalSummary()
    {
        try
        {
            var entries = _clinicalRepo.GetByPatient(_patientId, _currentUser);
            ClinicalSummaryRows.Clear();
            foreach (var entry in entries)
            {
                ClinicalSummaryRows.Add(new ProstheticClinicalSummaryRowViewModel(
                    entry, _canViewDiagnosis, _canViewTreatment, _canViewMedications));
            }
            ClinicalSummaryEmptyText.Visibility = ClinicalSummaryRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    // Patch 10: للقراءة فقط بالكامل - لا يوجد أي مسار في هذه النافذة (ولا في الـRepository نفسه)
    // يُعدِّل أو يحذف سطر History واحد. تُستخدم GetHistory(caseId) الموجودة بالفعل منذ الباتش 2.3،
    // ولم تُنشَأ أي دالة أو Repository جديد لهذا التبويب.
    private void LoadHistory()
    {
        if (_existingCase == null) return;
        try
        {
            // نحل WorkTypeID → الاسم هنا لعرض WorkTypeChanged بصيغة مفهومة (راجع
            // ProstheticHistoryRowViewModel.FormatWorkType) - activeOnly: false عمداً، لأن حركة
            // قديمة قد تُشير لنوع عمل عُطِّل أو حُذف لاحقاً من القوائم، ويجب أن يبقى اسمه القديم
            // قابلاً للعرض في السجل التاريخي رغم ذلك.
            var workTypeNamesById = _lookupRepo.GetWorkTypes(activeOnly: false)
                .ToDictionary(wt => wt.WorkTypeID, wt => wt.WorkTypeName);

            var entries = _caseRepo.GetHistory(_existingCase.CaseID, _currentUser);
            HistoryRows.Clear();
            foreach (var entry in entries)
            {
                // StageChanged تحديداً مصفَّاة إضافياً بـViewStage/EditStage (راجع تعليق CanViewHistory
                // أعلاه) - بقية الحركات مغطاة أصلاً بصلاحية EditCase التي تحكم رؤية هذا التبويب كله
                if (entry.ActionType == "StageChanged" && !_canViewStage) continue;

                HistoryRows.Add(new ProstheticHistoryRowViewModel(entry, workTypeNamesById));
            }
            HistoryEmptyText.Visibility = HistoryRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    private void RefreshFinanceSummary(decimal agreedPrice, decimal totalPaid)
    {
        var outstanding = Math.Max(0, agreedPrice - totalPaid);
        var credit = Math.Max(0, totalPaid - agreedPrice);
        AgreedPriceSummaryText.Text = agreedPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        TotalPaidSummaryText.Text = totalPaid.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        OutstandingSummaryText.Text = outstanding.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
        CreditSummaryText.Visibility = credit > 0 ? Visibility.Visible : Visibility.Collapsed;
        CreditSummaryText.Text = credit > 0
            ? LocalizationManager.T("ProsthPayment_CreditFormat", credit.ToString("N2", System.Globalization.CultureInfo.InvariantCulture))
            : string.Empty;
    }

    private void UpdateSessionButtonsState()
    {
        var hasSelection = SessionsGrid.SelectedItem != null;
        EditSessionButton.IsEnabled = hasSelection && _canEditSession;
        DeleteSessionButton.IsEnabled = hasSelection && _canDeleteSession;
    }

    private void UpdatePaymentButtonsState()
    {
        var hasSelection = PaymentsGrid.SelectedItem != null;
        EditPaymentButton.IsEnabled = hasSelection && _canEditPayment;
        DeletePaymentButton.IsEnabled = hasSelection && _canDeletePayment;
    }

    private void SessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSessionButtonsState();
    private void PaymentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePaymentButtonsState();

    private void AddSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_existingCase == null || !_canAddSession) return;
        var window = new ProstheticSessionEditWindow(_existingCase.CaseID, _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _sessionsOrPaymentsChanged = true;
            LoadSessions();
        }
    }

    private void EditSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEditSession || SessionsGrid.SelectedItem is not ProstheticSessionRowViewModel row) return;
        var window = new ProstheticSessionEditWindow(row.Session, _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _sessionsOrPaymentsChanged = true;
            LoadSessions();
        }
    }

    private void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canDeleteSession || SessionsGrid.SelectedItem is not ProstheticSessionRowViewModel row) return;

        var confirm = MessageBox.Show(
            LocalizationManager.T("ProsthSession_ConfirmDelete"),
            LocalizationManager.T("Common_Notice"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _sessionRepo.Delete(row.Session.ProstheticSessionID, _currentUser);
            _sessionsOrPaymentsChanged = true;
            LoadSessions();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_SaveErrorFormat", ex.Message);
        }
    }

    private void AddPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_existingCase == null || !_canAddPayment) return;
        var window = new ProstheticPaymentEditWindow(_existingCase.CaseID, _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _sessionsOrPaymentsChanged = true;
            LoadPayments();
        }
    }

    private void EditPaymentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEditPayment || PaymentsGrid.SelectedItem is not ProstheticPaymentRowViewModel row) return;
        var window = new ProstheticPaymentEditWindow(row.Payment, _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _sessionsOrPaymentsChanged = true;
            LoadPayments();
        }
    }

    private void DeletePaymentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canDeletePayment || PaymentsGrid.SelectedItem is not ProstheticPaymentRowViewModel row) return;

        var confirm = MessageBox.Show(
            LocalizationManager.T("ProsthPayment_ConfirmDelete"),
            LocalizationManager.T("Common_Notice"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _paymentRepo.DeletePayment(row.Payment.ProstheticPaymentID, _currentUser);
            _sessionsOrPaymentsChanged = true;
            LoadPayments();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_SaveErrorFormat", ex.Message);
        }
    }

    // حذف الحالة نهائياً (Patch 12) - يُغلق النافذة فوراً بـDialogResult=true عند النجاح (بدل انتظار
    // زر "حفظ" الرئيسي، الذي لا معنى له بعد حذف الحالة التي كان سيُحفَظ التعديل عليها) حتى تُحدِّث
    // كل الشاشات المستدعية (ProstheticCaseWindow من DoctorApp، وMainWindow من ProsthetistApp) قوائمها
    // فتختفي الحالة المحذوفة تلقائياً عند إعادة التحميل، بنفس نمط _sessionsOrPaymentsChanged تماماً.
    private void DeleteCaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_existingCase == null || !_canDeleteCase) return;

        var confirm = MessageBox.Show(
            LocalizationManager.T("ProsthCase_ConfirmDeleteFormat", _existingCase.CaseNumber),
            LocalizationManager.T("ProsthCase_ConfirmDeleteTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _caseRepo.PermanentlyDelete(_existingCase.CaseID, _currentUser);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_SaveErrorFormat", ex.Message);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // إغلاق بدون ضغط "حفظ" الرئيسي - لكن إن حدث أي تغيير في الجلسات/المدفوعات (تُحفَظ فوراً بذاتها)
    // يجب أن يُحدِّث المستدعي قائمته أيضاً، فنُرجِع DialogResult=true في هذه الحالة تحديداً
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _sessionsOrPaymentsChanged;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _sessionsOrPaymentsChanged;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var prosthetistId = (ProsthetistBox.SelectedItem as ComboBoxItem)?.Tag as int?;
        if (prosthetistId == null)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_ProsthetistRequired");
            return;
        }

        var includesUpper = IncludesUpperCheck.IsChecked == true;
        var includesLower = IncludesLowerCheck.IsChecked == true;
        var toothScope = ToothScopeOdontogram.SelectedTeeth.Count > 0
            ? string.Join(", ", ToothScopeOdontogram.SelectedTeeth.OrderBy(t => t))
            : null;

        if (!includesUpper && !includesLower && string.IsNullOrEmpty(toothScope))
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_ArchOrScopeRequired");
            return;
        }

        decimal totalPrice = 0;
        if (!string.IsNullOrWhiteSpace(TotalPriceBox.Text)
            && !decimal.TryParse(TotalPriceBox.Text.Trim(), out totalPrice))
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_InvalidPrice");
            return;
        }

        var workTypeId = (WorkTypeBox.SelectedItem as ComboBoxItem)?.Tag as int?;
        var stageId = (StageBox.SelectedItem as ComboBoxItem)?.Tag as int?;
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        try
        {
            if (IsEditMode)
            {
                if (_canEditCase)
                {
                    var updated = new ProstheticCase
                    {
                        CaseID = _existingCase!.CaseID,
                        WorkTypeID = workTypeId,
                        IncludesUpper = includesUpper,
                        IncludesLower = includesLower,
                        ToothScope = toothScope,
                        TotalAgreedPrice = totalPrice,
                        Notes = notes
                    };
                    _caseRepo.UpdateCaseInfo(updated, _currentUser.UserID, _currentUser);
                }

                // نقل المرمم (إن تغيَّر فقط، وإن كانت الصلاحية ممنوحة فعلياً) - يُسجَّل في History
                // تلقائياً بأسماء واضحة (وليس أرقام UserID خام)
                if (_canTransferCase && prosthetistId != _existingCase!.AssignedProsthetistUserID)
                {
                    var oldName = _existingCase.AssignedProsthetistName;
                    var newName = (ProsthetistBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                    _caseRepo.ReassignProsthetist(_existingCase.CaseID, prosthetistId, _currentUser.UserID, oldName, newName, _currentUser);
                }

                // تغيير المرحلة (إن تغيَّرت فقط، وإن كانت الصلاحية ممنوحة فعلياً)
                if (_canEditStage && stageId != _existingCase!.StageID)
                {
                    var oldStageName = _existingCase.StageName;
                    var newStageName = (StageBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                    _caseRepo.UpdateStage(_existingCase.CaseID, stageId, _currentUser.UserID, oldStageName, newStageName, _currentUser);
                }
            }
            else
            {
                var newCase = new ProstheticCase
                {
                    PatientID = _patientId,
                    WorkTypeID = workTypeId,
                    IncludesUpper = includesUpper,
                    IncludesLower = includesLower,
                    ToothScope = toothScope,
                    AssignedProsthetistUserID = prosthetistId,
                    StageID = stageId,
                    TotalAgreedPrice = totalPrice,
                    Notes = notes,
                    CreatedByUserID = _currentUser.UserID
                };

                _caseRepo.CreateCase(newCase, _currentUser);
                CaseCreated = true;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_SaveErrorFormat", ex.Message);
        }
    }
}

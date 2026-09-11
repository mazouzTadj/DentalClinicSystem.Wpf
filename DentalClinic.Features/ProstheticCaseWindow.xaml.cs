using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// صف عرض واحد لحالة ترميم في قائمة حالات المريض - نفس نمط SessionHistoryRowViewModel/UserRowViewModel
public class ProstheticCaseRowViewModel
{
    public ProstheticCase Case { get; }

    public int CaseNumber => Case.CaseNumber;
    public string PatientFullName => Case.PatientFullName;
    public string WorkTypeName => Case.WorkTypeName ?? "-";
    public string PatientPhoneNumber => string.IsNullOrWhiteSpace(Case.PatientPhoneNumber) ? "-" : Case.PatientPhoneNumber;
    public string ArchText
    {
        get
        {
            if (Case.IncludesUpper && Case.IncludesLower) return LocalizationManager.T("ProsthCase_ArchBoth");
            if (Case.IncludesUpper) return LocalizationManager.T("ProsthCase_Upper");
            if (Case.IncludesLower) return LocalizationManager.T("ProsthCase_Lower");
            return Case.ToothScope ?? "-";
        }
    }
    public string ProsthetistName => Case.AssignedProsthetistName ?? "-";
    public string StageName => Case.StageName ?? "-";
    public string StatusText => LocalizationManager.T(Case.CaseStatus switch
    {
        ProstheticCaseStatus.Completed => "ProsthCase_StatusCompleted",
        ProstheticCaseStatus.Cancelled => "ProsthCase_StatusCancelled",
        _ => "ProsthCase_StatusOpen"
    });
    public string CreatedAtText => Case.CreatedAt.ToString("yyyy-MM-dd");
    public string PriceText => Case.TotalAgreedPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
    public string OutstandingText => Case.OutstandingBalance.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    public ProstheticCaseRowViewModel(ProstheticCase c) => Case = c;
}

public partial class ProstheticCaseWindow : Window
{
    private readonly int _patientId;
    private readonly UserAccount _currentUser;
    private readonly ProstheticCaseRepository _caseRepo;

    public ObservableCollection<ProstheticCaseRowViewModel> Cases { get; } = new();

    public ProstheticCaseWindow(int patientId, UserAccount currentUser)
    {
        _patientId = patientId;
        _currentUser = currentUser;
        InitializeComponent();
        CasesGrid.ItemsSource = Cases;

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _caseRepo = new ProstheticCaseRepository(db);

        Loaded += (s, e) => LoadCases();
    }

    private void LoadCases()
    {
        try
        {
            var cases = _caseRepo.GetByPatient(_patientId);
            Cases.Clear();
            foreach (var c in cases)
            {
                Cases.Add(new ProstheticCaseRowViewModel(c));
            }
            StatusText.Text = LocalizationManager.T("ProsthCase_CountFormat", cases.Count);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationManager.T("ProsthCase_LoadErrorFormat", ex.Message);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // فتح تفاصيل الحالة كاملة للعرض/التعديل - هنا تظهر بقية المعلومات التي أُزيلت من الجدول
    // (نوع العمل، الفك، الحالة العامة، تاريخ الإنشاء، المتبقي) حتى لا يزدحم الجدول الرئيسي
    private void CasesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CasesGrid.SelectedItem is not ProstheticCaseRowViewModel row) return;

        if (_currentUser.Role != UserRole.Doctor)
        {
            MessageBox.Show(LocalizationManager.T("ProsthCase_OnlyDoctorCanCreate"), LocalizationManager.T("Common_Notice"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new ProstheticCaseEditWindow(row.Case, _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadCases();
        }
    }

    private void NewCaseButton_Click(object sender, RoutedEventArgs e)
    {
        // فحص دفاعي إضافي: هذه النافذة تُفتَح فقط من الطبيب أصلاً (زر PatientFileWindow مقيَّد بالدور)،
        // لكن نتحقق مجدداً هنا في الكود الخلفي نفسه، وليس فقط عبر إخفاء الزر في الواجهة (راجع بند الأمان
        // في المتطلبات: التحقق من الصلاحية في الواجهة والكود الخلفي، لا الاعتماد على إخفاء الزر فقط)
        if (_currentUser.Role != UserRole.Doctor)
        {
            MessageBox.Show(LocalizationManager.T("ProsthCase_OnlyDoctorCanCreate"), LocalizationManager.T("Common_Notice"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new ProstheticCaseEditWindow(_patientId, _currentUser) { Owner = this };
        if (window.ShowDialog() == true)
        {
            LoadCases();
        }
    }
}

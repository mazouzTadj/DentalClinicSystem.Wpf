using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// نافذة صغيرة لتغيير "حالة الملف" الإدارية (مفتوحة/مكتملة/ملغاة) - ميزة جديدة تُفتَح من النافذة
// الرئيسية لتطبيق المرمم مباشرة (زر "تغيير الحالة" أسفل قائمة الحالات)، بدل الاضطرار لفتح ملف
// الحالة كاملاً. تستخدم نفس ProstheticCaseRepository.UpdateStatus الموجودة أصلاً في الطبقة الخلفية
// (كانت بلا أي مستدعٍ فعلي قبل هذه الميزة) وتتحقق من نفس صلاحية Prosthetics.EditCase عبرها.
public partial class ChangeCaseStatusDialog : Window
{
    private readonly ProstheticCase _case;
    private readonly UserAccount _currentUser;
    private readonly ProstheticCaseRepository _caseRepo;
    private bool _isInitializing = true;

    public ChangeCaseStatusDialog(ProstheticCase existingCase, UserAccount currentUser, ProstheticCaseRepository caseRepo)
    {
        InitializeComponent();
        _case = existingCase;
        _currentUser = currentUser;
        _caseRepo = caseRepo;

        CaseSummaryText.Text = LocalizationManager.T("ProsthMain_ChangeStatusCaseFormat", _case.CaseNumber, _case.PatientFullName);

        switch (_case.CaseStatus)
        {
            case ProstheticCaseStatus.Completed:
                CompletedRadio.IsChecked = true;
                break;
            case ProstheticCaseStatus.Cancelled:
                CancelledRadio.IsChecked = true;
                break;
            default:
                OpenRadio.IsChecked = true;
                break;
        }

        _isInitializing = false;
    }

    private void StatusRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        ErrorText.Text = string.Empty;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private string? GetSelectedStatus()
    {
        if (OpenRadio.IsChecked == true) return ProstheticCaseStatus.Open;
        if (CompletedRadio.IsChecked == true) return ProstheticCaseStatus.Completed;
        if (CancelledRadio.IsChecked == true) return ProstheticCaseStatus.Cancelled;
        return null;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var newStatus = GetSelectedStatus();
        if (newStatus == null)
        {
            ErrorText.Text = LocalizationManager.T("ProsthMain_ChangeStatusPickOne");
            return;
        }

        // بلا تغيير فعلي - لا داعي لأي استدعاء للقاعدة أو سطر History جديد
        if (newStatus == _case.CaseStatus)
        {
            DialogResult = true;
            Close();
            return;
        }

        try
        {
            _caseRepo.UpdateStatus(_case.CaseID, newStatus, _currentUser.UserID, _case.CaseStatus, _currentUser);
            DialogResult = true;
            Close();
        }
        catch (UnauthorizedAccessException)
        {
            ErrorText.Text = LocalizationManager.T("Common_AccessDenied");
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("ProsthCase_SaveErrorFormat", ex.Message);
        }
    }
}

using System;
using System.Configuration;
using System.Windows;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// نافذة إضافة/تعديل جلسة ترميم واحدة - تُفتَح من تبويب "الجلسات" في ProstheticCaseEditWindow.
// وقت الجلسة: تاريخ فقط عبر DatePicker (تبسيطاً)؛ الوقت الدقيق يبقى كما كان عند الإنشاء
// (وقت الحفظ الفعلي لجلسة جديدة)، ولا يتغيَّر عند التعديل - لا حاجة لعنصر إدخال وقت منفصل.
public partial class ProstheticSessionEditWindow : Window
{
    private readonly int _caseId;
    private readonly UserAccount _currentUser;
    private readonly ProstheticSession? _existingSession; // null = إضافة
    private readonly ProstheticSessionRepository _sessionRepo;

    private bool IsEditMode => _existingSession != null;

    public ProstheticSessionEditWindow(int caseId, UserAccount currentUser)
    {
        _caseId = caseId;
        _existingSession = null;
        _currentUser = currentUser;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _sessionRepo = new ProstheticSessionRepository(db);

        TitleText.Text = LocalizationManager.T("ProsthSession_AddTitle");
        SessionDateBox.SelectedDate = DateTime.Now.Date;
    }

    public ProstheticSessionEditWindow(ProstheticSession existingSession, UserAccount currentUser)
    {
        _caseId = existingSession.CaseID;
        _existingSession = existingSession;
        _currentUser = currentUser;
        InitializeComponent();

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _sessionRepo = new ProstheticSessionRepository(db);

        TitleText.Text = LocalizationManager.T("ProsthSession_EditTitle");
        SessionDateBox.SelectedDate = existingSession.SessionDateTime.Date;
        DescriptionBox.Text = existingSession.Description ?? string.Empty;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        if (SessionDateBox.SelectedDate == null)
        {
            ErrorText.Text = LocalizationManager.T("ProsthSession_DateRequired");
            return;
        }

        var description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

        try
        {
            if (IsEditMode)
            {
                // نُبقي مكوِّن الوقت كما كان (وقت الإنشاء الفعلي)، ونُغيِّر التاريخ فقط إن اختلف
                var originalTimeOfDay = _existingSession!.SessionDateTime.TimeOfDay;
                var updated = new ProstheticSession
                {
                    ProstheticSessionID = _existingSession.ProstheticSessionID,
                    SessionDateTime = SessionDateBox.SelectedDate.Value.Date + originalTimeOfDay,
                    Description = description
                };
                _sessionRepo.Update(updated, _currentUser);
            }
            else
            {
                var newSession = new ProstheticSession
                {
                    CaseID = _caseId,
                    SessionDateTime = SessionDateBox.SelectedDate.Value.Date + DateTime.Now.TimeOfDay,
                    PerformedByUserID = _currentUser.UserID,
                    Description = description
                };
                _sessionRepo.Add(newSession, _currentUser);
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

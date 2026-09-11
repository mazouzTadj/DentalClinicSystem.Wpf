using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class AddPatientWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly Patient? _existingPatient; // null = وضع تسجيل مريض جديد، غير null = وضع تعديل بيانات مريض موجود

    private class DoctorAssignmentOption
    {
        public int? UserID { get; set; } // null = خيار "غير مُسنَد لطبيب بعد"
        public string DisplayName { get; set; } = string.Empty;
        public System.Windows.Media.Brush StatusBrush { get; set; } = System.Windows.Media.Brushes.Transparent;
    }

    // وضع الإضافة (كما كان سابقاً)
    public AddPatientWindow(UserAccount currentUser) : this(currentUser, null)
    {
    }

    // وضع التعديل: يُمرَّر المريض المطلوب تعديل بياناته - تُعبَّأ كل الحقول تلقائياً بقيمه الحالية
    public AddPatientWindow(UserAccount currentUser, Patient? existingPatient)
    {
        _currentUser = currentUser;
        _existingPatient = existingPatient;
        InitializeComponent();

        LoadAssignedDoctorOptions();

        if (_existingPatient != null)
        {
            TitleText.Text = LocalizationManager.T("AddPatient_TitleEdit");
            SaveButtonElement.Content = LocalizationManager.T("AddPatient_SaveChangesButton");

            FullNameBox.Text = _existingPatient.FullName;
            AgeBox.Text = _existingPatient.Age?.ToString() ?? string.Empty;
            PhoneBox.Text = _existingPatient.PhoneNumber;
            AddressBox.Text = _existingPatient.Address ?? string.Empty;
            NotesBox.Text = _existingPatient.BasicMedicalNotes ?? string.Empty;
            AssignedDoctorBox.SelectedValue = _existingPatient.AssignedDoctorUserID;

            foreach (ComboBoxItem item in GenderBox.Items)
            {
                if ((string?)item.Tag == _existingPatient.Gender)
                {
                    item.IsSelected = true;
                }
            }

            // خيار "حفظ + حجز" خاص بالمريض الجديد فقط؛ في وضع التعديل نُخفي هذا الزر فقط
            // ونُبقي الحاوية (NewPatientActionPanel) ظاهرة، وإلا يختفي زر الحفظ معها لأنه ابن لها.
            SaveAndBookButton.Visibility = Visibility.Collapsed;
            Grid.SetColumnSpan(SaveButtonElement, 3);
            SaveButtonElement.Content = LocalizationManager.T("AddPatient_SaveChangesButton");
            CancelButtonElement.Visibility = Visibility.Visible;
        }
    }

    // "غير مُسنَد" دائماً أولاً (وهي الافتراضية عند التسجيل الجديد)، ثم كل الأطباء النشطين مع نقطة
    // خضراء لمن هو متصل الآن فعلاً - تساعد الممرضة تختار طبيباً موجوداً فعلياً بالعيادة
    private void LoadAssignedDoctorOptions()
    {
        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var userRepo = new UserRepository(db);

            var options = new List<DoctorAssignmentOption>
            {
                new() { UserID = null, DisplayName = LocalizationManager.T("AddPatient_UnassignedDoctor") }
            };
            options.AddRange(userRepo.GetAllDoctors().Select(d => new DoctorAssignmentOption
            {
                UserID = d.UserID,
                DisplayName = d.FullName,
                StatusBrush = d.IsOnline
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xA0, 0x6B))
                    : System.Windows.Media.Brushes.Transparent
            }));

            AssignedDoctorBox.ItemsSource = options;
            AssignedDoctorBox.SelectedIndex = 0;
        }
        catch
        {
            // فشل نادر بالاتصال وقت فتح النافذة فقط - يبقى الحقل فارغاً، والحفظ الفعلي لاحقاً سيُظهر
            // رسالة الخطأ الحقيقية بوضوح إن استمر الفشل (راجع catch في SaveButton_Click)
        }
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var fullName = FullNameBox.Text.Trim();
        var phone = PhoneBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            ErrorText.Text = LocalizationManager.T("AddPatient_FullNameRequired");
            return;
        }

        int? age = null;
        if (!string.IsNullOrWhiteSpace(AgeBox.Text))
        {
            if (!int.TryParse(AgeBox.Text.Trim(), out int parsedAge) || parsedAge < 0 || parsedAge > 130)
            {
                ErrorText.Text = LocalizationManager.T("AddPatient_InvalidAge");
                return;
            }
            age = parsedAge;
        }

        // نقرأ Tag (القيمة الثابتة: Male/Female) بدل Content (النص المترجَم) حتى تبقى القيمة المخزَّنة
        // في قاعدة البيانات متسقة بغض النظر عن لغة الواجهة المستخدَمة عند التسجيل
        string? gender = (GenderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        int? assignedDoctorId = AssignedDoctorBox.SelectedValue as int?;

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);

            // ===== وضع التعديل: تحديث بيانات مريض موجود مسبقاً فقط - بلا فحص تكرار وبلا إضافة لقائمة الانتظار =====
            if (_existingPatient != null)
            {
                var updatedPatient = new Patient
                {
                    PatientID = _existingPatient.PatientID,
                    FullName = fullName,
                    Age = age,
                    Gender = gender,
                    PhoneNumber = phone,
                    Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                    BasicMedicalNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
                    AssignedDoctorUserID = assignedDoctorId
                };

                patientRepo.Update(updatedPatient);

                DialogResult = true;
                Close();
                return;
            }

            // ===== وضع الإضافة: تسجيل مريض جديد (السلوك الأصلي دون تغيير) =====
            var queueRepo = new QueueRepository(db);

            // تحقق أولاً: هل هذا المريض مسجَّل مسبقاً بنفس الاسم ونفس رقم الهاتف؟ (تطابق قوي = على الأغلب نفس الشخص)
            // (يُتخطى هذا الفحص إن لم يُدخَل رقم هاتف، لأن التطابق يفقد معناه بلا رقم مرجعي)
            var existing = string.IsNullOrWhiteSpace(phone) ? null : patientRepo.FindDuplicate(fullName, phone);
            if (existing != null)
            {
                var confirm = MessageBox.Show(
                    LocalizationManager.T("AddPatient_DuplicateFoundFormat", existing.FullName, existing.PhoneNumber),
                    LocalizationManager.T("AddPatient_DuplicateTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    var (success, message, _) = queueRepo.AddToQueue(existing.PatientID, _currentUser.UserID);
                    if (!success)
                    {
                        ErrorText.Text = message;
                        return;
                    }

                    DialogResult = true;
                    Close();
                    return;
                }
                // اختار "لا" => نفترض أنه فعلاً شخص مختلف ونكمل تسجيله كمريض جديد
            }
            else
            {
                // تطابق قوي غير موجود (الهاتف مختلف أو فارغ) - لكن هل يوجد مريض آخر بنفس الاسم بالضبط؟
                // بدون هذا التنبيه، كان النظام يسجّل مريضاً جديداً بصمت حتى لو الاسم مطابق تماماً لمريض موجود -
                // قد يبدو للممرضة لاحقاً وكأن البيانات "تكررت" أو "استُبدلت" رغم أنهما سجلّان منفصلان فعلياً
                var sameNameMatches = patientRepo.FindByNameOnly(fullName);
                if (sameNameMatches.Count > 0)
                {
                    var otherPhones = string.Join("، ", sameNameMatches.Select(p => string.IsNullOrWhiteSpace(p.PhoneNumber) ? "-" : p.PhoneNumber));
                    var proceedAnyway = MessageBox.Show(
                        LocalizationManager.T("AddPatient_SameNameWarningFormat", fullName, otherPhones),
                        LocalizationManager.T("AddPatient_DuplicateTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (proceedAnyway != MessageBoxResult.Yes)
                    {
                        return; // الممرضة قررت التوقف والتحقق يدوياً بدل المتابعة
                    }
                    // اختارت "نعم" => تؤكد أنه فعلاً شخص مختلف بنفس الاسم، نكمل تسجيله كمريض جديد
                }
            }

            var patient = new Patient
            {
                FullName = fullName,
                Age = age,
                Gender = gender,
                PhoneNumber = phone,
                Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                BasicMedicalNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
                RegisteredByUserID = _currentUser.UserID,
                AssignedDoctorUserID = assignedDoctorId
            };

            var newPatientId = patientRepo.Add(patient);
            var (queued, queueMessage, _) = queueRepo.AddToQueue(newPatientId, _currentUser.UserID);
            if (!queued)
            {
                ErrorText.Text = queueMessage;
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("AddPatient_SaveErrorFormat", ex.Message);
        }
    }

    // تسجيل مريض جديد ثم فتح نفس نافذة الحجز المستخدمة في بقية النظام.
    // لا نضيف المريض إلى قائمة الانتظار في هذا المسار: بعد إنشاء السجل ننتقل مباشرة إلى الحجز.
    private void SaveAndBookButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var fullName = FullNameBox.Text.Trim();
        var phone = PhoneBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            ErrorText.Text = LocalizationManager.T("AddPatient_FullNameRequired");
            return;
        }

        int? age = null;
        if (!string.IsNullOrWhiteSpace(AgeBox.Text))
        {
            if (!int.TryParse(AgeBox.Text.Trim(), out int parsedAge) || parsedAge < 0 || parsedAge > 130)
            {
                ErrorText.Text = LocalizationManager.T("AddPatient_InvalidAge");
                return;
            }
            age = parsedAge;
        }

        string? gender = (GenderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        int? assignedDoctorId = AssignedDoctorBox.SelectedValue as int?;

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var patientRepo = new PatientRepository(db);

            // نفس حماية التكرار الموجودة في المسار العادي: زر "حجز موعد" لا ينشئ مريضاً
            // جديداً إذا كان هناك تطابق قوي، ولا يحوّل المريض الموجود إلى حجز من هذه النافذة.
            var existing = string.IsNullOrWhiteSpace(phone) ? null : patientRepo.FindDuplicate(fullName, phone);
            if (existing != null)
            {
                MessageBox.Show(
                    LocalizationManager.T("AddPatient_BookingDuplicateMessageFormat", existing.FullName),
                    LocalizationManager.T("AddPatient_DuplicateTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var sameNameMatches = patientRepo.FindByNameOnly(fullName);
            if (sameNameMatches.Count > 0)
            {
                var otherPhones = string.Join("، ", sameNameMatches.Select(p => string.IsNullOrWhiteSpace(p.PhoneNumber) ? "-" : p.PhoneNumber));
                var proceedAnyway = MessageBox.Show(
                    LocalizationManager.T("AddPatient_SameNameWarningFormat", fullName, otherPhones),
                    LocalizationManager.T("AddPatient_DuplicateTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (proceedAnyway != MessageBoxResult.Yes)
                    return;
            }

            var patient = new Patient
            {
                FullName = fullName,
                Age = age,
                Gender = gender,
                PhoneNumber = phone,
                Address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                BasicMedicalNotes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
                RegisteredByUserID = _currentUser.UserID,
                AssignedDoctorUserID = assignedDoctorId
            };

            var newPatientId = patientRepo.Add(patient);
            var queueRepo = new QueueRepository(db);
            var appointmentDialog = new ScheduleAppointmentDialog(newPatientId, _currentUser.UserID, queueRepo, db)
            {
                Owner = this
            };

            // إذا ألغى المستخدم الحجز بعد إنشاء المريض، يبقى سجل المريض محفوظاً فقط ولا يدخل قائمة الانتظار.
            // هذا يحافظ على المعنى الصحيح: التسجيل ≠ تسجيل حضور.
            var booked = appointmentDialog.ShowDialog() == true;
            if (!booked)
            {
                MessageBox.Show(
                    LocalizationManager.T("AddPatient_BookingCancelledPatientSaved"),
                    LocalizationManager.T("Common_Notice"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("AddPatient_SaveErrorFormat", ex.Message);
        }
    }
}

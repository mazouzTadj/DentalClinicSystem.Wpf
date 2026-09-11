using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

// صف عرض واحد لنوع عمل في الجدول - يضيف نصوصاً جاهزة للعرض (الحالة/السعر المنسَّق) فوق النموذج الخام
public class WorkTypeRowViewModel
{
    public ProstheticWorkType WorkType { get; }
    public string WorkTypeName => WorkType.WorkTypeName;
    public decimal Price => WorkType.Price;
    public string PriceText => Price.ToString("0.##");
    public int SortOrder => WorkType.SortOrder;
    public bool IsActive => WorkType.IsActive;
    public string ActiveText => IsActive ? LocalizationManager.T("ProsthLookup_StatusActive") : LocalizationManager.T("ProsthLookup_StatusInactive");

    public WorkTypeRowViewModel(ProstheticWorkType workType) => WorkType = workType;
}

// نفس فكرة WorkTypeRowViewModel أعلاه، لمرحلة علاج بدل نوع عمل
public class StageRowViewModel
{
    public ProstheticStage Stage { get; }
    public string StageName => Stage.StageName;
    public int SortOrder => Stage.SortOrder;
    public bool IsActive => Stage.IsActive;
    public string ActiveText => IsActive ? LocalizationManager.T("ProsthLookup_StatusActive") : LocalizationManager.T("ProsthLookup_StatusInactive");

    public StageRowViewModel(ProstheticStage stage) => Stage = stage;
}

// نافذة إدارة "أنواع العمل" و"مراحل العلاج" الخاصة بالترميم - Patch 13.
// ⚠️ الطبيب الرئيسي فقط: بالإضافة لكون زر الفتح في DoctorApp مخفياً لغيره أصلاً (راجع
// DoctorApp.MainWindow)، هذه النافذة نفسها تتحقق مجدداً عند الإنشاء (دفاع في العمق مستوى ثانٍ)،
// وكل عملية كتابة تستدعيها تتحقق بذاتها للمرة الثالثة داخل ProstheticLookupRepository
// (EnsureMainDoctor) - ثلاث طبقات حماية مستقلة، وليس فقط إخفاء زر في XAML كما طُلب صراحةً.
// حذف نهائي حقيقي متاح الآن (Add / Rename+Price / Delete مع رفض ذكي إن كان العنصر مستخدَماً +
// اقتراح تعطيل بديل / Move Up / Move Down) - راجع BtnDeleteWorkType_Click وBtnDeleteStage_Click.
public partial class ProstheticLookupManagementWindow : Window
{
    private readonly UserAccount _currentUser;
    private readonly ProstheticLookupRepository? _lookupRepo;

    public ObservableCollection<WorkTypeRowViewModel> WorkTypes { get; } = new();
    public ObservableCollection<StageRowViewModel> Stages { get; } = new();

    private int? _editingWorkTypeId;
    private int? _editingStageId;

    public ProstheticLookupManagementWindow(UserAccount currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;

        // فحص دفاعي في الواجهة نفسها (طبقة إضافية فوق إخفاء الزر في DoctorApp) - إن فُتحت هذه
        // النافذة بطريقة ما بحساب ليس الطبيب الرئيسي، تُغلَق فوراً بلا تحميل أي بيانات أو تفعيل
        // أي زر إطلاقاً
        if (_currentUser.Role != UserRole.Doctor || !_currentUser.IsMainDoctor)
        {
            Loaded += (s, e) =>
            {
                MessageBox.Show(
                    LocalizationManager.T("ProsthLookup_MainDoctorOnly"),
                    LocalizationManager.T("Common_AccessDenied"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
            };
            return;
        }

        var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
        var db = new DatabaseHelper(connectionString);
        _lookupRepo = new ProstheticLookupRepository(db);

        WorkTypesGrid.ItemsSource = WorkTypes;
        StagesGrid.ItemsSource = Stages;

        Loaded += (s, e) =>
        {
            LoadWorkTypes();
            LoadStages();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ===================== تبديل بين لوحتَي "أنواع العمل" و"المراحل" - نفس نمط TreatmentManagementWindow =====================
    private void TabWorkTypes_Click(object sender, RoutedEventArgs e)
    {
        WorkTypesPanelGrid.Visibility = Visibility.Visible;
        StagesPanelGrid.Visibility = Visibility.Collapsed;
        TabWorkTypesButton.Style = (Style)FindResource("PrimaryButtonStyle");
        TabStagesButton.Style = (Style)FindResource("SecondaryButtonStyle");
    }

    private void TabStages_Click(object sender, RoutedEventArgs e)
    {
        StagesPanelGrid.Visibility = Visibility.Visible;
        WorkTypesPanelGrid.Visibility = Visibility.Collapsed;
        TabStagesButton.Style = (Style)FindResource("PrimaryButtonStyle");
        TabWorkTypesButton.Style = (Style)FindResource("SecondaryButtonStyle");
    }

    // ===================== أنواع العمل =====================

    private void LoadWorkTypes()
    {
        try
        {
            // activeOnly:false - نافذة الإدارة يجب أن تُظهر كل شيء (بما فيه المعطَّل) لتتمكن من
            // إعادة تفعيله لاحقاً، بعكس ComboBox إنشاء حالة جديدة الذي يعرض النشط فقط
            var items = _lookupRepo!.GetWorkTypes(activeOnly: false);
            WorkTypes.Clear();
            foreach (var wt in items) WorkTypes.Add(new WorkTypeRowViewModel(wt));
        }
        catch (Exception ex)
        {
            WorkTypeErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex.Message);
        }
    }

    private void BtnSaveWorkType_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtWorkTypeName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            WorkTypeErrorText.Text = LocalizationManager.T("ProsthLookup_NameRequired");
            return;
        }

        decimal price = 0;
        if (!string.IsNullOrWhiteSpace(TxtWorkTypePrice.Text) &&
            (!decimal.TryParse(TxtWorkTypePrice.Text.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out price) || price < 0))
        {
            WorkTypeErrorText.Text = LocalizationManager.T("ProsthLookup_InvalidPrice");
            return;
        }

        try
        {
            if (_editingWorkTypeId.HasValue)
            {
                _lookupRepo!.UpdateWorkType(_editingWorkTypeId.Value, name, price, _currentUser);
            }
            else
            {
                _lookupRepo!.AddWorkType(name, price, _currentUser);
            }
            CancelWorkTypeEdit();
            LoadWorkTypes();
        }
        catch (Exception ex)
        {
            WorkTypeErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex.Message);
        }
    }

    private void BtnEditWorkType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WorkTypeRowViewModel row }) return;
        _editingWorkTypeId = row.WorkType.WorkTypeID;
        TxtWorkTypeName.Text = row.WorkTypeName;
        TxtWorkTypePrice.Text = row.Price.ToString("0.##");
        WorkTypeErrorText.Text = "";
        BtnSaveWorkTypeButton.Content = LocalizationManager.T("ProsthLookup_SaveEdit");
        BtnCancelWorkTypeEdit.Visibility = Visibility.Visible;
    }

    private void BtnCancelWorkTypeEdit_Click(object sender, RoutedEventArgs e) => CancelWorkTypeEdit();

    private void CancelWorkTypeEdit()
    {
        _editingWorkTypeId = null;
        TxtWorkTypeName.Text = "";
        TxtWorkTypePrice.Text = "";
        WorkTypeErrorText.Text = "";
        BtnSaveWorkTypeButton.Content = LocalizationManager.T("ProsthLookup_AddButton");
        BtnCancelWorkTypeEdit.Visibility = Visibility.Collapsed;
    }

    // حذف نهائي حقيقي لنوع عمل. إن كان لا يزال مستخدَماً في حالة موجودة (ProstheticLookupInUseException)
    // نعرض ذلك بوضوح ونقترح "تعطيله" بدلاً من الحذف كحل وسط يحفظ السجل التاريخي للحالات القديمة.
    private void BtnDeleteWorkType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WorkTypeRowViewModel row }) return;

        var confirm = MessageBox.Show(
            LocalizationManager.T("ProsthLookup_ConfirmDeleteFormat", row.WorkTypeName),
            LocalizationManager.T("ProsthLookup_ConfirmDeleteTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _lookupRepo!.DeleteWorkType(row.WorkType.WorkTypeID, _currentUser);
            if (_editingWorkTypeId == row.WorkType.WorkTypeID) CancelWorkTypeEdit();
            LoadWorkTypes();
        }
        catch (ProstheticLookupInUseException ex)
        {
            var switchToDisable = MessageBox.Show(
                LocalizationManager.T("ProsthLookup_InUseFormat", ex.UsageCount),
                LocalizationManager.T("ProsthLookup_InUseTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (switchToDisable == MessageBoxResult.Yes)
            {
                try
                {
                    _lookupRepo!.SetWorkTypeActive(row.WorkType.WorkTypeID, false, _currentUser);
                    LoadWorkTypes();
                }
                catch (Exception ex2)
                {
                    WorkTypeErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex2.Message);
                }
            }
        }
        catch (Exception ex)
        {
            WorkTypeErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex.Message);
        }
    }

    private void BtnMoveWorkTypeUp_Click(object sender, RoutedEventArgs e) => MoveWorkType(sender, moveUp: true);
    private void BtnMoveWorkTypeDown_Click(object sender, RoutedEventArgs e) => MoveWorkType(sender, moveUp: false);

    private void MoveWorkType(object sender, bool moveUp)
    {
        if (sender is not Button { Tag: WorkTypeRowViewModel row }) return;
        try
        {
            _lookupRepo!.MoveWorkType(row.WorkType.WorkTypeID, moveUp, _currentUser);
            LoadWorkTypes();
        }
        catch (Exception ex)
        {
            WorkTypeErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex.Message);
        }
    }

    // ===================== مراحل العلاج =====================

    private void LoadStages()
    {
        try
        {
            var items = _lookupRepo!.GetStages(activeOnly: false);
            Stages.Clear();
            foreach (var st in items) Stages.Add(new StageRowViewModel(st));
        }
        catch (Exception ex)
        {
            StageErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex.Message);
        }
    }

    private void BtnSaveStage_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtStageName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StageErrorText.Text = LocalizationManager.T("ProsthLookup_NameRequired");
            return;
        }

        try
        {
            if (_editingStageId.HasValue)
            {
                _lookupRepo!.UpdateStage(_editingStageId.Value, name, _currentUser);
            }
            else
            {
                _lookupRepo!.AddStage(name, _currentUser);
            }
            CancelStageEdit();
            LoadStages();
        }
        catch (Exception ex)
        {
            StageErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex.Message);
        }
    }

    private void BtnEditStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageRowViewModel row }) return;
        _editingStageId = row.Stage.StageID;
        TxtStageName.Text = row.StageName;
        StageErrorText.Text = "";
        BtnSaveStageButton.Content = LocalizationManager.T("ProsthLookup_SaveEdit");
        BtnCancelStageEdit.Visibility = Visibility.Visible;
    }

    private void BtnCancelStageEdit_Click(object sender, RoutedEventArgs e) => CancelStageEdit();

    private void CancelStageEdit()
    {
        _editingStageId = null;
        TxtStageName.Text = "";
        StageErrorText.Text = "";
        BtnSaveStageButton.Content = LocalizationManager.T("ProsthLookup_AddButton");
        BtnCancelStageEdit.Visibility = Visibility.Collapsed;
    }

    // حذف نهائي حقيقي لمرحلة - نفس منطق BtnDeleteWorkType_Click بالحرف (راجع تعليقاته أعلاه).
    private void BtnDeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageRowViewModel row }) return;

        var confirm = MessageBox.Show(
            LocalizationManager.T("ProsthLookup_ConfirmDeleteFormat", row.StageName),
            LocalizationManager.T("ProsthLookup_ConfirmDeleteTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _lookupRepo!.DeleteStage(row.Stage.StageID, _currentUser);
            if (_editingStageId == row.Stage.StageID) CancelStageEdit();
            LoadStages();
        }
        catch (ProstheticLookupInUseException ex)
        {
            var switchToDisable = MessageBox.Show(
                LocalizationManager.T("ProsthLookup_InUseFormat", ex.UsageCount),
                LocalizationManager.T("ProsthLookup_InUseTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (switchToDisable == MessageBoxResult.Yes)
            {
                try
                {
                    _lookupRepo!.SetStageActive(row.Stage.StageID, false, _currentUser);
                    LoadStages();
                }
                catch (Exception ex2)
                {
                    StageErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex2.Message);
                }
            }
        }
        catch (Exception ex)
        {
            StageErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex.Message);
        }
    }

    private void BtnMoveStageUp_Click(object sender, RoutedEventArgs e) => MoveStage(sender, moveUp: true);
    private void BtnMoveStageDown_Click(object sender, RoutedEventArgs e) => MoveStage(sender, moveUp: false);

    private void MoveStage(object sender, bool moveUp)
    {
        if (sender is not Button { Tag: StageRowViewModel row }) return;
        try
        {
            _lookupRepo!.MoveStage(row.Stage.StageID, moveUp, _currentUser);
            LoadStages();
        }
        catch (Exception ex)
        {
            StageErrorText.Text = LocalizationManager.T("ProsthLookup_ErrorFormat", ex.Message);
        }
    }
}

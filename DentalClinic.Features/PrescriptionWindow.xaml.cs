using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Drawing.Printing;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class PrescriptionWindow : Window
{
    private readonly string _patientName;
    private readonly int? _patientAge;
    public ObservableCollection<PrescriptionLineViewModel> Lines { get; } = new();

    public PrescriptionWindow(string patientName, List<string>? initialMedicationNames = null, int? patientAge = null,
        List<string>? initialCertificateNames = null)
    {
        _patientName = patientName;
        _patientAge = patientAge;
        InitializeComponent();

        // نفس إصلاح PatientFileWindow: لا تسمح للنافذة أن تتجاوز الشاشات الصغيرة
        var maxAvailableHeight = SystemParameters.WorkArea.Height - 20;
        if (Height > maxAvailableHeight) Height = maxAvailableHeight;

        LinesItems.ItemsSource = Lines;
        LinesItems.ItemTemplateSelector = new PrescriptionLineTemplateSelector
        {
            MedicationTemplate = (DataTemplate)FindResource("MedicationLineTemplate"),
            CertificateTemplate = (DataTemplate)FindResource("CertificateLineTemplate")
        };
        PatientHeaderText.Text = LocalizationManager.T("Rx_PatientHeaderFormat", patientName);
        DateText.Text = DateTime.Now.ToString("yyyy-MM-dd");

        List<MedicationPreset> presets = new();
        List<CertificatePreset> certificatePresets = new();
        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var presetRepo = new MedicationPresetRepository(db);
            presets = presetRepo.GetActivePresets();
            PresetBox.ItemsSource = presets;

            var certificatePresetRepo = new CertificatePresetRepository(db);
            certificatePresets = certificatePresetRepo.GetActivePresets();
            CertificatePresetBox.ItemsSource = certificatePresets;

                // Populate printer picker with installed printers
                try
                {
                    var printers = DentalClinic.Printing.SilentPdfPrinter.GetInstalledPrinterNames();
                    PrinterBox.ItemsSource = printers;
                    var defaultPrinter = new PrinterSettings().PrinterName;
                    if (!string.IsNullOrWhiteSpace(defaultPrinter) && printers.Contains(defaultPrinter))
                    {
                        PrinterBox.SelectedItem = defaultPrinter;
                    }
                }
                catch
                {
                    // Ignore printer list errors; printing will handle errors at print time
                }
        }
        catch
        {
            // القائمة السريعة اختيارية بحتة - عدم توفرها لا يمنع كتابة وصفة يدوياً بالكامل
        }

        // كل دواء تم اختياره في ملف المريض يظهر مباشرة كسطر جاهز هنا - مع تعبئة الجرعة/المدة
        // تلقائياً إن وُجد دواء بنفس الاسم في القائمة السريعة، وإلا يبقى السطر بلا جرعة/مدة لتُكتب يدوياً
        if (initialMedicationNames != null)
        {
            foreach (var name in initialMedicationNames.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var matchedPreset = presets.FirstOrDefault(p => p.MedicationName == name);
                Lines.Add(new PrescriptionLineViewModel
                {
                    MedicationName = name.Trim(),
                    Dosage = matchedPreset?.DefaultDosage ?? string.Empty
                });
            }
        }

        // نفس المنطق تماماً للشهادات/العطل المختارة في ملف المريض: تظهر مباشرة هنا كسطر جاهز،
        // بنص الفقرة الكامل (DefaultText) إن وُجدت شهادة بنفس الاسم في القائمة السريعة
        if (initialCertificateNames != null)
        {
            foreach (var name in initialCertificateNames.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var matchedCertificate = certificatePresets.FirstOrDefault(c => c.CertificateName == name);
                Lines.Add(new PrescriptionLineViewModel
                {
                    MedicationName = matchedCertificate?.DefaultText ?? name.Trim(),
                    Dosage = string.Empty,
                    BoxCount = string.Empty,
                    IsCertificate = true
                });
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AddPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not MedicationPreset preset)
        {
            ErrorText.Text = LocalizationManager.T("Rx_SelectMedicationFirst");
            return;
        }

        ErrorText.Text = string.Empty;
        Lines.Add(new PrescriptionLineViewModel
        {
            MedicationName = preset.MedicationName,
            Dosage = preset.DefaultDosage ?? string.Empty
        });
    }

    private void AddCustomLineButton_Click(object sender, RoutedEventArgs e)
    {
        Lines.Add(new PrescriptionLineViewModel());
    }

    private void AddCertificatePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (CertificatePresetBox.SelectedItem is not CertificatePreset preset)
        {
            ErrorText.Text = LocalizationManager.T("Rx_SelectCertificateFirst");
            return;
        }

        ErrorText.Text = string.Empty;
        Lines.Add(new PrescriptionLineViewModel
        {
            MedicationName = preset.DefaultText ?? preset.CertificateName,
            Dosage = string.Empty,
            BoxCount = string.Empty,
            IsCertificate = true
        });
    }

    private void AddCustomCertificateButton_Click(object sender, RoutedEventArgs e)
    {
        Lines.Add(new PrescriptionLineViewModel { IsCertificate = true, BoxCount = string.Empty });
    }

    private void RemoveLineButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PrescriptionLineViewModel line })
        {
            Lines.Remove(line);
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var validLines = Lines.Where(l => !string.IsNullOrWhiteSpace(l.MedicationName)).ToList();
        if (validLines.Count == 0)
        {
            ErrorText.Text = LocalizationManager.T("Rx_AddAtLeastOneLine");
            return;
        }

        try
        {
            // Determine selected printer (if user picked one)
            string? selectedPrinter = null;
            try
            {
                if (PrinterBox?.SelectedItem is string s && !string.IsNullOrWhiteSpace(s)) selectedPrinter = s;
            }
            catch
            {
                // ignore; selectedPrinter stays null
            }

            // Silent direct print using SilentPdfPrinter (PdfiumViewer)
            PrescriptionPdfExporter.GenerateAndPrint(_patientName, DateTime.Now, validLines, NotesBox.Text, _patientAge, selectedPrinter);

            // Provide user feedback on success
            try
            {
                var successMsg = LocalizationManager.T("Rx_PrintSuccess");
                if (string.IsNullOrWhiteSpace(successMsg)) successMsg = "Prescription sent to printer.";
                var title = LocalizationManager.T("Rx_Title");
                if (string.IsNullOrWhiteSpace(title)) title = "Information";
                MessageBox.Show(this, successMsg, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                // If localization or MessageBox fails for any reason, fall back to a simple close
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = LocalizationManager.T("Rx_GenerateFailedFormat", ex.Message);
        }
    }
}

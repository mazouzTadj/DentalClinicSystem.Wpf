using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DentalClinic.Data.DataAccess;
using DentalClinic.Data.Models;
using DentalClinic.UI.Localization;

namespace DentalClinic.Features;

public partial class PrescriptionWindow : Window
{
    private readonly string _patientName;
    private readonly int? _patientAge;
    public ObservableCollection<PrescriptionLineViewModel> Lines { get; } = new();

    public PrescriptionWindow(string patientName, List<string>? initialMedicationNames = null, int? patientAge = null)
    {
        _patientName = patientName;
        _patientAge = patientAge;
        InitializeComponent();

        LinesItems.ItemsSource = Lines;
        PatientHeaderText.Text = LocalizationManager.T("Rx_PatientHeaderFormat", patientName);
        DateText.Text = DateTime.Now.ToString("yyyy-MM-dd");

        List<MedicationPreset> presets = new();
        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var presetRepo = new MedicationPresetRepository(db);
            presets = presetRepo.GetActivePresets();
            PresetBox.ItemsSource = presets;
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
                    Dosage = matchedPreset?.DefaultDosage ?? string.Empty,
                    Duration = matchedPreset?.DefaultDuration ?? string.Empty
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
            Dosage = preset.DefaultDosage ?? string.Empty,
            Duration = preset.DefaultDuration ?? string.Empty
        });
    }

    private void AddCustomLineButton_Click(object sender, RoutedEventArgs e)
    {
        Lines.Add(new PrescriptionLineViewModel());
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
            ErrorText.Text = LocalizationManager.T("Rx_AddAtLeastOneMedication");
            return;
        }

        try
        {
            var pdfBytes = PrescriptionPdfExporter.Generate(_patientName, DateTime.Now, validLines, NotesBox.Text, _patientAge);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"Prescription_{_patientName.Replace(' ', '_')}_{DateTime.Now:yyyy-MM-dd}.pdf",
                Filter = LocalizationManager.T("PF_PdfFilter"),
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            System.IO.File.WriteAllBytes(dialog.FileName, pdfBytes);

            var openIt = MessageBox.Show(
                LocalizationManager.T("Rx_SavedMessage"),
                LocalizationManager.T("PF_ExportCompleteTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (openIt == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName)
                {
                    UseShellExecute = true
                });
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

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
    public ObservableCollection<PrescriptionLineViewModel> Lines { get; } = new();

    public PrescriptionWindow(string patientName, string? initialMedicationText)
    {
        _patientName = patientName;
        InitializeComponent();

        LinesItems.ItemsSource = Lines;
        PatientHeaderText.Text = LocalizationManager.T("Rx_PatientHeaderFormat", patientName);
        DateText.Text = DateTime.Now.ToString("yyyy-MM-dd");

        if (!string.IsNullOrWhiteSpace(initialMedicationText))
        {
            Lines.Add(new PrescriptionLineViewModel { MedicationName = initialMedicationText.Trim() });
        }

        try
        {
            var connectionString = ConfigurationManager.ConnectionStrings["DentalClinicDB"].ConnectionString;
            var db = new DatabaseHelper(connectionString);
            var presetRepo = new MedicationPresetRepository(db);
            var presets = presetRepo.GetActivePresets();
            PresetBox.ItemsSource = presets;
        }
        catch
        {
            // القائمة السريعة اختيارية بحتة - عدم توفرها لا يمنع كتابة وصفة يدوياً بالكامل
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
            ErrorText.Text = LocalizationManager.T("Rx_AddAtLeastOne");
            return;
        }

        try
        {
            var pdfBytes = PrescriptionPdfExporter.Generate(_patientName, DateTime.Now, validLines, NotesBox.Text);

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"Prescription_{_patientName.Replace(' ', '_')}_{DateTime.Now:yyyy-MM-dd}.pdf",
                Filter = "PDF Files (*.pdf)|*.pdf",
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            System.IO.File.WriteAllBytes(dialog.FileName, pdfBytes);

            var openIt = MessageBox.Show(
                LocalizationManager.T("Rx_SavedSuccessMessage"),
                LocalizationManager.T("Rx_ExportCompleteTitle"),
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
            ErrorText.Text = LocalizationManager.T("Rx_GenerateErrorFormat", ex.Message);
        }
    }
}

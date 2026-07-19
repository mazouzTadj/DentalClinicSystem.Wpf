using System.Windows;
using DentalClinic.Data.Models;

namespace DentalClinic.DoctorApp;

public partial class MainWindow : Window
{
    private readonly UserAccount _currentUser;

    public MainWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        Title = $"Doctor App - Dental Clinic | Dr. {_currentUser.FullName}";
    }
}

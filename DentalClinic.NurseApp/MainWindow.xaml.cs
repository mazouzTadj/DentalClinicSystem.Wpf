using System.Windows;
using DentalClinic.Data.Models;

namespace DentalClinic.NurseApp;

public partial class MainWindow : Window
{
    private readonly UserAccount _currentUser;

    public MainWindow(UserAccount currentUser)
    {
        _currentUser = currentUser;
        InitializeComponent();
        Title = $"Reception App - Dental Clinic | Welcome {_currentUser.FullName}";
    }
}

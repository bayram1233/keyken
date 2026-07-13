using System.Windows;
using FendrSystemCare.ViewModels;

namespace FendrSystemCare.Views;

public partial class LicenseWindow : Window
{
    public LicenseWindow(LicenseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

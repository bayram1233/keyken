using System.Windows.Controls;
using System.Windows.Input;
using FendrSystemCare.ViewModels;
namespace FendrSystemCare.Views;

public partial class DriverCenterPage : UserControl
{
    public DriverCenterPage() => InitializeComponent();

    private void OnDeviceDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is DriverCenterViewModel vm)
            vm.ShowDeviceDetailsCommand.Execute(null);
    }
}

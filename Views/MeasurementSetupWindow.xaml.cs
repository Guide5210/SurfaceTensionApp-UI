using System.Windows;

namespace SurfaceTensionApp.Views;

public partial class MeasurementSetupWindow : Window
{
    public MeasurementSetupWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

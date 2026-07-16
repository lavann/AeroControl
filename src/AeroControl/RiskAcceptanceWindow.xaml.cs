using System.Windows;

namespace AeroControl;

public partial class RiskAcceptanceWindow : Window
{
    public RiskAcceptanceWindow()
    {
        InitializeComponent();
    }

    public bool Accepted { get; private set; }

    private void AcceptCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        AcceptButton.IsEnabled = AcceptCheckBox.IsChecked == true;
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

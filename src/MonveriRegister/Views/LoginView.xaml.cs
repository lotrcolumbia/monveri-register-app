using System.Windows;
using System.Windows.Controls;

namespace MonveriRegister.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (PinInput.Visibility == Visibility.Visible)
            PinInput.Focus();
    }
}

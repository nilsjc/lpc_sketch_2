using Avalonia.Controls;
using UserGUI.ViewModels;

namespace UserGUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
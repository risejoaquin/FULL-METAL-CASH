using System.Windows;
using SolidPOS.PosBuilder.Wpf.Composition;

namespace SolidPOS.PosBuilder.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var shell = new MainWindow { DataContext = PosBuilderCompositionRoot.CreateBuilderViewModel() };
        shell.Show();
    }
}

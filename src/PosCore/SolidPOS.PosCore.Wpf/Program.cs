using System;
using Velopack;

namespace SolidPOS.PosCore.Wpf;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        // Velopack lifecycle hooks must be the first application lifecycle action.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

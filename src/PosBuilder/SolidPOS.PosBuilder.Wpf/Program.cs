using System;
using System.Linq;

namespace SolidPOS.PosBuilder.Wpf;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return PosBuilderSelfTestRunner.Run(args);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

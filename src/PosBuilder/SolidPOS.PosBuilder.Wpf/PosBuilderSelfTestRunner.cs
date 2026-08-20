using System;
using System.Collections.Generic;
using System.IO;
using SolidPOS.PosBuilder.Wpf.Composition;

namespace SolidPOS.PosBuilder.Wpf;

public static class PosBuilderSelfTestRunner
{
    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        return null;
    }

    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            Directory.CreateDirectory(".\\.runtime");
            var output = GetOption(args, "--output") ?? ".\\.runtime\\tenant-branding-package.json";
            var viewModel = PosBuilderCompositionRoot.CreateBuilderViewModel();
            viewModel.OutputPath = output;
            viewModel.GeneratePackage();
            viewModel.ValidatePackage();

            var lines = new[]
            {
                "PosBuilder branding self-test started.",
                $"Builder shell initialized: {viewModel.Status}",
                $"Tenant branding package generated: tenantName={viewModel.TenantName}; appName={viewModel.AppName}; output={viewModel.OutputPath}",
                $"Branding colors: primary={viewModel.PrimaryColorHex}; accent={viewModel.AccentColorHex}",
                $"Branding validation: isValid={viewModel.IsValid}; errors={viewModel.ErrorCount}; warnings={viewModel.WarningCount}",
                "PosBuilder tenant branding package validation completed."
            };

            File.WriteAllLines(".\\.runtime\\posbuilder-branding-self-test.log", lines);
            foreach (var line in lines) Console.WriteLine(line);
            return viewModel.IsValid ? 0 : 2;
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(".\\.runtime");
            var lines = new[]
            {
                "PosBuilder branding self-test failed.",
                ex.GetType().FullName ?? "Exception",
                ex.Message
            };
            File.WriteAllLines(".\\.runtime\\posbuilder-branding-self-test.log", lines);
            foreach (var line in lines) Console.Error.WriteLine(line);
            return 1;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using SolidPOS.PosBuilder.Wpf.Composition;
using SolidPOS.PosCore.Application.Updates;
using SolidPOS.PosCore.Infrastructure.Updates;

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

            var lines = new List<string>
            {
                "PosBuilder branding self-test started.",
                $"Builder shell initialized: {viewModel.Status}",
                $"Tenant branding package generated: tenantName={viewModel.TenantName}; appName={viewModel.AppName}; output={viewModel.OutputPath}",
                $"Branding colors: primary={viewModel.PrimaryColorHex}; accent={viewModel.AccentColorHex}",
                $"Branding validation: isValid={viewModel.IsValid}; errors={viewModel.ErrorCount}; warnings={viewModel.WarningCount}",
                "PosBuilder tenant branding package validation completed."
            };

            var updatePackagePath = GetOption(args, "--update-package");
            var updateManifestPath = GetOption(args, "--update-manifest");
            if (!string.IsNullOrWhiteSpace(updatePackagePath) && !string.IsNullOrWhiteSpace(updateManifestPath))
            {
                var updateDirectory = Path.GetDirectoryName(updatePackagePath);
                if (!string.IsNullOrWhiteSpace(updateDirectory)) Directory.CreateDirectory(updateDirectory);
                File.WriteAllLines(updatePackagePath, new[]
                {
                    "SolidPOS PosBuilder self-test update package",
                    $"tenantId={viewModel.TenantId}",
                    $"tenantName={viewModel.TenantName}",
                    $"appName={viewModel.AppName}",
                    $"releaseVersion={GetOption(args, "--release-version") ?? "1.0.0"}",
                    $"channel={GetOption(args, "--channel") ?? "stable"}"
                });

                var updateService = new UpdatePackageManifestService(new JsonUpdatePackageManifestStore());
                var updateManifest = updateService.CreateFromPackageFile(
                    Guid.Parse(viewModel.TenantId),
                    viewModel.TenantName,
                    viewModel.AppName,
                    GetOption(args, "--release-version") ?? "1.0.0",
                    GetOption(args, "--channel") ?? "stable",
                    "local-poscore-package",
                    updatePackagePath,
                    "1.0.0",
                    "1.0.0",
                    "1.0",
                    DateTimeOffset.UtcNow,
                    "PosBuilder self-test update package.");
                updateService.SaveValidatedAsync(updateManifest, updateManifestPath, updatePackagePath).GetAwaiter().GetResult();
                var updateValidation = updateService.Validate(updateManifest, updatePackagePath);
                lines.Add($"Update package manifest generated: releaseVersion={updateManifest.ReleaseVersion}; channel={updateManifest.Channel}; packageFile={updateManifest.PackageFileName}; sha256={updateManifest.Sha256}");
                lines.Add($"Update package validation: isValid={updateValidation.IsValid}; errors={updateValidation.Errors.Count}; warnings={updateValidation.Warnings.Count}");
            }

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

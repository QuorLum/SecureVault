using SecureVault.Installer.Forms;
using SecureVault.Installer.Services;

namespace SecureVault.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Check if running in uninstall mode
        if (args.Length > 0 && (args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
                               args[0].Equals("-uninstall", StringComparison.OrdinalIgnoreCase) ||
                               args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstall();
            return;
        }

        Application.Run(new InstallerForm());
    }

    private static void RunUninstall()
    {
        var confirm = MessageBox.Show(
            "Are you sure you want to completely uninstall SecureVault and remove all shortcuts?\n\nNote: Your encrypted .vault data containers will NOT be deleted.",
            "Uninstall SecureVault",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            string installDir = AppDomain.CurrentDomain.BaseDirectory;
            InstallerEngine.UninstallAsync(installDir).GetAwaiter().GetResult();

            MessageBox.Show(
                "SecureVault was successfully removed from your computer.",
                "Uninstall Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"An error occurred during uninstallation:\n\n{ex.Message}",
                "Uninstall Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

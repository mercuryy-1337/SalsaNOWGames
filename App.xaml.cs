using System;
using System.Threading.Tasks;
using System.Windows;
using SalsaNOWGames.Services;
using SalsaNOWGames.Views;

namespace SalsaNOWGames
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Check for updates before showing main window
            await CheckForUpdatesAsync();
            
            // Show main window
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var updateService = new UpdateService();
                var info = await updateService.CheckForUpdateAsync();
                
                if (info != null && !string.IsNullOrEmpty(info.DownloadUrl))
                {
                    // Ask user if they want to update using modern dialog
                    bool wantsUpdate = ModernDialog.ShowUpdate(null, info.Version);

                    if (wantsUpdate)
                    {
                        try
                        {
                            // Show downloading message
                            ModernDialog.ShowInfo(null, "Downloading Update", 
                                "Downloading update...\n\nThe app will restart automatically.",
                                "Please wait while the update is downloaded.");

                            var downloaded = await updateService.DownloadUpdateAsync(info);
                            if (string.IsNullOrEmpty(downloaded))
                            {
                                ModernDialog.ShowWarning(null, "Update Failed",
                                    "Failed to download update.\n\nThe app will continue with the current version.");
                                return;
                            }

                            var updater = new UpdaterService();
                            bool launched = updater.LaunchSeamlessUpdate(downloaded);
                            if (launched)
                            {
                                // Shut down to allow update
                                Shutdown();
                                return;
                            }
                            else
                            {
                                ModernDialog.ShowWarning(null, "Update Failed",
                                    "Failed to launch updater.\n\nThe app will continue with the current version.");
                            }
                        }
                        catch (Exception ex)
                        {
                            ModernDialog.ShowError(null, "Update Failed",
                                $"Update failed: {ex.Message}",
                                "The app will continue with the current version.");
                        }
                    }
                }
            }
            catch
            {
                // Silently ignore update check failures - don't block app startup
            }
        }
    }
}

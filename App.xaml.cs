using System;
using System.Threading.Tasks;
using System.Windows;
using SalsaNOWGames.Services;

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
                    // Ask user if they want to update
                    var result = MessageBox.Show(
                        $"A new version ({info.Version}) is available.\n\nWould you like to update now?",
                        "Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            // Show downloading message
                            MessageBox.Show(
                                "Downloading update... The app will restart automatically.",
                                "Updating",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                            var downloaded = await updateService.DownloadUpdateAsync(info);
                            if (string.IsNullOrEmpty(downloaded))
                            {
                                MessageBox.Show(
                                    "Failed to download update. The app will continue with the current version.",
                                    "Update Failed",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
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
                                MessageBox.Show(
                                    "Failed to launch updater. The app will continue with the current version.",
                                    "Update Failed",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                $"Update failed: {ex.Message}\n\nThe app will continue with the current version.",
                                "Update Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
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

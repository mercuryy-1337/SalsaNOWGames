using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SalsaNOWGames.Models;

namespace SalsaNOWGames.Services
{
    public class DepotDownloaderService
    {
        private readonly string _salsaNowDirectory;
        private readonly string _depotDownloaderPath;
        private readonly string _gamesDirectory;
        private Process _currentProcess;
        private CancellationTokenSource _cancellationTokenSource;
        private string _currentDownloadAppId;
        
        // Throttle output to prevent UI overhead affecting download speed
        private DateTime _lastOutputTime = DateTime.MinValue;
        private int _preallocFileCount = 0;
        private bool _isPreallocating = false;
        
        // Debug mode for verbose logging to output window
        public bool DebugMode { get; set; } = false;
        
        // Track if current download is using no-mobile mode (manual code entry)
        public bool IsNoMobileMode { get; private set; } = false;

        public event Action<string> OnOutputReceived;
        public event Action<double> OnProgressChanged;
        public event Action<bool, string> OnDownloadComplete;
        public event Action<string> OnSteamGuardRequired;
        public event Action<bool, int> OnPreallocatingChanged;
        public event Action<bool> OnCleanupInProgress;
        public event Action<bool> OnNoMobileModeChanged;

        public DepotDownloaderService(string installDirectory = null)
        {
            _salsaNowDirectory = @"I:\Apps\SalsaNOW";
            _depotDownloaderPath = Path.Combine(_salsaNowDirectory, "DepotDownloader", "DepotDownloader.exe");
            _gamesDirectory = installDirectory ?? Path.Combine(_salsaNowDirectory, "DepotDownloader", "Games");
        }

        public string GamesDirectory => _gamesDirectory;

        public async Task EnsureDepotDownloaderInstalledAsync()
        {
            string zipPath = Path.Combine(_salsaNowDirectory, "DepotDownloader-windows-x64.zip");
            string extractPath = Path.Combine(_salsaNowDirectory, "DepotDownloader");
            string depotDownloaderUrl = "https://github.com/dpadGuy/SalsaNOWThings/releases/download/Things/DepotDownloader-windows-x64.zip";

            try
            {
                Directory.CreateDirectory(_salsaNowDirectory);

                if (!File.Exists(_depotDownloaderPath))
                {
                    OnOutputReceived?.Invoke("Downloading DepotDownloader...");

                    using (WebClient webClient = new WebClient())
                    {
                        await webClient.DownloadFileTaskAsync(new Uri(depotDownloaderUrl), zipPath);
                    }

                    OnOutputReceived?.Invoke("Extracting DepotDownloader...");
                    
                    if (Directory.Exists(extractPath))
                    {
                        Directory.Delete(extractPath, true);
                    }
                    
                    ZipFile.ExtractToDirectory(zipPath, extractPath);
                    
                    // Clean up zip file
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }

                    OnOutputReceived?.Invoke("DepotDownloader ready!");
                }
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"Error setting up DepotDownloader: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DownloadGameAsync(string appId, string username, string password, bool noMobile = false)
        {
            try
            {
                await EnsureDepotDownloaderInstalledAsync();

                string gameDirectory = Path.Combine(_gamesDirectory, appId);
                Directory.CreateDirectory(gameDirectory);

                // Create steam_appid.txt
                File.WriteAllText(Path.Combine(gameDirectory, "steam_appid.txt"), appId);

                // Track current download for cleanup on cancel
                _currentDownloadAppId = appId;
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Reset tracking state
                _isPreallocating = false;
                _preallocFileCount = 0;
                _lastOutputTime = DateTime.MinValue;
                
                // Track no-mobile mode and notify UI
                IsNoMobileMode = noMobile;
                OnNoMobileModeChanged?.Invoke(noMobile);

                // Build arguments - include -no-mobile if user wants manual code entry
                string noMobileArg = noMobile ? " -no-mobile" : "";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _depotDownloaderPath,
                    Arguments = $"-app \"{appId}\" -username \"{username}\" -password \"{password}\" -remember-password -os windows{noMobileArg} -dir \"{gameDirectory}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                };

                _currentProcess = new Process { StartInfo = psi };
                
                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    
                    // Handle pre-allocation - heavily throttled
                    if (e.Data.StartsWith("Pre-allocating"))
                    {
                        _preallocFileCount++;
                        
                        // Only update UI every 1 second during pre-allocation
                        var now = DateTime.Now;
                        if ((now - _lastOutputTime).TotalMilliseconds >= 1000)
                        {
                            _lastOutputTime = now;
                            OnOutputReceived?.Invoke($"Pre-allocating... ({_preallocFileCount} files)");
                        }
                        
                        if (!_isPreallocating)
                        {
                            _isPreallocating = true;
                            OnPreallocatingChanged?.Invoke(true, 0);
                        }
                        return;
                    }
                    
                    // End of pre-allocation phase
                    if (_isPreallocating)
                    {
                        OnOutputReceived?.Invoke($"Pre-allocation complete. ({_preallocFileCount} files)");
                        _isPreallocating = false;
                        _preallocFileCount = 0;
                        OnPreallocatingChanged?.Invoke(false, 0);
                    }
                    
                    // Check for Steam Guard prompt - always process immediately
                    // Catch various Steam Guard/2FA prompts from DepotDownloader (need to change this soon, like real soon)
                    string dataLower = e.Data.ToLowerInvariant();
                    if (e.Data.Contains("STEAM GUARD") || 
                        e.Data.Contains("two-factor") || 
                        e.Data.Contains("2FA") || 
                        e.Data.Contains("Please enter") ||
                        e.Data.Contains("Enter the current code") ||
                        dataLower.Contains("steam guard") ||
                        dataLower.Contains("authenticator") ||
                        dataLower.Contains("verification code") ||
                        dataLower.Contains("enter code") ||
                        dataLower.Contains("enter your code") ||
                        dataLower.Contains("mobile authenticator") ||
                        dataLower.Contains("email code") ||
                        (dataLower.Contains("code") && dataLower.Contains("enter")))
                    {
                        OnOutputReceived?.Invoke(e.Data);
                        OnSteamGuardRequired?.Invoke(e.Data);
                        return;
                    }
                    
                    // Check for progress - always parse but throttle UI updates
                    bool hasProgress = ParseProgress(e.Data);
                    
                    // Important messages to always show
                    bool isImportant = e.Data.Contains("Downloading depot") ||
                                       e.Data.Contains("Download complete") ||
                                       e.Data.Contains("Total downloaded") ||
                                       e.Data.Contains("Error") ||
                                       e.Data.Contains("error") ||
                                       e.Data.Contains("Failed") ||
                                       e.Data.Contains("failed") ||
                                       e.Data.Contains("Logging") ||
                                       e.Data.Contains("logged") ||
                                       e.Data.Contains("Got session") ||
                                       e.Data.Contains("Connecting");
                    
                    if (isImportant)
                    {
                        OnOutputReceived?.Invoke(e.Data);
                        return;
                    }
                    
                    // In debug mode, show everything (throttled)
                    if (DebugMode)
                    {
                        var now = DateTime.Now;
                        if ((now - _lastOutputTime).TotalMilliseconds >= 100)
                        {
                            _lastOutputTime = now;
                            OnOutputReceived?.Invoke(e.Data);
                        }
                    }
                };
                
                _currentProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // Check stderr for Steam Guard prompts too (some versions output there)
                        string dataLower = e.Data.ToLowerInvariant();
                        if (dataLower.Contains("steam guard") ||
                            dataLower.Contains("two-factor") ||
                            dataLower.Contains("2fa") ||
                            dataLower.Contains("enter") && dataLower.Contains("code") ||
                            dataLower.Contains("authenticator"))
                        {
                            OnOutputReceived?.Invoke(e.Data);
                            OnSteamGuardRequired?.Invoke(e.Data);
                            return;
                        }
                        OnOutputReceived?.Invoke($"[Error] {e.Data}");
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                await Task.Run(() => _currentProcess?.WaitForExit(), _cancellationTokenSource.Token);

                // Check if process was cancelled/killed
                if (_currentProcess == null)
                {
                    return false;
                }

                bool success = _currentProcess.ExitCode == 0;
                OnDownloadComplete?.Invoke(success, success ? "Download complete!" : "Download failed.");
                
                _currentProcess = null;
                if (success)
                {
                    _currentDownloadAppId = null; // Clear on success, keep for cleanup on failure
                }
                return success;
            }
            catch (OperationCanceledException)
            {
                // Don't clear _currentDownloadAppId here - CancelDownload will handle cleanup
                OnDownloadComplete?.Invoke(false, "Download cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                // Ignore null reference errors from cancelled downloads
                if (_currentProcess == null)
                {
                    return false;
                }
                OnOutputReceived?.Invoke($"Error: {ex.Message}");
                OnDownloadComplete?.Invoke(false, ex.Message);
                return false;
            }
        }

        /* Submit Steam Guard code to the running process */
        public void SubmitSteamGuardCode(string code)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    _currentProcess.StandardInput.WriteLine(code);
                    _currentProcess.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    OnOutputReceived?.Invoke($"Error submitting code: {ex.Message}");
                }
            }
        }

        /*
         * Download a game using saved Steam session (WebView2 login)
         * Uses the -remember-password flag which looks for saved credentials in ~/.DepotDownloader/
         */
        public async Task<bool> DownloadGameWithSessionAsync(string appId, SteamSession session)
        {
            try
            {
                await EnsureDepotDownloaderInstalledAsync();

                // Make sure DepotDownloader config is set up with session
                var authService = new SteamAuthService();
                authService.SaveDepotDownloaderConfig(session);

                string gameDirectory = Path.Combine(_gamesDirectory, appId);
                Directory.CreateDirectory(gameDirectory);

                // Create steam_appid.txt
                File.WriteAllText(Path.Combine(gameDirectory, "steam_appid.txt"), appId);

                // Track current download for cleanup on cancel
                _currentDownloadAppId = appId;
                _cancellationTokenSource = new CancellationTokenSource();

                // Use -remember-password to use saved credentials from ~/.DepotDownloader/
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _depotDownloaderPath,
                    Arguments = $"-app \"{appId}\" -remember-password -os windows -dir \"{gameDirectory}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _currentProcess = new Process { StartInfo = psi };
                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        OnOutputReceived?.Invoke(e.Data);
                        ParseProgress(e.Data);
                    }
                };
                _currentProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        OnOutputReceived?.Invoke($"[Error] {e.Data}");
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                await Task.Run(() => _currentProcess.WaitForExit(), _cancellationTokenSource.Token);

                bool success = _currentProcess.ExitCode == 0;
                OnDownloadComplete?.Invoke(success, success ? "Download complete!" : "Download failed.");
                
                _currentProcess = null;
                return success;
            }
            catch (OperationCanceledException)
            {
                OnDownloadComplete?.Invoke(false, "Download cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"Error: {ex.Message}");
                OnDownloadComplete?.Invoke(false, ex.Message);
                return false;
            }
        }

        public void CancelDownload()
        {
            string appIdToClean = _currentDownloadAppId;
            _currentDownloadAppId = null;
            
            try
            {
                _cancellationTokenSource?.Cancel();
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill();
                    _currentProcess = null;
                }
            }
            catch { }

            // Clean up partial download files asynchronously to prevent UI freeze
            if (!string.IsNullOrEmpty(appIdToClean))
            {
                string gameDirectory = Path.Combine(_gamesDirectory, appIdToClean);
                if (Directory.Exists(gameDirectory))
                {
                    // Fire and forget - cleanup runs in background
                    Task.Run(() => CleanupDirectory(gameDirectory));
                }
            }
        }

        private void CleanupDirectory(string directoryPath)
        {
            try
            {
                OnCleanupInProgress?.Invoke(true);
                OnOutputReceived?.Invoke("Cleaning up partial download...");
                
                var dir = new DirectoryInfo(directoryPath);
                if (!dir.Exists) return;

                // Delete files in batches to allow progress updates
                var files = dir.GetFiles("*", SearchOption.AllDirectories);
                int totalFiles = files.Length;
                int deletedCount = 0;
                DateTime lastUpdate = DateTime.MinValue;

                foreach (var file in files)
                {
                    try
                    {
                        file.Delete();
                        deletedCount++;
                        
                        // Throttle UI updates to every 500ms
                        var now = DateTime.Now;
                        if ((now - lastUpdate).TotalMilliseconds >= 500)
                        {
                            lastUpdate = now;
                            OnOutputReceived?.Invoke($"Cleaning up... ({deletedCount:N0}/{totalFiles:N0} files deleted)");
                        }
                    }
                    catch { }
                }

                // Delete empty directories bottom-up
                var dirs = dir.GetDirectories("*", SearchOption.AllDirectories);
                // Sort by depth (deepest first) to delete children before parents
                Array.Sort(dirs, (a, b) => b.FullName.Length.CompareTo(a.FullName.Length));
                
                foreach (var subDir in dirs)
                {
                    try
                    {
                        if (subDir.Exists && subDir.GetFiles().Length == 0 && subDir.GetDirectories().Length == 0)
                        {
                            subDir.Delete();
                        }
                    }
                    catch { }
                }

                // Finally delete the root directory
                try
                {
                    if (dir.Exists)
                    {
                        dir.Delete(true);
                    }
                }
                catch { }

                OnOutputReceived?.Invoke($"Cleanup complete. Deleted {deletedCount:N0} files.");
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"Cleanup error: {ex.Message}");
            }
            finally
            {
                OnCleanupInProgress?.Invoke(false);
            }
        }

        private bool ParseProgress(string output)
        {
            try
            {
                // Try to parse progress from DepotDownloader output
                // Example: "50.00% 500.00 MB / 1.00 GB"
                var match = Regex.Match(output, @"(\d+\.?\d*)\s*%");
                if (match.Success && double.TryParse(match.Groups[1].Value, out double progress))
                {
                    OnProgressChanged?.Invoke(progress);
                    return true;
                }
            }
            catch { }
            return false;
        }

        public string GetGameInstallPath(string appId)
        {
            return Path.Combine(_gamesDirectory, appId);
        }

        public bool IsGameInstalled(string appId)
        {
            string gamePath = GetGameInstallPath(appId);
            return Directory.Exists(gamePath) && 
                   File.Exists(Path.Combine(gamePath, "steam_appid.txt"));
        }

        public long GetGameSize(string appId)
        {
            string gamePath = GetGameInstallPath(appId);
            if (!Directory.Exists(gamePath)) return 0;

            return GetDirectorySize(new DirectoryInfo(gamePath));
        }

        /*
         * Recursively calculates directory size
         * Source: https://stackoverflow.com/questions/468119/whats-the-best-way-to-calculate-the-size-of-a-directory-in-net
         */
        private long GetDirectorySize(DirectoryInfo dir)
        {
            long size = 0;
            try
            {
                foreach (FileInfo file in dir.GetFiles())
                {
                    size += file.Length;
                }
                foreach (DirectoryInfo subDir in dir.GetDirectories())
                {
                    size += GetDirectorySize(subDir);
                }
            }
            catch { }
            return size;
        }

        public void DeleteGameAsync(string appId, Action<bool> onComplete)
        {
            string gamePath = GetGameInstallPath(appId);
            if (!Directory.Exists(gamePath))
            {
                onComplete?.Invoke(false);
                return;
            }

            // Run deletion on background thread to prevent UI freeze
            Task.Run(() =>
            {
                try
                {
                    CleanupDirectory(gamePath);
                    onComplete?.Invoke(true);
                }
                catch (Exception ex)
                {
                    OnOutputReceived?.Invoke($"Error deleting game: {ex.Message}");
                    onComplete?.Invoke(false);
                }
            });
        }

        public void OpenGameFolder(string appId)
        {
            string gamePath = GetGameInstallPath(appId);
            if (Directory.Exists(gamePath))
            {
                Process.Start("explorer.exe", gamePath);
            }
        }

        /// <summary>
        /// Downloads the game icon to the game's install folder.
        /// Returns the local icon path or null on failure.
        /// </summary>
        public async Task<string> DownloadGameIconAsync(string appId, string iconUrl)
        {
            if (string.IsNullOrEmpty(iconUrl))
            {
                return null;
            }

            try
            {
                string gamePath = GetGameInstallPath(appId);
                if (!Directory.Exists(gamePath))
                {
                    return null;
                }

                // Determine file extension from URL or default to .jpg
                string extension = ".jpg";
                if (iconUrl.Contains(".png"))
                {
                    extension = ".png";
                }
                else if (iconUrl.Contains(".ico"))
                {
                    extension = ".ico";
                }

                string iconFileName = $"icon{extension}";
                string iconPath = Path.Combine(gamePath, iconFileName);

                // Download the icon
                using (var webClient = new System.Net.WebClient())
                {
                    await webClient.DownloadFileTaskAsync(new Uri(iconUrl), iconPath);
                }

                if (File.Exists(iconPath))
                {
                    LogService.Log($"Downloaded icon for {appId} to {iconPath}");
                    return iconPath;
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to download icon for {appId}", ex);
            }

            return null;
        }
    }
}

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
        private readonly string _logFilePath;
        private Process _currentProcess;
        private CancellationTokenSource _cancellationTokenSource;
        private StreamWriter _logWriter;
        private string _currentDownloadAppId;

        public event Action<string> OnOutputReceived;
        public event Action<double> OnProgressChanged;
        public event Action<bool, string> OnDownloadComplete;
        public event Action<string> OnSteamGuardRequired;

        public DepotDownloaderService(string installDirectory = null)
        {
            _salsaNowDirectory = @"I:\Apps\SalsaNOW";
            _depotDownloaderPath = Path.Combine(_salsaNowDirectory, "DepotDownloader", "DepotDownloader.exe");
            _gamesDirectory = installDirectory ?? Path.Combine(_salsaNowDirectory, "DepotDownloader", "Games");
            _logFilePath = Path.Combine(_salsaNowDirectory, "SalsaNOWGames.log");
            
            // Initialize log file
            InitializeLogFile();
        }

        private void InitializeLogFile()
        {
            try
            {
                Directory.CreateDirectory(_salsaNowDirectory);
                _logWriter = new StreamWriter(_logFilePath, true) { AutoFlush = true };
                LogToFile($"=== SalsaNOW Games Started at {DateTime.Now} ===");
            }
            catch { }
        }

        public void LogToFile(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _logWriter?.WriteLine($"[{timestamp}] {message}");
            }
            catch { }
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

        public async Task<bool> DownloadGameAsync(string appId, string username, string password)
        {
            try
            {
                LogToFile($"Starting download for AppID: {appId}, User: {username}");
                await EnsureDepotDownloaderInstalledAsync();

                string gameDirectory = Path.Combine(_gamesDirectory, appId);
                Directory.CreateDirectory(gameDirectory);

                // Create steam_appid.txt
                File.WriteAllText(Path.Combine(gameDirectory, "steam_appid.txt"), appId);

                // Track current download for cleanup on cancel
                _currentDownloadAppId = appId;
                _cancellationTokenSource = new CancellationTokenSource();

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _depotDownloaderPath,
                    Arguments = $"-app \"{appId}\" -username \"{username}\" -password \"{password}\" -remember-password -os windows -dir \"{gameDirectory}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                };

                _currentProcess = new Process { StartInfo = psi };
                
                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        LogToFile($"[OUT] {e.Data}");
                        OnOutputReceived?.Invoke(e.Data);
                        ParseProgress(e.Data);
                        
                        // Check for Steam Guard prompt
                        if (e.Data.Contains("STEAM GUARD") || e.Data.Contains("two-factor") || 
                            e.Data.Contains("2FA") || e.Data.Contains("Please enter") ||
                            e.Data.Contains("Enter the current code"))
                        {
                            LogToFile("Steam Guard code required!");
                            OnSteamGuardRequired?.Invoke(e.Data);
                        }
                    }
                };
                _currentProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        LogToFile($"[ERR] {e.Data}");
                        OnOutputReceived?.Invoke($"[Error] {e.Data}");
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                LogToFile("Process started, waiting for completion...");
                await Task.Run(() => _currentProcess.WaitForExit(), _cancellationTokenSource.Token);

                bool success = _currentProcess.ExitCode == 0;
                LogToFile($"Download finished. Exit code: {_currentProcess.ExitCode}, Success: {success}");
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
                LogToFile("Download cancelled by user.");
                // Don't clear _currentDownloadAppId here - CancelDownload will handle cleanup
                OnDownloadComplete?.Invoke(false, "Download cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                LogToFile($"Error during download: {ex.Message}");
                OnOutputReceived?.Invoke($"Error: {ex.Message}");
                OnDownloadComplete?.Invoke(false, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Submit Steam Guard code to the running process
        /// </summary>
        public void SubmitSteamGuardCode(string code)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    LogToFile($"Submitting Steam Guard code: {code}");
                    _currentProcess.StandardInput.WriteLine(code);
                    _currentProcess.StandardInput.Flush();
                    LogToFile("Steam Guard code submitted successfully.");
                }
                catch (Exception ex)
                {
                    LogToFile($"Error submitting Steam Guard code: {ex.Message}");
                    OnOutputReceived?.Invoke($"Error submitting code: {ex.Message}");
                }
            }
            else
            {
                LogToFile("Cannot submit Steam Guard code - no active process.");
            }
        }

        /// <summary>
        /// Download a game using saved Steam session (WebView2 login)
        /// Uses the -remember-password flag which looks for saved credentials in ~/.DepotDownloader/
        /// </summary>
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

            // Clean up partial download files
            if (!string.IsNullOrEmpty(appIdToClean))
            {
                try
                {
                    string gameDirectory = Path.Combine(_gamesDirectory, appIdToClean);
                    if (Directory.Exists(gameDirectory))
                    {
                        LogToFile($"Cleaning up cancelled download: {gameDirectory}");
                        Directory.Delete(gameDirectory, true);
                        LogToFile($"Successfully deleted partial download for AppID: {appIdToClean}");
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"Failed to clean up partial download: {ex.Message}");
                }
            }
            _currentDownloadAppId = null;
        }

        private void ParseProgress(string output)
        {
            try
            {
                // Try to parse progress from DepotDownloader output
                // Example: "50.00% 500.00 MB / 1.00 GB"
                var match = Regex.Match(output, @"(\d+\.?\d*)\s*%");
                if (match.Success && double.TryParse(match.Groups[1].Value, out double progress))
                {
                    OnProgressChanged?.Invoke(progress);
                }
            }
            catch { }
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

        public bool DeleteGame(string appId)
        {
            try
            {
                string gamePath = GetGameInstallPath(appId);
                if (Directory.Exists(gamePath))
                {
                    Directory.Delete(gamePath, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"Error deleting game: {ex.Message}");
            }
            return false;
        }

        public void OpenGameFolder(string appId)
        {
            string gamePath = GetGameInstallPath(appId);
            if (Directory.Exists(gamePath))
            {
                Process.Start("explorer.exe", gamePath);
            }
        }
    }
}

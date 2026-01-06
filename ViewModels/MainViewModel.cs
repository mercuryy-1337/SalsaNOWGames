using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SalsaNOWGames.Models;
using SalsaNOWGames.Services;
using SalsaNOWGames.Views;

namespace SalsaNOWGames.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService;
        private readonly SteamApiService _steamApiService;
        private readonly DepotDownloaderService _depotDownloaderService;
        private readonly SteamAuthService _steamAuthService;
        private readonly GamesLibraryService _gamesLibraryService;
        private readonly SteamVdfService _steamVdfService;
        private readonly OwnedGamesService _ownedGamesService;

        // Login state
        private bool _isLoggedIn;
        private string _steamUsername;        // Actual username for API calls
        private string _displayName;          // PersonaName for UI display
        private SteamSession _currentSession;
        private string _avatarUrl;
        private string _loginError;
        private bool _isLoggingIn;

        // Game library state
        private ObservableCollection<GameInfo> _installedGames;
        private ObservableCollection<GameInfo> _searchResults;
        private GameInfo _selectedGame;
        private string _searchQuery;
        private string _appIdInput;
        private bool _isSearching;
        private bool _isDownloading;
        private bool _isCleaningUp;
        private string _downloadOutput;
        private string _statusMessage;

        // View state
        private string _currentView; // "login", "library", "download", "search"
        private DispatcherTimer _statusClearTimer;
        private string _driveUsage;

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _steamApiService = new SteamApiService();
            _depotDownloaderService = new DepotDownloaderService(_settingsService.Settings.InstallDirectory);
            _steamAuthService = new SteamAuthService();
            _gamesLibraryService = new GamesLibraryService();
            _steamVdfService = new SteamVdfService();
            _ownedGamesService = new OwnedGamesService(_steamVdfService);

            _installedGames = new ObservableCollection<GameInfo>();
            _searchResults = new ObservableCollection<GameInfo>();
            _currentView = "login";

            // Initialize status clear timer (clears after 5 seconds)
            _statusClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _statusClearTimer.Tick += (s, e) =>
            {
                _statusClearTimer.Stop();
                UpdateDefaultStatus();
            };

            // Subscribe to owned games updated event (auto-refresh when background fetch completes)
            _ownedGamesService.OnOwnedGamesUpdated += (response) =>
            {
                Application.Current.Dispatcher.Invoke(async () =>
                {
                    // Merge any games.json data first
                    if (_gamesLibraryService.Games.Any())
                    {
                        _ownedGamesService.MergeFromGamesJson(_gamesLibraryService.Games);
                    }
                    await RefreshInstalledGamesAsync();
                });
            };

            // Initialize commands
            LoginCommand = new RelayCommand(async () => await LoginAsync(), () => !IsLoggingIn);
            LogoutCommand = new RelayCommand(Logout);
            SearchGamesCommand = new RelayCommand(async () => await SearchGamesAsync(), () => !IsSearching);
            DownloadByAppIdCommand = new RelayCommand(async () => await DownloadByAppIdAsync(), () => !IsDownloading);
            DownloadGameCommand = new RelayCommand(async (o) => await DownloadGameAsync(o as GameInfo), (o) => !IsDownloading);
            DeleteGameCommand = new RelayCommand(DeleteGame);
            CreateShortcutCommand = new RelayCommand(CreateShortcut);
            OpenSteamLibraryCommand = new RelayCommand(OpenSteamLibrary);
            OpenFolderCommand = new RelayCommand(OpenGameFolder);
            CancelDownloadCommand = new RelayCommand(CancelDownload, () => IsDownloading);
            EnterSteamGuardCommand = new RelayCommand(ManuallyEnterSteamGuard, () => IsDownloading);
            ShowLibraryCommand = new RelayCommand(() => CurrentView = "library");
            ShowSearchCommand = new RelayCommand(async () => await ShowSearchViewAsync());
            ShowDownloadCommand = new RelayCommand(() => CurrentView = "download");
            RefreshLibraryCommand = new RelayCommand(async () => await RefreshInstalledGamesAsync());

            // Wire up depot downloader events
            _depotDownloaderService.OnOutputReceived += (output) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DownloadOutput += output + Environment.NewLine;
                });
            };

            _depotDownloaderService.OnProgressChanged += (progress) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (SelectedGame != null)
                    {
                        SelectedGame.DownloadProgress = progress;
                        SelectedGame.DownloadStatus = $"Downloading... {progress:F1}%";
                    }
                });
            };
            
            _depotDownloaderService.OnPreallocatingChanged += (isPreallocating, fileCount) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (SelectedGame != null && isPreallocating)
                    {
                        SelectedGame.DownloadStatus = "Pre-allocating files...";
                    }
                });
            };

            _depotDownloaderService.OnCleanupInProgress += (isCleaningUp) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _isCleaningUp = isCleaningUp;
                    if (isCleaningUp)
                    {
                        StatusMessage = "Cleaning up cancelled download...";
                    }
                    else
                    {
                        StatusMessage = "Cleanup complete.";
                    }
                });
            };

            _depotDownloaderService.OnDownloadComplete += (success, message) =>
            {
                Application.Current.Dispatcher.Invoke(async () =>
                {
                    IsDownloading = false;
                    StatusMessage = message;
                    if (success && SelectedGame != null)
                    {
                        // Get install path
                        string installPath = _depotDownloaderService.GetGameInstallPath(SelectedGame.AppId);
                        
                        // Update salsa.vdf with install status (primary)
                        _ownedGamesService.MarkGameAsInstalled(SelectedGame.AppId, installPath);
                        
                        // Also update games.json for backward compatibility (will be deprecated sometime idk)
                        _gamesLibraryService.AddGame(SelectedGame, installPath);
                        
                        _settingsService.AddInstalledGame(SelectedGame.AppId);
                        SelectedGame.IsDownloading = false;
                        SelectedGame.IsInstalled = true;
                        SelectedGame.IsInstalledViaSalsa = true;
                        SelectedGame.DownloadStatus = "Installed";
                        await RefreshInstalledGamesAsync();
                    }
                });
            };

            // Steam Guard code entry is handled manually via the "Enter Code" button
            // No automatic popup - users click the button when needed

            // Check for existing login
            CheckExistingLogin();
        }

        private void PromptForSteamGuardCode(string message)
        {
            var dialog = new SteamGuardDialog(message);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Code))
            {
                _depotDownloaderService.SubmitSteamGuardCode(dialog.Code);
                DownloadOutput += $"Steam Guard code submitted.{Environment.NewLine}";
            }
            else
            {
                DownloadOutput += $"Steam Guard code cancelled.{Environment.NewLine}";
                CancelDownload();
            }
        }

        private void ManuallyEnterSteamGuard()
        {
            PromptForSteamGuardCode("Enter your Steam Guard code from your mobile app or email.");
        }

        #region Properties

        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set => SetProperty(ref _isLoggedIn, value);
        }

        // Display name shown in UI (PersonaName from Steam)
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        // Internal username for API calls (not shown in UI)
        private string SteamUsername
        {
            get => _steamUsername;
            set => _steamUsername = value;
        }

        public SteamSession CurrentSession
        {
            get => _currentSession;
            set => SetProperty(ref _currentSession, value);
        }

        public string AvatarUrl
        {
            get => _avatarUrl;
            set => SetProperty(ref _avatarUrl, value);
        }

        public string LoginError
        {
            get => _loginError;
            set => SetProperty(ref _loginError, value);
        }

        public bool IsLoggingIn
        {
            get => _isLoggingIn;
            set => SetProperty(ref _isLoggingIn, value);
        }

        public ObservableCollection<GameInfo> InstalledGames
        {
            get => _installedGames;
            set => SetProperty(ref _installedGames, value);
        }

        public ObservableCollection<GameInfo> SearchResults
        {
            get => _searchResults;
            set => SetProperty(ref _searchResults, value);
        }

        public GameInfo SelectedGame
        {
            get => _selectedGame;
            set => SetProperty(ref _selectedGame, value);
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        public string AppIdInput
        {
            get => _appIdInput;
            set => SetProperty(ref _appIdInput, value);
        }

        public bool IsSearching
        {
            get => _isSearching;
            set => SetProperty(ref _isSearching, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set => SetProperty(ref _isDownloading, value);
        }

        public string DownloadOutput
        {
            get => _downloadOutput;
            set => SetProperty(ref _downloadOutput, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string DriveUsage
        {
            get => _driveUsage;
            set => SetProperty(ref _driveUsage, value);
        }

        public string AppVersion => $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}";

        public string CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        #endregion

        #region Commands

        public ICommand LoginCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand SearchGamesCommand { get; }
        public ICommand DownloadByAppIdCommand { get; }
        public ICommand DownloadGameCommand { get; }
        public ICommand DeleteGameCommand { get; }
        public ICommand CreateShortcutCommand { get; }
        public ICommand OpenSteamLibraryCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand EnterSteamGuardCommand { get; }
        public ICommand ShowLibraryCommand { get; }
        public ICommand ShowSearchCommand { get; }
        public ICommand ShowDownloadCommand { get; }
        public ICommand RefreshLibraryCommand { get; }

        #endregion

        #region Methods

        private void CheckExistingLogin()
        {
            // First check for saved Steam session
            var savedSession = _steamAuthService.LoadSession();
            if (savedSession != null && savedSession.IsValid)
            {
                // Try to restore password from session
                string restoredPassword = savedSession.GetPassword();
                
                // If password can't be decrypted (e.g., new VM, different user profile),
                // clear the session and force re-login
                if (string.IsNullOrEmpty(restoredPassword))
                {
                    _steamAuthService.ClearSession();
                    _settingsService.ClearLogin();
                    LoginError = "Session expired. Please sign in again.";
                    return;
                }
                
                CurrentSession = savedSession;
                IsLoggedIn = true;
                SteamUsername = savedSession.Username;
                DisplayName = _steamVdfService.GetPersonaName(savedSession.Username) ?? "Steam User";
                AvatarUrl = savedSession.AvatarUrl;
                _steamPassword = restoredPassword;
                
                CurrentView = "library";
                StatusMessage = $"Welcome back, {DisplayName}!";
                _ = RefreshInstalledGamesAsync();
                
                // Fetch owned games in background (doesn't block startup)
                _ownedGamesService.StartBackgroundRefresh(savedSession.Username);
                return;
            }

            // Fallback to old settings
            if (_settingsService.IsLoggedIn)
            {
                IsLoggedIn = true;
                SteamUsername = _settingsService.Settings.SteamUsername;
                DisplayName = _steamVdfService.GetPersonaName(_settingsService.Settings.SteamUsername) ?? "Steam User";
                AvatarUrl = _settingsService.Settings.AvatarUrl;
                CurrentView = "library";
                _ = RefreshInstalledGamesAsync();
                
                // Fetch owned games in background
                _ownedGamesService.StartBackgroundRefresh(_settingsService.Settings.SteamUsername);
            }
        }

        private string _steamPassword;
        private bool _useNoMobile; // True = manual code entry, False = mobile app approval

        private async Task LoginAsync()
        {
            IsLoggingIn = true;
            LoginError = "";

            try
            {
                // Open Steam login window
                var loginWindow = new SteamLoginWindow();
                loginWindow.Owner = Application.Current.MainWindow;
                var result = loginWindow.ShowDialog();

                if (result == true && !string.IsNullOrEmpty(loginWindow.Username) && !string.IsNullOrEmpty(loginWindow.Password))
                {
                    // Store password for downloads
                    _steamPassword = loginWindow.Password;
                    
                    // Store no-mobile preference (manual code entry vs app approval)
                    _useNoMobile = loginWindow.UseNoMobile;
                    
                    // Create session with encrypted password
                    CurrentSession = loginWindow.Session ?? new SteamSession
                    {
                        Username = loginWindow.Username,
                        ExpiresAt = DateTime.UtcNow.AddDays(365) // 1 year expiry
                    };
                    
                    // Store encrypted password in session for persistence
                    CurrentSession.SetPassword(loginWindow.Password);
                    
                    // Save session for future use
                    _steamAuthService.SaveSession(CurrentSession);

                    // Update UI
                    IsLoggedIn = true;
                    SteamUsername = loginWindow.Username;
                    DisplayName = _steamVdfService.GetPersonaName(loginWindow.Username) ?? loginWindow.Username;
                    CurrentView = "library";
                    StatusMessage = $"Welcome, {DisplayName}!";
                    await RefreshInstalledGamesAsync();
                }
                else
                {
                    LoginError = "Login cancelled or failed.";
                }
            }
            catch (Exception ex)
            {
                LoginError = $"Login failed: {ex.Message}";
            }
            finally
            {
                IsLoggingIn = false;
            }
        }

        private void Logout()
        {
            _settingsService.ClearLogin();
            _steamAuthService.ClearSession();
            CurrentSession = null;
            IsLoggedIn = false;
            SteamUsername = "";
            DisplayName = "";
            AvatarUrl = "";
            CurrentView = "login";
            InstalledGames.Clear();
            SearchResults.Clear();
            StatusMessage = "";
        }

        private async Task RefreshInstalledGamesAsync()
        {
            InstalledGames.Clear();

            // Refresh Steam library detection
            _gamesLibraryService.RefreshSteamLibrary();
            _gamesLibraryService.LoadGames();

            // Get owned games from salsa.vdf cache
            var ownedGames = _ownedGamesService.GetCachedOwnedGames(SteamUsername);
            if (ownedGames == null || ownedGames.Games == null || ownedGames.Games.Count == 0)
            {
                // Fallback to games.json if salsa.vdf is empty - sort alphabetically
                var allGames = _gamesLibraryService.Games.OrderBy(g => g.Name).ToList();
                foreach (var game in allGames)
                {
                    var gameInfo = CreateGameInfoFromLibrary(game);
                    InstalledGames.Add(gameInfo);
                }
            }
            else
            {
                // Merge games.json data into salsa.vdf (one-time migration per session)
                if (_gamesLibraryService.Games.Any())
                {
                    _ownedGamesService.MergeFromGamesJson(_gamesLibraryService.Games);
                }

                // Use salsa.vdf as primary source - sort alphabetically
                var sortedGames = ownedGames.Games.OrderBy(g => g.Name).ToList();
                foreach (var game in sortedGames)
                {
                    string appId = game.AppId.ToString();
                    
                    // Use salsa.vdf for Salsa install status, SteamLibraryService for Steam
                    bool steamInstalled = _gamesLibraryService.IsInstalledViaSteam(appId);
                    bool salsaInstalled = game.InstallSalsa;
                    string installPath = !string.IsNullOrEmpty(game.InstallPath) 
                        ? game.InstallPath 
                        : _gamesLibraryService.GetInstallPath(appId);
                    
                    var gameInfo = new GameInfo
                    {
                        AppId = appId,
                        Name = game.Name,
                        HeaderImageUrl = game.HeaderImageUrl,
                        IconUrl = game.IconUrl,
                        PlaytimeMinutes = game.PlaytimeForeverMinutes,
                        IsInstalled = steamInstalled || salsaInstalled,
                        IsInstalledViaSteam = steamInstalled,
                        IsInstalledViaSalsa = salsaInstalled,
                        HasShortcut = game.HasShortcut,
                        InstallPath = installPath
                    };

                    // Validate install path exists for Salsa installs
                    if (salsaInstalled && (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)))
                    {
                        // Salsa install path invalid, mark as uninstalled in salsa.vdf
                        _ownedGamesService.MarkGameAsUninstalled(appId);
                        gameInfo.IsInstalledViaSalsa = false;
                        gameInfo.IsInstalled = steamInstalled;
                        gameInfo.InstallPath = null;
                    }

                    // Get size on disk if installed
                    if (gameInfo.IsInstalled && !string.IsNullOrEmpty(gameInfo.InstallPath) && Directory.Exists(gameInfo.InstallPath))
                    {
                        gameInfo.SizeOnDisk = _depotDownloaderService.GetGameSize(appId);
                    }

                    InstalledGames.Add(gameInfo);
                }
            }

            UpdateDefaultStatus();
            UpdateDriveUsage();
            await Task.CompletedTask;
        }

        private GameInfo CreateGameInfoFromLibrary(InstalledGame game)
        {
            bool steamInstalled = _gamesLibraryService.IsInstalledViaSteam(game.Id);
            bool salsaInstalled = game.Install?.Salsa ?? false;
            string installPath = _gamesLibraryService.GetInstallPath(game.Id);
            
            var gameInfo = new GameInfo
            {
                AppId = game.Id,
                Name = game.Name,
                HeaderImageUrl = game.HeaderImageUrl,
                InstallPath = installPath,
                IsInstalled = steamInstalled || salsaInstalled,
                IsInstalledViaSteam = steamInstalled,
                IsInstalledViaSalsa = salsaInstalled,
                HasShortcut = game.HasShortcut
            };

            if (gameInfo.IsInstalled && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                gameInfo.SizeOnDisk = _depotDownloaderService.GetGameSize(game.Id);
            }
            else if (salsaInstalled && (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)))
            {
                // Salsa install path invalid, mark as uninstalled
                _gamesLibraryService.MarkAsUninstalled(game.Id);
                gameInfo.IsInstalled = steamInstalled;
            }

            return gameInfo;
        }

        /*
         * Switch to search view and reload search results if there's a query
         * This ensures games.json is rescanned for installed status
         */
        private async Task ShowSearchViewAsync()
        {
            CurrentView = "search";
            
            // If there's a search query, reload the search to refresh installed status
            if (!string.IsNullOrWhiteSpace(SearchQuery) && SearchResults.Count > 0)
            {
                await SearchGamesAsync();
            }
        }

        private async Task SearchGamesAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                StatusMessage = "Please enter a search query.";
                return;
            }

            IsSearching = true;
            SearchResults.Clear();

            try
            {
                var results = await _steamApiService.SearchGamesAsync(SearchQuery);
                foreach (var game in results)
                {
                    // Check if installed using games.json (more accurate than folder check)
                    game.IsInstalled = _gamesLibraryService.IsGameInstalled(game.AppId);
                    SearchResults.Add(game);
                }

                ShowTemporaryStatus($"Found {results.Count} game(s)");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        private async Task DownloadByAppIdAsync()
        {
            if (string.IsNullOrWhiteSpace(AppIdInput))
            {
                StatusMessage = "Please enter an App ID.";
                return;
            }

            var gameInfo = await _steamApiService.GetGameInfoAsync(AppIdInput);
            await DownloadGameAsync(gameInfo);
        }

        private async Task DownloadGameAsync(GameInfo game)
        {
            if (game == null) return;

            // Check if we have valid credentials
            if (string.IsNullOrEmpty(SteamUsername) || string.IsNullOrEmpty(_steamPassword))
            {
                StatusMessage = "Please sign in to Steam to download games.";
                LoginError = "Your session has expired. Please sign in again.";
                CurrentView = "login";
                return;
            }

            SelectedGame = game;
            game.IsDownloading = true;
            game.DownloadProgress = 0;
            game.DownloadStatus = "Starting download...";
            IsDownloading = true;
            DownloadOutput = "";
            CurrentView = "download";
            StatusMessage = $"Downloading {game.Name}...";

            try
            {
                // Use username/password based download with no-mobile preference
                await _depotDownloaderService.DownloadGameAsync(game.AppId, SteamUsername, _steamPassword, _useNoMobile);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Download failed: {ex.Message}";
                game.IsDownloading = false;
            }
        }

        private void DeleteGame(object parameter)
        {
            if (parameter is GameInfo game)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete {game.Name}?\n\nThis will permanently remove all game files.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    StatusMessage = $"Deleting {game.Name}...";
                    
                    _depotDownloaderService.DeleteGameAsync(game.AppId, (success) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (success)
                            {
                                // Mark as uninstalled in salsa.vdf (primary)
                                _ownedGamesService.MarkGameAsUninstalled(game.AppId);
                                
                                // Also update games.json for backward compatibility
                                _gamesLibraryService.MarkAsUninstalled(game.AppId);
                                _settingsService.RemoveInstalledGame(game.AppId);
                                
                                // Update the game in the list instead of removing
                                game.IsInstalled = false;
                                game.IsInstalledViaSalsa = false;
                                game.SizeOnDisk = 0;
                                
                                // Also update in search results if present
                                var searchGame = SearchResults.FirstOrDefault(g => g.AppId == game.AppId);
                                if (searchGame != null)
                                {
                                    searchGame.IsInstalled = false;
                                }
                                
                                ShowTemporaryStatus($"{game.Name} has been deleted.");
                                UpdateDriveUsage();
                            }
                            else
                            {
                                ShowTemporaryStatus("Failed to delete game.");
                            }
                        });
                    });
                }
            }
        }

        private void ShowTemporaryStatus(string message)
        {
            StatusMessage = message;
            _statusClearTimer.Stop();
            _statusClearTimer.Start();
        }

        private void CreateShortcut(object parameter)
        {
            if (parameter is GameInfo game)
            {
                try
                {
                    // TODO: Implement actual shortcut creation to Steam
                    // For now, just mark as having a shortcut in salsa.vdf (primary)
                    _ownedGamesService.UpdateGameShortcut(game.AppId, true);
                    
                    // Also update games.json for backward compatibility
                    _gamesLibraryService.SetHasShortcut(game.AppId, true);
                    game.HasShortcut = true;
                    ShowTemporaryStatus($"Shortcut created for {game.Name}");
                }
                catch (Exception ex)
                {
                    ShowTemporaryStatus($"Failed to create shortcut: {ex.Message}");
                }
            }
        }

        private void OpenSteamLibrary(object parameter)
        {
            if (parameter is GameInfo game)
            {
                try
                {
                    // Open Steam library
                    System.Diagnostics.Process.Start("steam://open/library");
                    ShowTemporaryStatus("Opening Steam Library...");
                }
                catch (Exception ex)
                {
                    ShowTemporaryStatus($"Failed to open Steam: {ex.Message}");
                }
            }
        }

        private void UpdateDefaultStatus()
        {
            // Get count from salsa.vdf (owned games)
            var ownedGames = _ownedGamesService.GetCachedOwnedGames(SteamUsername);
            int totalGames = ownedGames?.Games?.Count ?? _gamesLibraryService.Games.Count;
            int installedCount = InstalledGames.Count(g => g.IsInstalled);
            StatusMessage = $"{installedCount} installed, {totalGames} game(s) in library";
        }

        private void UpdateDriveUsage()
        {
            try
            {
                var driveInfo = new DriveInfo("I");
                double freeGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                double totalGB = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                DriveUsage = $"⛃ {freeGB:F1} GB free of {totalGB:F1} GB";
            }
            catch
            {
                DriveUsage = "Drive info unavailable";
            }
        }

        private void OpenGameFolder(object parameter)
        {
            if (parameter is GameInfo game)
            {
                _depotDownloaderService.OpenGameFolder(game.AppId);
            }
        }

        private void CancelDownload()
        {
            _depotDownloaderService.CancelDownload();
            IsDownloading = false;
            if (SelectedGame != null)
            {
                SelectedGame.IsDownloading = false;
                SelectedGame.DownloadStatus = "Cancelled";
            }
            StatusMessage = "Download cancelled.";
        }

        #endregion
    }
}

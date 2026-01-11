using System;
using System.Collections.Generic;
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
        private readonly SteamHeaderService _headerService;
        private readonly SteamShortcutManager _shortcutManager;

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
            LogService.Log("=== SalsaNOW Games Starting ===");
            LogService.Log($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            LogService.Log($"Log file: {LogService.GetLogFilePath()}");
            
            _settingsService = new SettingsService();
            _steamApiService = new SteamApiService();
            _depotDownloaderService = new DepotDownloaderService(_settingsService.Settings.InstallDirectory);
            _steamAuthService = new SteamAuthService();
            _gamesLibraryService = new GamesLibraryService();
            _steamVdfService = new SteamVdfService();
            _ownedGamesService = new OwnedGamesService(_steamVdfService);
            _headerService = new SteamHeaderService();
            _shortcutManager = new SteamShortcutManager();

            _installedGames = new ObservableCollection<GameInfo>();
            _searchResults = new ObservableCollection<GameInfo>();
            _currentView = "login";
            
            LogService.Log("Services initialized");

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
                LogService.Log($"OnOwnedGamesUpdated triggered with {response?.GameCount ?? 0} games");
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
            PlayGameCommand = new RelayCommand(PlayGame);
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
        public ICommand PlayGameCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand EnterSteamGuardCommand { get; }
        public ICommand ShowLibraryCommand { get; }
        public ICommand ShowSearchCommand { get; }
        public ICommand ShowDownloadCommand { get; }
        public ICommand RefreshLibraryCommand { get; }

        #endregion

        #region Methods

        private async void CheckExistingLogin()
        {
            LogService.Log("CheckExistingLogin started");
            
            // First check for saved Steam session
            var savedSession = _steamAuthService.LoadSession();
            if (savedSession != null && savedSession.IsValid)
            {
                LogService.Log($"Found valid saved session for: {savedSession.Username}");
                
                // Try to restore password from session
                string restoredPassword = savedSession.GetPassword();
                
                // If password can't be decrypted (e.g., new VM, different user profile),
                // clear the session and force re-login
                if (string.IsNullOrEmpty(restoredPassword))
                {
                    LogService.LogWarning("Could not restore password from session");
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
                
                // Refresh library (will fetch from API if no cache)
                await RefreshInstalledGamesAsync();
                
                // Start background refresh for next time (only if cache exists)
                var cachedGames = _ownedGamesService.GetCachedOwnedGames(savedSession.Username);
                if (cachedGames != null && cachedGames.Games != null && cachedGames.Games.Count > 0)
                {
                    _ownedGamesService.StartBackgroundRefresh(savedSession.Username);
                }
                return;
            }

            // Fallback to old settings
            if (_settingsService.IsLoggedIn)
            {
                LogService.Log($"Restoring from old settings: {_settingsService.Settings.SteamUsername}");
                IsLoggedIn = true;
                SteamUsername = _settingsService.Settings.SteamUsername;
                DisplayName = _steamVdfService.GetPersonaName(_settingsService.Settings.SteamUsername) ?? "Steam User";
                AvatarUrl = _settingsService.Settings.AvatarUrl;
                CurrentView = "library";
                
                await RefreshInstalledGamesAsync();
                
                // Start background refresh for next time
                var cachedGames = _ownedGamesService.GetCachedOwnedGames(_settingsService.Settings.SteamUsername);
                if (cachedGames != null && cachedGames.Games != null && cachedGames.Games.Count > 0)
                {
                    _ownedGamesService.StartBackgroundRefresh(_settingsService.Settings.SteamUsername);
                }
            }
            else
            {
                LogService.Log("No existing login found");
            }
        }

        private string _steamPassword;
        private bool _useNoMobile; // True = manual code entry, False = mobile app approval

        private async Task LoginAsync()
        {
            LogService.Log("LoginAsync started");
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
                    LogService.Log($"Login attempt for user: {loginWindow.Username}");
                    
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
            LogService.Log("RefreshInstalledGamesAsync started");
            InstalledGames.Clear();

            // Refresh Steam library detection
            _gamesLibraryService.RefreshSteamLibrary();
            _gamesLibraryService.LoadGames();

            // Get owned games from salsa.vdf cache
            var ownedGames = _ownedGamesService.GetCachedOwnedGames(SteamUsername);
            
            // If no cache exists, fetch from API synchronously (first login scenario)
            if (ownedGames == null || ownedGames.Games == null || ownedGames.Games.Count == 0)
            {
                LogService.Log("No cached games found, fetching from API...");
                StatusMessage = "Loading your Steam library...";
                
                // Try to fetch from API
                ownedGames = await _ownedGamesService.GetOwnedGamesAsync(SteamUsername, forceRefresh: true);
                
                if (ownedGames != null && ownedGames.Games != null && ownedGames.Games.Count > 0)
                {
                    LogService.Log($"Fetched {ownedGames.Games.Count} games from API");
                }
            }
            
            if (ownedGames == null || ownedGames.Games == null || ownedGames.Games.Count == 0)
            {
                LogService.LogWarning("No games from API either, falling back to games.json");
                // Fallback to games.json if salsa.vdf is empty - sort alphabetically
                var allGames = _gamesLibraryService.Games.OrderBy(g => g.Name).ToList();
                foreach (var game in allGames)
                {
                    var gameInfo = CreateGameInfoFromLibrary(game);
                    InstalledGames.Add(gameInfo);
                }
                StatusMessage = allGames.Count > 0 
                    ? $"Showing {allGames.Count} games from local library" 
                    : "No games found. Search for games to add them to your library.";
            }
            else
            {
                LogService.Log($"Loading {ownedGames.Games.Count} games into library view");
                
                // Merge games.json data into salsa.vdf (one-time migration per session)
                if (_gamesLibraryService.Games.Any())
                {
                    _ownedGamesService.MergeFromGamesJson(_gamesLibraryService.Games);
                }

                // Use salsa.vdf as primary source - sort alphabetically
                var sortedGames = ownedGames.Games.OrderBy(g => g.Name).ToList();
                
                // Collect app IDs that need header image fetching (either no URL or has slsapi portrait URL)
                var appIdsNeedingHeaders = sortedGames
                    .Where(g => string.IsNullOrEmpty(g.HeaderImageUrl) || 
                               !g.HeaderImageUrl.Contains("store_item_assets") ||
                               g.HeaderImageUrl.Contains("library_600x900"))
                    .Select(g => g.AppId.ToString())
                    .ToList();
                
                foreach (var game in sortedGames)
                {
                    string appId = game.AppId.ToString();
                    
                    // Use salsa.vdf for Salsa install status, SteamLibraryService for Steam
                    bool steamInstalled = _gamesLibraryService.IsInstalledViaSteam(appId);
                    bool salsaInstalled = game.InstallSalsa;
                    string installPath = !string.IsNullOrEmpty(game.InstallPath) 
                        ? game.InstallPath 
                        : _gamesLibraryService.GetInstallPath(appId);
                    
                    // Only use header URL if it's a valid store_item_assets URL (not slsapi portrait)
                    bool hasValidHeader = !string.IsNullOrEmpty(game.HeaderImageUrl) && 
                                         game.HeaderImageUrl.Contains("store_item_assets") &&
                                         !game.HeaderImageUrl.Contains("library_600x900");
                    
                    // Check local cache first
                    string cachedPath = _headerService.GetCachedImagePath(appId);
                    bool isCached = _headerService.IsImageCached(appId);
                    
                    // Use cached local file, valid URL, or nothing (will show loading)
                    string headerUrl = isCached ? cachedPath 
                                     : hasValidHeader ? game.HeaderImageUrl 
                                     : null;
                    
                    bool needsLoading = appIdsNeedingHeaders.Contains(appId);
                    
                    // For Salsa installs, verify shortcut actually exists in Steam's shortcuts.vdf
                    bool hasShortcut = game.HasShortcut;
                    if (salsaInstalled && hasShortcut)
                    {
                        // Verify the shortcut still exists in Steam
                        bool shortcutVerified = _shortcutManager.VerifyShortcutExists(game.Name);
                        if (!shortcutVerified)
                        {
                            // Shortcut was deleted externally, update our records
                            hasShortcut = false;
                            _ownedGamesService.UpdateGameShortcut(appId, false);
                            _gamesLibraryService.SetHasShortcut(appId, false);
                            LogService.Log($"Shortcut for {game.Name} no longer exists in Steam, updating status");
                        }
                    }
                    
                    var gameInfo = new GameInfo
                    {
                        AppId = appId,
                        Name = game.Name,
                        HeaderImageUrl = headerUrl,
                        IconUrl = game.IconUrl,
                        IconPath = game.IconPath,
                        PlaytimeMinutes = game.PlaytimeForeverMinutes,
                        IsInstalled = steamInstalled || salsaInstalled,
                        IsInstalledViaSteam = steamInstalled,
                        IsInstalledViaSalsa = salsaInstalled,
                        HasShortcut = hasShortcut,
                        InstallPath = installPath,
                        IsLoadingImage = needsLoading && !isCached,
                        // Enable shortcut button for all Salsa installs - eligibility checked on click
                        CanAddShortcut = salsaInstalled && !hasShortcut
                    };

                    // Validate install path exists for Salsa installs
                    if (salsaInstalled && (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)))
                    {
                        // Salsa install path invalid, mark as uninstalled in salsa.vdf
                        _ownedGamesService.MarkGameAsUninstalled(appId);
                        gameInfo.IsInstalledViaSalsa = false;
                        gameInfo.IsInstalled = steamInstalled;
                        gameInfo.InstallPath = null;
                        gameInfo.CanAddShortcut = false;
                    }

                    // Get size on disk if installed
                    if (gameInfo.IsInstalled && !string.IsNullOrEmpty(gameInfo.InstallPath) && Directory.Exists(gameInfo.InstallPath))
                    {
                        gameInfo.SizeOnDisk = _depotDownloaderService.GetGameSize(appId);
                    }

                    InstalledGames.Add(gameInfo);
                }
                
                // Fetch header images in background (after adding all games to list)
                if (appIdsNeedingHeaders.Count > 0)
                {
                    _ = FetchHeaderImagesAsync(appIdsNeedingHeaders);
                }
            }

            UpdateDefaultStatus();
            UpdateDriveUsage();
            await Task.CompletedTask;
        }
        
        // Fetches header images (cached or from network), caches locally, and updates UI progressively
        private async Task FetchHeaderImagesAsync(List<string> appIds)
        {
            try
            {
                LogService.Log($"Fetching and caching header images for {appIds.Count} games...");
                var headerUrlsToSave = new Dictionary<string, string>();
                
                // Process in batches of 3 to update UI progressively
                for (int i = 0; i < appIds.Count; i += 3)
                {
                    var batch = appIds.Skip(i).Take(3).ToList();
                    var tasks = batch.Select(async appId =>
                    {
                        // Get the header URL (checks cache first, then CDN, then API)
                        var url = await _headerService.GetHeaderImageAsync(appId);
                        
                        // Download and cache the image locally
                        string cachedPath = url;
                        if (!string.IsNullOrEmpty(url) && !url.StartsWith(_headerService.GetCachedImagePath("")))
                        {
                            cachedPath = await _headerService.DownloadAndCacheImageAsync(appId, url);
                        }
                        
                        return new { AppId = appId, Url = url, CachedPath = cachedPath };
                    });
                    
                    var results = await Task.WhenAll(tasks);
                    
                    // Update UI immediately for this batch
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var result in results)
                        {
                            var game = InstalledGames.FirstOrDefault(g => g.AppId == result.AppId);
                            if (game != null)
                            {
                                game.HeaderImageUrl = result.CachedPath;
                                game.IsLoadingImage = false;
                            }
                            
                            // Store URL for saving to salsa.vdf
                            if (!string.IsNullOrEmpty(result.Url))
                            {
                                headerUrlsToSave[result.AppId] = result.Url;
                            }
                        }
                    });
                    
                    // Small delay between batches
                    if (i + 3 < appIds.Count)
                    {
                        await Task.Delay(100);
                    }
                }
                
                // Batch update salsa.vdf with URLs (not local paths)
                if (headerUrlsToSave.Count > 0)
                {
                    _ownedGamesService.UpdateHeaderImageUrls(headerUrlsToSave);
                }
                
                LogService.Log($"Header images fetched and cached for {appIds.Count} games");
            }
            catch (Exception ex)
            {
                LogService.LogError("Failed to fetch header images", ex);
            }
        }

        private GameInfo CreateGameInfoFromLibrary(InstalledGame game)
        {
            bool steamInstalled = _gamesLibraryService.IsInstalledViaSteam(game.Id);
            bool salsaInstalled = game.Install?.Salsa ?? false;
            string installPath = _gamesLibraryService.GetInstallPath(game.Id);
            
            // Only use header URL if it's valid (not slsapi portrait)
            bool hasValidHeader = !string.IsNullOrEmpty(game.HeaderImageUrl) && 
                                 game.HeaderImageUrl.Contains("store_item_assets") &&
                                 !game.HeaderImageUrl.Contains("library_600x900");
            
            // Check local cache
            string cachedPath = _headerService.GetCachedImagePath(game.Id);
            bool isCached = _headerService.IsImageCached(game.Id);
            
            string headerUrl = isCached ? cachedPath 
                             : hasValidHeader ? game.HeaderImageUrl 
                             : null;
            
            var gameInfo = new GameInfo
            {
                AppId = game.Id,
                Name = game.Name,
                HeaderImageUrl = headerUrl,
                InstallPath = installPath,
                IsInstalled = steamInstalled || salsaInstalled,
                IsInstalledViaSteam = steamInstalled,
                IsInstalledViaSalsa = salsaInstalled,
                HasShortcut = game.HasShortcut,
                IsLoadingImage = !isCached && !hasValidHeader
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
                // Check if this is a Steam-installed game
                bool isSteamInstalled = game.IsInstalledViaSteam && !game.IsInstalledViaSalsa;
                
                string message = isSteamInstalled
                    ? $"Are you sure you want to uninstall {game.Name}?\n\nThis will open Steam's uninstaller."
                    : $"Are you sure you want to delete {game.Name}?\n\nThis will permanently remove all game files.";

                string secondaryMessage = null;
                if (!isSteamInstalled && game.HasShortcut)
                {
                    secondaryMessage = "The Steam shortcut will also be removed.";
                }

                bool confirmed = ModernDialog.ShowCustom(
                    Application.Current.MainWindow,
                    "Confirm Delete",
                    message,
                    ModernDialog.DialogType.Warning,
                    secondaryMessage,
                    "Delete",
                    "Cancel");

                if (confirmed)
                {
                    if (isSteamInstalled)
                    {
                        // Uninstall via Steam
                        UninstallSteamGame(game);
                    }
                    else
                    {
                        // Delete Salsa-installed game
                        DeleteSalsaGame(game);
                    }
                }
            }
        }

        private void UninstallSteamGame(GameInfo game)
        {
            StatusMessage = $"Uninstalling {game.Name} via Steam...";
            
            // Get manifest path before launching uninstaller
            string manifestPath = _gamesLibraryService.GetSteamManifestPath(game.AppId);
            
            // Launch Steam uninstaller
            try
            {
                System.Diagnostics.Process.Start($"steam://uninstall/{game.AppId}");
            }
            catch (Exception ex)
            {
                LogService.Log($"Failed to launch Steam uninstaller: {ex.Message}");
                ShowTemporaryStatus("Failed to launch Steam uninstaller.");
                return;
            }

            // Poll for uninstall completion in background
            System.Threading.Tasks.Task.Run(async () =>
            {
                int maxAttempts = 120; // 2 minutes max
                int attempts = 0;
                
                while (attempts < maxAttempts)
                {
                    await System.Threading.Tasks.Task.Delay(1000); // Check every second
                    attempts++;
                    
                    // Check if manifest file is gone
                    bool stillExists = !string.IsNullOrEmpty(manifestPath) && System.IO.File.Exists(manifestPath);
                    
                    // Also check via service
                    if (!stillExists)
                    {
                        _gamesLibraryService.RefreshSteamLibrary();
                        stillExists = _gamesLibraryService.IsInstalledViaSteam(game.AppId);
                    }
                    
                    if (!stillExists)
                    {
                        // Game was uninstalled
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            game.IsInstalled = false;
                            game.IsInstalledViaSteam = false;
                            game.SizeOnDisk = 0;
                            
                            var searchGame = SearchResults.FirstOrDefault(g => g.AppId == game.AppId);
                            if (searchGame != null)
                            {
                                searchGame.IsInstalled = false;
                                searchGame.IsInstalledViaSteam = false;
                            }
                            
                            LogService.Log($"Steam uninstall completed for {game.Name} (AppId: {game.AppId})");
                            ShowTemporaryStatus($"{game.Name} has been uninstalled.");
                        });
                        return;
                    }
                }
                
                // Timeout - user may have cancelled
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LogService.Log($"Steam uninstall timed out or cancelled for {game.Name}");
                    ShowTemporaryStatus("Uninstall cancelled or timed out.");
                });
            });
        }

        private void DeleteSalsaGame(GameInfo game)
        {
            StatusMessage = $"Deleting {game.Name}...";
            
            // Track if game had a shortcut before deletion
            bool hadShortcut = game.HasShortcut;
            string gameName = game.Name;
            
            _depotDownloaderService.DeleteGameAsync(game.AppId, (success) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (success)
                    {
                        // Remove shortcut if it existed
                        bool shortcutRemoved = false;
                        if (hadShortcut)
                        {
                            shortcutRemoved = _shortcutManager.RemoveShortcut(gameName);
                            if (shortcutRemoved)
                            {
                                LogService.Log($"Removed Steam shortcut for {gameName}");
                            }
                        }
                        
                        // Mark as uninstalled in salsa.vdf (primary)
                        _ownedGamesService.MarkGameAsUninstalled(game.AppId);
                        
                        // Clear shortcut and icon path data
                        _ownedGamesService.UpdateGameShortcut(game.AppId, false);
                        _ownedGamesService.UpdateGameIconPath(game.AppId, "");
                        
                        // Also update games.json for backward compatibility
                        _gamesLibraryService.MarkAsUninstalled(game.AppId);
                        _gamesLibraryService.SetHasShortcut(game.AppId, false);
                        _settingsService.RemoveInstalledGame(game.AppId);
                        
                        // Update the game in the list instead of removing
                        game.IsInstalled = false;
                        game.IsInstalledViaSalsa = false;
                        game.HasShortcut = false;
                        game.IconPath = null;
                        game.SizeOnDisk = 0;
                        game.CanAddShortcut = false;
                        
                        // Also update in search results if present
                        var searchGame = SearchResults.FirstOrDefault(g => g.AppId == game.AppId);
                        if (searchGame != null)
                        {
                            searchGame.IsInstalled = false;
                            searchGame.HasShortcut = false;
                        }
                        
                        UpdateDriveUsage();
                        
                        // If shortcut was removed, prompt to restart Steam
                        if (shortcutRemoved)
                        {
                            bool restartSteam = ModernDialog.ShowCustom(
                                Application.Current.MainWindow,
                                "Game Deleted",
                                $"{gameName} and its Steam shortcut have been removed.\n\nRestart Steam to update your library?",
                                ModernDialog.DialogType.Confirm,
                                "Steam will close and reopen automatically.",
                                "Restart Steam",
                                "Later");
                            
                            if (restartSteam)
                            {
                                RestartSteam();
                            }
                            else
                            {
                                ShowTemporaryStatus($"{gameName} has been deleted.");
                            }
                        }
                        else
                        {
                            ShowTemporaryStatus($"{gameName} has been deleted.");
                        }
                    }
                    else
                    {
                        ShowTemporaryStatus("Failed to delete game.");
                    }
                });
            });
        }

        private void ShowTemporaryStatus(string message)
        {
            StatusMessage = message;
            _statusClearTimer.Stop();
            _statusClearTimer.Start();
        }

        private async void CreateShortcut(object parameter)
        {
            if (parameter is GameInfo game)
            {
                try
                {
                    // Check shortcut eligibility to get the exe path
                    var eligibility = _shortcutManager.CheckShortcutEligibility(game.InstallPath);
                    
                    if (!eligibility.CanAddShortcut)
                    {
                        // Show modern error dialog and disable button for this game
                        ModernDialog.ShowError(
                            Application.Current.MainWindow,
                            "Cannot Create Shortcut",
                            eligibility.ErrorMessage,
                            "You can add it manually in Steam:\nGames → Add a Non-Steam Game to My Library");
                        
                        // Disable the button for this game
                        game.CanAddShortcut = false;
                        game.ShortcutErrorMessage = eligibility.ErrorMessage;
                        return;
                    }

                    // Check if shortcut already exists
                    if (_shortcutManager.ShortcutExists(eligibility.ExePath, game.Name))
                    {
                        ShowTemporaryStatus($"Shortcut already exists for {game.Name}");
                        _ownedGamesService.UpdateGameShortcut(game.AppId, true);
                        _gamesLibraryService.SetHasShortcut(game.AppId, true);
                        game.HasShortcut = true;
                        return;
                    }

                    // Download icon if not already present
                    string iconPath = game.IconPath;
                    if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                    {
                        // Try to download the icon
                        ShowTemporaryStatus($"Downloading icon for {game.Name}...");
                        iconPath = await _depotDownloaderService.DownloadGameIconAsync(game.AppId, game.IconUrl);
                        
                        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                        {
                            game.IconPath = iconPath;
                            _ownedGamesService.UpdateGameIconPath(game.AppId, iconPath);
                            LogService.Log($"Downloaded icon for {game.Name}: {iconPath}");
                        }
                        else
                        {
                            // No icon available, will use exe icon
                            iconPath = null;
                            LogService.Log($"No icon available for {game.Name}, will use exe icon");
                        }
                    }

                    bool success = _shortcutManager.AddGameShortcut(
                        game.Name,
                        eligibility.ExePath,
                        game.InstallPath,
                        iconPath);

                    if (success)
                    {
                        // Mark as having shortcut in salsa.vdf (primary)
                        _ownedGamesService.UpdateGameShortcut(game.AppId, true);
                        
                        // Also update games.json for backward compatibility
                        _gamesLibraryService.SetHasShortcut(game.AppId, true);
                        game.HasShortcut = true;

                        // Ask user if they want to restart Steam
                        bool restartSteam = ModernDialog.ShowRestartSteam(
                            Application.Current.MainWindow,
                            game.Name);

                        if (restartSteam)
                        {
                            RestartSteam();
                        }
                        else
                        {
                            ShowTemporaryStatus($"✓ Shortcut created for {game.Name}");
                        }
                    }
                    else
                    {
                        ModernDialog.ShowError(
                            Application.Current.MainWindow,
                            "Shortcut Failed",
                            $"Could not add shortcut for {game.Name}",
                            "Please try again or add the game manually in Steam.");
                    }
                }
                catch (Exception ex)
                {
                    LogService.LogError($"Failed to create shortcut for {game.Name}", ex);
                    ModernDialog.ShowError(
                        Application.Current.MainWindow,
                        "Error",
                        $"Failed to create shortcut: {ex.Message}");
                }
            }
        }

        private void RestartSteam()
        {
            try
            {
                ShowTemporaryStatus("Restarting Steam...");
                
                // Kill Steam process
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/f /im steam.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var killProcess = System.Diagnostics.Process.Start(psi);
                killProcess.WaitForExit(5000);

                // Short delay to ensure Steam closes
                System.Threading.Thread.Sleep(500);

                // Reopen Steam library
                System.Diagnostics.Process.Start("steam://open/library");
                ShowTemporaryStatus("Steam restarted - check your library!");
            }
            catch (Exception ex)
            {
                LogService.LogError("Failed to restart Steam", ex);
                ShowTemporaryStatus("Could not restart Steam automatically");
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

        private void PlayGame(object parameter)
        {
            if (parameter is GameInfo game)
            {
                try
                {
                    // Launch game via Steam
                    string steamUrl = $"steam://run/{game.AppId}";
                    System.Diagnostics.Process.Start(steamUrl);
                    LogService.Log($"Launching game via Steam: {game.Name} (AppID: {game.AppId})");
                    ShowTemporaryStatus($"Launching {game.Name}...");
                }
                catch (Exception ex)
                {
                    LogService.LogError($"Failed to launch game {game.AppId}", ex);
                    ShowTemporaryStatus($"Failed to launch game: {ex.Message}");
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

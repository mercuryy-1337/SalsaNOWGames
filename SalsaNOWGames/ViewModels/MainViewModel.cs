using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SalsaNOWGames.Models;
using SalsaNOWGames.Services;

namespace SalsaNOWGames.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService;
        private readonly SteamApiService _steamApiService;
        private readonly DepotDownloaderService _depotDownloaderService;

        // Login state
        private bool _isLoggedIn;
        private string _steamUsername;
        private string _steamPassword;
        private string _avatarUrl;
        private string _loginError;
        private bool _isLoggingIn;
        private bool _rememberMe;

        // Game library state
        private ObservableCollection<GameInfo> _installedGames;
        private ObservableCollection<GameInfo> _searchResults;
        private GameInfo _selectedGame;
        private string _searchQuery;
        private string _appIdInput;
        private bool _isSearching;
        private bool _isDownloading;
        private string _downloadOutput;
        private string _statusMessage;

        // View state
        private string _currentView; // "login", "library", "download", "search"

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _steamApiService = new SteamApiService();
            _depotDownloaderService = new DepotDownloaderService(_settingsService.Settings.InstallDirectory);

            _installedGames = new ObservableCollection<GameInfo>();
            _searchResults = new ObservableCollection<GameInfo>();
            _currentView = "login";
            _rememberMe = true;

            // Initialize commands
            LoginCommand = new RelayCommand(async () => await LoginAsync(), () => !IsLoggingIn);
            LogoutCommand = new RelayCommand(Logout);
            SearchGamesCommand = new RelayCommand(async () => await SearchGamesAsync(), () => !IsSearching);
            DownloadByAppIdCommand = new RelayCommand(async () => await DownloadByAppIdAsync(), () => !IsDownloading);
            DownloadGameCommand = new RelayCommand(async (o) => await DownloadGameAsync(o as GameInfo), (o) => !IsDownloading);
            DeleteGameCommand = new RelayCommand(DeleteGame);
            OpenFolderCommand = new RelayCommand(OpenGameFolder);
            CancelDownloadCommand = new RelayCommand(CancelDownload, () => IsDownloading);
            ShowLibraryCommand = new RelayCommand(() => CurrentView = "library");
            ShowSearchCommand = new RelayCommand(() => CurrentView = "search");
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

            _depotDownloaderService.OnDownloadComplete += (success, message) =>
            {
                Application.Current.Dispatcher.Invoke(async () =>
                {
                    IsDownloading = false;
                    StatusMessage = message;
                    if (success && SelectedGame != null)
                    {
                        _settingsService.AddInstalledGame(SelectedGame.AppId);
                        SelectedGame.IsDownloading = false;
                        SelectedGame.IsInstalled = true;
                        SelectedGame.DownloadStatus = "Installed";
                        await RefreshInstalledGamesAsync();
                    }
                });
            };

            // Check for existing login
            CheckExistingLogin();
        }

        #region Properties

        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set => SetProperty(ref _isLoggedIn, value);
        }

        public string SteamUsername
        {
            get => _steamUsername;
            set => SetProperty(ref _steamUsername, value);
        }

        public string SteamPassword
        {
            get => _steamPassword;
            set => SetProperty(ref _steamPassword, value);
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

        public bool RememberMe
        {
            get => _rememberMe;
            set => SetProperty(ref _rememberMe, value);
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
        public ICommand OpenFolderCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand ShowLibraryCommand { get; }
        public ICommand ShowSearchCommand { get; }
        public ICommand ShowDownloadCommand { get; }
        public ICommand RefreshLibraryCommand { get; }

        #endregion

        #region Methods

        private void CheckExistingLogin()
        {
            if (_settingsService.IsLoggedIn)
            {
                IsLoggedIn = true;
                SteamUsername = _settingsService.Settings.SteamUsername;
                AvatarUrl = _settingsService.Settings.AvatarUrl;
                CurrentView = "library";
                _ = RefreshInstalledGamesAsync();
            }
        }

        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(SteamUsername))
            {
                LoginError = "Please enter your Steam username.";
                return;
            }

            if (string.IsNullOrWhiteSpace(SteamPassword))
            {
                LoginError = "Please enter your Steam password.";
                return;
            }

            IsLoggingIn = true;
            LoginError = "";

            try
            {
                // For now, we'll just save the credentials and let DepotDownloader handle auth
                // Steam doesn't have a public OAuth for game downloads
                
                if (RememberMe)
                {
                    // Store login info (username only for display, not password)
                    _settingsService.SetLogin(SteamUsername, SteamUsername, "");
                }

                IsLoggedIn = true;
                CurrentView = "library";
                StatusMessage = $"Welcome, {SteamUsername}!";
                await RefreshInstalledGamesAsync();
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
            IsLoggedIn = false;
            SteamUsername = "";
            SteamPassword = "";
            AvatarUrl = "";
            CurrentView = "login";
            InstalledGames.Clear();
            SearchResults.Clear();
            StatusMessage = "";
        }

        private async Task RefreshInstalledGamesAsync()
        {
            InstalledGames.Clear();

            string gamesDir = _depotDownloaderService.GamesDirectory;
            if (!Directory.Exists(gamesDir)) return;

            foreach (var dir in Directory.GetDirectories(gamesDir))
            {
                string appIdFile = Path.Combine(dir, "steam_appid.txt");
                if (File.Exists(appIdFile))
                {
                    string appId = File.ReadAllText(appIdFile).Trim();
                    var gameInfo = await _steamApiService.GetGameInfoAsync(appId);
                    gameInfo.IsInstalled = true;
                    gameInfo.InstallPath = dir;
                    gameInfo.SizeOnDisk = _depotDownloaderService.GetGameSize(appId);
                    InstalledGames.Add(gameInfo);
                }
            }

            StatusMessage = $"{InstalledGames.Count} game(s) in library";
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
                    // Check if already installed
                    game.IsInstalled = _depotDownloaderService.IsGameInstalled(game.AppId);
                    SearchResults.Add(game);
                }

                StatusMessage = $"Found {results.Count} game(s)";
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

            if (string.IsNullOrWhiteSpace(SteamPassword))
            {
                StatusMessage = "Please enter your Steam password to download games.";
                CurrentView = "download";
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
                await _depotDownloaderService.DownloadGameAsync(game.AppId, SteamUsername, SteamPassword);
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
                    if (_depotDownloaderService.DeleteGame(game.AppId))
                    {
                        _settingsService.RemoveInstalledGame(game.AppId);
                        InstalledGames.Remove(game);
                        StatusMessage = $"{game.Name} has been deleted.";
                    }
                    else
                    {
                        StatusMessage = "Failed to delete game.";
                    }
                }
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

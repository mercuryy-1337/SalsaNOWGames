using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SalsaNOWGames.Models;

namespace SalsaNOWGames.Services
{
    [DataContract]
    public class OwnedGamesResponse
    {
        [DataMember(Name = "cache_hit")]
        public bool CacheHit { get; set; }

        [DataMember(Name = "game_count")]
        public int GameCount { get; set; }

        [DataMember(Name = "games")]
        public List<OwnedGameData> Games { get; set; }

        [DataMember(Name = "generated_at")]
        public string GeneratedAt { get; set; }

        [DataMember(Name = "steamid64")]
        public string SteamId64 { get; set; }
    }

    [DataContract]
    public class OwnedGameData
    {
        [DataMember(Name = "appid")]
        public int AppId { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "header_image_url")]
        public string HeaderImageUrl { get; set; }

        [DataMember(Name = "icon_url")]
        public string IconUrl { get; set; }

        [DataMember(Name = "playtime_forever_minutes")]
        public int PlaytimeForeverMinutes { get; set; }

        // Install status fields (merged from games.json)
        [DataMember(Name = "install_salsa")]
        public bool InstallSalsa { get; set; }

        [DataMember(Name = "install_steam")]
        public bool InstallSteam { get; set; }

        [DataMember(Name = "install_path")]
        public string InstallPath { get; set; }

        [DataMember(Name = "has_shortcut")]
        public bool HasShortcut { get; set; }

        [DataMember(Name = "installed_date")]
        public string InstalledDate { get; set; }
    }

    // Fetches owned games from SalsaNOW API, caches locally in salsa.vdf (6-hour expiry)
    public class OwnedGamesService
    {
        private const string ApiBaseUrl = "https://slsapi.geforcenowspecs.cloud/owned/v1?steamid=";
        //private const string ApiBaseUrl = "http://127.0.0.1:5555/owned/v1?steamid=";
        private const int CacheExpiryHours = 6;
        private const int MinRefreshIntervalSeconds = 30;
        
        private readonly SteamVdfService _steamVdfService;
        private readonly string _cacheFilePath;
        
        private OwnedGamesResponse _cachedResponse;
        private DateTime _lastFetchTime = DateTime.MinValue;
        private DateTime _lastRefreshAttempt = DateTime.MinValue;
        
        public event Action<OwnedGamesResponse> OnOwnedGamesUpdated;

        public OwnedGamesService()
        {
            _steamVdfService = new SteamVdfService();
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalsaNOWGames");
            Directory.CreateDirectory(appDataPath);
            _cacheFilePath = Path.Combine(appDataPath, "salsa.vdf");
        }

        public OwnedGamesService(SteamVdfService steamVdfService)
        {
            _steamVdfService = steamVdfService;
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalsaNOWGames");
            Directory.CreateDirectory(appDataPath);
            _cacheFilePath = Path.Combine(appDataPath, "salsa.vdf");
        }

        public async Task<OwnedGamesResponse> GetOwnedGamesAsync(string accountName, bool forceRefresh = false)
        {
            LogService.Log($"GetOwnedGamesAsync called for account: {accountName}, forceRefresh: {forceRefresh}");
            
            if (string.IsNullOrEmpty(accountName))
            {
                LogService.LogWarning("GetOwnedGamesAsync: accountName is null or empty");
                return null;
            }

            string steamId64 = _steamVdfService.GetSteamId64(accountName);
            if (string.IsNullOrEmpty(steamId64))
            {
                LogService.LogWarning($"GetOwnedGamesAsync: Could not resolve SteamID64 for account '{accountName}'");
                return null;
            }
            
            LogService.Log($"Resolved SteamID64: {steamId64}");

            if (forceRefresh)
            {
                var timeSinceLastRefresh = DateTime.Now - _lastRefreshAttempt;
                if (timeSinceLastRefresh.TotalSeconds < MinRefreshIntervalSeconds)
                {
                    LogService.Log("Refresh throttled, returning cached data");
                    return _cachedResponse ?? LoadFromCache(steamId64);
                }
                _lastRefreshAttempt = DateTime.Now;
            }

            if (_cachedResponse == null)
                _cachedResponse = LoadFromCache(steamId64);

            bool cacheValid = _cachedResponse != null && 
                              (DateTime.Now - _lastFetchTime).TotalHours < CacheExpiryHours;

            if (cacheValid && !forceRefresh)
            {
                LogService.Log($"Using valid cache ({_cachedResponse?.GameCount ?? 0} games)");
                if ((DateTime.Now - _lastFetchTime).TotalHours > 3)
                    _ = RefreshInBackgroundAsync(steamId64);
                return _cachedResponse;
            }

            // Cache expired or force refresh - fetch new data
            LogService.Log("Cache invalid or force refresh - fetching from API");
            if (forceRefresh || !cacheValid)
            {
                var freshData = await FetchFromApiAsync(steamId64);
                if (freshData != null)
                {
                    _cachedResponse = freshData;
                    _lastFetchTime = DateTime.Now;
                    SaveToCache(freshData);
                    return freshData;
                }
                else
                {
                    LogService.LogWarning("API returned null, falling back to cached data");
                }
            }

            return _cachedResponse;
        }

        // Returns cached data immediately (no network calls) - use for instant startup
        public OwnedGamesResponse GetCachedOwnedGames(string accountName)
        {
            LogService.Log($"GetCachedOwnedGames called for account: {accountName}");
            
            if (string.IsNullOrEmpty(accountName))
            {
                LogService.LogWarning("GetCachedOwnedGames: accountName is null or empty");
                return null;
            }

            string steamId64 = _steamVdfService.GetSteamId64(accountName);
            if (string.IsNullOrEmpty(steamId64))
            {
                LogService.LogWarning($"GetCachedOwnedGames: Could not resolve SteamID64 for account '{accountName}'");
                return null;
            }

            if (_cachedResponse != null)
            {
                LogService.Log($"Returning in-memory cache ({_cachedResponse.GameCount} games)");
                return _cachedResponse;
            }

            _cachedResponse = LoadFromCache(steamId64);
            if (_cachedResponse == null)
            {
                LogService.LogWarning("No cached data available - library will be empty until API fetch completes");
            }
            return _cachedResponse;
        }

        // Starts background refresh without blocking
        public void StartBackgroundRefresh(string accountName)
        {
            LogService.Log($"StartBackgroundRefresh called for account: {accountName}");
            
            if (string.IsNullOrEmpty(accountName))
            {
                LogService.LogWarning("StartBackgroundRefresh: accountName is null or empty");
                return;
            }

            string steamId64 = _steamVdfService.GetSteamId64(accountName);
            if (string.IsNullOrEmpty(steamId64))
            {
                LogService.LogWarning($"StartBackgroundRefresh: Could not resolve SteamID64 for account '{accountName}'");
                return;
            }

            LogService.Log($"Starting background refresh for SteamID64: {steamId64}");
            _ = RefreshInBackgroundAsync(steamId64);
        }

        private async Task RefreshInBackgroundAsync(string steamId64)
        {
            try
            {
                var freshData = await FetchFromApiAsync(steamId64);
                if (freshData != null)
                {
                    _cachedResponse = freshData;
                    _lastFetchTime = DateTime.Now;
                    SaveToCache(freshData);
                    LogService.Log($"Background refresh completed with {freshData.GameCount} games");
                    OnOwnedGamesUpdated?.Invoke(freshData);
                }
                else
                {
                    LogService.LogWarning("Background refresh returned null - API may be unavailable");
                }
            }
            catch (Exception ex)
            {
                LogService.LogError("Background refresh failed", ex);
            }
        }

        private async Task<OwnedGamesResponse> FetchFromApiAsync(string steamId64)
        {
            string url = ApiBaseUrl + steamId64;
            try
            {
                LogService.Log($"Fetching owned games from API for SteamID: {steamId64}");
                LogService.Log($"API URL: {url}");
                string json;

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "SalsaNOWGames/1.0");
                    json = await client.DownloadStringTaskAsync(url);
                }

                LogService.Log($"API response received, parsing JSON ({json.Length} bytes)");
                
                var serializer = new DataContractJsonSerializer(typeof(OwnedGamesResponse));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var response = (OwnedGamesResponse)serializer.ReadObject(ms);
                    LogService.LogApi(url, true, $"Got {response?.GameCount ?? 0} games");
                    return response;
                }
            }
            catch (WebException webEx)
            {
                string errorDetails = $"Status: {webEx.Status}";
                if (webEx.Response is HttpWebResponse httpResponse)
                {
                    errorDetails += $", HTTP {(int)httpResponse.StatusCode} {httpResponse.StatusDescription}";
                }
                LogService.LogApi(url, false, errorDetails);
                LogService.LogError("API request failed", webEx);
                return null;
            }
            catch (Exception ex)
            {
                LogService.LogApi(url, false, ex.Message);
                LogService.LogError("Failed to fetch owned games", ex);
                return null;
            }
        }

        public async Task<OwnedGamesResponse> GetOwnedGamesBySteamIdAsync(string steamId64)
        {
            if (string.IsNullOrEmpty(steamId64))
                return null;

            if (_cachedResponse != null && _cachedResponse.SteamId64 == steamId64)
            {
                if ((DateTime.Now - _lastFetchTime).TotalHours < CacheExpiryHours)
                    return _cachedResponse;
            }

            return await FetchFromApiAsync(steamId64);
        }

        #region VDF Cache

        private void SaveToCache(OwnedGamesResponse response)
        {
            if (response == null) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("\"SalsaCache\"");
                sb.AppendLine("{");
                sb.AppendLine($"\t\"steamid64\"\t\t\"{response.SteamId64}\"");
                sb.AppendLine($"\t\"cached_at\"\t\t\"{DateTime.Now:O}\"");
                sb.AppendLine($"\t\"game_count\"\t\t\"{response.GameCount}\"");
                sb.AppendLine("\t\"games\"");
                sb.AppendLine("\t{");

                if (response.Games != null)
                {
                    foreach (var game in response.Games)
                    {
                        sb.AppendLine($"\t\t\"{game.AppId}\"");
                        sb.AppendLine("\t\t{");
                        sb.AppendLine($"\t\t\t\"name\"\t\t\"{EscapeVdfString(game.Name)}\"");
                        sb.AppendLine($"\t\t\t\"header_image_url\"\t\t\"{game.HeaderImageUrl}\"");
                        sb.AppendLine($"\t\t\t\"icon_url\"\t\t\"{game.IconUrl}\"");
                        sb.AppendLine($"\t\t\t\"playtime_forever_minutes\"\t\t\"{game.PlaytimeForeverMinutes}\"");
                        sb.AppendLine($"\t\t\t\"install_salsa\"\t\t\"{(game.InstallSalsa ? "1" : "0")}\"");
                        sb.AppendLine($"\t\t\t\"install_steam\"\t\t\"{(game.InstallSteam ? "1" : "0")}\"");
                        sb.AppendLine($"\t\t\t\"install_path\"\t\t\"{EscapeVdfString(game.InstallPath ?? "")}\"");
                        sb.AppendLine($"\t\t\t\"has_shortcut\"\t\t\"{(game.HasShortcut ? "1" : "0")}\"");
                        sb.AppendLine($"\t\t\t\"installed_date\"\t\t\"{EscapeVdfString(game.InstalledDate ?? "")}\"");
                        sb.AppendLine("\t\t}");
                    }
                }

                sb.AppendLine("\t}");
                sb.AppendLine("}");

                File.WriteAllText(_cacheFilePath, sb.ToString(), Encoding.UTF8);
                LogService.Log($"Saved {response.GameCount} games to cache: {_cacheFilePath}");
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to save cache to {_cacheFilePath}", ex);
            }
        }

        private OwnedGamesResponse LoadFromCache(string expectedSteamId64)
        {
            try
            {
                LogService.Log($"Loading cache from: {_cacheFilePath}");
                
                if (!File.Exists(_cacheFilePath))
                {
                    LogService.LogWarning($"Cache file does not exist: {_cacheFilePath}");
                    return null;
                }

                string content = File.ReadAllText(_cacheFilePath, Encoding.UTF8);
                LogService.Log($"Cache file loaded ({content.Length} bytes)");

                var cachedAtMatch = Regex.Match(content, @"""cached_at""\s*""([^""]+)""");
                if (cachedAtMatch.Success && DateTime.TryParse(cachedAtMatch.Groups[1].Value, out DateTime cachedAt))
                    _lastFetchTime = cachedAt;

                var steamIdMatch = Regex.Match(content, @"""steamid64""\s*""(\d+)""");
                if (!steamIdMatch.Success)
                {
                    LogService.LogWarning("Cache file missing steamid64");
                    return null;
                }

                string cachedSteamId = steamIdMatch.Groups[1].Value;
                if (!string.IsNullOrEmpty(expectedSteamId64) && cachedSteamId != expectedSteamId64)
                {
                    LogService.LogWarning($"Cache steamid mismatch: expected {expectedSteamId64}, got {cachedSteamId}");
                    return null;
                }

                var gameCountMatch = Regex.Match(content, @"""game_count""\s*""(\d+)""");
                int gameCount = gameCountMatch.Success ? int.Parse(gameCountMatch.Groups[1].Value) : 0;

                var games = new List<OwnedGameData>();
                var gamesBlockMatch = Regex.Match(content, @"""games""\s*\{([\s\S]*?)\n\t\}", RegexOptions.Singleline);
                if (gamesBlockMatch.Success)
                {
                    string gamesBlock = gamesBlockMatch.Groups[1].Value;
                    
                    // Updated pattern to include new fields (optional)
                    var gamePattern = new Regex(
                        @"""(\d+)""\s*\{([^}]*)\}",
                        RegexOptions.Singleline);

                    var matches = gamePattern.Matches(gamesBlock);
                    foreach (Match match in matches)
                    {
                        string appId = match.Groups[1].Value;
                        string gameBlock = match.Groups[2].Value;

                        var nameMatch = Regex.Match(gameBlock, @"""name""\s*""([^""]*)""");
                        var headerMatch = Regex.Match(gameBlock, @"""header_image_url""\s*""([^""]*)""");
                        var iconMatch = Regex.Match(gameBlock, @"""icon_url""\s*""([^""]*)""");
                        var playtimeMatch = Regex.Match(gameBlock, @"""playtime_forever_minutes""\s*""(\d+)""");
                        var installSalsaMatch = Regex.Match(gameBlock, @"""install_salsa""\s*""([^""]*)""");
                        var installSteamMatch = Regex.Match(gameBlock, @"""install_steam""\s*""([^""]*)""");
                        var installPathMatch = Regex.Match(gameBlock, @"""install_path""\s*""([^""]*)""");
                        var hasShortcutMatch = Regex.Match(gameBlock, @"""has_shortcut""\s*""([^""]*)""");
                        var installedDateMatch = Regex.Match(gameBlock, @"""installed_date""\s*""([^""]*)""");

                        games.Add(new OwnedGameData
                        {
                            AppId = int.Parse(appId),
                            Name = nameMatch.Success ? UnescapeVdfString(nameMatch.Groups[1].Value) : "",
                            HeaderImageUrl = headerMatch.Success ? headerMatch.Groups[1].Value : "",
                            IconUrl = iconMatch.Success ? iconMatch.Groups[1].Value : "",
                            PlaytimeForeverMinutes = playtimeMatch.Success ? int.Parse(playtimeMatch.Groups[1].Value) : 0,
                            InstallSalsa = installSalsaMatch.Success && installSalsaMatch.Groups[1].Value == "1",
                            InstallSteam = installSteamMatch.Success && installSteamMatch.Groups[1].Value == "1",
                            InstallPath = installPathMatch.Success ? UnescapeVdfString(installPathMatch.Groups[1].Value) : "",
                            HasShortcut = hasShortcutMatch.Success && hasShortcutMatch.Groups[1].Value == "1",
                            InstalledDate = installedDateMatch.Success ? UnescapeVdfString(installedDateMatch.Groups[1].Value) : ""
                        });
                    }
                }

                var result = new OwnedGamesResponse
                {
                    SteamId64 = cachedSteamId,
                    GameCount = gameCount,
                    Games = games,
                    CacheHit = true
                };
                
                LogService.Log($"Cache loaded successfully: {games.Count} games for SteamID {cachedSteamId}");
                return result;
            }
            catch (Exception ex)
            {
                LogService.LogError("Failed to load cache", ex);
                return null;
            }
        }

        private string EscapeVdfString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private string UnescapeVdfString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        #endregion

        public List<GameInfo> ConvertToGameInfoList(OwnedGamesResponse response)
        {
            var games = new List<GameInfo>();
            if (response?.Games == null)
                return games;

            foreach (var game in response.Games)
            {
                games.Add(new GameInfo
                {
                    AppId = game.AppId.ToString(),
                    Name = game.Name ?? $"App {game.AppId}",
                    HeaderImageUrl = game.HeaderImageUrl,
                    IconUrl = game.IconUrl,
                    PlaytimeMinutes = game.PlaytimeForeverMinutes,
                    IsInstalled = false
                });
            }
            return games;
        }

        public string GetSteamId64(string accountName) => _steamVdfService.GetSteamId64(accountName);

        public bool IsCacheExpired() => (DateTime.Now - _lastFetchTime).TotalHours >= CacheExpiryHours;

        public void ClearCache()
        {
            _cachedResponse = null;
            _lastFetchTime = DateTime.MinValue;
            try
            {
                if (File.Exists(_cacheFilePath))
                    File.Delete(_cacheFilePath);
            }
            catch { }
        }

        #region Game Data Management

        // Update install status for a game in salsa.vdf
        public void UpdateGameInstallStatus(string appId, bool salsaInstalled, bool steamInstalled, string installPath = null)
        {
            if (_cachedResponse?.Games == null) return;

            var game = _cachedResponse.Games.FirstOrDefault(g => g.AppId.ToString() == appId);
            if (game != null)
            {
                game.InstallSalsa = salsaInstalled;
                game.InstallSteam = steamInstalled;
                if (installPath != null)
                    game.InstallPath = installPath;
                if (salsaInstalled && string.IsNullOrEmpty(game.InstalledDate))
                    game.InstalledDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                SaveToCache(_cachedResponse);
            }
        }

        // Update has_shortcut for a game
        public void UpdateGameShortcut(string appId, bool hasShortcut)
        {
            if (_cachedResponse?.Games == null) return;

            var game = _cachedResponse.Games.FirstOrDefault(g => g.AppId.ToString() == appId);
            if (game != null)
            {
                game.HasShortcut = hasShortcut;
                SaveToCache(_cachedResponse);
            }
        }

        // Mark game as installed via Salsa
        public void MarkGameAsInstalled(string appId, string installPath)
        {
            UpdateGameInstallStatus(appId, true, false, installPath);
        }

        // Mark game as uninstalled (Salsa)
        public void MarkGameAsUninstalled(string appId)
        {
            if (_cachedResponse?.Games == null) return;

            var game = _cachedResponse.Games.FirstOrDefault(g => g.AppId.ToString() == appId);
            if (game != null)
            {
                game.InstallSalsa = false;
                game.InstallPath = "";
                SaveToCache(_cachedResponse);
            }
        }

        // Get game data by appId
        public OwnedGameData GetGameData(string appId)
        {
            return _cachedResponse?.Games?.FirstOrDefault(g => g.AppId.ToString() == appId);
        }

        // Check if game is installed via Salsa (from salsa.vdf)
        public bool IsInstalledViaSalsa(string appId)
        {
            var game = GetGameData(appId);
            return game?.InstallSalsa ?? false;
        }

        // Check if game has shortcut (from salsa.vdf)
        public bool HasShortcut(string appId)
        {
            var game = GetGameData(appId);
            return game?.HasShortcut ?? false;
        }

        // Get install path (from salsa.vdf)
        public string GetInstallPath(string appId)
        {
            var game = GetGameData(appId);
            return game?.InstallPath;
        }

        // Merge data from games.json into salsa.vdf (one-time migration)
        // If a game in games.json doesn't exist in salsa.vdf, ADD it
        public void MergeFromGamesJson(List<InstalledGame> gamesJsonData)
        {
            if (_cachedResponse == null || gamesJsonData == null) return;
            if (_cachedResponse.Games == null)
                _cachedResponse.Games = new List<OwnedGameData>();

            bool anyChanges = false;
            foreach (var jsonGame in gamesJsonData)
            {
                var vdfGame = _cachedResponse.Games.FirstOrDefault(g => g.AppId.ToString() == jsonGame.Id);
                
                if (vdfGame == null)
                {
                    // Game doesn't exist in salsa.vdf - ADD it from games.json
                    // Convert old header.jpg format to library_600x900.jpg format
                    string headerUrl = ConvertToLibrary600x900(jsonGame.HeaderImageUrl);
                    
                    var newGame = new OwnedGameData
                    {
                        AppId = int.TryParse(jsonGame.Id, out int appId) ? appId : 0,
                        Name = jsonGame.Name ?? $"App {jsonGame.Id}",
                        HeaderImageUrl = headerUrl,
                        IconUrl = "",
                        PlaytimeForeverMinutes = 0,
                        InstallSalsa = jsonGame.Install?.Salsa ?? false,
                        InstallSteam = jsonGame.Install?.Steam ?? false,
                        InstallPath = jsonGame.InstallPath ?? "",
                        HasShortcut = jsonGame.HasShortcut,
                        InstalledDate = jsonGame.InstalledDate ?? ""
                    };
                    _cachedResponse.Games.Add(newGame);
                    _cachedResponse.GameCount = _cachedResponse.Games.Count;
                    anyChanges = true;
                }
                else
                {
                    // Game exists - merge/update data
                    if (jsonGame.Install?.Salsa == true && !vdfGame.InstallSalsa)
                    {
                        vdfGame.InstallSalsa = true;
                        anyChanges = true;
                    }
                    if (jsonGame.Install?.Steam == true && !vdfGame.InstallSteam)
                    {
                        vdfGame.InstallSteam = true;
                        anyChanges = true;
                    }
                    
                    // Merge install path
                    if (!string.IsNullOrEmpty(jsonGame.InstallPath) && string.IsNullOrEmpty(vdfGame.InstallPath))
                    {
                        vdfGame.InstallPath = jsonGame.InstallPath;
                        anyChanges = true;
                    }
                    
                    // Merge shortcut status
                    if (jsonGame.HasShortcut && !vdfGame.HasShortcut)
                    {
                        vdfGame.HasShortcut = true;
                        anyChanges = true;
                    }
                    
                    // Merge installed date
                    if (!string.IsNullOrEmpty(jsonGame.InstalledDate) && string.IsNullOrEmpty(vdfGame.InstalledDate))
                    {
                        vdfGame.InstalledDate = jsonGame.InstalledDate;
                        anyChanges = true;
                    }
                }
            }

            if (anyChanges)
                SaveToCache(_cachedResponse);
        }

        // Convert old header.jpg URL to library_600x900.jpg format
        private string ConvertToLibrary600x900(string headerUrl)
        {
            if (string.IsNullOrEmpty(headerUrl))
                return "";
            
            // Already in correct format
            if (headerUrl.Contains("library_600x900"))
                return headerUrl;
            
            // Convert header.jpg to library_600x900.jpg
            // e.g. https://cdn.akamai.steamstatic.com/steam/apps/2129510/header.jpg - cataire :)
            // ->   https://cdn.akamai.steamstatic.com/steam/apps/2129510/library_600x900.jpg
            if (headerUrl.Contains("/header.jpg"))
                return headerUrl.Replace("/header.jpg", "/library_600x900.jpg");
            
            // Also handle capsule images
            if (headerUrl.Contains("/capsule_"))
                return Regex.Replace(headerUrl, @"/capsule_\d+x\d+\.jpg", "/library_600x900.jpg");
            
            return headerUrl;
        }

        #endregion
    }
}

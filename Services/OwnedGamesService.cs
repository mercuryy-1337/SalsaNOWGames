using System;
using System.Collections.Generic;
using System.IO;
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
    }

    // Fetches owned games from SalsaNOW API, caches locally in salsa.vdf (6-hour expiry)
    public class OwnedGamesService
    {
        private const string ApiBaseUrl = "https://slsapi.geforcenowspecs.cloud/owned/v1?steamid=";
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
            if (string.IsNullOrEmpty(accountName))
                return null;

            string steamId64 = _steamVdfService.GetSteamId64(accountName);
            if (string.IsNullOrEmpty(steamId64))
                return null;

            if (forceRefresh)
            {
                var timeSinceLastRefresh = DateTime.Now - _lastRefreshAttempt;
                if (timeSinceLastRefresh.TotalSeconds < MinRefreshIntervalSeconds)
                    return _cachedResponse ?? LoadFromCache(steamId64);
                _lastRefreshAttempt = DateTime.Now;
            }

            if (_cachedResponse == null)
                _cachedResponse = LoadFromCache(steamId64);

            bool cacheValid = _cachedResponse != null && 
                              (DateTime.Now - _lastFetchTime).TotalHours < CacheExpiryHours;

            if (cacheValid && !forceRefresh)
            {
                if ((DateTime.Now - _lastFetchTime).TotalHours > 3)
                    _ = RefreshInBackgroundAsync(steamId64);
                return _cachedResponse;
            }

            // Cache expired or force refresh - fetch new data
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
            }

            return _cachedResponse;
        }

        // Returns cached data immediately (no network calls) - use for instant startup
        public OwnedGamesResponse GetCachedOwnedGames(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
                return null;

            string steamId64 = _steamVdfService.GetSteamId64(accountName);
            if (string.IsNullOrEmpty(steamId64))
                return null;

            if (_cachedResponse != null)
                return _cachedResponse;

            _cachedResponse = LoadFromCache(steamId64);
            return _cachedResponse;
        }

        // Starts background refresh without blocking
        public void StartBackgroundRefresh(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
                return;

            string steamId64 = _steamVdfService.GetSteamId64(accountName);
            if (string.IsNullOrEmpty(steamId64))
                return;

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
                    OnOwnedGamesUpdated?.Invoke(freshData);
                }
            }
            catch { }
        }

        private async Task<OwnedGamesResponse> FetchFromApiAsync(string steamId64)
        {
            try
            {
                string url = ApiBaseUrl + steamId64;
                string json;

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "SalsaNOWGames/1.0");
                    json = await client.DownloadStringTaskAsync(url);
                }

                var serializer = new DataContractJsonSerializer(typeof(OwnedGamesResponse));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (OwnedGamesResponse)serializer.ReadObject(ms);
                }
            }
            catch
            {
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
                        sb.AppendLine("\t\t}");
                    }
                }

                sb.AppendLine("\t}");
                sb.AppendLine("}");

                File.WriteAllText(_cacheFilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private OwnedGamesResponse LoadFromCache(string expectedSteamId64)
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                    return null;

                string content = File.ReadAllText(_cacheFilePath, Encoding.UTF8);

                var cachedAtMatch = Regex.Match(content, @"""cached_at""\s*""([^""]+)""");
                if (cachedAtMatch.Success && DateTime.TryParse(cachedAtMatch.Groups[1].Value, out DateTime cachedAt))
                    _lastFetchTime = cachedAt;

                var steamIdMatch = Regex.Match(content, @"""steamid64""\s*""(\d+)""");
                if (!steamIdMatch.Success)
                    return null;

                string cachedSteamId = steamIdMatch.Groups[1].Value;
                if (!string.IsNullOrEmpty(expectedSteamId64) && cachedSteamId != expectedSteamId64)
                    return null;

                var gameCountMatch = Regex.Match(content, @"""game_count""\s*""(\d+)""");
                int gameCount = gameCountMatch.Success ? int.Parse(gameCountMatch.Groups[1].Value) : 0;

                var games = new List<OwnedGameData>();
                var gamesBlockMatch = Regex.Match(content, @"""games""\s*\{([\s\S]*?)\n\t\}", RegexOptions.Singleline);
                if (gamesBlockMatch.Success)
                {
                    string gamesBlock = gamesBlockMatch.Groups[1].Value;
                    var gamePattern = new Regex(
                        @"""(\d+)""\s*\{[^}]*""name""\s*""([^""]*)""\s*""header_image_url""\s*""([^""]*)""\s*""icon_url""\s*""([^""]*)""\s*""playtime_forever_minutes""\s*""(\d+)""[^}]*\}",
                        RegexOptions.Singleline);

                    var matches = gamePattern.Matches(gamesBlock);
                    foreach (Match match in matches)
                    {
                        games.Add(new OwnedGameData
                        {
                            AppId = int.Parse(match.Groups[1].Value),
                            Name = UnescapeVdfString(match.Groups[2].Value),
                            HeaderImageUrl = match.Groups[3].Value,
                            IconUrl = match.Groups[4].Value,
                            PlaytimeForeverMinutes = int.Parse(match.Groups[5].Value)
                        });
                    }
                }

                return new OwnedGamesResponse
                {
                    SteamId64 = cachedSteamId,
                    GameCount = gameCount,
                    Games = games,
                    CacheHit = true
                };
            }
            catch
            {
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
    }
}

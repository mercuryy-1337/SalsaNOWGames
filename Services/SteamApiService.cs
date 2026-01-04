using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using SalsaNOWGames.Models;

namespace SalsaNOWGames.Services
{
    public class SteamApiService
    {
        private readonly HttpClient _httpClient;
        private readonly JavaScriptSerializer _jsonSerializer;

        public SteamApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SalsaNOWGames/1.0");
            _jsonSerializer = new JavaScriptSerializer();
        }

        /*
         * Gets the Steam header image URL for a given app ID.
         * Format: https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg
         */
        public string GetHeaderImageUrl(string appId)
        {
            return $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";
        }

        /* Gets the Steam library capsule image URL (600x900 vertical). */
        public string GetLibraryCapsuleUrl(string appId)
        {
            return $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg";
        }

        /* Gets game info from Steam Store API. */
        public async Task<GameInfo> GetGameInfoAsync(string appId)
        {
            var gameInfo = new GameInfo
            {
                AppId = appId,
                HeaderImageUrl = GetHeaderImageUrl(appId)
            };

            try
            {
                string url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
                var response = await _httpClient.GetStringAsync(url);
                
                var data = _jsonSerializer.Deserialize<Dictionary<string, object>>(response);
                if (data != null && data.ContainsKey(appId))
                {
                    var appData = data[appId] as Dictionary<string, object>;
                    if (appData != null && appData.ContainsKey("success") && (bool)appData["success"])
                    {
                        var gameData = appData["data"] as Dictionary<string, object>;
                        if (gameData != null)
                        {
                            if (gameData.ContainsKey("name"))
                            {
                                gameInfo.Name = gameData["name"]?.ToString();
                            }
                            if (gameData.ContainsKey("header_image"))
                            {
                                gameInfo.HeaderImageUrl = gameData["header_image"]?.ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get game info: {ex.Message}");
                gameInfo.Name = $"App {appId}";
            }

            if (string.IsNullOrEmpty(gameInfo.Name))
            {
                gameInfo.Name = $"App {appId}";
            }

            return gameInfo;
        }

        /* Search for games on Steam. */
        public async Task<List<GameInfo>> SearchGamesAsync(string query)
        {
            var results = new List<GameInfo>();

            try
            {
                // Using Steam's store search API
                string url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(query)}&l=english&cc=US";
                var response = await _httpClient.GetStringAsync(url);

                var data = _jsonSerializer.Deserialize<Dictionary<string, object>>(response);
                if (data != null && data.ContainsKey("items"))
                {
                    var items = data["items"] as System.Collections.ArrayList;
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            var itemDict = item as Dictionary<string, object>;
                            if (itemDict != null)
                            {
                                var gameInfo = new GameInfo();

                                if (itemDict.ContainsKey("id"))
                                {
                                    gameInfo.AppId = itemDict["id"]?.ToString();
                                }
                                if (itemDict.ContainsKey("name"))
                                {
                                    gameInfo.Name = itemDict["name"]?.ToString();
                                }
                                
                                gameInfo.HeaderImageUrl = GetHeaderImageUrl(gameInfo.AppId);

                                if (!string.IsNullOrEmpty(gameInfo.AppId))
                                {
                                    results.Add(gameInfo);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Search failed: {ex.Message}");
            }

            return results;
        }

        /* Gets Steam user profile using Steam Web API (requires Steam ID). */
        public async Task<(string username, string avatarUrl)> GetUserProfileAsync(string steamId)
        {
            try
            {
                // This requires an API key for full profile, but we can get basic info from community
                string url = $"https://steamcommunity.com/profiles/{steamId}/?xml=1";
                var response = await _httpClient.GetStringAsync(url);

                // Parse XML response (basic parsing)
                string username = ExtractXmlValue(response, "steamID");
                string avatarUrl = ExtractXmlValue(response, "avatarMedium");

                return (username ?? steamId, avatarUrl ?? "");
            }
            catch
            {
                return (steamId, "");
            }
        }

        private string ExtractXmlValue(string xml, string tagName)
        {
            try
            {
                string startTag = $"<{tagName}>";
                string endTag = $"</{tagName}>";
                int startIndex = xml.IndexOf(startTag);
                if (startIndex < 0) return null;
                startIndex += startTag.Length;
                int endIndex = xml.IndexOf(endTag, startIndex);
                if (endIndex < 0) return null;
                
                string value = xml.Substring(startIndex, endIndex - startIndex);
                // Handle CDATA
                if (value.StartsWith("<![CDATA["))
                {
                    value = value.Substring(9);
                    if (value.EndsWith("]]>"))
                        value = value.Substring(0, value.Length - 3);
                }
                return value;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SalsaNOWGames.Services
{
    /// <summary>
    /// Fetches Steam game header images with local caching.
    /// Flow: Local cache -> CDN fallback -> IStoreBrowseService API
    /// </summary>
    public class SteamHeaderService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer();
        private static readonly SemaphoreSlim _fetchSemaphore = new SemaphoreSlim(3, 3); // Max 3 concurrent fetches
        
        private readonly string _cacheDirectory;
        
        // CDN options for constructing full URLs, don't really know which cdn steam will try to use for said image so we just try all 3
        private static readonly string[] CdnHosts = new[]
        {
            "https://shared.fastly.steamstatic.com",
            "https://shared.akamai.steamstatic.com",
            "https://shared.cloudflare.steamstatic.com"
        };

        // Fallback CDN URLs (tried first before API)
        private static readonly string[] FallbackPatterns = new[]
        {
            "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{0}/header.jpg",
            "https://cdn.akamai.steamstatic.com/steam/apps/{0}/header.jpg"
        };

        static SteamHeaderService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SalsaNOWGames/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public SteamHeaderService()
        {
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalsaNOWGames",
                "cached_library_images");
            Directory.CreateDirectory(_cacheDirectory);
        }

        /// <summary>
        /// Gets header image path - checks local cache first, then fetches online
        /// Returns local file path if cached, otherwise URL
        /// </summary>
        public async Task<string> GetHeaderImageAsync(string appId, bool forceOnline = false)
        {
            // Check local cache first (unless forced online)
            if (!forceOnline)
            {
                string cachedPath = GetCachedImagePath(appId);
                if (File.Exists(cachedPath))
                {
                    return cachedPath;
                }
            }

            // Try CDN fallback URLs first (faster, no API call)
            foreach (var pattern in FallbackPatterns)
            {
                string cdnUrl = string.Format(pattern, appId);
                if (await IsValidImageUrlAsync(cdnUrl))
                {
                    return cdnUrl;
                }
            }

            // CDN failed - try IStoreBrowseService API
            var apiResult = await GetHeaderPathFromApiAsync(appId);
            if (apiResult.success && !string.IsNullOrEmpty(apiResult.headerPath))
            {
                // Try each CDN with the API path
                foreach (var cdnHost in CdnHosts)
                {
                    string fullUrl = cdnHost + apiResult.headerPath;
                    if (await IsValidImageUrlAsync(fullUrl))
                    {
                        return fullUrl;
                    }
                }
                
                // Return first CDN even if not validated
                return CdnHosts[0] + apiResult.headerPath;
            }

            // Last resort: return first fallback pattern
            return string.Format(FallbackPatterns[0], appId);
        }

        /// <summary>
        /// Downloads and caches header image locally
        /// </summary>
        public async Task<string> DownloadAndCacheImageAsync(string appId, string imageUrl)
        {
            LogService.Log($"Downloading header image for {appId} from {imageUrl}...");
            await _fetchSemaphore.WaitAsync();
            try
            {
                string cachedPath = GetCachedImagePath(appId);
                
                // Skip if already cached
                if (File.Exists(cachedPath))
                {
                    return cachedPath;
                }

                // Download the image
                var response = await _httpClient.GetAsync(imageUrl);
                if (response.IsSuccessStatusCode)
                {
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                    if (contentType.StartsWith("image/"))
                    {
                        byte[] imageData = await response.Content.ReadAsByteArrayAsync();
                        File.WriteAllBytes(cachedPath, imageData);
                        LogService.Log($"Cached header image for {appId}");
                        return cachedPath;
                    }
                }
                
                return imageUrl; // Return URL if caching failed
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"Failed to cache image for {appId}: {ex.Message}");
                return imageUrl;
            }
            finally
            {
                _fetchSemaphore.Release();
            }
        }

        /// <summary>
        /// Gets the local cache path for an app's header image
        /// </summary>
        public string GetCachedImagePath(string appId)
        {
            return Path.Combine(_cacheDirectory, $"{appId}_header.jpg");
        }

        /// <summary>
        /// Checks if an image is cached locally
        /// </summary>
        public bool IsImageCached(string appId)
        {
            return File.Exists(GetCachedImagePath(appId));
        }

        /// <summary>
        /// Gets header path from IStoreBrowseService API
        /// Returns path like "/store_item_assets/steam/apps/624270/header.jpg?t=1766452249"
        /// </summary>
        public async Task<(bool success, string headerPath)> GetHeaderPathFromApiAsync(string appId)
        {
            await _fetchSemaphore.WaitAsync();
            try
            {
                // Build the JSON payload manually to match expected format
                string jsonPayload = string.Format(
                    @"{{""ids"":[{{""appid"":{0}}}],""context"":{{""country_code"":""US""}},""data_request"":{{""include_assets"":true}}}}",
                    appId);

                string encodedPayload = Uri.EscapeDataString(jsonPayload);
                string url = $"https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json={encodedPayload}";

                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    LogService.LogWarning($"IStoreBrowseService failed for {appId}. Status: {response.StatusCode}");
                    return (false, null);
                }

                string json = await response.Content.ReadAsStringAsync();
                var data = _jsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (data == null || !data.ContainsKey("response"))
                {
                    return (false, null);
                }
                
                var responseObj = data["response"] as Dictionary<string, object>;
                if (responseObj == null || !responseObj.ContainsKey("store_items"))
                {
                    return (false, null);
                }
                
                var storeItems = responseObj["store_items"] as System.Collections.ArrayList;
                if (storeItems == null || storeItems.Count == 0)
                {
                    return (false, null);
                }
                
                var storeItem = storeItems[0] as Dictionary<string, object>;
                if (storeItem == null || !storeItem.ContainsKey("assets"))
                {
                    return (false, null);
                }
                
                var assets = storeItem["assets"] as Dictionary<string, object>;
                if (assets == null)
                {
                    return (false, null);
                }
                
                if (!assets.ContainsKey("asset_url_format") || !assets.ContainsKey("header"))
                {
                    return (false, null);
                }
                
                string assetUrlFormat = assets["asset_url_format"]?.ToString();
                string headerFilename = assets["header"]?.ToString();
                
                if (string.IsNullOrEmpty(assetUrlFormat) || string.IsNullOrEmpty(headerFilename))
                {
                    return (false, null);
                }

                string headerPath = "/store_item_assets/" + assetUrlFormat.Replace("${FILENAME}", headerFilename);
                return (true, headerPath);
            }
            catch (Exception ex)
            {
                LogService.LogWarning($"IStoreBrowseService exception for {appId}: {ex.Message}");
                return (false, null);
            }
            finally
            {
                _fetchSemaphore.Release();
            }
        }

        /// <summary>
        /// Checks if a URL returns a valid image (200 OK with image content-type)
        /// </summary>
        private async Task<bool> IsValidImageUrlAsync(string url)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    request.Dispose();
                    response.Dispose();
                    return false;
                }
                
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                var isImage = contentType.StartsWith("image/");
                
                request.Dispose();
                response.Dispose();
                return isImage;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets fallback header URL (no API call, no validation)
        /// </summary>
        public static string GetFallbackHeaderUrl(string appId)
        {
            return string.Format(FallbackPatterns[0], appId);
        }

        /// <summary>
        /// Batch fetch header URLs for multiple app IDs with throttling (3 at a time)
        /// </summary>
        public async Task<Dictionary<string, string>> GetHeaderUrlsBatchAsync(IEnumerable<string> appIds)
        {
            var results = new Dictionary<string, string>();
            var appIdList = appIds.ToList();
            
            // Process in batches of 3
            for (int i = 0; i < appIdList.Count; i += 3)
            {
                var batch = appIdList.Skip(i).Take(3).ToList();
                var tasks = batch.Select(async appId =>
                {
                    var url = await GetHeaderImageAsync(appId);
                    return new KeyValuePair<string, string>(appId, url);
                });
                
                var completed = await Task.WhenAll(tasks);
                foreach (var kvp in completed)
                {
                    results[kvp.Key] = kvp.Value;
                }
                
                // Small delay between batches to avoid rate limiting
                if (i + 3 < appIdList.Count)
                {
                    await Task.Delay(100);
                }
            }

            return results;
        }

        /// <summary>
        /// Downloads and caches images for library games in batches
        /// </summary>
        public async Task CacheLibraryImagesAsync(IEnumerable<KeyValuePair<string, string>> appIdUrlPairs)
        {
            var pairs = appIdUrlPairs.ToList();
            
            // Process in batches of 3
            for (int i = 0; i < pairs.Count; i += 3)
            {
                LogService.Log($"Caching header images for batch starting at index {i}...");
                var batch = pairs.Skip(i).Take(3).ToList();
                var tasks = batch.Select(kvp => DownloadAndCacheImageAsync(kvp.Key, kvp.Value));
                await Task.WhenAll(tasks);
                
                // Small delay between batches
                if (i + 3 < pairs.Count)
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}

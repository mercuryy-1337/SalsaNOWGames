using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SalsaNOWGames.Models;

namespace SalsaNOWGames.Services
{
    public class SteamAuthService
    {
        private readonly string _sessionPath;
        private readonly string _depotDownloaderConfigPath;
        private SteamSession _currentSession;

        public SteamAuthService()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalsaNOWGames");
            Directory.CreateDirectory(appDataPath);
            _sessionPath = Path.Combine(appDataPath, "steam_session.json");
            
            // DepotDownloader stores its config here
            _depotDownloaderConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".DepotDownloader");
        }

        public SteamSession CurrentSession => _currentSession;
        public bool IsLoggedIn => _currentSession?.IsValid == true;

        public SteamSession LoadSession()
        {
            try
            {
                if (File.Exists(_sessionPath))
                {
                    string json = File.ReadAllText(_sessionPath);
                    var serializer = new DataContractJsonSerializer(typeof(SteamSession));
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        _currentSession = (SteamSession)serializer.ReadObject(ms);
                    }

                    // Check if session is still valid
                    if (_currentSession != null && !_currentSession.IsValid)
                    {
                        _currentSession = null;
                    }
                }
            }
            catch
            {
                _currentSession = null;
            }
            return _currentSession;
        }

        public void SaveSession(SteamSession session)
        {
            _currentSession = session;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(SteamSession));
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, session);
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    File.WriteAllText(_sessionPath, json);
                }

                // Also save for DepotDownloader
                SaveDepotDownloaderConfig(session);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save session: {ex.Message}");
            }
        }

        public void SaveDepotDownloaderConfig(SteamSession session)
        {
            try
            {
                Directory.CreateDirectory(_depotDownloaderConfigPath);

                // DepotDownloader uses a specific format for stored credentials
                // It looks for .DepotDownloader/{steamid}.json or account.json
                if (!string.IsNullOrEmpty(session.RefreshToken))
                {
                    string accountFile = Path.Combine(_depotDownloaderConfigPath, $"{session.SteamId}.json");
                    
                    // Create the config that DepotDownloader expects
                    var config = new
                    {
                        Username = session.Username,
                        RefreshToken = session.RefreshToken
                    };

                    string configJson = $"{{\"Username\":\"{session.Username}\",\"RefreshToken\":\"{session.RefreshToken}\"}}";
                    File.WriteAllText(accountFile, configJson);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save DepotDownloader config: {ex.Message}");
            }
        }

        public void ClearSession()
        {
            _currentSession = null;
            try
            {
                if (File.Exists(_sessionPath))
                {
                    File.Delete(_sessionPath);
                }

                // Also clear DepotDownloader stored credentials if needed
                if (Directory.Exists(_depotDownloaderConfigPath))
                {
                    foreach (var file in Directory.GetFiles(_depotDownloaderConfigPath, "*.json"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        /* Parse cookies from WebView2 to create a Steam session. */
        public SteamSession ParseCookies(string cookieString, string currentUrl)
        {
            var session = new SteamSession();

            // Parse steamLoginSecure cookie - this is the main auth cookie
            // Format: steamLoginSecure=steamid||token
            var loginSecureMatch = Regex.Match(cookieString, @"steamLoginSecure=([^;]+)");
            if (loginSecureMatch.Success)
            {
                session.SteamLoginSecure = Uri.UnescapeDataString(loginSecureMatch.Groups[1].Value);
                
                // Extract SteamID from the cookie value (format: steamid||token)
                var parts = session.SteamLoginSecure.Split(new[] { "||", "%7C%7C" }, StringSplitOptions.None);
                if (parts.Length >= 1)
                {
                    session.SteamId = parts[0];
                }
            }

            // Parse sessionid
            var sessionIdMatch = Regex.Match(cookieString, @"sessionid=([^;]+)");
            if (sessionIdMatch.Success)
            {
                session.SessionId = sessionIdMatch.Groups[1].Value;
            }

            // Parse steamRememberLogin for refresh capability
            var rememberMatch = Regex.Match(cookieString, @"steamRememberLogin=([^;]+)");
            if (rememberMatch.Success)
            {
                session.SteamRememberLogin = Uri.UnescapeDataString(rememberMatch.Groups[1].Value);
            }

            // Set expiry (Steam sessions typically last ~2 weeks, but we'll be conservative)
            session.ExpiresAt = DateTime.Now.AddDays(7);

            return session;
        }

        /*
         * Extract the refresh token from Steam's new JWT-based auth.
         * The steamLoginSecure cookie now contains a JWT that can be used as a refresh token.
         */
        public string ExtractRefreshToken(string steamLoginSecure)
        {
            if (string.IsNullOrEmpty(steamLoginSecure)) return null;

            // The new format is steamid||jwt_token
            var parts = steamLoginSecure.Split(new[] { "||", "%7C%7C" }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                return parts[1];
            }
            return null;
        }

        /* Check if we have a valid DepotDownloader saved session. */
        public bool HasDepotDownloaderSession(string username)
        {
            try
            {
                var files = Directory.GetFiles(_depotDownloaderConfigPath, "*.json");
                foreach (var file in files)
                {
                    string content = File.ReadAllText(file);
                    if (content.Contains($"\"{username}\""))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /* Get the arguments needed for DepotDownloader based on current auth state. */
        public string GetDepotDownloaderAuthArgs()
        {
            if (_currentSession == null || !_currentSession.IsValid)
            {
                return "";
            }

            // If we have a refresh token, DepotDownloader can use -remember-password
            // But we need to ensure the token is saved in the right location first
            return $"-username \"{_currentSession.Username}\" -remember-password";
        }
    }
}

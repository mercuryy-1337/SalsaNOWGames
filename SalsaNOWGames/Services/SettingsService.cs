using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SalsaNOWGames.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private Models.UserSettings _settings;

        public SettingsService()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SalsaNOWGames");
            Directory.CreateDirectory(appDataPath);
            _settingsPath = Path.Combine(appDataPath, "settings.json");
            LoadSettings();
        }

        public Models.UserSettings Settings => _settings;

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var serializer = new DataContractJsonSerializer(typeof(Models.UserSettings));
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        _settings = (Models.UserSettings)serializer.ReadObject(ms);
                    }
                }
            }
            catch
            {
                _settings = null;
            }

            if (_settings == null)
            {
                _settings = new Models.UserSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(Models.UserSettings));
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, _settings);
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    File.WriteAllText(_settingsPath, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public bool IsLoggedIn => !string.IsNullOrEmpty(_settings?.SteamId);

        public void ClearLogin()
        {
            _settings.SteamId = null;
            _settings.SteamUsername = null;
            _settings.AvatarUrl = null;
            _settings.LastLogin = null;
            SaveSettings();
        }

        public void SetLogin(string steamId, string username, string avatarUrl)
        {
            _settings.SteamId = steamId;
            _settings.SteamUsername = username;
            _settings.AvatarUrl = avatarUrl;
            _settings.LastLogin = DateTime.Now;
            SaveSettings();
        }

        public void AddInstalledGame(string appId)
        {
            if (!_settings.InstalledAppIds.Contains(appId))
            {
                _settings.InstalledAppIds.Add(appId);
                SaveSettings();
            }
        }

        public void RemoveInstalledGame(string appId)
        {
            if (_settings.InstalledAppIds.Contains(appId))
            {
                _settings.InstalledAppIds.Remove(appId);
                SaveSettings();
            }
        }
    }
}

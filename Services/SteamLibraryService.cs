using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SalsaNOWGames.Services
{
    // Scans Steam's appmanifest files to detect installed games
    public class SteamLibraryService
    {
        private readonly string _steamAppsPath;
        private HashSet<string> _installedAppIds;

        public SteamLibraryService()
        {
            _steamAppsPath = @"C:\Program Files (x86)\Steam\steamapps";
            _installedAppIds = new HashSet<string>();
            ScanInstalledGames();
        }

        public SteamLibraryService(string steamAppsPath)
        {
            _steamAppsPath = steamAppsPath;
            _installedAppIds = new HashSet<string>();
            ScanInstalledGames();
        }

        // Scans steamapps folder for appmanifest_*.acf files
        public void ScanInstalledGames()
        {
            _installedAppIds.Clear();

            if (!Directory.Exists(_steamAppsPath))
                return;

            try
            {
                var manifestFiles = Directory.GetFiles(_steamAppsPath, "appmanifest_*.acf");
                foreach (var file in manifestFiles)
                {
                    string appId = ExtractAppIdFromManifest(file);
                    if (!string.IsNullOrEmpty(appId))
                        _installedAppIds.Add(appId);
                }
            }
            catch { }
        }

        private string ExtractAppIdFromManifest(string filePath)
        {
            try
            {
                // Extract from filename: appmanifest_730.acf -> 730
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                var match = Regex.Match(fileName, @"appmanifest_(\d+)");
                if (match.Success)
                    return match.Groups[1].Value;

                // Fallback: read file and parse "appid" field
                string content = File.ReadAllText(filePath);
                var appIdMatch = Regex.Match(content, @"""appid""\s*""(\d+)""");
                if (appIdMatch.Success)
                    return appIdMatch.Groups[1].Value;
            }
            catch { }
            return null;
        }

        // Checks if a game is installed via Steam
        public bool IsInstalledViaSteam(string appId)
        {
            return _installedAppIds.Contains(appId);
        }

        // Gets all Steam-installed app IDs
        public HashSet<string> GetInstalledAppIds()
        {
            return new HashSet<string>(_installedAppIds);
        }

        // Gets the install path for a Steam game from its manifest
        public string GetSteamInstallPath(string appId)
        {
            if (!Directory.Exists(_steamAppsPath))
                return null;

            string manifestPath = Path.Combine(_steamAppsPath, $"appmanifest_{appId}.acf");
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                string content = File.ReadAllText(manifestPath);
                var installDirMatch = Regex.Match(content, @"""installdir""\s*""([^""]+)""");
                if (installDirMatch.Success)
                {
                    string installDir = installDirMatch.Groups[1].Value;
                    return Path.Combine(_steamAppsPath, "common", installDir);
                }
            }
            catch { }
            return null;
        }

        // Force rescan (call when refreshing library)
        public void Refresh()
        {
            ScanInstalledGames();
        }

        // Gets the manifest file path for a game
        public string GetManifestPath(string appId)
        {
            if (!Directory.Exists(_steamAppsPath))
                return null;

            string manifestPath = Path.Combine(_steamAppsPath, $"appmanifest_{appId}.acf");
            return File.Exists(manifestPath) ? manifestPath : null;
        }
    }
}

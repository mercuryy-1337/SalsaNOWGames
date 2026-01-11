using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using SalsaNOWGames.Models;

namespace SalsaNOWGames.Services
{
    public class GamesLibraryService
    {
        private readonly string _gamesJsonPath;
        private readonly SteamLibraryService _steamLibraryService;
        private List<InstalledGame> _games;

        public GamesLibraryService()
        {
            string salsaNowDirectory = @"I:\Apps\SalsaNOW";
            Directory.CreateDirectory(salsaNowDirectory);
            _gamesJsonPath = Path.Combine(salsaNowDirectory, "games.json");
            _steamLibraryService = new SteamLibraryService();
            LoadGames();
        }

        public List<InstalledGame> Games => _games;

        public void LoadGames()
        {
            _games = new List<InstalledGame>();
            try
            {
                if (File.Exists(_gamesJsonPath))
                {
                    string json = File.ReadAllText(_gamesJsonPath);
                    var serializer = new DataContractJsonSerializer(typeof(List<InstalledGame>));
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        _games = (List<InstalledGame>)serializer.ReadObject(ms) ?? new List<InstalledGame>();
                    }
                    
                    // Ensure all games have Install object
                    foreach (var game in _games)
                    {
                        if (game.Install == null)
                            game.Install = new InstallStatus();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load games.json: {ex.Message}");
                _games = new List<InstalledGame>();
            }
        }

        public void SaveGames()
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(List<InstalledGame>));
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, _games);
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    File.WriteAllText(_gamesJsonPath, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save games.json: {ex.Message}");
            }
        }

        // Add game installed via Salsa
        public void AddGame(GameInfo gameInfo, string installPath)
        {
            var existingGame = _games.FirstOrDefault(g => g.Id == gameInfo.AppId);
            if (existingGame != null)
            {
                existingGame.Name = gameInfo.Name;
                existingGame.Install.Salsa = true;
                existingGame.HeaderImageUrl = gameInfo.HeaderImageUrl;
                existingGame.InstallPath = installPath;
                existingGame.InstalledDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                _games.Add(new InstalledGame
                {
                    Id = gameInfo.AppId,
                    Name = gameInfo.Name,
                    Install = new InstallStatus { Salsa = true, Steam = false },
                    HeaderImageUrl = gameInfo.HeaderImageUrl,
                    InstallPath = installPath,
                    InstalledDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            SaveGames();
        }

        public void RemoveGame(string appId)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            if (game != null)
            {
                _games.Remove(game);
                SaveGames();
            }
        }

        // Mark Salsa installation as uninstalled
        public void MarkAsUninstalled(string appId)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            if (game != null)
            {
                game.Install.Salsa = false;
                SaveGames();
            }
        }

        // Checks if game is installed (via Steam OR Salsa)
        public bool IsGameInstalled(string appId)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            bool salsaInstalled = game?.Install?.Salsa ?? false;
            bool steamInstalled = _steamLibraryService.IsInstalledViaSteam(appId);
            return salsaInstalled || steamInstalled;
        }

        // Checks if specifically installed via Salsa
        public bool IsInstalledViaSalsa(string appId)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            return game?.Install?.Salsa ?? false;
        }

        // Checks if specifically installed via Steam
        public bool IsInstalledViaSteam(string appId)
        {
            return _steamLibraryService.IsInstalledViaSteam(appId);
        }

        // Gets the SizeOnDisk for a Steam-installed game from its manifest
        public long GetSteamSizeOnDisk(string appId)
        {
            return _steamLibraryService.GetSizeOnDisk(appId);
        }

        public InstalledGame GetGame(string appId)
        {
            return _games.FirstOrDefault(g => g.Id == appId);
        }

        public List<InstalledGame> GetInstalledGames()
        {
            // Refresh Steam library scan
            _steamLibraryService.Refresh();
            
            // Return games that are installed via either method
            return _games.Where(g => g.Install?.Salsa == true || 
                                     _steamLibraryService.IsInstalledViaSteam(g.Id)).ToList();
        }

        // Gets the install path - prioritizes Salsa path, falls back to Steam
        public string GetInstallPath(string appId)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            if (game?.Install?.Salsa == true && !string.IsNullOrEmpty(game.InstallPath))
                return game.InstallPath;
            
            return _steamLibraryService.GetSteamInstallPath(appId);
        }

        // Gets install source description
        public string GetInstallSource(string appId)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            bool salsa = game?.Install?.Salsa ?? false;
            bool steam = _steamLibraryService.IsInstalledViaSteam(appId);
            
            if (salsa && steam) return "Steam + Salsa";
            if (salsa) return "Salsa";
            if (steam) return "Steam";
            return "Not installed";
        }

        // Refresh Steam library detection
        public void RefreshSteamLibrary()
        {
            _steamLibraryService.Refresh();
        }

        // Gets the Steam manifest path for a game
        public string GetSteamManifestPath(string appId)
        {
            return _steamLibraryService.GetManifestPath(appId);
        }

        // Check if game has a shortcut created
        public bool HasShortcut(string appId)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            return game?.HasShortcut ?? false;
        }

        // Set the shortcut flag for a game
        public void SetHasShortcut(string appId, bool hasShortcut)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            if (game != null)
            {
                game.HasShortcut = hasShortcut;
                SaveGames();
            }
        }
    }
}

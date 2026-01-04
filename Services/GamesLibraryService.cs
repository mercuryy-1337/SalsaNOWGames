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
        private List<InstalledGame> _games;

        public GamesLibraryService()
        {
            string salsaNowDirectory = @"I:\Apps\SalsaNOW";
            Directory.CreateDirectory(salsaNowDirectory);
            _gamesJsonPath = Path.Combine(salsaNowDirectory, "games.json");
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

        public void AddGame(GameInfo gameInfo, string installPath)
        {
            // Check if game already exists
            var existingGame = _games.FirstOrDefault(g => g.Id == gameInfo.AppId);
            if (existingGame != null)
            {
                // Update existing game
                existingGame.Name = gameInfo.Name;
                existingGame.IsInstalled = true;
                existingGame.HeaderImageUrl = gameInfo.HeaderImageUrl;
                existingGame.InstallPath = installPath;
                existingGame.InstalledDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                // Add new game
                _games.Add(new InstalledGame
                {
                    Id = gameInfo.AppId,
                    Name = gameInfo.Name,
                    IsInstalled = true,
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

        public void MarkAsUninstalled(string appId)
        {
            var game = _games.FirstOrDefault(g => g.Id == appId);
            if (game != null)
            {
                game.IsInstalled = false;
                SaveGames();
            }
        }

        public bool IsGameInstalled(string appId)
        {
            return _games.Any(g => g.Id == appId && g.IsInstalled);
        }

        public InstalledGame GetGame(string appId)
        {
            return _games.FirstOrDefault(g => g.Id == appId);
        }

        public List<InstalledGame> GetInstalledGames()
        {
            return _games.Where(g => g.IsInstalled).ToList();
        }
    }
}

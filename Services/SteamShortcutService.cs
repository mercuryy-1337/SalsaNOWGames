using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SalsaNOWGames.Services
{
    /// <summary>
    /// Result of checking shortcut eligibility
    /// </summary>
    public class ShortcutEligibility
    {
        public bool CanAddShortcut { get; set; }
        public string ErrorMessage { get; set; }
        public string ExePath { get; set; }
        public int ExeCount { get; set; }
    }

    /// <summary>
    /// Represents a non-Steam game shortcut entry
    /// </summary>
    public class ShortcutEntry
    {
        public string AppName { get; set; } = "";
        public string Exe { get; set; } = "";
        public string StartDir { get; set; } = "";
        public string Icon { get; set; } = "";
        public string ShortcutPath { get; set; } = "";
        public string LaunchOptions { get; set; } = "";
        public bool IsHidden { get; set; } = false;
        public bool AllowDesktopConfig { get; set; } = true;
        public bool AllowOverlay { get; set; } = true;
        public bool OpenVR { get; set; } = false;
        public bool Devkit { get; set; } = false;
        public string DevkitGameID { get; set; } = "";
        public uint DevkitOverrideAppID { get; set; } = 0;
        public uint LastPlayTime { get; set; } = 0;
        public string FlatpakAppID { get; set; } = "";
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Utility class for adding non-Steam games to Steam
    /// </summary>
    public class SteamShortcutManager
    {
        private const byte VDF_TYPE_MAP = 0x00;
        private const byte VDF_TYPE_STRING = 0x01;
        private const byte VDF_TYPE_INT32 = 0x02;
        private const byte VDF_TYPE_MAP_END = 0x08;

        /// <summary>
        /// Checks if a game can be added as a Steam shortcut
        /// </summary>
        /// <param name="installPath">The game's installation directory</param>
        /// <returns>Eligibility result with exe path if eligible</returns>
        public ShortcutEligibility CheckShortcutEligibility(string installPath)
        {
            var result = new ShortcutEligibility { CanAddShortcut = false };

            if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            {
                result.ErrorMessage = "Install path not found";
                return result;
            }

            // Check for steam_appid.txt to find the correct directory
            string steamAppIdPath = Path.Combine(installPath, "steam_appid.txt");
            string targetDir = installPath;

            if (!File.Exists(steamAppIdPath))
            {
                // Search subdirectories for steam_appid.txt
                var appIdFiles = Directory.GetFiles(installPath, "steam_appid.txt", SearchOption.AllDirectories);
                if (appIdFiles.Length == 0)
                {
                    result.ErrorMessage = "No steam_appid.txt found";
                    return result;
                }
                targetDir = Path.GetDirectoryName(appIdFiles[0]) ?? installPath;
            }

            // Find exe files in the same directory as steam_appid.txt
            var exeFiles = Directory.GetFiles(targetDir, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !IsSystemExe(f))
                .ToArray();

            result.ExeCount = exeFiles.Length;

            if (exeFiles.Length == 0)
            {
                result.ErrorMessage = "No executable found in game directory";
                return result;
            }

            if (exeFiles.Length > 1)
            {
                // If there are multiple executables, try to ignore known crash-handler binaries.
                // If exactly one candidate remains, auto-select it.
                // If multiple remain, keep the existing multiple-exe error.
                var ignoreRegex = new Regex(@"^(crs-.*|.*UnityCrashHandler.*)\.exe$", RegexOptions.IgnoreCase);
                var filtered = exeFiles
                    .Where(f => !ignoreRegex.IsMatch(Path.GetFileName(f)))
                    .ToArray();

                if (filtered.Length == 1)
                {
                    result.CanAddShortcut = true;
                    result.ExePath = filtered[0];
                    result.ErrorMessage = null;
                    return result;
                }

                result.ErrorMessage = $"Multiple executables found ({exeFiles.Length}). Please add shortcut manually in Steam.";
                return result;
            }

            // Single exe found - eligible
            result.CanAddShortcut = true;
            result.ExePath = exeFiles[0];
            result.ErrorMessage = null;
            return result;
        }

        /// <summary>
        /// Checks if an exe is a system/launcher exe that should be ignored
        /// </summary>
        private bool IsSystemExe(string exePath)
        {
            string fileName = Path.GetFileName(exePath).ToLowerInvariant();
            string[] systemExes = new[]
            {
                "unins000.exe", "uninstall.exe", "uninst.exe",
                "crashhandler.exe", "crashreporter.exe", "crashpad_handler.exe",
                "ue4prereqsetup_x64.exe", "vc_redist.x64.exe", "vcredist_x64.exe",
                "dxsetup.exe", "dxwebsetup.exe", "dotnetfx.exe",
                "steamclient.exe", "steam_api.exe", "steam_api64.exe",
                "updater.exe", "launcher.exe", "bootstrapper.exe"
            };
            return systemExes.Contains(fileName);
        }

        /// <summary>
        /// Adds a game as a non-Steam shortcut using game info
        /// </summary>
        /// <param name="appName">Game name</param>
        /// <param name="exePath">Path to the executable</param>
        /// <param name="startDir">Start directory (install path)</param>
        /// <param name="iconUrl">URL or path to icon (can be URL, will use exe if not local)</param>
        /// <returns>True if successful, throws exception otherwise</returns>
        public bool AddGameShortcut(string appName, string exePath, string startDir, string iconUrl = null)
        {
            // Validate executable path
            string fullExePath = Path.GetFullPath(exePath);
            if (!File.Exists(fullExePath))
            {
                throw new FileNotFoundException($"Executable not found: {fullExePath}");
            }

            // For icon, if it's a local file that exists, use it; otherwise use the exe itself
            string iconPath = fullExePath;
            if (!string.IsNullOrEmpty(iconUrl))
            {
                // Check if it's a local file path (not a URL)
                if (!iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && File.Exists(iconUrl))
                {
                    iconPath = Path.GetFullPath(iconUrl);
                    LogService.Log($"Using custom icon for {appName}: {iconPath}");
                }
                else
                {
                    LogService.Log($"Icon path not found or is URL for {appName}, using exe icon: {iconUrl}");
                }
            }
            else
            {
                LogService.Log($"No icon specified for {appName}, using exe icon");
            }

            // Use the exe's directory as start dir if not specified
            if (string.IsNullOrEmpty(startDir))
            {
                startDir = Path.GetDirectoryName(fullExePath) ?? "";
            }

            return AddNonSteamGameInternal(fullExePath, appName, startDir, iconPath);
        }

        /// <summary>
        /// Verifies if a shortcut exists in Steam's shortcuts.vdf by game name only.
        /// This is used to validate HasShortcut status when loading games.
        /// </summary>
        /// <param name="appName">The game name to search for</param>
        /// <returns>True if a shortcut with this name exists in any Steam user's shortcuts.vdf</returns>
        public bool VerifyShortcutExists(string appName)
        {
            if (string.IsNullOrEmpty(appName))
                return false;

            try
            {
                string programFilesX86 = Environment.GetEnvironmentVariable("PROGRAMFILES(X86)");
                if (string.IsNullOrEmpty(programFilesX86)) return false;

                string userDataPath = Path.Combine(programFilesX86, "Steam", "userdata");
                if (!Directory.Exists(userDataPath)) return false;

                foreach (string userDir in Directory.GetDirectories(userDataPath))
                {
                    string shortcutsFile = Path.Combine(userDir, "config", "shortcuts.vdf");
                    if (File.Exists(shortcutsFile))
                    {
                        try
                        {
                            var shortcuts = ReadShortcutsVdf(shortcutsFile);
                            foreach (var entry in shortcuts.Values)
                            {
                                // Match by app name (case-insensitive)
                                if (string.Equals(entry.AppName, appName, StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                            }
                        }
                        catch
                        {
                            // Ignore read errors for individual files
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"Error verifying shortcut for {appName}", ex);
            }

            return false;
        }

        /// <summary>
        /// Removes a shortcut from Steam's shortcuts.vdf by game name.
        /// </summary>
        /// <param name="appName">The game name to remove</param>
        /// <returns>True if shortcut was removed from any user's shortcuts.vdf</returns>
        public bool RemoveShortcut(string appName)
        {
            if (string.IsNullOrEmpty(appName))
                return false;

            bool removedFromAny = false;

            try
            {
                string programFilesX86 = Environment.GetEnvironmentVariable("PROGRAMFILES(X86)");
                if (string.IsNullOrEmpty(programFilesX86)) return false;

                string userDataPath = Path.Combine(programFilesX86, "Steam", "userdata");
                if (!Directory.Exists(userDataPath)) return false;

                foreach (string userDir in Directory.GetDirectories(userDataPath))
                {
                    string shortcutsFile = Path.Combine(userDir, "config", "shortcuts.vdf");
                    if (File.Exists(shortcutsFile))
                    {
                        try
                        {
                            var shortcuts = ReadShortcutsVdf(shortcutsFile);
                            string keyToRemove = null;

                            // Find the shortcut to remove
                            foreach (var kvp in shortcuts)
                            {
                                if (string.Equals(kvp.Value.AppName, appName, StringComparison.OrdinalIgnoreCase))
                                {
                                    keyToRemove = kvp.Key;
                                    break;
                                }
                            }

                            if (keyToRemove != null)
                            {
                                shortcuts.Remove(keyToRemove);

                                // Re-index shortcuts (Steam expects sequential indices)
                                var reindexed = new Dictionary<string, ShortcutEntry>();
                                int index = 0;
                                foreach (var entry in shortcuts.Values)
                                {
                                    reindexed[index.ToString()] = entry;
                                    index++;
                                }

                                WriteShortcutsVdf(shortcutsFile, reindexed);
                                removedFromAny = true;
                                LogService.Log($"Removed shortcut for {appName} from user {Path.GetFileName(userDir)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.LogError($"Error removing shortcut from {shortcutsFile}", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.LogError($"Error removing shortcut for {appName}", ex);
            }

            return removedFromAny;
        }

        /// <summary>
        /// Checks if a shortcut already exists for the given exe and app name
        /// </summary>
        public bool ShortcutExists(string exePath, string appName)
        {
            string normalizedExe = Path.GetFullPath(exePath).Replace("\\", "\\\\");
            
            string programFilesX86 = Environment.GetEnvironmentVariable("PROGRAMFILES(X86)");
            if (string.IsNullOrEmpty(programFilesX86)) return false;

            string userDataPath = Path.Combine(programFilesX86, "Steam", "userdata");
            if (!Directory.Exists(userDataPath)) return false;

            foreach (string userDir in Directory.GetDirectories(userDataPath))
            {
                string shortcutsFile = Path.Combine(userDir, "config", "shortcuts.vdf");
                if (File.Exists(shortcutsFile))
                {
                    try
                    {
                        var shortcuts = ReadShortcutsVdf(shortcutsFile);
                        foreach (var entry in shortcuts.Values)
                        {
                            if (entry.Exe == normalizedExe || entry.AppName == appName)
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore read errors
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Internal method for adding non-Steam game shortcut
        /// </summary>
        private bool AddNonSteamGameInternal(string fullExePath, string appName, string startDir, string iconPath)
        {
            string programFilesX86 = Environment.GetEnvironmentVariable("PROGRAMFILES(X86)");
            if (string.IsNullOrEmpty(programFilesX86))
            {
                throw new DirectoryNotFoundException("PROGRAMFILES(X86) environment variable not found");
            }

            string userDataPath = Path.Combine(programFilesX86, "Steam", "userdata");
            if (!Directory.Exists(userDataPath))
            {
                throw new DirectoryNotFoundException("Steam userdata directory not found");
            }

            // Create shortcut entry
            var shortcut = new ShortcutEntry
            {
                AppName = appName,
                Exe = fullExePath.Replace("\\", "\\\\"),
                StartDir = startDir.Replace("\\", "\\\\"),
                Icon = iconPath.Replace("\\", "\\\\"),
                ShortcutPath = "",
                LaunchOptions = "",
                IsHidden = false,
                AllowDesktopConfig = true,
                AllowOverlay = true,
                OpenVR = false,
                Devkit = false,
                DevkitGameID = "",
                DevkitOverrideAppID = 0,
                LastPlayTime = 0,
                FlatpakAppID = ""
            };

            bool addedToAny = false;

            // Process all user accounts
            foreach (string userDir in Directory.GetDirectories(userDataPath))
            {
                string shortcutsFile = Path.Combine(userDir, "config", "shortcuts.vdf");
                string configDir = Path.GetDirectoryName(shortcutsFile);

                Dictionary<string, ShortcutEntry> shortcuts;

                // Load existing shortcuts or create new
                if (File.Exists(shortcutsFile))
                {
                    shortcuts = ReadShortcutsVdf(shortcutsFile);
                }
                else
                {
                    shortcuts = new Dictionary<string, ShortcutEntry>();
                    Directory.CreateDirectory(configDir);
                }

                // Check if shortcut already exists
                bool exists = false;
                foreach (var entry in shortcuts.Values)
                {
                    if (entry.Exe == shortcut.Exe || entry.AppName == shortcut.AppName)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    LogService.Log($"Shortcut already exists for {appName} in user {Path.GetFileName(userDir)}");
                    continue;
                }

                // Add new shortcut
                int nextIndex = shortcuts.Count;
                shortcuts[nextIndex.ToString()] = shortcut;

                // Write updated shortcuts
                WriteShortcutsVdf(shortcutsFile, shortcuts);
                addedToAny = true;

                LogService.Log($"Added shortcut for {appName} to user {Path.GetFileName(userDir)}");
            }

            return addedToAny;
        }

        /// <summary>
        /// Adds a non-Steam game shortcut to Steam
        /// </summary>
        /// <param name="exePath">Path to the executable file</param>
        /// <param name="appName">Name to display in Steam (defaults to exe filename)</param>
        /// <param name="startDir">Working directory (defaults to exe's directory)</param>
        /// <param name="iconPath">Path to custom icon (defaults to exe icon)</param>
        public void AddNonSteamGame(string exePath, string appName = null, string startDir = null, string iconPath = null)
        {
            // Validate executable path
            string fullExePath = Path.GetFullPath(exePath);
            if (!File.Exists(fullExePath))
            {
                throw new FileNotFoundException($"Executable not found: {fullExePath}");
            }

            // Set default values
            if (string.IsNullOrEmpty(appName))
                appName = Path.GetFileNameWithoutExtension(fullExePath);
            if (string.IsNullOrEmpty(startDir))
                startDir = Path.GetDirectoryName(fullExePath) ?? "";
            if (string.IsNullOrEmpty(iconPath))
                iconPath = fullExePath;

            AddNonSteamGameInternal(fullExePath, appName, startDir, iconPath);
        }

        /// <summary>
        /// Reads and parses a shortcuts.vdf file
        /// </summary>
        private Dictionary<string, ShortcutEntry> ReadShortcutsVdf(string filePath)
        {
            var shortcuts = new Dictionary<string, ShortcutEntry>();
            byte[] data = File.ReadAllBytes(filePath);
            int offset = 0;

            // Read root map header
            if (data[offset] != VDF_TYPE_MAP)
            {
                throw new InvalidDataException("Invalid VDF file: expected map type");
            }
            offset++;

            // Read "shortcuts" key
            string rootKey = ReadNullTerminatedString(data, ref offset);
            if (rootKey != "shortcuts")
            {
                throw new InvalidDataException("Invalid shortcuts.vdf: expected 'shortcuts' key");
            }

            // Read shortcut entries
            while (offset < data.Length && data[offset] != VDF_TYPE_MAP_END)
            {
                if (data[offset] == VDF_TYPE_MAP)
                {
                    offset++;
                    string index = ReadNullTerminatedString(data, ref offset);
                    var entry = ReadShortcutEntry(data, ref offset);
                    shortcuts[index] = entry;
                }
                else
                {
                    break;
                }
            }

            return shortcuts;
        }

        /// <summary>
        /// Reads a single shortcut entry from VDF data
        /// </summary>
        private ShortcutEntry ReadShortcutEntry(byte[] data, ref int offset)
        {
            var entry = new ShortcutEntry();

            while (offset < data.Length && data[offset] != VDF_TYPE_MAP_END)
            {
                byte type = data[offset];
                offset++;

                if (type == VDF_TYPE_MAP_END)
                {
                    break;
                }

                string key = ReadNullTerminatedString(data, ref offset);

                switch (type)
                {
                    case VDF_TYPE_STRING:
                        string strValue = ReadNullTerminatedString(data, ref offset);
                        SetShortcutProperty(entry, key, strValue);
                        break;

                    case VDF_TYPE_INT32:
                        uint intValue = BitConverter.ToUInt32(data, offset);
                        offset += 4;
                        SetShortcutProperty(entry, key, intValue);
                        break;

                    case VDF_TYPE_MAP:
                        // Handle tags map
                        if (key.Equals("tags", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Tags = ReadTagsMap(data, ref offset);
                        }
                        else
                        {
                            SkipMap(data, ref offset);
                        }
                        break;
                }
            }

            if (offset < data.Length && data[offset] == VDF_TYPE_MAP_END)
            {
                offset++;
            }

            return entry;
        }

        /// <summary>
        /// Reads the tags map from VDF data
        /// </summary>
        private Dictionary<string, string> ReadTagsMap(byte[] data, ref int offset)
        {
            var tags = new Dictionary<string, string>();

            while (offset < data.Length && data[offset] != VDF_TYPE_MAP_END)
            {
                byte type = data[offset];
                offset++;

                if (type == VDF_TYPE_MAP_END)
                {
                    break;
                }

                string key = ReadNullTerminatedString(data, ref offset);

                if (type == VDF_TYPE_STRING)
                {
                    string value = ReadNullTerminatedString(data, ref offset);
                    tags[key] = value;
                }
                else if (type == VDF_TYPE_INT32)
                {
                    offset += 4;
                }
            }

            if (offset < data.Length && data[offset] == VDF_TYPE_MAP_END)
            {
                offset++;
            }

            return tags;
        }

        /// <summary>
        /// Skips over a map in VDF data
        /// </summary>
        private void SkipMap(byte[] data, ref int offset)
        {
            while (offset < data.Length && data[offset] != VDF_TYPE_MAP_END)
            {
                byte type = data[offset];
                offset++;

                if (type == VDF_TYPE_MAP_END)
                {
                    break;
                }

                ReadNullTerminatedString(data, ref offset); // Skip key

                switch (type)
                {
                    case VDF_TYPE_STRING:
                        ReadNullTerminatedString(data, ref offset);
                        break;
                    case VDF_TYPE_INT32:
                        offset += 4;
                        break;
                    case VDF_TYPE_MAP:
                        SkipMap(data, ref offset);
                        break;
                }
            }

            if (offset < data.Length && data[offset] == VDF_TYPE_MAP_END)
            {
                offset++;
            }
        }

        /// <summary>
        /// Sets a property on a ShortcutEntry based on key name
        /// </summary>
        private void SetShortcutProperty(ShortcutEntry entry, string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "appname": entry.AppName = value; break;
                case "exe": entry.Exe = value; break;
                case "startdir": entry.StartDir = value; break;
                case "icon": entry.Icon = value; break;
                case "shortcutpath": entry.ShortcutPath = value; break;
                case "launchoptions": entry.LaunchOptions = value; break;
                case "devkitgameid": entry.DevkitGameID = value; break;
                case "flatpakappid": entry.FlatpakAppID = value; break;
            }
        }

        /// <summary>
        /// Sets a property on a ShortcutEntry based on key name
        /// </summary>
        private void SetShortcutProperty(ShortcutEntry entry, string key, uint value)
        {
            switch (key.ToLowerInvariant())
            {
                case "ishidden": entry.IsHidden = value != 0; break;
                case "allowdesktopconfig": entry.AllowDesktopConfig = value != 0; break;
                case "allowoverlay": entry.AllowOverlay = value != 0; break;
                case "openvr": entry.OpenVR = value != 0; break;
                case "devkit": entry.Devkit = value != 0; break;
                case "devkitoverrideappid": entry.DevkitOverrideAppID = value; break;
                case "lastplaytime": entry.LastPlayTime = value; break;
            }
        }

        /// <summary>
        /// Reads a null-terminated string from byte array
        /// </summary>
        private string ReadNullTerminatedString(byte[] data, ref int offset)
        {
            int start = offset;
            while (offset < data.Length && data[offset] != 0)
            {
                offset++;
            }
            string result = Encoding.UTF8.GetString(data, start, offset - start);
            offset++; // Skip null terminator
            return result;
        }

        /// <summary>
        /// Writes shortcuts to a VDF file
        /// </summary>
        private void WriteShortcutsVdf(string filePath, Dictionary<string, ShortcutEntry> shortcuts)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Write root map
                writer.Write(VDF_TYPE_MAP);
                WriteNullTerminatedString(writer, "shortcuts");

                // Write each shortcut
                foreach (var kvp in shortcuts)
                {
                    writer.Write(VDF_TYPE_MAP);
                    WriteNullTerminatedString(writer, kvp.Key);
                    WriteShortcutEntry(writer, kvp.Value);
                    writer.Write(VDF_TYPE_MAP_END);
                }

                // Close shortcuts map
                writer.Write(VDF_TYPE_MAP_END);
                // Close root map
                writer.Write(VDF_TYPE_MAP_END);

                File.WriteAllBytes(filePath, ms.ToArray());
            }
        }

        /// <summary>
        /// Writes a single shortcut entry to VDF
        /// </summary>
        private void WriteShortcutEntry(BinaryWriter writer, ShortcutEntry entry)
        {
            // String properties
            WriteStringProperty(writer, "AppName", entry.AppName);
            WriteStringProperty(writer, "Exe", entry.Exe);
            WriteStringProperty(writer, "StartDir", entry.StartDir);
            WriteStringProperty(writer, "icon", entry.Icon);
            WriteStringProperty(writer, "ShortcutPath", entry.ShortcutPath);
            WriteStringProperty(writer, "LaunchOptions", entry.LaunchOptions);

            // Integer/Boolean properties
            WriteInt32Property(writer, "IsHidden", entry.IsHidden ? 1u : 0u);
            WriteInt32Property(writer, "AllowDesktopConfig", entry.AllowDesktopConfig ? 1u : 0u);
            WriteInt32Property(writer, "AllowOverlay", entry.AllowOverlay ? 1u : 0u);
            WriteInt32Property(writer, "OpenVR", entry.OpenVR ? 1u : 0u);
            WriteInt32Property(writer, "Devkit", entry.Devkit ? 1u : 0u);
            WriteStringProperty(writer, "DevkitGameID", entry.DevkitGameID);
            WriteInt32Property(writer, "DevkitOverrideAppID", entry.DevkitOverrideAppID);
            WriteInt32Property(writer, "LastPlayTime", entry.LastPlayTime);
            WriteStringProperty(writer, "FlatpakAppID", entry.FlatpakAppID);

            // Tags map
            writer.Write(VDF_TYPE_MAP);
            WriteNullTerminatedString(writer, "tags");
            foreach (var tag in entry.Tags)
            {
                WriteStringProperty(writer, tag.Key, tag.Value);
            }
            writer.Write(VDF_TYPE_MAP_END);
        }

        /// <summary>
        /// Writes a string property to VDF
        /// </summary>
        private void WriteStringProperty(BinaryWriter writer, string key, string value)
        {
            writer.Write(VDF_TYPE_STRING);
            WriteNullTerminatedString(writer, key);
            WriteNullTerminatedString(writer, value);
        }

        /// <summary>
        /// Writes an int32 property to VDF
        /// </summary>
        private void WriteInt32Property(BinaryWriter writer, string key, uint value)
        {
            writer.Write(VDF_TYPE_INT32);
            WriteNullTerminatedString(writer, key);
            writer.Write(value);
        }

        /// <summary>
        /// Writes a null-terminated string
        /// </summary>
        private void WriteNullTerminatedString(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte)0);
        }
    }
}

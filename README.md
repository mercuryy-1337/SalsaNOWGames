# SalsaNOW Games

A modern WPF Steam game downloader built on top of [DepotDownloader](https://github.com/SteamRE/DepotDownloader).

![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blue)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)

## Features

- 🎮 **Steam Library Management** - Browse and manage your downloaded games
- 🔍 **Game Search** - Search for games by name or App ID
- 📥 **Game Downloads** - Download games directly from Steam's CDN
- 🔐 **Steam Authentication** - Secure login with Steam Guard support
- 💾 **Persistent Sessions** - Stay logged in between app restarts
- 🎨 **Modern Dark UI** - Steam-inspired dark theme interface
- 📝 **Download Logging** - All operations logged to `SalsaNOWGames.log`

## Roadmap

🚧 **Coming Soon**: Direct Steam Library integration - automatically show all games from your Steam library without searching!

## Requirements

- Windows 10/11
- .NET Framework 4.8
- Steam account

## Usage

1. Launch the application
2. Sign in with your Steam credentials
3. If prompted, enter your Steam Guard code
4. Search for games or enter an App ID
5. Click download and wait for completion

## Configuration

- **Install Directory**: `I:\Apps\SalsaNOW\DepotDownloader\Games\`
- **Log File**: `I:\Apps\SalsaNOW\SalsaNOWGames.log`

## Building

```bash
# Using MSBuild
MSBuild SalsaNOWGames.csproj /p:Configuration=Release
```

## Credits

This project is a fork of [SalsaNOWgames](https://github.com/dpadGuy/SalsaNOWgames) by [@dpadGuy](https://github.com/dpadGuy).

### Dependencies

- [DepotDownloader](https://github.com/SteamRE/DepotDownloader) - Steam depot downloading tool

## License

This project is provided as-is for educational purposes.

## Disclaimer

This tool is intended for downloading games you legally own on Steam. Please respect copyright laws and Steam's Terms of Service.



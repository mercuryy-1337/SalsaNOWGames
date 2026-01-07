# Changelog

All notable changes to SalsaNOW Games will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.2] - 2026-01-07

### Added
- **Play Button** - Launch games installed via Steam directly from the library
  - Click the ▶ button on any Steam-installed game to launch via through steam
- **Steam Uninstall** - Delete button now properly uninstalls Steam-installed games
  - Opens Steam's native uninstaller via `steam://uninstall/{appid}`
  - Automatically detects when uninstall completes by polling the manifest file

### Changed
- Download view now displays header images instead of library portraits
  - Automatically converts `library_600x900.jpg` to `header.jpg` when downloading

## [1.1.1] - 2026-01-07

### Fixed
- Games not loading on first login - library now waits for API fetch when no cache exists
- Empty library issue on fresh installs resolved

### Added
- Comprehensive logging system for debugging user issues
  - Logs written to `%LOCALAPPDATA%\SalsaNOWGames\salsa.log`
  - Tracks API calls, cache operations, and login flow
  - Auto-rotates logs when exceeding 5MB

## [1.1.0] - 2026-01-07

### Added
- **Steam Library Integration** - Your owned games now automatically appear in the library
- `salsa.vdf` as single source of truth for game data (replaces games.json)
- Manual Steam Guard code entry option with `-no-mobile` flag support
- "Enter Code" button in download view for manual Steam Guard entry
- Background refresh of owned games on login

### Changed
- Modernised UI with larger game cards (200x300) for better visibility
- Improved card sizing consistency for search results
- Steam-inspired dark theme scrollbars
- Better button layout and spacing in download view
- Alphabetical sorting for installed games
- Header images now use library_600x900 format for better quality

### Fixed
- Steam Guard dialog window being cut off (increased height, added SizeToContent)
- Removed automatic Steam Guard popup that triggered unnecessarily

## [1.0.6] - 2026-01-06

### Added
- Groundwork for Direct Steam Library integration
- `OwnedGamesService` for fetching and caching owned games from SalsaNOW API
- `SteamVdfService` for parsing Steam's VDF files (loginusers.vdf)

### Fixed
- UI freeze when deleting files (cancel download / library delete)
- Async file operations now properly run on background threads

## [1.0.5] - 2026-01-06

### Notes
- Base release for this changelog
- Initial stable version with core functionality

---

[1.1.2]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.0.6...v1.1.0
[1.0.6]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/mercuryy-1337/SalsaNOWGames/releases/tag/v1.0.5

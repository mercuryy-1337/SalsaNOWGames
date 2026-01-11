# Changelog

All notable changes to SalsaNOW Games will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.1] - 2025-01-11

### Added
- **Install Source Indicator** - Visual indicator showing where each game is installed from
  - Shows "Steam" (blue) for games installed via Steam
  - Shows "SalsaNOW" (orange) for games installed via SalsaNOW Games
  - Displayed next to game size in the library view
  - Includes tooltips for clarity

### Fixed
- **Steam Game Sizes** - Fixed size display for Steam-installed games
  - Now reads size from Steam's manifest file instead of calculating from directory
  - Sizes now match what Windows Explorer and Steam UI display (GiB, not GB)

## [1.2.0] - 2026-01-11

### Added
- **Steam Shortcut Integration** - Add Salsa-installed games to Steam as non-Steam games
  - Click the 🔗 button on any Salsa-installed game to add it to your Steam library
  - Automatically detects the game executable from the install directory
  - Downloads game icon and uses it for the Steam shortcut
  - Shortcut appears in Steam after restarting the Steam client
  - If multiple executables are found, shows a helpful message to add manually
- **Steam Restart Integration** - Option to restart Steam after creating a shortcut
  - After successful shortcut creation, prompted to restart Steam
  - Click "Restart Steam" to automatically close and reopen Steam
  - Steam reopens directly to your library
- **Game Icon Download** - Icons are downloaded when creating shortcuts
  - Icon downloaded on-demand when clicking the shortcut button
  - Icon saved to game folder alongside `steam_appid.txt`
  - Used for Steam shortcut with local file path
  - Tracked in `salsa.vdf` via new `icon_path` field
- **Open in Steam Library** - 🎮 button now works for Salsa games with shortcuts
  - Appears when game has a shortcut (HasShortcut = true)
  - Opens Steam directly to your game library
- **Reusable ModernDialog** - New modern themed dialog system for all popups
  - Supports Info, Success, Warning, Error, Confirm, Update, and RestartSteam types
  - Dark themed with Steam-like styling to match app design
- **Shortcut Removal on Delete** - Deleting a Salsa-installed game removes its Steam shortcut
  - `RemoveShortcut()` method added to `SteamShortcutService`
  - After deletion, prompts to restart Steam if shortcut was removed
  - Clears `HasShortcut` and `IconPath` data from salsa.vdf

### Changed
- `SteamShortcutService` refactored with new APIs for shortcut eligibility checking
  - Added `VerifyShortcutExists()` method to check if shortcut exists in Steam's shortcuts.vdf
  - Added `RemoveShortcut()` method to remove shortcuts by game name
  - Fixed icon path handling - properly uses downloaded icon instead of exe
- Games now track `CanAddShortcut`, `ShortcutErrorMessage`, and `IconPath` properties
- the icon path is now stored in salsa.vdf for persistent icon location
- Replaced all `MessageBox.Show` calls with `ModernDialog` for consistent UI theme
- Shortcut creation downloads icon on-demand if not already cached
- Delete confirmation dialog now shows warning about shortcut removal
- **Shortcut Verification** - App now verifies shortcuts exist in Steam on startup
  - We check steam's shortcuts.vdf file to confirm shortcuts still exist
  - Automatically updates `HasShortcut` status if shortcut was deleted externally
  - Fixes issue where deleting shortcuts manually would leave stale data

## [1.1.3] - 2026-01-11

### Added
- **Loading Spinner** - Animated loading indicator while header images are being fetched
  - Shows spinning circle overlay on game cards while images load
  - Images appear progressively as they're fetched (3 at a time)

### Changed
- **Library View Redesign** - Switched to landscape-oriented game cards
  - Cards now display header images (460x215) instead of portrait library images
  - More compact layout with 3 columns for better space utilization
  - Inline action buttons for cleaner appearance
- **New Header Image System** - Uses Steam CDN with IStoreBrowseService API fallback
  - CDN-first approach: tries Akamai/Fastly CDN URLs before API calls
  - Validates URLs with HEAD requests (Content-Type: image/*) before using
  - Falls back to IStoreBrowseService API when CDN fails
  - Header images cached locally in `%LOCALAPPDATA%\SalsaNOWGames\cached_library_images\`
  - Rate limited to 3 concurrent downloads to avoid Steam throttling
  - Saves resolved header URLs back to salsa.vdf for faster startup
  - Never uses invalid portrait images from slsapi (library_600x900)
- **Centralized Header Service** - New `SteamHeaderService` handles all header image fetching
  - Used consistently across Library, Search, and Download views
  - Batch fetching for better performance on large libraries
  - Progressive UI updates as each batch completes
- Download view now displays header images instead of library portraits

### Removed
- Removed `ConvertToLibrary600x900` function (no longer needed)
- Removed portrait image conversion logic
- Removed old `appdetails` API for header images (deprecated)
- Removed `FetchHeaderImageUrlAsync` from SteamApiService

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

[1.1.4]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.1.3...v1.1.4
[1.1.3]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.1.2...v1.1.3
[1.1.2]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.1.1...v1.1.2
[1.1.1]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.0.6...v1.1.0
[1.0.6]: https://github.com/mercuryy-1337/SalsaNOWGames/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/mercuryy-1337/SalsaNOWGames/releases/tag/v1.0.5

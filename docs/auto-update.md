# Auto-Update Feature

## Overview

MauiSherpa includes an auto-update mechanism that checks for new releases on GitHub and notifies users when updates are available.

## How It Works

### Update Check Flow

1. **On App Startup**: After a 2-second delay (to avoid blocking app initialization), the app checks for updates
2. **GitHub API Call**: The app queries the GitHub Releases API for the redth/MAUI.Sherpa repository
3. **Version Comparison**: Compares the current version (from `AppInfo.VersionString`) with the latest stable release
4. **User Notification**: If a newer version is available, displays a modal with release notes
5. **User Action**: User can either:
   - **Download Update**: Opens the GitHub release page in their default browser
   - **Not Now**: Dismisses the notification

### Key Components

#### IUpdateService Interface
Located in `src/MauiSherpa.Core/Interfaces.cs`

```csharp
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitHubRelease>> GetAllReleasesAsync(CancellationToken cancellationToken = default);
    string GetCurrentVersion();
}
```

#### UpdateService Implementation
Located in `src/MauiSherpa.Core/Services/UpdateService.cs`

- Uses `HttpClient` to fetch releases from GitHub API
- Uses `AppInfo.VersionString` to get the current version from the app manifest
- Filters out pre-releases and draft releases
- Compares semantic versions to determine if an update is available

#### UpdateAvailableModal Component
Located in `src/MauiSherpa/Components/UpdateAvailableModal.razor`

- Displays current version vs. new version
- Shows release name and published date
- Renders release notes using Markdown (via Markdig)
- Provides "Download Update" and "Not Now" buttons

#### App Integration
Located in `src/MauiSherpa/Components/App.razor`

- Performs update check on `OnInitializedAsync`
- Shows modal when update is available
- Handles user acceptance by opening the release URL in browser

### Version Configuration

The app version is configured in `src/MauiSherpa/MauiSherpa.csproj`:

```xml
<ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
<ApplicationVersion>1</ApplicationVersion>
```

The `UpdateService` retrieves this via `AppInfo.VersionString`.

### GitHub Releases

Updates are published as GitHub Releases in the `redth/MAUI.Sherpa` repository:
- Release tags should follow semantic versioning (e.g., `v0.1.0`, `v0.2.0`)
- Release notes are displayed to users in the update modal
- Pre-release and draft releases are ignored

### Testing

Unit tests are located in `tests/MauiSherpa.Core.Tests/Services/UpdateServiceTests.cs`:
- Tests version comparison logic
- Tests filtering of pre-releases and drafts
- Tests error handling for network failures
- Uses mocked `HttpClient` for predictable testing

## User Experience

### Update Available Modal

When an update is available, users see:
- Visual comparison: Current version → New version
- Release name and date
- Full release notes with formatting
- Two clear action buttons

### No Interruption

- Update check happens in background
- Only appears once per app launch
- User can decline and continue using the app
- No forced updates

## Future Enhancements

Potential improvements:
- Remember "skipped" versions to avoid repeated notifications
- Check for updates periodically (e.g., daily)
- Download and install updates automatically (platform permitting)
- Show "What's New" history for multiple recent releases

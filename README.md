# KadampaScreenSaver - Installation and Configuration Guide
Pulls large images from wordpress site, kadampa.org/news

## Overview

This application automatically downloads images from specific web pages, applies text overlays (such as titles, dates, and descriptions), and manages these images based on defined policies. It runs on **Windows, macOS, and Linux**.

## Requirements

- .NET 10 Runtime or SDK.
- An internet connection for downloading images.
- Basic understanding of JSON configuration (for setting up `appsettings.json`).
- **Windows**: Microsoft Edge (used by Playwright for scraping).
- **macOS / Linux**: Playwright's bundled Chromium is used automatically.

## Installation Steps

1. **Install .NET**: Make sure the .NET 10 Runtime or SDK is installed on your system. Download from the [official Microsoft .NET website](https://dotnet.microsoft.com/download).

2. **Download Application**: Obtain the application package from the provided source located under Releases on the right.

3. **Extract Files**: Extract the downloaded package to a folder on your computer.

4. **Install Playwright browsers** (first run):
   ```bash
   pwsh bin/Debug/net10.0/playwright.ps1 install chromium
   ```

5. **Run the application**:
   - **Windows**: `KadampaScreenSaver.exe` — also registers a daily Task Scheduler task automatically.
   - **macOS**: `./KadampaScreenSaver` — creates a LaunchAgent plist if configured.
   - **Linux**: `./KadampaScreenSaver` — creates a cron job if configured.

6. **Set up as screensaver**:
   - **Windows**: Settings > Personalization > Lock screen > Screen saver > Photos. Browse to the configured image directory.
   - **macOS**: System Settings > Screen Saver. Point to the configured image directory.
   - **Linux**: Use your desktop environment's screensaver/slideshow settings with the configured image directory.

7. **May Dharma Flourish**.

## Configuration

Edit `appsettings.json` to control the application's behavior.

### Basic Configuration

- **StartPage**: The URL to scrape for images (default: `https://kadampa.org/news`).
- **Policies**: Set the depth of links to follow (`LinkDepth`) and the number of days to retain downloaded images (`RetentionDays`).
- **Directories**: Configure the base directory for saving images (`Base`). Set `UseMyPictures` to `true` to use your Pictures folder (works on all platforms).
- **PhotoText**: Customize text overlay settings like font (`Font`) and whether to include the image file name or date.
- **Task Scheduler**: Set `StartTime` (e.g. `"05:30"`) to register a daily scheduled task. Remove or leave empty to skip.

### Cross-Platform Notes

| Setting | Windows | macOS | Linux |
|---------|---------|-------|-------|
| `Directories:Base` | `d:/temp/` | `/tmp/` or `~/Pictures/` | `/tmp/` or `~/Pictures/` |
| `PhotoText:Font` | `Palatino Linotype` | `Palatino` | `DejaVu Serif` or `Liberation Serif` |
| `Directories:UseMyPictures` | `C:\Users\{name}\Pictures` | `~/Pictures` | `~/Pictures` |

### Example Configuration

```json
{
  "StartPage": "https://kadampa.org/news",
  "Policies": {
    "LinkDepth": 7,
    "RetentionDays": 7
  },
  "Task Scheduler": {
    "StartTime": "05:30"
  },
  "Directories": {
    "UseMyPictures": true,
    "PhotoText": true,
    "SubDirectory": "KadampaScreenSaver"
  },
  "PhotoText": {
    "Font": "Palatino Linotype",
    "DateInclude": true,
    "DateFormat": "MM/dd",
    "DatePrefix": " - ",
    "ImageFileName": false,
    "RemoveDashKadampaBuddhism": true
  }
}
```

## Scheduling

The application automatically sets up platform-native scheduling when `Task Scheduler:StartTime` is configured:

- **Windows**: Creates a Windows Task Scheduler daily task.
- **macOS**: Creates a LaunchAgent plist in `~/Library/LaunchAgents/`. You may need to run `launchctl load <path>` to activate it.
- **Linux**: Adds a cron job to the current user's crontab.

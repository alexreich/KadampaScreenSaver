# KadampaScreenSaver - Installation and Configuration Guide
Pulls large images from wordpress site, kadampa.org/news

## Overview

This application automatically downloads images from specific web pages, applies text overlays (such as titles, dates, and descriptions), and manages these images based on defined policies. This guide will walk you through setting up and configuring the application on your system.

## Requirements

- .NET Core Runtime or SDK (Version 8 or higher).
- An internet connection for downloading images.
- Basic understanding of JSON configuration (for setting up `appsettings.json`).

## Installation Steps

1. **Install .NET Core**: Make sure the .NET Core Runtime or SDK is installed on your system. You can download it from the [official Microsoft .NET website](https://dotnet.microsoft.com/download).

2. **Download Application**: Obtain the application package from the provided source located under Releases on the right. It should include an executable file (e.g., `KadampaScreenSaver.exe`) and a configuration file (`appsettings.json`).

3. **Extract Files**: Extract the downloaded package to a folder on your computer.

4. **Install** scheduled task by running application. Running a second time will launch it immediately.

5. **Verify** Open Task Scheduler (Start Menu > Programs > Administrative Tools) - Verify Kadampa News is listed.

6. **Screensaver** Change to Photos. Settings to Browse - under Photos / Kadampa Pbotos (or configured).

7. **May Dharma Flourish**.

## Configuration

Before running the application, you need to configure it by editing the `appsettings.json` file. This file contains various settings that control the application's behavior.

### Basic Configuration

- **Policies**: Set the depth of links to follow (`LinkDepth`) and the number of days to retain downloaded images (`RetentionDays`).
- **Directories**: Configure the base directory for saving images (`Base`). If you prefer to use your 'My Pictures' folder, set `UseMyPictures` to `true`.
- **PhotoText**: Customize text settings like font (`Font`) and whether to include the image file name or date on the image.

### Advanced Configuration

- **Brand Colors**: The application uses a predefined set of colors for text overlay. If necessary, these can be modified by an experienced user familiar with color codes.

### Example Configuration

Here's an example snippet of the `appsettings.json` file:

```json
{
  "Policies": {
    "LinkDepth": 5,
    "RetentionDays": 30
  },
  "Directories": {
    "Base": "C:\\Images",
    "UseMyPictures": false,
    "SubDirectory": "DownloadedImages"
  },
  "PhotoText": {
    "Font": "Arial",
    "ImageFileName": true,
    "DateInclude": true,
    "DateFormat": "MM-dd-yyyy",
    "DatePrefix": " - "
  }
}

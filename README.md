# APM Tracker

A modern Actions-Per-Minute (APM) tracker for Windows that captures all keyboard and mouse inputs.

## Features

-  **Real-time APM Tracking** - Measures actions per minute in real-time
-  **Keyboard Capture** - Counts all key presses
-  **Mouse Capture** - Counts clicks (Left, Right, Middle, Extra) and mouse wheel
-  **Statistics** - Peak APM, Average, Session time
-  **Modern Design** - Dark, borderless UI with neon accents
-  **Always on Top** - Stays always in foreground
-  **Streamer Mode** - Compact overlay widget for streaming
-  **Customizable Colors** - Customize APM colors for different ranges
-  **Click Sounds** - Optional click sound feedback
-  **Settings Persistence** - All settings are saved automatically
-  **Auto-Updates** - Automatically checks for updates on startup

## Requirements

- Windows 10/11
- .NET 8.0 Runtime (included in self-contained builds)

## Installation & Build

```bash
# Clone the repository
git clone git@github.com:justinjd00/APMTracker.git
cd APMTracker

# Build and run
dotnet run
```

Or build as self-contained release:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable will be in `bin/Release/net8.0-windows/publish/ApmTracker.exe`

## Folder Structure
📁 YourFolder/

  ├── ApmTracker.exe          (The main application)
  
  ├── settings.json           (Created automatically on first start)
  
  └── Sounds/                 (Optional - created automatically)
  
      ├── README.txt          (Created automatically with info)
      
      └── YourSounds.wav      (Optional: Custom WAV/MP3/OGG files)

Important Notes:
1. Fonts: Already embedded in the EXE. No separate Fonts folder needed.
2. Sounds Folder:
* Created automatically when the app starts for the first time
* Optional: Place custom WAV/MP3/OGG files here
* The app automatically detects up to 10 sound files
* Sounds appear in the app after restart
3. settings.json:
* Created automatically on first start
* Stores all settings (position, colors, sounds, etc.)
* Located in the same folder as the EXE
4. Minimal Structure (EXE only):
```bash
   ApmTracker.exe
```
Everything else is created automatically when needed.
Summary:
* Only the EXE is required
* Sounds/ and settings.json are created automatically
* Fonts are embedded in the EXE
* The app works standalone without additional files
## Usage

- **START/PAUSE** - Start or pause tracking
- **RESET** - Reset all statistics
- **Move window** - Click and drag anywhere on the window
- **Minimize** - Click "─" button
- **Close** - Click "✕" button
- **Streamer Mode** - Double-click in streamer mode to switch back to main menu
- **Check for Updates** - Click "⬆" button to manually check for updates

## Auto-Updates

The application automatically checks for updates when it starts. If a new version is available, an update button (⬆) will appear in the title bar. Click it to download and install the update.

## Releases

Releases are automatically created when you push a tag starting with `v` (e.g., `v1.0.2`):

```bash
git tag v1.0.2
git push origin v1.0.2
```

This will trigger the CI/CD pipeline to build and create a GitHub release.

## Technical Details

- Uses Raw Input API for keyboard (anti-cheat safe)
- Uses Low-Level Hook for mouse (reliable detection)
- APM calculated using 60-second sliding window
- Update rate: 200ms
- Quantized display for smoother value changes

## License

MIT License

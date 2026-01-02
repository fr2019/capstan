# Capstan

A Windows utility that repurposes the Caps Lock key for keyboard layout switching and adds macOS-style accent input via long-press.

## Features

- **Layout Switching**: Press Caps Lock to switch keyboard layouts — toggle between two favorites or cycle through all installed layouts
- **Accent Input**: Long-press a letter to see available accents (like macOS), then select with number keys or mouse
- **Currency Symbols**: Long-press $ (or £, €, ¥) to quickly access other currency symbols
- **On-screen Overlay**: Shows current layout when switching and accent options when long-pressing
- **Shift for Case**: Hold Shift while the accent overlay is open to toggle between lowercase and uppercase accents
- **Background Operation**: Runs quietly in the system tray

## Installation

### Easy Way (Recommended)

1. Go to the [Releases](https://github.com/fr2019/capstan/releases) page
2. Download the latest `Capstan-vX.X.X-win-x64.zip` file
3. Extract to a folder of your choice (e.g., `C:\Capstan`)
4. Run `Capstan.exe`
5. (Optional) Enable "Run at Windows startup" in the app settings

No .NET installation required — everything is included.

## Building from Source

### Prerequisites

- Visual Studio 2022 with ".NET desktop development" workload
- .NET 8.0 SDK

### Steps

1. Clone the repository:
   ```
   git clone https://github.com/fr2019/capstan.git
   ```

2. Open `Capstan.sln` in Visual Studio 2022

3. Build and run:
   - Press `F5` to build and run in debug mode, or
   - Select **Build → Publish** to create a release package

### Creating a Release Package

```powershell
dotnet publish Capstan.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

Zip the `publish` folder for distribution.

## Usage

### Layout Switching

1. Run Capstan
2. Choose your switching mode:
   - **Toggle between two favorites**: Select your two preferred layouts from the dropdowns — Caps Lock switches between just these two
   - **Cycle through all layouts**: Caps Lock cycles through all keyboard layouts you have installed in Windows
3. Click "Hide to Background"
4. Press **Caps Lock** to switch layouts

### Accent Input

1. Enable "Long-press for accents" in settings
2. In any text field, hold down a letter (e.g., `e`)
3. After a moment, the accent overlay appears
4. Select an accent by:
   - Pressing a number key (1-9)
   - Clicking with the mouse
   - Using arrow keys and Enter
5. Hold **Shift** to see uppercase variants

### Supported Characters

- **Vowels**: a, e, i, o, u, y (and uppercase)
- **Consonants**: c, n, s, z, l, d, t (and uppercase)
- **Punctuation**: ? ! " ' -
- **Currency**: $ € £ ¥ ¢ ₹ ₽ ₩ ₪ ₿
- **Greek & Cyrillic**: Common accented variants

## Notes

- Caps Lock no longer toggles capitalization — use Shift for capitals
- The app runs in the background after hiding
- Right-click the system tray icon to access settings or exit
- Settings are preserved between sessions

## Troubleshooting

**The overlay doesn't appear**: Make sure "Long-press for accents" is enabled in settings.

**Layouts aren't switching**: Ensure you have at least two keyboard layouts installed in Windows Settings.

**App won't start**: Make sure you extracted all files from the zip, not just the .exe.

**App crashes or behaves unexpectedly**: Check `capstan.log` in the same folder as `Capstan.exe` for error details. The log file is automatically kept under 1MB.

## Logging

Capstan writes diagnostic information to `capstan.log` in the application folder. This log:
- Records app startup, shutdown, and errors
- Helps diagnose crashes and unexpected behavior
- Automatically resets when it exceeds 1MB
- Can be safely deleted if not needed

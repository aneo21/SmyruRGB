# SmyruRGB

SmyruRGB is a lightweight desktop application for controlling the ARGB lighting of Nollie controllers over HID. The project is built in C# on .NET 8 with Avalonia UI, providing a straightforward interface for selecting devices, configuring LED channels, saving profiles, and running lighting effects without the overhead of larger RGB suites.

## Features

- Detects compatible Nollie controllers connected over USB
- Supports configuring the LED count for each channel individually
- Saves per-device profiles and can restore the last used profile on startup
- Lets you set a primary color and, for selected effects, a background color
- Runs animated LED effects with adjustable speed and additional parameters
- Includes tray support, Windows startup integration, and minimized startup behavior

## Supported Controllers

The application currently recognizes the following devices:

- Nollie 32
- Nollie 32 OS2
- Nollie 32 OS2.1
- Nollie 16
- Nollie 16 OS2
- Nollie 16 OS2.1
- Nollie 8
- Nollie 8 OS2
- Nollie 8 OS2.1

Depending on the model, the app supports up to 8, 16, or 32 channels and device-specific LED limits.

## Available Effects

SmyruRGB includes a built-in set of effects:

- Static color
- Breathing
- Fire
- Meteor rain
- Rainbow
- Strobe
- Twinkle rainbow
- Wave
- Color wipe
- Sparkle
- Scanner

Each effect uses the same LED frame engine, so you can switch modes without reworking your channel configuration.

## Technology Stack

- .NET 8
- Avalonia UI 11
- ReactiveUI
- HidSharp

## Quick Start

### Requirements

- .NET SDK 8.0
- A compatible Nollie controller connected over USB
- Windows for startup-with-Windows integration

### Run Locally

```powershell
dotnet restore .\SmyruRGB.csproj
dotnet run --project .\SmyruRGB.csproj
```

### Build

```powershell
dotnet build .\SmyruRGB.csproj
```

## How To Use

1. Connect your Nollie controller over USB.
2. Launch the application and refresh the device list if the controller is not detected automatically.
3. Select the controller and set the LED count for each channel.
4. Open the effects tab, choose a mode, color, and animation speed.
5. Save the configuration as a profile if you want to reuse it quickly.

## Settings Storage

UI settings and device profiles are stored locally in the user data directory:

```text
%LOCALAPPDATA%\SmyruRGB\settings.json
```

This allows each supported device to keep its own profiles and last remembered configuration.

## Project Structure

- `Effects/` - LED effects, color models, and frame generation
- `Services/` - HID communication, settings persistence, and Windows startup integration
- `ViewModels/` - UI logic and application state management
- `Views/` - Avalonia views and tab layouts

## Project Status

The project is a practical alternative to heavier RGB applications for Nollie controller setups. If you want to extend it with additional controllers or effects, the main integration points are in the `Effects/` and `Services/` directories.

## License

This project is distributed under the terms described in [LICENSE](LICENSE).

<p align="center">
  <img src="./Piece%20Launcher/Assets/Piece%20Launcher%20banner.png" alt="Piece Launcher banner" width="800">
</p>

<p align="center">
  <img src="./Piece%20Launcher/Assets/Piece%20Launcher.png" alt="Piece Launcher logo" width="120">
</p>

<h1 align="center">Piece Launcher</h1>

<p align="center">A dark, compact and fast Minecraft launcher.</p>

<p align="center">
  <a href="https://github.com/luaukrodev/Piece-Launcher/releases/latest"><img alt="Download" src="https://img.shields.io/badge/Download-latest-e67e22?style=for-the-badge"></a>
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-0f0f1a?style=flat-square">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-0f0f1a?style=flat-square">
  <img alt="Avalonia" src="https://img.shields.io/badge/UI-Avalonia-0f0f1a?style=flat-square">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-0f0f1a?style=flat-square">
</p>

---

## Features

- **Minecraft Java Edition** launcher with Microsoft account login
- **Mod loaders:** Forge, NeoForge, Fabric and Quilt
- **Profiles and Versions:** manage your installed versions and play with one click
- **Discover:** search and install mods, resource packs, shaders and data packs from [Modrinth](https://modrinth.com), filtered by your selected Minecraft version and loader
- **6 themes:** Dark Orange (default), Crimson Red, Forest Green, Midnight Blue, Dark Purple and Light
- **5 languages:** English, Português, Español, Français and Deutsch
- **Settings:** launch on Windows startup, keep the launcher open while the game runs, choose your startup page
- **Automatic updates** through [Velopack](https://velopack.io)

> **Maps** is a placeholder for now and is planned for a future release.

## Installation

1. Go to the [latest release](https://github.com/luaukrodev/Piece-Launcher/releases/latest).
2. Download the `Setup.exe` file and run it.
3. If Windows SmartScreen shows "Windows protected your PC", click **More info** and then **Run anyway**. The installer is not code-signed yet.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/luaukrodev/Piece-Launcher.git
cd Piece-Launcher/"Piece Launcher"
dotnet run
```

To create the installer:

```bash
dotnet publish -c Release -r win-x64 --self-contained -o publish
vpk pack --packId PieceLauncher --packTitle "Piece Launcher" --packVersion <version> --packDir publish --mainExe "Piece Launcher.exe" --icon "Assets/Piece Launcher.ico"
```

## Built with

- [Avalonia UI](https://avaloniaui.net) and [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) for installing and launching Minecraft
- [Modrinth API](https://docs.modrinth.com) for the Discover page
- [Velopack](https://velopack.io) for installer and updates

## License

Released under the [MIT License](./LICENSE.txt).

Piece Launcher is not affiliated with Mojang Studios or Microsoft. Minecraft is a trademark of Mojang Studios.

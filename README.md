<p align="center">
  <img src="./Piece%20Launcher/Assets/Piece%20Launcher%20banner.png" alt="Piece Launcher banner" width="800">
</p>

<p align="center">
  <img src="./Piece%20Launcher/Assets/Piece%20Launcher.png" alt="Piece Launcher logo" width="120">
</p>

<h1 align="center">Piece Launcher</h1>

<p align="center">A dark, compact and fast Minecraft launcher.</p>

<p align="center">
  <a href="https://github.com/luaukrodev/Piece-Launcher/releases"><img alt="Download" src="https://img.shields.io/badge/Download-latest-e67e22?style=for-the-badge"></a>
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
- **Profiles and Versions:** install game versions, create separately launchable named copies of installed versions, rename display names, browse version files, open installation folders, or delete versions with dependency checks
- **Skins:** view profile skins on a rotatable 3D model; import and save local skins for offline profiles
- **Discover and manage:** browse and install content from [Modrinth](https://modrinth.com) and [CurseForge](https://www.curseforge.com), then enable, disable or remove installed items; update is available when source metadata is known
- **Minecraft instances:** create isolated vanilla, Fabric, Forge, NeoForge or Quilt instances, select or delete them, and import Modrinth `.mrpack` or CurseForge export `.zip` modpacks; saves and mods stay separate while downloaded game files are reused
- **10 themes:** Dark Orange (default), Crimson Red, Forest Green, Midnight Blue, Dark Purple, Ocean Teal, Sakura, Desert Sand, Monochrome and Light
- **10 languages:** English, Português, Español, Français, Deutsch, Italiano, 日本語, 简体中文, 한국어 and Русский
- **Settings:** launch on Windows startup, keep the launcher open while the game runs, choose your startup page
- **Automatic update checks** through [Velopack](https://velopack.io), with update downloads and restart available in Settings

> **Maps** is a placeholder for now and is planned for a future release.

The Mods library scans the selected instance and includes content copied into its folders. Modpack imports are limited to 500 MB archives and 4 GB of imported files; downloads are checked against source hashes and sizes when provided. CurseForge pack files that do not permit third-party distribution cannot be imported by the launcher. CurseForge imports require the deployed Worker to expose the modpack file-resolution route.

## Installation

1. Go to the [releases page](https://github.com/luaukrodev/Piece-Launcher/releases).
2. Download `PieceLauncher-win-Setup.exe` and run it.
3. If Windows SmartScreen shows "Windows protected your PC", click **More info** and then **Run anyway**. The installer is not code-signed yet.

The installed launcher checks GitHub for updates at startup and asks before downloading one. You can continue without updating, disable startup checks in Settings, or check manually there. Updates require a Velopack-installed copy; portable and development builds are not updated in place. Releases are built and uploaded automatically by pushing a tag like `v0.9.0-alpha`.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Node.js.

CurseForge requests go through the Cloudflare Worker in `backend/curseforge-worker`; the API key stays on the Worker and must never be added to the launcher. To run it locally, create `backend/curseforge-worker/.dev.vars` from `.dev.vars.example`, replace the placeholder with your CurseForge API key, then start the Worker:

```bash
cd backend/curseforge-worker
npm install --legacy-peer-deps
npm run dev
```

In another terminal, point the launcher at the local Worker:

```powershell
$env:CURSEFORGE_PROXY_URL = "http://127.0.0.1:8787"
cd "Piece Launcher"
dotnet run
```

To deploy, run `npx wrangler login`, save the API key with `npx wrangler secret put CURSEFORGE_API_KEY`, and run `npm run deploy` from `backend/curseforge-worker`. Redeploy the Worker after changing its code, including to enable CurseForge modpack imports. The deployed Worker URL is the launcher's default; `CURSEFORGE_PROXY_URL` can override it for local development. The URL is public configuration, unlike the API key.

## Built with

- [Avalonia UI](https://avaloniaui.net) and [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) for installing and launching Minecraft
- [Modrinth API](https://docs.modrinth.com) for the Discover page
- [CurseForge API](https://docs.curseforge.com/rest-api/) for CurseForge mod discovery and downloads
- [Velopack](https://velopack.io) for installer and updates

## License

Released under the [MIT License](./LICENSE.txt).

Piece Launcher is not affiliated with Mojang Studios or Microsoft. Minecraft is a trademark of Mojang Studios.

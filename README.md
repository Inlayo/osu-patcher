<p align="center">
  <img align="center" width="400" alt="osu! logo" src=".github/assets/logo.png">
</p>

# osu! patcher

Apply four Relax-related patches to osu!stable.

This is for use in offline play or private servers that allow modifications **only**.
Use at your own risk! Modifications are disallowed on most servers, even though this project this 
*does not provide an unfair advantage*.

Using this on official Bancho servers WILL get you banned.

## Features

### Relax
- Show misses on hit objects while playing Relax
- Re-enable combo break sounds with Relax enabled
- Automatically save Relax scores to local leaderboards
- Re-enable the low HP glow with shaders on

<sup>Note: Relax refers to Relax *or* Autopilot</sup>

## Usage

Until I have a release ready, go to the [latest actions build](https://github.com/Inlayo-osu/osu-patcher/actions?query=branch%3Amaster)
for the `master` branch and download the attached artifact to extract. No automatic updater is included.

Only the `Stable` release stream is officially supported! `Cutting Edge` and `Beta` release streams
may have changes that cause errors or crashes. No support will be provided.

On first launch, the patcher looks for `osu!.exe` in common install locations, checking
`%LOCALAPPDATA%\osu!\osu!.exe` first, and opens the file picker in that folder if it cannot find it.
The selected path is saved to `%LOCALAPPDATA%\Inlayo-patcher\settings.json` for future launches.
The patcher temporarily creates a read-only `_staging` file in the osu! folder to prevent the
client from applying updates, then starts osu! with `-nosplash -devserver inlayo.com` and applies the
patches. Before launch, the patcher downloads the injection DLLs from
`https://assets.inlayo.com/patcher/` and caches them under `%TEMP%\Inlayo-patcher\injection`.
The following files must be available directly at that URL:

- `osu!.hook.dll`
- `osu!.stubs.dll`
- `utils.dll`
- `0Harmony.dll`
- `JetBrains.Annotations.dll`

An internet connection is required each time the patcher starts. The `_staging` file is removed
after injection (or if startup fails). If osu! exits during startup, the patcher retries once before
showing an error.

Latest tested `Stable` version: [`b20250309.2`](https://osu.ppy.sh/home/changelog/stable40/20250309.2).

Your antivirus may detect it as malware, however this is completely expected as it contains code to inject 
into processes. If you aren't convinced it isn't a false positive, feel free to build from source code.

## Compiling

1. Install the .NET SDK 8 and the .NET Framework 4.5.2 developer pack.
2. Build a small, framework-dependent single-file executable for 32-bit Windows:
   ```powershell
   dotnet publish Osu.Patcher.Injector -c Release -r win-x86 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
   ```
3. Output will be located in `./Osu.Patcher.Injector/bin/Release/net8.0-windows/win-x86/publish/` with
   `Inlayo-patcher.exe` as the single-file launcher. The .NET 8 Desktop Runtime (x86) must be installed on the
   target computer. The injection DLLs are downloaded at launch from
   `https://assets.inlayo.com/patcher/`, so the publish folder contains only the executable.

## How

This uses [ManagedInjector](https://github.com/holly-hacker/ManagedInjector) to inject a .NET DLL into an osu! process, which uses [Harmony](https://github.com/pardeike/Harmony) to hook methods/
rewrite IL instructions. To find obfuscated methods, "signatures" based on a portion of the IL instructions from the 
target method are used to locate it, and then patch it. Since this method doesn't rely on neither the Eazfuscator 
obfuscation key nor the obfuscated names, it should work on any version with matching IL even if the method names change.

## is this okay?

This was initially made after the Akatsuki private server's patcher broke for multiple months and no alternative
existed to fix the issues listed above. This project does not and never intends to bypass the
anti-cheat built into osu! (to allow modifications), and for that reason this project is only usable when osu! is
launched with a custom `-devserver` (for offline play, something like `-devserver example.com`).

I like improving the games I like (having submitted multiple PRs to osu!lazer as well), and given stable is essentially
dead in terms of bug-fixes and features I decided to write a utility to make the game I like better.
This is not a cheat, I don't make cheats.

# LunarRhythm

LunarRhythm is a pad rhythm game, built as a fork of [YARG](https://github.com/YARC-Official/YARG) (Yet Another Rhythm
Game). It plays Festival-style songs on keyboard or controller: five lanes, four parts (Lead, Bass, Drums, Vocals),
lifts, Overdrive and Festival scoring, on a 2D highway (or the classic 3D one). Songs come from an online song list,
with leaderboards, seasons, levels and cosmetics, and online lobbies of up to four players.

This repository is the game's source code, published as YARG's license (LGPL-3.0) requires. The servers it talks to
(song list, file hosting and leaderboards) are not part of it, and neither are any songs.

> [!IMPORTANT]
> LunarRhythm doesn't support piracy. It ships no copyrighted music or charts; play only songs you have the
> rights to. All charts on the online database are strictly custom made charts and are NOT licensed for this game.

## Changes from YARG

LunarRhythm is a modified version of YARG and YARG.Core. The main changes, and where they are:

- **Festival songs** (`YARG.Core/YARG.Core/Song/Entries/Ini/FestivalSong.cs`):
  - Song folders with `info.json` and `notes.mid` are read as Festival songs.
  - The four pad parts (`PART GUITAR`, `PART BASS`, `PART DRUMS`, `PART VOCALS`) are converted in memory to YARG's
    five-lane parts.
  - Lift markers become lift notes, and chords that mix lifts and presses are split.
  - Solo markers are moved to the note YARG reads.
  - Track names are moved to tick 0, where the scanner reads them.
  - Lyrics are read from LRC files.
- **Festival engine** (`YARG.Core/YARG.Core/Engine/Keys/FiveLaneKeys/`):
  - Timing is Perfect (36 points) or Good (24).
  - The multiplier goes up every 10 notes, to 4×, or 6× on bass and vocals.
  - Sustains score 12 points per beat (25 on Keys).
  - Solos give no bonus (`BaseEngine.Generic.cs`).
  - With the "Overdrive hits notes" setting, the Overdrive key also hits notes, sustains and lifts.
- **Online songs** (`YARG.Core/YARG.Core/Song/Online/OnlineSongs.cs`, `Assets/Script/Song/SongContainer.cs`):
  - The song list is downloaded at startup.
  - Covers and lyrics are saved; charts and audio stay in memory only.
  - Files are fetched through short-lived signed links, which only signed-in players get.
  - Builds only play online songs. Local songs are an editor-only debug option.
- **Leaderboards** (`YARG.Core/YARG.Core/Song/Online/Leaderboards.cs`, `Assets/Script/Menu/MusicLibrary/LeaderboardCard.cs`,
  `Assets/Script/Settings/Metadata/PreviewBuilders/LeaderboardAccountPanelBuilder.cs`):
  - Sign in with Discord from the settings; only your Discord name and avatar are read.
  - The session token is stored encrypted for your Windows account.
  - Runs are sent with the chart's hash and checked by the server.
  - Runs that fail to send are queued and retried.
  - Each song in the library shows its board.
- **Accounts and progression** (`Assets/Script/Menu/Main`, `YARG.Core/YARG.Core/Song/Online/Leaderboards.cs`):
  - Playing needs a Discord sign-in (`AccountGate.cs`); a saved sign-in allows offline play for 24 hours.
  - Seasons and featured songs head the library, with a season leaderboard.
  - XP and levels from accepted runs, with daily, weekly and season challenges (`ChallengesMenu.cs`).
  - A profile card and popup for cosmetics: aliases, frames, banners, note and highway colours, note shapes and hit
    effects (`ProfileCard.cs`, `ProfilePopup.cs`, `YARG.Core/YARG.Core/Song/Online/OnlineThemes.cs`).
  - Each run is sent with a recording of its inputs, so the server can replay it and check the score.
- **Online play** (`Assets/Script/Menu/Online`, `YARG.Core/YARG.Core/Song/Online/Lobby.cs`):
  - Lobbies of up to four with join codes, a shared setlist (one pick each), and a synchronised start.
  - The other players' highways play from their streamed inputs, a quarter of a second behind.
- **2D highway** (`Assets/Script/Gameplay/Visuals/FlatHighway.cs`, `FlatHighwayArt.cs`):
  - The default view: the track from above, leaning back a little, with flat notes, frets and lane art drawn in
    code. Overdrive phrases turn notes white.
  - Several players stand side by side at the same size, each with a name tag and a compact HUD.
  - The **2D highway** setting (Graphics) turns it off for YARG's 3D highway.
- **Gameplay and menus** (`Assets/Script/Gameplay`, `Assets/Script/Menu`):
  - All four parts play on a five-lane pad track.
  - The solo box shows no bonus step.
  - Every menu is redrawn in one flat night look ("Moonlight", `Menu/Common/Moonlight.cs`), with its own
    transitions (`Menu/Common/Moonrise.cs`) and a first-run tour.
  - Song backgrounds are a music visualizer that changes style with the song's genre (`Gameplay/Visuals/MoonVisualizer.cs`).
  - A jukebox plays in the menus, and a news line runs on the title screen (`Menu/Main/NewsTicker.cs`).
  - Settings sliders can be dragged; lanes, Overdrive, Pause and the menu keys are rebound from the Players menu.
  - Default bindings are reworked for the pad game.
- **Auto-updater** (`Assets/Script/Menu/Persistent/AutoUpdater.cs`, `YARG.Core/YARG.Core/Song/Online/BuildUpdate.cs`):
  - At start, the game compares itself with the latest build's file list and downloads only the files that changed,
    each checked against its SHA-256. Each build is stamped with when it was made
    (`Assets/Editor/Build/BuildStamp.cs`), so only a newer build replaces it.

YARG's own history is in the [YARG](https://github.com/YARC-Official/YARG) and
[YARG.Core](https://github.com/YARC-Official/YARG.Core) repositories.

## Building

You need:

- Unity **6000.3.5f2** (Unity Hub will offer it when you add the project)
- the [.NET SDK](https://dotnet.microsoft.com/download) (the SDK, not just the runtime)
- [Git LFS](https://git-lfs.com) and [Blender](https://www.blender.org/) (Unity uses it to import models)

Then:

1. Clone the repository, then run `git lfs pull`. YARG.Core is included as a folder, not a submodule.
2. Add the folder in Unity Hub and open it. If Unity asks about Safe Mode, choose **Ignore**.
3. If packages are missing, run **NuGet → Restore Packages** from the menu bar.
4. Build from **File → Build Profiles** as usual.

YARG's own build notes (Linux controller setup, the Unity YAML merge tool) apply here too: see the
[YARG README](https://github.com/YARC-Official/YARG#readme).

## Running your own servers

Out of the box the game points at placeholder addresses (`example.com`), so online songs and leaderboards won't work
until you set up your own:

| What | Where |
| --- | --- |
| Song list address, and which hosts get the API key | `DATABASE_URL` and `KEY_DOMAIN` in `YARG.Core/YARG.Core/Song/Online/OnlineSongs.cs` |
| Leaderboard server address (also used for sign-in, progression and the lobby WebSocket) | `BASE_URL` in `YARG.Core/YARG.Core/Song/Online/Leaderboards.cs` |
| Where the auto-updater looks for new builds | `MANIFEST_URL` and `ZIP_URL` in `YARG.Core/YARG.Core/Song/Online/BuildUpdate.cs` |
| API key the game sends | `Assets/Resources/OnlineApiKey.txt` (one line; git-ignored, so never commit it). **LunarRhythm → Scramble the API key** in the editor stores it scrambled |

`OnlineSongs.cs`, `Leaderboards.cs` and `Lobby.cs` show the request and response formats the game expects. Without a
server, the game stops at the sign-in screen, in builds and in the editor alike.

## Testing with local songs

Builds only play online songs. In the Unity editor, turn on **LunarRhythm → Load Local Songs (debug)** and put song
folders in a `songs` folder next to the executable (in the editor, that's the project folder). Signing in still goes through
your leaderboard server. A Festival song folder
looks like this:

```
My Song/
  info.json      title, artist, album, release_year, charter, genre, length, diff, preview_start_time
  notes.mid      PART GUITAR, PART BASS, PART DRUMS, PART VOCALS (track names at tick 0)
  lead.ogg  bass.ogg  drums.ogg  vocals.ogg  backing.ogg
  album.png      cover (optional)
  lyrics.txt     LRC lyrics (optional)
```

Regular YARG song folders (`song.ini` with `notes.mid` or `notes.chart`) still load too.

## Tests

```
dotnet test YARG.Core/YARG.Core.UnitTests
```

## License

LunarRhythm is licensed, like YARG, under the
[GNU Lesser General Public License v3.0](https://www.gnu.org/licenses/lgpl-3.0.en.html) or later; see
[`LICENSE`](LICENSE). It's based on the work of the YARG team and its
[contributors](https://github.com/YARC-Official/Contributors), and isn't made or endorsed by them.

### External licenses

Libraries and assets packaged with the source code:

| Library | License |
| --- | --- |
| [NuGet for Unity](https://github.com/GlitchEnzo/NuGetForUnity) | [MIT](https://github.com/GlitchEnzo/NuGetForUnity/blob/master/LICENSE) |
| [Unity Standalone File Browser](https://github.com/gkngkc/UnityStandaloneFileBrowser) | [MIT](https://github.com/gkngkc/UnityStandaloneFileBrowser/blob/master/LICENSE.txt) |
| [Discord GameSDK](https://discord.com/developers/docs/game-sdk/sdk-starter-guide) | Licenseless |
| [Lucide](https://lucide.dev/) | [ISC](https://lucide.dev/license) |
| [Unbounded](https://fonts.google.com/specimen/Unbounded), [Barlow](https://fonts.google.com/specimen/Barlow), [Noto Sans Japanese](https://fonts.google.com/noto/specimen/Noto+Sans+JP) and [Red Hat Display](https://fonts.google.com/specimen/Red+Hat+Display) | [Open Font License](https://scripts.sil.org/cms/scripts/page.php?site_id=nrsi&id=OFL) |
| [PolyHaven](https://polyhaven.com/) | [CC0](https://creativecommons.org/publicdomain/zero/1.0/) |
| [BASS](https://www.un4seen.com/) | [Proprietary](https://www.un4seen.com/) (free for non-commercial use) |
| [Haukcode.sACN](https://github.com/HakanL/Haukcode.sACN) | [MIT](https://github.com/HakanL/Haukcode.sACN/blob/master/LICENSE) |
| [aperitif chatter.wav by soundslikewillem](https://freesound.org/s/449550/) | [Attribution NonCommercial 4.0](https://creativecommons.org/licenses/by-nc/4.0/) |
| [Crowd after Encore.wav by soundslikewillem](https://freesound.org/s/193064/) | [Attribution NonCommercial 4.0](https://creativecommons.org/licenses/by-nc/4.0/) |

### Libraries installed by NuGet, Unity's package manager or a submodule

| Link | Type | Use |
| --- | --- | --- |
| [YARG.Core](https://github.com/YARC-Official/YARG.Core) | Library | Most of the game's backend (engine, replays, song scanning); included here with LunarRhythm's changes |
| [PlasticBand](https://github.com/TheNathannator/PlasticBand) | Reference | Controller support info |
| [GuitarGame_ChartFormats](https://github.com/TheNathannator/GuitarGame_ChartFormats) | Reference | File format documentation |
| [PlasticBand-Unity](https://github.com/TheNathannator/PlasticBand-Unity) | Library | GH/RB controller support |
| [HIDrogen](https://github.com/TheNathannator/HIDrogen) | Library | Linux HID controller support |
| [EasySharpIni](https://www.nuget.org/packages/EasySharpIni/) | Library | Parsing `song.ini` files |
| [DryWetMidi](https://www.nuget.org/packages/Melanchall.DryWetMidi) | Library | Parsing `.mid` files |
| [Minis](https://github.com/keijiro/Minis/tree/master) | Library | MIDI input for Unity |
| [DOTween](https://github.com/Demigiant/dotween) | Library | Animation |
| [UniTask](https://github.com/Cysharp/UniTask) | Library | Async |
| [unity-toolbar-extender](https://github.com/marijnz/unity-toolbar-extender/) | Library | Unity editor utility |
| [SoftMaskForUGUI](https://github.com/mob-sakai/SoftMaskForUGUI) | Library | UI utility |
| [Unity-Dependencies-Hunter](https://github.com/AlexeyPerov/Unity-Dependencies-Hunter) | Library | Unity editor utility |
| [tmpro-dynamic-data-cleaner](https://github.com/STARasGAMES/tmpro-dynamic-data-cleaner) | Library | Prevents git change spam |
| [openvat-unity](https://github.com/sharpen3d/openvat-unity.git) | Library | Vertex animated textures |

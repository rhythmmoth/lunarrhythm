using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Helpers;
using YARG.Song;

namespace YARG.Menu.Persistent
{
    /// <summary>
    /// The title screen jukebox: the server's track list (edited in the admin panel), each file downloaded once into the
    /// data folder. Plays shuffled with no repeats until the list runs out. Offline, the tracks already downloaded keep
    /// playing. A re-uploaded file under the same name isn't downloaded again: give changed tracks a new file name.
    /// </summary>
    public static class Jukebox
    {
        public class Track
        {
            public string Title;
            public string Artist;
            public string AudioPath;
            public string CoverPath;
            /// <summary>Shortname of the game song this track promotes, or null.</summary>
            public string Song;
        }

        private const string LIST_FILE = "list.json";
        // The same rule as the server and the CDN: a plain name directly in /jukebox/
        private static readonly Regex FileName = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.(mp3|png|jpg|jpeg|webp)$");
        private static readonly HttpClient Http = CreateClient();
        private static readonly Random Rng = new();
        private static readonly List<int> Bag = new();
        private static List<Leaderboards.JukeboxTrack> _tracks;
        private static bool _online;

        private static string Folder => Path.Combine(PathHelper.PersistentDataPath, "jukebox");

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LunarRhythm");
            return client;
        }

        /// <summary>The next track to play, downloaded and ready, or null if there's nothing playable. Main thread only.</summary>
        public static async Task<Track> Next()
        {
            var tracks = await LoadList();
            for (int tries = 0; tries < tracks.Count; tries++)
            {
                if (Bag.Count == 0)
                {
                    Bag.AddRange(Enumerable.Range(0, tracks.Count).OrderBy(_ => Rng.Next()));
                }
                var track = tracks[Bag[^1]];
                Bag.RemoveAt(Bag.Count - 1);

                string audio = await Fetch(track.Url);
                if (audio == null)
                {
                    continue;
                }
                return new Track
                {
                    Title = track.Title, Artist = track.Artist, AudioPath = audio, Song = track.Song,
                    CoverPath = track.Cover != null ? await Fetch(track.Cover) : null,
                };
            }
            return null;
        }

        /// <summary>
        /// The server's list once per session. Until that works (offline, or not signed in yet) the last saved list is
        /// used, limited to tracks already downloaded, and the server is asked again next time.
        /// </summary>
        private static async Task<List<Leaderboards.JukeboxTrack>> LoadList()
        {
            if (_online)
            {
                return _tracks;
            }
            Directory.CreateDirectory(Folder);
            string listPath = Path.Combine(Folder, LIST_FILE);

            var online = await Leaderboards.GetJukebox();
            if (online != null)
            {
                _tracks = online;
                _online = true;
                Bag.Clear();
                File.WriteAllText(listPath, JsonConvert.SerializeObject(online));
                Prune(online);
                return _tracks;
            }

            if (_tracks == null)
            {
                try
                {
                    var saved = File.Exists(listPath)
                        ? JsonConvert.DeserializeObject<List<Leaderboards.JukeboxTrack>>(File.ReadAllText(listPath))
                        : null;
                    _tracks = (saved ?? new()).Where(t => LocalPath(t.Url) is { } p && File.Exists(p)).ToList();
                }
                catch (Exception e) when (e is IOException or JsonException)
                {
                    _tracks = new();
                }
            }
            return _tracks;
        }

        /// <summary>The cached file for a link, downloading it first if needed. Null if it can't be had.</summary>
        private static async Task<string> Fetch(string url)
        {
            string path = LocalPath(url);
            if (path == null)
            {
                return null;
            }
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                return path;
            }

            string part = path + ".part";
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(part, bytes);
                File.Delete(path); // Unity's .NET has no overwriting Move
                File.Move(part, path);
                return path;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                YargLogger.LogWarning($"Jukebox: couldn't download {Path.GetFileName(path)} ({e.Message})");
                File.Delete(part);
                return null;
            }
        }

        /// <summary>Where a link is cached, or null for anything that isn't a plain file in our CDN's /jukebox/.</summary>
        private static string LocalPath(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || !(uri.Host == "example.com" || uri.Host.EndsWith(".example.com")) || uri.Query.Length > 0)
            {
                return null;
            }
            string name = Path.GetFileName(uri.AbsolutePath);
            return uri.AbsolutePath == "/jukebox/" + name && FileName.IsMatch(name) && !name.Contains("..")
                ? Path.Combine(Folder, name)
                : null;
        }

        /// <summary>Drops downloads the list no longer uses, so the folder doesn't grow forever.</summary>
        private static void Prune(List<Leaderboards.JukeboxTrack> tracks)
        {
            var keep = new HashSet<string>(tracks.SelectMany(t => new[] { LocalPath(t.Url), t.Cover != null ? LocalPath(t.Cover) : null })
                .Where(p => p != null), StringComparer.OrdinalIgnoreCase) { Path.Combine(Folder, LIST_FILE) };
            foreach (string file in Directory.GetFiles(Folder))
            {
                if (!keep.Contains(file))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException)
                    {
                        // In use or locked: try again next launch
                    }
                }
            }
        }

        /// <summary>The game song a track promotes, if it's in the library.</summary>
        public static SongEntry FindSong(string shortname)
        {
            if (string.IsNullOrEmpty(shortname))
            {
                return null;
            }
            return SongContainer.Songs.FirstOrDefault(s => OnlineSongs.IsOnlineFolder(s.ActualLocation)
                && Path.GetFileName(s.ActualLocation.TrimEnd('/', '\\')) == shortname);
        }
    }
}

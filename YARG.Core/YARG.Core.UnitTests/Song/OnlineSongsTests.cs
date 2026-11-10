using Newtonsoft.Json.Linq;
using NUnit.Framework;
using YARG.Core.Song;
using YARG.Core.Song.Cache;

namespace YARG.Core.UnitTests.Song;

public class OnlineSongsTests
{
    private static readonly JObject Entry = JObject.Parse("""
        { "artist": "Martin Solveig", "title": "Hello", "album": "Smash", "release_year": "2011", "charter": "Tester",
          "assets": { "cover": "https://cdn.example/songs/hello/album.png", "video_bkg": "" },
          "stems": { "lead": "https://cdn.example/songs/hello/lead.ogg", "backing": "", "kazoo": "https://cdn.example/k.ogg" },
          "data": { "notes": "https://cdn.example/songs/hello/notes.mid" },
          "other_info": { "diff": { "vocals": 2, "drums": 2, "bass": 1, "guitar": 4 },
                          "metadata": { "length": 201, "preview_start_time": 38671, "genre": "Dance/Electronic" } },
          "serverChangeList": 3 }
        """);

    [Test]
    public void MakeInfo_ReadsBackAsFestivalMetadata()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, OnlineSongs.MakeInfo(Entry).ToString());
            var ini = FestivalSong.ReadInfo(path);
            Assert.Multiple(() =>
            {
                Assert.That(ini.Extract("name", out string name) && name == "Hello");
                Assert.That(ini.Extract("genre", out string genre) && genre == "Dance/Electronic");
                Assert.That(ini.Extract("song_length", out long length) && length == 201000);
                Assert.That(ini.Extract("preview_start_time", out long preview) && preview == 38671);
                Assert.That(ini.Extract("diff_guitar", out int guitar) && guitar == 4);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ChartStamp_SameChartSameStamp()
    {
        byte[] a = "chart one"u8.ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(OnlineSongs.ChartStamp(a), Is.EqualTo(OnlineSongs.ChartStamp("chart one"u8.ToArray())));
            Assert.That(OnlineSongs.ChartStamp(a), Is.Not.EqualTo(OnlineSongs.ChartStamp("chart two"u8.ToArray())));
        }
    }

    [Test]
    public void Sync_ReadsNewsAndDoesNotTreatItAsASong()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, OnlineSongs.DATABASE_FILE), """{ "_news": "New songs every Friday" }""");
            OnlineSongs.Sync(root, allowNetwork: false, null);
            Assert.That(OnlineSongs.News, Is.EqualTo("New songs every Friday"));
            Assert.That(Directory.GetDirectories(root), Is.Empty);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase("https://cdn.example.com/songs/a/lead.ogg", true)]
    [TestCase("https://example.com/x", true)]
    [TestCase("http://cdn.example.com/x", false)]
    [TestCase("https://example.com.evil.com/x", false)]
    [TestCase("https://evilexample.com/x", false)]
    public void ApiKey_OnlyGoesToOwnHttpsHosts(string url, bool expected)
    {
        Assert.That(OnlineSongs.IsKeyHost(new Uri(url)), Is.EqualTo(expected));
    }

    // Hits the real database. Set YARG_TEST_ONLINE=1 (and YARG_TEST_ONLINE_KEY once the API needs a key) to run.
    private static string? Key => Environment.GetEnvironmentVariable("YARG_TEST_ONLINE_KEY");

    [Test, Category("Integration")]
    public void Sync_LiveDatabase_BuildsScannableFoldersAndFetchesMediaOnDemand()
    {
        if (Environment.GetEnvironmentVariable("YARG_TEST_ONLINE") != "1")
        {
            Assert.Ignore("Set YARG_TEST_ONLINE=1 to run.");
        }

        string root = Path.Combine(Path.GetTempPath(), "online_songs_test");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, "removed-song"));

        // Signed out, the CDN won't serve anything: no song folders appear
        Leaderboards.Init(root);
        OnlineSongs.Sync(root, allowNetwork: true, Key);
        Assert.That(Directory.GetDirectories(root), Is.Empty, "signed out: nothing downloaded");

        // Song files need a signed-in player. Copy the first line of the game's leaderboard_token file here.
        string? token = Environment.GetEnvironmentVariable("YARG_TEST_ONLINE_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Assert.Ignore("Set YARG_TEST_ONLINE_TOKEN to also test downloading.");
        }
        File.WriteAllText(Path.Combine(root, "leaderboard_token"), token);
        Leaderboards.Init(root);
        Directory.CreateDirectory(Path.Combine(root, "removed-song"));
        OnlineSongs.Sync(root, allowNetwork: true, Key);
        File.Delete(Path.Combine(root, "leaderboard_token"));
        var db = JObject.Parse(File.ReadAllText(Path.Combine(root, OnlineSongs.DATABASE_FILE)));
        db.Remove(OnlineSongs.NEWS_KEY);
        Assert.That(Directory.Exists(Path.Combine(root, "removed-song")), Is.False, "songs gone from the database are removed");
        foreach (var song in db.Properties())
        {
            string folder = Path.Combine(root, song.Name);
            Assert.That(OnlineSongs.TryGetChart(folder, out _), song.Name + ": chart in memory");
            Assert.That(File.Exists(Path.Combine(folder, "notes.mid")), Is.False, song.Name + ": chart not saved");
            Assert.That(File.Exists(Path.Combine(folder, FestivalSong.INFO_FILE)), song.Name);
        }

        string tmp = Path.Combine(root, "..", "online_songs_test_cache");
        Directory.CreateDirectory(tmp);
        var cache = CacheHandler.RunScan(false, Path.Combine(tmp, "cache.bin"), Path.Combine(tmp, "bad.txt"), false, new List<string> { root });
        var entries = cache.Entries.Values.SelectMany(e => e).ToList();
        Assert.That(entries, Has.Count.EqualTo(db.Count), "every song scans from its in-memory chart");
        Assert.That(entries.All(e => OnlineSongs.IsOnline(root, e)));
        Assert.That(entries[0].LoadChart(), Is.Not.Null, "and loads for play from it");

        string first = entries[0].ActualLocation;
        Assert.That(OnlineSongs.StemsReady(first), Is.False);
        Assert.That(OnlineSongs.FetchStems(first), Is.True);
        TestContext.Out.WriteLine($"{OnlineSongs.GetStems(first)!.Count} stems in memory for {first}");
        Assert.That(OnlineSongs.GetStems(first), Is.Not.Empty);
        Assert.That(Directory.EnumerateFiles(first).Where(f => f.EndsWith(".ogg") || f.EndsWith(".opus")), Is.Empty,
            "audio isn't saved");

        // The cache from the first scan is still good for the same charts
        var again = CacheHandler.RunScan(true, Path.Combine(tmp, "cache.bin"), Path.Combine(tmp, "bad.txt"), false, new List<string> { root });
        Assert.That(again.Entries.Values.SelectMany(e => e).Count(), Is.EqualTo(db.Count));

        // Offline: no charts in memory, so the online songs aren't playable
        OnlineSongs.Sync(root, allowNetwork: false, Key, url: "https://invalid.invalid/songdb.json");
        Assert.That(OnlineSongs.TryGetChart(first, out _), Is.False);
        Directory.Delete(tmp, true);
    }
}

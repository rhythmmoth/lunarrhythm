using NUnit.Framework;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Song;

public class LeaderboardsTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lb_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        Leaderboards.BaseUrl = Leaderboards.BASE_URL;
        Leaderboards.Init(Path.GetTempPath()); // forget the test token
        Directory.Delete(_dir, true);
    }

    private static Leaderboards.Run Run() => new()
    {
        Shortname = "1999", Instrument = "guitar", Difficulty = "expert", Chart = "abc", Score = 100,
    };

    [Test]
    public void MapsFestivalPartsToTheirApiNames()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Leaderboards.InstrumentName(Instrument.FiveFretGuitar), Is.EqualTo("guitar"));
            Assert.That(Leaderboards.InstrumentName(Instrument.FiveFretBass), Is.EqualTo("bass"));
            Assert.That(Leaderboards.InstrumentName(Instrument.Keys), Is.EqualTo("vocals"));
            Assert.That(Leaderboards.InstrumentName(Instrument.FiveFretRhythm), Is.EqualTo("drums"));
            Assert.That(Leaderboards.InstrumentName(Instrument.ProDrums), Is.Null);
            Assert.That(Leaderboards.DifficultyName(Difficulty.Expert), Is.EqualTo("expert"));
            Assert.That(Leaderboards.DifficultyName(Difficulty.ExpertPlus), Is.Null);
        }
    }

    [Test]
    public void ReadsTheServersProfileAnswer()
    {
        var p = Newtonsoft.Json.JsonConvert.DeserializeObject<Leaderboards.Profile>("""
            {"name": "Nova", "level": 10, "into": 5, "need": 4750,
             "look": {"alias": "Full Combo Chaser", "frame": "#f3e8cc", "color": "#eef0ff", "banner": "#18204a",
                      "highway": "default-highway", "notes": "classic-notes", "ids": {"alias": "fc-chaser"}},
             "stats": {"rank": 2, "totalScore": 5503014, "fcs": 25, "plays": 40, "favourite": "drums", "created": 1700000000},
             "instruments": {"drums": {"level": 6, "stars": 72}},
             "season": {"number": 1, "name": "Moonrise", "level": 3},
             "items": [{"id": "gold-frame", "type": "frame", "label": "Gold", "color": "#f2c94c", "rarity": "rare", "owned": false,
                        "equipped": false, "requirement": "Level 30", "have": 10, "need": 30, "unlocked": null}],
             "recent": [{"id": "fc-chaser", "label": "Full Combo Chaser", "type": "alias", "date": 1700000500}],
             "next": [{"id": "gold-frame", "label": "Gold", "type": "frame", "requirement": "Level 30", "have": 10, "need": 30}]}
            """)!;
        Assert.That((p.Name, p.Level, p.Look.Alias, p.Look.Ids["alias"], p.Stats.Rank, p.Instruments["drums"].Stars, p.Season!.Level),
            Is.EqualTo(("Nova", 10, "Full Combo Chaser", "fc-chaser", (int?) 2, 72, 3)));
        Assert.That((p.Items[0].Requirement, p.Items[0].Owned, p.Recent[0].Date, p.Next[0].Need), Is.EqualTo(("Level 30", false, (long?) 1700000500, 30L)));
    }

    [Test]
    public void ReadsTheServersXpAndProgressAnswer()
    {
        var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Leaderboards.Result>("""
            {"accepted": true, "personalBest": false, "rank": 2, "best": 900,
             "xp": {"lines": [{"label": "Song finished", "xp": 120}, {"label": "Full combo", "xp": 200}], "total": 320, "levelBefore": 3},
             "completed": ["FC a song"],
             "progress": {"level": 4, "into": 50, "need": 2500, "total": 6000,
               "instruments": {"drums": {"level": 2, "into": 10, "need": 1750}},
               "daily": {"ends": "2026-12-03T00:00:00.0000000Z", "items": [{"id": "fc", "label": "FC a song", "xp": 300, "reward": null,
                 "count": 1, "progress": 1, "done": true, "note": null}]},
               "weekly": {"ends": "2026-12-07T00:00:00.0000000Z", "bonus": 1500, "done": false, "multiplier": 2,
                 "songs": [{"shortname": "a", "done": true}]},
               "season": {"number": 1, "name": "Moonrise", "start": "2026-12-01", "end": "2027-03-01", "running": true,
                 "level": 2, "into": 5, "need": 1750, "challenges": [{"id": "top-10", "label": "Top 10", "reward": "Halo",
                 "count": 1, "progress": 0, "done": false, "note": "Now #12 · given at season end"}]}}}
            """)!;
        Assert.That((result.Xp!.Total, result.Xp.Lines[1].Label, result.Xp.LevelBefore), Is.EqualTo((320, "Full combo", 3)));
        Assert.That(result.Completed, Is.EqualTo(new[] { "FC a song" }));
        var p = result.Progress!;
        Assert.That((p.Level, p.Into, p.Need, p.Instruments["drums"].Level), Is.EqualTo((4, 50L, 2500L, 2)));
        Assert.That((p.Daily.Items[0].Done, p.Weekly.Songs[0].Done, p.Season!.Challenges[0].Note),
            Is.EqualTo((true, true, "Now #12 · given at season end")));
        Assert.That(p.Daily.Ends, Is.EqualTo(new DateTime(2026, 12, 3, 0, 0, 0, DateTimeKind.Utc)).Within(TimeSpan.Zero).Or.EqualTo(new DateTime(2026, 12, 3)));
    }

    [Test]
    public void ChartHashIsTheSha1OfNotesMid()
    {
        File.WriteAllText(Path.Combine(_dir, "notes.mid"), "abc");
        // Same as Python's hashlib.sha1(b"abc").hexdigest(), which the songdb tool publishes
        Assert.That(Leaderboards.ChartHash(_dir), Is.EqualTo("a9993e364706816aba3e25717850c26c9cd0d89d"));
    }

    [Test]
    public async Task KeepsRunsWhenTheServerIsUnreachable()
    {
        File.WriteAllText(Path.Combine(_dir, "leaderboard_token"), "token");
        Leaderboards.Init(_dir);
        Leaderboards.BaseUrl = "http://127.0.0.1:9/lb/"; // nothing listens there

        Assert.That(await Leaderboards.Submit(Run()), Is.Null);
        await Leaderboards.RetryQueued();
        string queue = File.ReadAllText(Path.Combine(_dir, "leaderboard_queue.json"));
        Assert.That(queue, Does.Contain("\"Shortname\":\"1999\"").And.Not.Contain("},{"), "still queued, once");
    }

    private static long UnixHoursAgo(double hours) => DateTimeOffset.UtcNow.AddHours(-hours).ToUnixTimeSeconds();

    [Test]
    public async Task ABanAndTheLastCheckSurviveARelaunchAndGoingOffline()
    {
        File.WriteAllText(Path.Combine(_dir, "leaderboard_token"), $"token\nTester\n{UnixHoursAgo(2)}\nbanned");
        Leaderboards.Init(_dir);
        Leaderboards.BaseUrl = "http://127.0.0.1:9/lb/"; // offline
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Leaderboards.Banned, Is.True);
            Assert.That(Leaderboards.OfflineTimeLeft.TotalHours, Is.InRange(21.9, 22.1));
            Assert.That(await Leaderboards.CheckAccount(), Is.EqualTo(Leaderboards.AccountState.Banned), "offline doesn't lift a ban");
        }
    }

    [TestCase(25, Description = "the window ran out")]
    [TestCase(-1, Description = "the clock was set back")]
    public async Task OfflinePlayEndsAfter24HoursOrIfTheClockGoesBack(double hoursAgo)
    {
        File.WriteAllText(Path.Combine(_dir, "leaderboard_token"), $"token\nTester\n{UnixHoursAgo(hoursAgo)}\n");
        Leaderboards.Init(_dir);
        Leaderboards.BaseUrl = "http://127.0.0.1:9/lb/";
        Assert.That(Leaderboards.OfflineTimeLeft, Is.EqualTo(TimeSpan.Zero));
        Assert.That(await Leaderboards.CheckAccount(), Is.EqualTo(Leaderboards.AccountState.Offline));
    }

    [Test]
    public async Task ReadsTheBanFromTheServerAndSavesTheCheck()
    {
        int port = 47000 + Random.Shared.Next(1000);
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/lb/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            byte[] body = System.Text.Encoding.UTF8.GetBytes("{\"name\":\"Tester\",\"banned\":true,\"banReason\":\"cheating\",\"avatar\":\"avatar/7/abc.png\"}");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        });

        File.WriteAllText(Path.Combine(_dir, "leaderboard_token"), "token\nTester");
        Leaderboards.Init(_dir);
        Leaderboards.BaseUrl = $"http://localhost:{port}/lb/";
        Assert.That(await Leaderboards.CheckAccount(), Is.EqualTo(Leaderboards.AccountState.Banned));
        Assert.That(Leaderboards.BanReason, Is.EqualTo("cheating"));

        Leaderboards.Init(_dir); // relaunch
        Assert.That((Leaderboards.Banned, Leaderboards.OfflineTimeLeft.TotalHours > 23.9), Is.EqualTo((true, true)));
        Assert.That(Leaderboards.PlayerAvatar, Is.EqualTo("avatar/7/abc.png"), "the picture shows offline too");
    }

    [Test]
    public void KeptRunsAreGivenUpAfterTenTriesOrAWeek()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var run = new Leaderboards.Run { QueuedAt = now.ToUnixTimeSeconds() };
        for (int i = 1; i < Leaderboards.MAX_TRIES; i++) Assert.That(Leaderboards.KeepTrying(run, now), Is.True, "try " + i);
        Assert.That(Leaderboards.KeepTrying(run, now), Is.False, "the tenth failure gives up");

        var old = new Leaderboards.Run { QueuedAt = now.ToUnixTimeSeconds() };
        Assert.That(Leaderboards.KeepTrying(old, now.AddDays(8)), Is.False, "kept over a week");
        var fromOldBuild = new Leaderboards.Run(); // no QueuedAt: counts from now
        Assert.That(Leaderboards.KeepTrying(fromOldBuild, now), Is.True);
        Assert.That(fromOldBuild.QueuedAt, Is.EqualTo(now.ToUnixTimeSeconds()));
    }

    [Test]
    public void RemembersTheDiscordName()
    {
        File.WriteAllText(Path.Combine(_dir, "leaderboard_token"), "token\nTester");
        Leaderboards.Init(_dir);
        Assert.That((Leaderboards.SignedIn, Leaderboards.PlayerName), Is.EqualTo((true, "Tester")));

        File.WriteAllText(Path.Combine(_dir, "leaderboard_token"), "token"); // signed in before names were saved
        Leaderboards.Init(_dir);
        Assert.That((Leaderboards.SignedIn, Leaderboards.PlayerName), Is.EqualTo((true, (string?) null)));
    }

    [Test]
    public void TokenFileIsEncryptedForThisWindowsAccount()
    {
        if (!TokenFile.Encrypted)
        {
            Assert.Ignore("DPAPI is Windows only.");
        }
        string file = Path.Combine(_dir, "leaderboard_token");
        File.WriteAllText(file, "secret-token-value\nTester"); // as saved before encryption existed

        Leaderboards.Init(_dir);
        string onDisk = File.ReadAllText(file);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((Leaderboards.SignedIn, Leaderboards.PlayerName), Is.EqualTo((true, "Tester")));
            Assert.That(onDisk, Does.StartWith("DPAPI1:"), "rewritten encrypted on first read");
            Assert.That(onDisk, Does.Not.Contain("secret-token-value").And.Not.Contain("Tester"));
        }

        Leaderboards.Init(_dir); // reads the encrypted file back
        Assert.That((Leaderboards.SignedIn, Leaderboards.PlayerName), Is.EqualTo((true, "Tester")));
    }

    [Test]
    public void TokenFileFromAnotherAccountMeansSignedOut()
    {
        // What a file copied from another PC looks like here: DPAPI can't decrypt it
        File.WriteAllText(Path.Combine(_dir, "leaderboard_token"), "DPAPI1:" + Convert.ToBase64String(new byte[64]));
        Leaderboards.Init(_dir);
        Assert.That(Leaderboards.SignedIn, Is.False);
    }

    [Test]
    public async Task NotSignedInSendsNothing()
    {
        Leaderboards.Init(_dir);
        Assert.That(Leaderboards.SignedIn, Is.False);
        Assert.That(await Leaderboards.Submit(Run()), Is.Null);
        Assert.That(File.Exists(Path.Combine(_dir, "leaderboard_queue.json")), Is.False);
    }

    // Hits the real server. Set YARG_TEST_ONLINE=1 and YARG_TEST_ONLINE_KEY to run.
    [Test]
    public async Task LiveServerRejectsAnUnknownTokenAndAnUnknownSignIn()
    {
        if (Environment.GetEnvironmentVariable("YARG_TEST_ONLINE") != "1")
        {
            Assert.Ignore("Set YARG_TEST_ONLINE=1 to run.");
        }
        OnlineSongs.ApiKey = Environment.GetEnvironmentVariable("YARG_TEST_ONLINE_KEY");
        File.WriteAllText(Path.Combine(_dir, "leaderboard_token"), "not-a-real-token");
        Leaderboards.Init(_dir);

        var result = await Leaderboards.Submit(Run());
        Assert.That(result?.Reason, Is.EqualTo("signed_out"), "the key got through nginx and the server refused the token");
        Assert.That(Leaderboards.SignedIn, Is.False);

        Assert.That(await Leaderboards.SignIn(_ => { }), Is.Null, "a sign-in that was never started");

        var board = await Leaderboards.GetBoard("1999", "guitar", 5);
        Assert.That(board?.Ranked, Is.True, "a published song with CHOpt ceilings");
        Assert.That((await Leaderboards.GetBoard("no-such-song", "guitar", 5))?.Ranked, Is.False);
    }
}

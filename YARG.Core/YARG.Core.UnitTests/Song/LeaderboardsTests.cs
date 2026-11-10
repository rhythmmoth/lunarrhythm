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

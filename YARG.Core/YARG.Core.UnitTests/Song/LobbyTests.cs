using NUnit.Framework;
using YARG.Core.Engine.Guitar;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Replays;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Song;

public class LobbyTests
{
    [TearDown]
    public void TearDown()
    {
        Lobby.State = null;
        Lobby.Setups.Clear();
        Lobby.Feeds.Clear();
    }

    [Test]
    public void ClockTakesTheFastestPingAndReadsLobbyLists()
    {
        // Server 500 ms ahead: a 40 ms trip pins it to within 20 ms; a slow 900 ms trip (read late) can't move it
        Lobby.AddClockSample(1_000, 1_040, 1_520);
        Lobby.AddClockSample(2_000, 2_900, 3_100);
        Assert.That(Lobby.ClockOffsetMs, Is.EqualTo(500));

        Lobby.Apply("""{"t":"lobbies","list":[{"code":"MOTH-4K2","host":{"name":"Kyle","level":31},"players":["Kyle","akira"]}]}""", 0);
        Assert.That(Lobby.Open![0].Code, Is.EqualTo("MOTH-4K2"));
        Assert.That(Lobby.Open[0].Host.Name, Is.EqualTo("Kyle"));
        Assert.That(Lobby.Open[0].Players, Has.Count.EqualTo(2));

        Lobby.Apply("""{"t":"state","you":1,"code":"MOTH-4K2","private":true,"phase":"between","players":[{"id":2,"name":"akira","away":true}],"setlist":[],"song":1}""", 0);
        Assert.That(Lobby.State!.Phase, Is.EqualTo("between"));
        Assert.That(Lobby.State.Private, Is.True);
        Assert.That(Lobby.State.ById(2)!.Away, Is.True);
    }

    [Test]
    public void SetupRoundTripsWithoutInputs()
    {
        var profile = new YargProfile { Name = "Nova", GameMode = GameMode.FiveFretGuitar, CurrentInstrument = Instrument.FiveFretBass,
            CurrentDifficulty = Difficulty.Hard, NoteSpeed = 7.5f };
        var param = EnginePreset.Default.FiveFretGuitar.Create(new[] { 0.21f, 0.46f, 0.77f, 1.85f, 3.08f, 4.29f },
            new[] { 0.21f, 0.46f, 0.77f, 1.85f, 3.08f, 4.29f }, true);
        var frame = new ReplayFrame(profile, param, new GuitarStats(), new[] { new GameInput(1, 2, 3) });

        string setup = Lobby.EncodeSetup(frame);
        Assert.That(setup.Length, Is.LessThan(3000), "the server takes 3000 at most");
        var back = Lobby.DecodeSetup(setup)!;
        Assert.That(back.Profile.CurrentInstrument, Is.EqualTo(Instrument.FiveFretBass));
        Assert.That(back.Profile.CurrentDifficulty, Is.EqualTo(Difficulty.Hard));
        Assert.That(back.Profile.NoteSpeed, Is.EqualTo(7.5f));
        Assert.That(back.EngineParameters, Is.InstanceOf<GuitarEngineParameters>());
        Assert.That(back.Inputs, Is.Empty);
        Assert.That(back.Profile.Name, Is.Empty, "the profile name stays on this PC");
        Assert.That(profile.Name, Is.EqualTo("Nova"));
        Assert.That(Lobby.DecodeSetup("UlBGTQ=="), Is.Null);
        TestContext.WriteLine($"setup is {setup.Length} chars");
    }

    [Test]
    public void SetupsAndPressesAreKeptPerPlayerAndSong()
    {
        Lobby.Apply("""{"t":"setup","from":7,"song":0,"setup":"UlBGTQ=="}""", 0);
        Assert.That(Lobby.Setups[7], Is.EqualTo((0, "UlBGTQ==")));

        Lobby.Apply("""{"t":"in","from":7,"song":0,"w":1.5,"i":[[1.25,3,1],[1.4,3,0]]}""", 0);
        var feed = Lobby.Feeds[7];
        Assert.That(feed.Watermark, Is.EqualTo(1.5));
        Assert.That(feed.Inputs.TryDequeue(out var first), Is.True);
        Assert.That((first.Time, first.Action, first.Integer), Is.EqualTo((1.25, 3, 1)));
        Lobby.Apply("""{"t":"in","from":7,"song":0,"w":1.0,"i":[]}""", 0);
        Assert.That(feed.Watermark, Is.EqualTo(1.5), "never goes back");

        Lobby.Apply("""{"t":"in","from":7,"song":1,"w":0.2,"i":[]}""", 0);
        Assert.That(Lobby.Feeds[7], Is.Not.SameAs(feed), "a new song starts a new feed");
        Assert.That(Lobby.Feeds[7].Watermark, Is.EqualTo(0.2));
    }

    [Test]
    public void ServerStateBecomesTheLobby()
    {
        Lobby.Apply("""
            {"t":"state","now":5,"you":2,"code":"ABCD-EFG","phase":"playing","host":1,
             "players":[{"id":1,"name":"Kyle","level":31,"alias":"Lane Breaker","frame":"#f2c94c","instrument":"FiveFretGuitar",
                         "difficulty":"Expert","pick":"songA","ready":true,"progress":100,"loaded":true,"sitsOut":false,"dropped":false},
                        {"id":2,"name":"Nova","level":24,"pick":"songB","ready":true,"loaded":true,"sitsOut":true}],
             "setlist":[{"song":"songA","by":1,"results":[{"id":1,"score":1000,"stars":5,"fc":true}]},{"song":"songB","by":2,"results":[]}],
             "song":0,"startAt":123456,"deadline":999,"last":null}
            """, 0);
        var s = Lobby.State!;
        Assert.That(s.Code, Is.EqualTo("ABCD-EFG"));
        Assert.That(s.Me!.Name, Is.EqualTo("Nova"));
        Assert.That(s.Me.SitsOut, Is.True);
        Assert.That(s.IsHost, Is.False);
        Assert.That(s.Current!.Song, Is.EqualTo("songA"));
        Assert.That(s.Current.Results[0].Fc, Is.True);
        Assert.That(s.StartAt, Is.EqualTo(123456));
        Assert.That(s.ById(1)!.Alias, Is.EqualTo("Lane Breaker"));
    }

    [Test]
    public void ErrorsAreKeptOnceAndLeavingClearsTheLobby()
    {
        Lobby.Apply("""{"t":"state","you":1,"code":"X","players":[]}""", 0);
        Lobby.Apply("""{"t":"error","reason":"lobby_full"}""", 0);
        Assert.That(Lobby.TakeError(), Is.EqualTo("lobby_full"));
        Assert.That(Lobby.TakeError(), Is.Null);
        Lobby.Apply("""{"t":"left"}""", 0);
        Assert.That(Lobby.State, Is.Null);
        Lobby.Apply("not json", 0);
    }
}

using System.IO.Compression;
using NUnit.Framework;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Song;

public class BuildUpdateTests
{
    private string _root = "";

    [SetUp]
    public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "lr-update-" + Guid.NewGuid())).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    private static string Json(Dictionary<string, string> files) => Newtonsoft.Json.JsonConvert.SerializeObject(new
    {
        files = files.ToDictionary(f => f.Key, f => new { size = (long) System.Text.Encoding.UTF8.GetByteCount(f.Value), sha256 = Sha(f.Value) }),
    });

    private static string Sha(string text) =>
        string.Concat(System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));

    [Test]
    public void OnlyChangedFilesAreStagedAndCheckedAgainstTheList()
    {
        // The game as installed: one file up to date, one old, one missing
        string game = Directory.CreateDirectory(Path.Combine(_root, "game")).FullName;
        Directory.CreateDirectory(Path.Combine(game, "LunarRhythm_Data"));
        File.WriteAllText(Path.Combine(game, "LunarRhythm.exe"), "exe v2");
        File.WriteAllText(Path.Combine(game, "LunarRhythm_Data", "data.bin"), "data v1");
        var latest = new Dictionary<string, string>
        {
            ["LunarRhythm.exe"] = "exe v2", ["LunarRhythm_Data/data.bin"] = "data v2", ["LunarRhythm_Data/new.bin"] = "new",
        };
        var manifest = BuildUpdate.Parse(Json(latest))!;

        var changed = BuildUpdate.Changed(manifest, game);
        Assert.That(changed, Is.EquivalentTo(new[] { "LunarRhythm_Data/data.bin", "LunarRhythm_Data/new.bin" }));

        string zip = Path.Combine(_root, "latest.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (path, text) in latest)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(text);
            }
        }
        string staging = Path.Combine(_root, "staging");
        Assert.That(BuildUpdate.Stage(zip, manifest, changed, staging), Is.True);
        Assert.That(Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(staging, f).Replace('\\', '/')),
            Is.EquivalentTo(changed), "only what changed");
        Assert.That(File.ReadAllText(Path.Combine(staging, "LunarRhythm_Data", "data.bin")), Is.EqualTo("data v2"));

        // A zip whose file doesn't match the list stages nothing
        var tampered = BuildUpdate.Parse(Json(new Dictionary<string, string>(latest) { ["LunarRhythm_Data/new.bin"] = "something else" }))!;
        Assert.That(BuildUpdate.Stage(zip, tampered, new[] { "LunarRhythm_Data/new.bin" }, staging), Is.False);
        Assert.That(Directory.Exists(staging), Is.False);
    }

    [Test]
    public void ListsThatReachOutsideTheGameFolderAreRefused()
    {
        Assert.That(BuildUpdate.Parse(Json(new() { ["../evil.dll"] = "x" })), Is.Null);
        Assert.That(BuildUpdate.Parse(Json(new() { ["C:/Windows/evil.dll"] = "x" })), Is.Null);
        Assert.That(BuildUpdate.Parse(Json(new() { ["a/./b"] = "x" })), Is.Null);
        Assert.That(BuildUpdate.Parse("not json"), Is.Null);
        Assert.That(BuildUpdate.Parse(Json(new() { ["LunarRhythm_Data/ok.bin"] = "x" })), Is.Not.Null);
    }

    [Test]
    public void OnlyANewerBuildReplacesThisOne()
    {
        var list = BuildUpdate.Parse("{\"built\": 200, \"files\": {\"LunarRhythm.exe\": {\"size\": 1, \"sha256\": \"00\"}}}")!;
        Assert.That(list.Built, Is.EqualTo(200));
        Assert.That(BuildUpdate.IsNewer(list, 100), Is.True, "an older game updates");
        Assert.That(BuildUpdate.IsNewer(list, 200), Is.False, "the same build doesn't");
        Assert.That(BuildUpdate.IsNewer(list, 300), Is.False, "a build made after the upload isn't swapped back");
        Assert.That(BuildUpdate.IsNewer(list, 0), Is.True, "a game from before stamps checks as it used to");
        var unstamped = BuildUpdate.Parse(Json(new() { ["LunarRhythm.exe"] = "x" }))!;
        Assert.That(BuildUpdate.IsNewer(unstamped, 300), Is.False, "a list from before stamps is older than any stamped game");

        Directory.CreateDirectory(Path.Combine(_root, "sa"));
        Assert.That(BuildUpdate.ReadStamp(Path.Combine(_root, "sa")), Is.EqualTo(0));
        File.WriteAllText(Path.Combine(_root, "sa", "build_stamp.txt"), "1795600000\n");
        Assert.That(BuildUpdate.ReadStamp(Path.Combine(_root, "sa")), Is.EqualTo(1795600000));
    }
}

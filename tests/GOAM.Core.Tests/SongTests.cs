using Goam.Core.Songs;

namespace Goam.Core.Tests;

public class SongNamesTests
{
    [Theory]
    [InlineData("Hello World!", "Hello_World")]
    [InlineData("  --Test Song--  ", "Test_Song")]
    [InlineData("AC/DC - Back In Black (Official)", "AC_DC___Back_In_Black__Official")]
    [InlineData("Canción ñandú", "Canción_ñandú")]
    [InlineData("日本語のタイトル", "日本語のタイトル")]
    [InlineData("🎵Song🎵", "Song")]
    [InlineData("../../evil", "evil")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("", "song")]
    [InlineData("!!!", "song")]
    [InlineData("CON", "CON_")]
    [InlineData("nul", "nul_")]
    public void TitleBecomesSafeFileStem(string title, string expected)
    {
        Assert.Equal(expected, SongNames.FromTitle(title));
    }

    [Fact]
    public void TitleIsCappedAt100Characters()
    {
        string stem = SongNames.FromTitle(new string('a', 250));
        Assert.Equal(100, stem.Length);
    }

    [Theory]
    [InlineData("My Song", "My Song")]
    [InlineData("My Song.wav", "My Song")]
    [InlineData("  padded  ", "padded")]
    [InlineData("a:b*c?d", "a_b_c_d")]
    [InlineData("..\\..\\boom", ".._.._boom")]
    [InlineData("trailing. . ", "trailing")]
    public void RenameInputIsNormalised(string input, string expected)
    {
        Assert.True(SongNames.TryNormalizeRename(input, out string stem));
        Assert.Equal(expected, stem);
        Assert.DoesNotContain('\\', stem);
        Assert.DoesNotContain('/', stem);
        Assert.DoesNotContain(':', stem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("___")]
    [InlineData("CON")]
    [InlineData("com1")]
    [InlineData("LPT9.wav")]
    public void RenameRejectsEmptyOrReserved(string input)
    {
        Assert.False(SongNames.TryNormalizeRename(input, out _));
    }

    [Theory]
    [InlineData("..\\evil.wav")]
    [InlineData("../evil.wav")]
    [InlineData("sub\\x.wav")]
    [InlineData("C:\\Windows\\x.wav")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("name.wav:stream")]
    public void ResolveInsideRejectsTraversal(string fileName)
    {
        using var dir = new TempDir();
        Assert.Throws<ArgumentException>(() => SongNames.ResolveInside(dir.Path, fileName));
    }

    [Fact]
    public void ResolveInsideAcceptsPlainNames()
    {
        using var dir = new TempDir();
        string full = SongNames.ResolveInside(dir.Path, "ok song.wav");
        Assert.Equal(Path.Combine(dir.Path, "ok song.wav"), full);
    }
}

public class SongLibraryTests
{
    private static void Touch(string dir, string name) => File.WriteAllBytes(Path.Combine(dir, name), new byte[] { 0 });

    [Fact]
    public void LoadsOnlyWavFilesSortedByLowerCaseStem()
    {
        using var dir = new TempDir();
        foreach (string n in new[] { "b.wav", "A.wav", "c.WAV", "a b.wav", "notes.txt", "d.mp3" })
        {
            Touch(dir.Path, n);
        }

        Directory.CreateDirectory(Path.Combine(dir.Path, "nested.wav")); // a folder, must be ignored

        IReadOnlyList<SongEntry> songs = new SongLibrary(dir.Path).Load();

        // "a" < "a b" (stem order, not file-name order where "a b.wav" < "a.wav")
        Assert.Equal(new[] { "A", "a b", "b", "c" }, songs.Select(s => s.Stem).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, songs.Select(s => s.Index).ToArray());
    }

    [Fact]
    public void MissingFolderIsEmptyNotAnError()
    {
        using var dir = new TempDir();
        Assert.Empty(new SongLibrary(Path.Combine(dir.Path, "nope")).Load());
    }

    [Fact]
    public void RenameKeepsFileInsideFolderAndReturnsNewStem()
    {
        using var dir = new TempDir();
        Touch(dir.Path, "old.wav");
        var lib = new SongLibrary(dir.Path);
        SongEntry song = lib.Load().Single();

        string stem = lib.Rename(song, "../../outside.wav");

        Assert.Equal(".._.._outside", stem);
        Assert.True(File.Exists(Path.Combine(dir.Path, ".._.._outside.wav")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "old.wav")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(dir.Path)!, "outside.wav")));
    }

    [Fact]
    public void RenameOntoExistingSongIsRefused()
    {
        using var dir = new TempDir();
        Touch(dir.Path, "one.wav");
        Touch(dir.Path, "two.wav");
        var lib = new SongLibrary(dir.Path);
        SongEntry one = lib.Load().First(s => s.Stem == "one");

        var ex = Assert.Throws<SongNameException>(() => lib.Rename(one, "two"));
        Assert.Equal("exists", ex.Message);
        Assert.True(File.Exists(Path.Combine(dir.Path, "one.wav")));
    }

    [Fact]
    public void RenameCanChangeOnlyTheCase()
    {
        using var dir = new TempDir();
        Touch(dir.Path, "song.wav");
        var lib = new SongLibrary(dir.Path);
        SongEntry s = lib.Load().Single();

        Assert.Equal("Song", lib.Rename(s, "Song"));
        Assert.Equal("Song", lib.Load().Single().Stem);
    }

    [Fact]
    public void DeleteRemovesOnlyThatFile()
    {
        using var dir = new TempDir();
        Touch(dir.Path, "keep.wav");
        Touch(dir.Path, "drop.wav");
        var lib = new SongLibrary(dir.Path);
        lib.Delete(lib.Load().First(s => s.Stem == "drop"));

        Assert.Equal(new[] { "keep" }, lib.Load().Select(s => s.Stem).ToArray());
    }

    [Fact]
    public void DeleteRefusesAForgedEntryOutsideTheFolder()
    {
        using var dir = new TempDir();
        string outside = Path.Combine(Path.GetDirectoryName(dir.Path)!, "victim-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(outside, new byte[] { 1 });
        try
        {
            var lib = new SongLibrary(dir.Path);
            var forged = new SongEntry(1, "x", "..\\" + Path.GetFileName(outside), outside);
            Assert.Throws<ArgumentException>(() => lib.Delete(forged));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }
}

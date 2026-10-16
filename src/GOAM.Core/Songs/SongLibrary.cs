namespace Goam.Core.Songs;

/// <summary>One song on disk. <see cref="Index"/> is 1-based and is the number used in-game.</summary>
public sealed record SongEntry(int Index, string Stem, string FileName, string FullPath);

public sealed class SongNameException : Exception
{
    public SongNameException(string message) : base(message)
    {
    }
}

/// <summary>The .wav files in the songs folder, in the order the game's numbering uses.</summary>
public sealed class SongLibrary
{
    public SongLibrary(string directory)
    {
        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }

    /// <summary>
    /// Lists *.wav (non-recursive) sorted by lower-cased stem, ordinal. One ordering for the UI and the
    /// generated .cfg so the numbers always agree (GOAM 1.x sorted the two lists differently).
    /// </summary>
    public IReadOnlyList<SongEntry> Load()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return Array.Empty<SongEntry>();
        }

        var files = new List<string>();
        foreach (string f in System.IO.Directory.EnumerateFiles(Directory, "*.wav", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetExtension(f), SongNames.Extension, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(f);
            }
        }

        files.Sort((a, b) =>
        {
            string sa = Path.GetFileNameWithoutExtension(a);
            string sb = Path.GetFileNameWithoutExtension(b);
            int c = string.CompareOrdinal(sa.ToLowerInvariant(), sb.ToLowerInvariant());
            return c != 0 ? c : string.CompareOrdinal(sa, sb);
        });

        var list = new List<SongEntry>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            string name = Path.GetFileName(files[i]);
            list.Add(new SongEntry(i + 1, Path.GetFileNameWithoutExtension(name), name, files[i]));
        }

        return list;
    }

    public void Delete(SongEntry song)
    {
        string path = SongNames.ResolveInside(Directory, song.FileName);
        File.Delete(path);
    }

    /// <summary>Renames a song; returns the new stem. Throws <see cref="SongNameException"/> on bad/duplicate names.</summary>
    public string Rename(SongEntry song, string requestedName)
    {
        if (!SongNames.TryNormalizeRename(requestedName, out string newStem))
        {
            throw new SongNameException("invalid");
        }

        string source = SongNames.ResolveInside(Directory, song.FileName);
        string target = SongNames.ResolveInside(Directory, newStem + SongNames.Extension);

        bool sameFile = string.Equals(source, target, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && File.Exists(target))
        {
            throw new SongNameException("exists");
        }

        if (!string.Equals(source, target, StringComparison.Ordinal))
        {
            File.Move(source, target);
        }

        return newStem;
    }
}

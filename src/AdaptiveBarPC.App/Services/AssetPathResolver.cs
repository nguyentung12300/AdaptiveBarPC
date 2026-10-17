using System.IO;

namespace AdaptiveBarPC.Services;

public sealed class AssetPathResolver
{
    private readonly string _root;

    public AssetPathResolver(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    public static AssetPathResolver ForProfilesDirectory(string profilesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesDirectory);
        var fullProfilesPath = Path.GetFullPath(profilesDirectory);
        return new AssetPathResolver(Path.GetDirectoryName(fullProfilesPath) ?? fullProfilesPath);
    }

    public string? ResolveExistingFile(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        try
        {
            var rooted = Path.IsPathRooted(reference);
            var resolved = Path.GetFullPath(rooted ? reference : Path.Combine(_root, reference));
            if (!rooted && !IsInsideRoot(resolved))
                return null;

            return File.Exists(resolved) ? resolved : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }

    private bool IsInsideRoot(string path)
    {
        if (path.Equals(_root, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}

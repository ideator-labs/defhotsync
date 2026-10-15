namespace DefHotSync;

/// <summary>
/// Folder of definition files named {Id}@{Version}.json or .yaml.
/// Example: HotOrder@1.json
/// </summary>
public sealed class FileDefinitionStore : IDefinitionStore
{
    private readonly string _folder;

    public FileDefinitionStore(string folder)
    {
        _folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(_folder);
    }

    public DefinitionRecord? TryGet(string id, int? version = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (version is int v)
            return ReadExact(id, v);

        DefinitionRecord? best = null;
        foreach (var path in Directory.EnumerateFiles(_folder))
        {
            var parsed = ParsePath(path);
            if (parsed is null)
                continue;
            var (fileId, fileVersion, format) = parsed.Value;
            if (!string.Equals(fileId, id, StringComparison.OrdinalIgnoreCase))
                continue;
            if (best is null || fileVersion > best.Version)
                best = new DefinitionRecord(fileId, fileVersion, format, File.ReadAllText(path));
        }
        return best;
    }

    private DefinitionRecord? ReadExact(string id, int version)
    {
        foreach (var format in new[] { "json", "yaml", "yml" })
        {
            var path = Path.Combine(_folder, $"{id}@{version}.{format}");
            if (!File.Exists(path))
                continue;
            var normalized = format == "yml" ? "yaml" : format;
            return new DefinitionRecord(id, version, normalized, File.ReadAllText(path));
        }
        return null;
    }

    private static (string Id, int Version, string Format)? ParsePath(string path)
    {
        var name = Path.GetFileName(path);
        var at = name.LastIndexOf('@');
        if (at <= 0)
            return null;
        var id = name[..at];
        var rest = name[(at + 1)..];
        var dot = rest.LastIndexOf('.');
        if (dot <= 0)
            return null;
        if (!int.TryParse(rest[..dot], out var version))
            return null;
        var ext = rest[(dot + 1)..].ToLowerInvariant();
        if (ext is not ("json" or "yaml" or "yml"))
            return null;
        var format = ext == "yml" ? "yaml" : ext;
        return (id, version, format);
    }
}

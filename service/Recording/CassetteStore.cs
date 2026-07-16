using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CmProjectX.Recording;

// A directory of cassette JSON files, indexed by "{METHOD} {url}". Replay reads
// from here; record writes to here. File names are a deterministic short hash of
// the key, so re-recording the same interaction overwrites the same file and git
// diffs stay clean.
public sealed class CassetteStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _dir;
    private readonly Dictionary<string, Cassette> _byKey = new();

    public CassetteStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
        Load();
    }

    public int Count => _byKey.Count;

    public static string Key(string method, string url) => $"{method.ToUpperInvariant()} {url}";

    public Cassette? TryGet(string method, string url) =>
        _byKey.TryGetValue(Key(method, url), out var c) ? c : null;

    public void Save(Cassette cassette)
    {
        _byKey[Key(cassette.Request.Method, cassette.Request.Url)] = cassette;
        File.WriteAllText(
            Path.Combine(_dir, FileName(cassette.Request)),
            JsonSerializer.Serialize(cassette, Json));
    }

    private void Load()
    {
        _byKey.Clear();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            var cassette = JsonSerializer.Deserialize<Cassette>(File.ReadAllText(file), Json);
            if (cassette is not null)
                _byKey[Key(cassette.Request.Method, cassette.Request.Url)] = cassette;
        }
    }

    private static string FileName(CassetteRequest req)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Key(req.Method, req.Url))))[..12].ToLowerInvariant();
        return $"{req.Method.ToLowerInvariant()}-{hash}.json";
    }
}

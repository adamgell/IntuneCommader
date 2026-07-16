using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Kiota.Serialization.Json;

namespace CmProjectX.Api;

// Shared JSON <-> Graph-model helpers for the per-resource CRUD endpoint modules.
// Kept in its own file (which freely uses Microsoft.Graph.Beta.Models via the
// modules) so Graph type names never appear in Program.cs — a Graph `using` there
// shadows IResult and breaks Minimal API lambda overload resolution.
//
// Graph beta models are Kiota IParsable, so KiotaJsonSerializer round-trips them
// (and resolves polymorphic @odata.type subtypes on the way in).
internal static class CrudJson
{
    /// Read the request body JSON into a typed, write-ready Graph model.
    public static async Task<T> ReadModelAsync<T>(HttpRequest req, CancellationToken ct)
        where T : IParsable, new()
    {
        using var reader = new StreamReader(req.Body);
        var json = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Empty {typeof(T).Name} request body");
        var model = await KiotaJsonSerializer.DeserializeAsync<T>(json, ct)
            ?? throw new InvalidOperationException($"Could not parse {typeof(T).Name} from body");
        MakeWriteReady(model);
        return model;
    }

    /// Make a freshly-deserialized Graph model serialize in FULL on Post/PatchAsync.
    ///
    /// A model deserialized through the Graph backing-store parse factory is marked
    /// "initialized with no changes", so the backing-store serialization proxy used by
    /// Post/PatchAsync emits ONLY changed values — i.e. nothing, collapsing the request
    /// body to an abstract/empty object. Graph then rejects polymorphic creates with
    /// "Cannot create an abstract class" (and PATCHes silently become no-ops). Resetting
    /// the backing store marks every populated value as a change so the full object
    /// (incl. the @odata.type discriminator) is sent — the inbound mirror of ToJson's
    /// outbound flip. Safe to call on any IParsable (no-op when not backed).
    public static void MakeWriteReady(IParsable model)
    {
        if (model is IBackedModel backed && backed.BackingStore is not null)
        {
            backed.BackingStore.ReturnOnlyChangedValues = false;
            backed.BackingStore.InitializationCompleted = false;
        }
    }

    /// Serialize a Graph model back to a JSON string (for GET detail).
    /// Graph models fetched from Graph use a backing store that, by default, only
    /// emits values changed *after* the GET — which is none, yielding "{}". Flip
    /// that off (recursively, best-effort) so the editor sees the real object.
    public static string ToJson<T>(T model) where T : IParsable
    {
        if (model is IBackedModel backed && backed.BackingStore is not null)
        {
            backed.BackingStore.ReturnOnlyChangedValues = false;
            backed.BackingStore.InitializationCompleted = false;
        }
        using var writer = new JsonSerializationWriter();
        writer.WriteObjectValue(null, model);
        using var content = writer.GetSerializedContent();
        using var reader = new StreamReader(content);
        return reader.ReadToEnd();
    }
}

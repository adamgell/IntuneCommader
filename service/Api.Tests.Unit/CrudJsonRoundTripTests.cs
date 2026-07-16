using System.Text.Json;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Kiota.Serialization.Json;
using Xunit;

namespace CmProjectX.Tests.Unit;

// Reproduction + regression for the create-endpoint @odata.type drop: a CREATE body
// for a polymorphic Graph type (DeviceConfiguration → windows10GeneralConfiguration)
// must round-trip through the deserialize-then-serialize path the CRUD POST uses
// WITHOUT losing the @odata.type discriminator. If it does, Graph rejects the POST
// with "Cannot create an abstract class".
public class CrudJsonRoundTripTests
{
    // The live sidecar registers the Kiota JSON factories when it builds the
    // GraphServiceClient; do the same here so DeserializeAsync has a factory.
    static CrudJsonRoundTripTests()
    {
        ParseNodeFactoryRegistry.DefaultInstance.ContentTypeAssociatedFactories
            .TryAdd("application/json", new JsonParseNodeFactory());
        SerializationWriterFactoryRegistry.DefaultInstance.ContentTypeAssociatedFactories
            .TryAdd("application/json", new JsonSerializationWriterFactory());
    }

    private const string CreateBody =
        """{"@odata.type":"#microsoft.graph.windows10GeneralConfiguration","displayName":"zzz-cmpx-test","description":"y"}""";

    // Serialize a model the way Graph's request builder PostAsync does — straight
    // through the JSON serialization writer, with NO backing-store flag flip (that
    // flip is only in CrudJson.ToJson for the GET-detail path).
    private static string SerializeForPost(IParsable model)
    {
        using var writer = new JsonSerializationWriter();
        writer.WriteObjectValue(null, model);
        using var content = writer.GetSerializedContent();
        using var reader = new StreamReader(content);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task Deserialize_ResolvesDerivedSubtype()
    {
        var model = await KiotaJsonSerializer.DeserializeAsync<DeviceConfiguration>(CreateBody);
        Assert.NotNull(model);
        Assert.IsType<Windows10GeneralConfiguration>(model);
        Assert.Equal("#microsoft.graph.windows10GeneralConfiguration", model!.OdataType);
    }

    // The live path, reproduced: a model deserialized through the Graph SDK's
    // backing-store parse node factory is "initialized with no changes", so the
    // Post/PatchAsync backing-store proxy (ReturnOnlyChangedValues) serializes an
    // EMPTY body — dropping @odata.type and triggering "Cannot create an abstract
    // class". CrudJson.ReadModelAsync's MakeWriteReady flip (InitializationCompleted
    // = false) is what restores the full body. This pins both the bug and the fix.
    [Fact]
    public async Task BackingStoreModel_NeedsInitReset_ToSurviveChangedOnlySerialization()
    {
        var bsFactory = new BackingStoreParseNodeFactory(new JsonParseNodeFactory());
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(CreateBody));
        var node = await bsFactory.GetRootParseNodeAsync("application/json", stream);
        var model = node.GetObjectValue(DeviceConfiguration.CreateFromDiscriminatorValue);
        Assert.NotNull(model);
        var backed = (IBackedModel)model!;

        backed.BackingStore.ReturnOnlyChangedValues = true; // what PostAsync's proxy does
        var withoutFix = SerializeForPost(model!);
        Assert.DoesNotContain("@odata.type", withoutFix); // the bug: discriminator dropped

        backed.BackingStore.InitializationCompleted = false; // the fix (ReadModelAsync.MakeWriteReady)
        var withFix = SerializeForPost(model!);
        using var doc = JsonDocument.Parse(withFix);
        Assert.True(doc.RootElement.TryGetProperty("@odata.type", out var t),
            $"fix failed — @odata.type still dropped.\nbody: {withFix}");
        Assert.Equal("#microsoft.graph.windows10GeneralConfiguration", t.GetString());
    }
}

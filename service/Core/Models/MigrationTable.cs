using System.Text.Json.Serialization;

namespace Intune.Commander.Core.Models;

public class MigrationTable
{
    [JsonPropertyName("entries")]
    public List<MigrationEntry> Entries { get; set; } = [];

    public void AddOrUpdate(MigrationEntry entry)
    {
        var existing = Entries.FirstOrDefault(e =>
            e.ObjectType == entry.ObjectType && e.OriginalId == entry.OriginalId);

        if (existing != null)
        {
            existing.NewId = entry.NewId;
            existing.Name = entry.Name;
        }
        else
        {
            Entries.Add(entry);
        }
    }
}

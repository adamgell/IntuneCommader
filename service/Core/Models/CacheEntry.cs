namespace Intune.Commander.Core.Models;

/// <summary>
/// Represents a cached data entry stored in the LiteDB cache database.
/// Each entry stores a JSON blob of Graph data, scoped by tenant and data type.
/// </summary>
public class CacheEntry
{
    /// <summary>
    /// Composite key: "{TenantId}:{DataType}"
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Azure AD tenant ID that owns this data.
    /// </summary>
    public string TenantId { get; set; } = "";

    /// <summary>
    /// The type of data cached (e.g., "DeviceConfigurations", "CompliancePolicies").
    /// </summary>
    public string DataType { get; set; } = "";

    /// <summary>
    /// Serialized JSON blob of the cached data.
    /// </summary>
    public string JsonData { get; set; } = "";

    /// <summary>
    /// When this entry was cached (UTC).
    /// </summary>
    public DateTime CachedAtUtc { get; set; }

    /// <summary>
    /// When this entry expires (UTC).
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Number of items in the cached collection, for logging/debugging.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// When greater than zero, this entry is a chunked manifest.
    /// The actual data is stored in sibling documents with IDs
    /// "{Id}__chunk_0", "{Id}__chunk_1", etc.
    /// <c>JsonData</c> is empty for manifest entries.
    /// </summary>
    public int ChunkCount { get; set; }
}

namespace CmProjectX.Api;

// Shared helpers for projecting Graph element types into the normalized ListItemDto,
// so every list endpoint resolves the optional rich-list columns the same way.
internal static class ListProjection
{
    // Best-effort platform label from a Graph @odata.type (e.g.
    // "#microsoft.graph.windows10CompliancePolicy" -> "Windows"). Null when unknown,
    // so the client only shows a Platform column/chips for surfaces that resolve one.
    public static string? PlatformOf(string? odataType)
    {
        if (string.IsNullOrEmpty(odataType)) return null;
        var t = odataType.ToLowerInvariant();
        if (t.Contains("aosp")) return "Android (AOSP)";
        if (t.Contains("android")) return "Android";
        if (t.Contains("ios")) return "iOS";
        if (t.Contains("macos") || t.Contains("mac")) return "macOS";
        if (t.Contains("windows") || t.Contains("win32")) return "Windows";
        if (t.Contains("linux")) return "Linux";
        return null;
    }
}

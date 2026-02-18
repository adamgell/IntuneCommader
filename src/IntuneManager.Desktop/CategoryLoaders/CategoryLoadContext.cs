using System;
using IntuneManager.Core.Services;

namespace IntuneManager.Desktop.CategoryLoaders;

/// <summary>
/// Captures the VM-level services and callbacks that
/// <see cref="CategoryLoadHelper.ExecuteAsync{T}"/> needs to:
/// <list type="bullet">
///   <item>set busy / status / error state on the VM</item>
///   <item>write to the cache after network fetch</item>
///   <item>trigger the active filter after loading</item>
///   <item>format exceptions for display, with special handling for ODataError</item>
/// </list>
/// Cache reads are NOT handled by the helper — they occur separately via
/// <c>TryLoadLazyCacheEntry</c> in the <c>SelectedNavCategory</c> setter.
/// </summary>
/// <param name="CacheService">Shared encrypted cache.</param>
/// <param name="TenantId">Active tenant ID used as the cache partition key; null disables caching.</param>
/// <param name="SetStatus">Updates <c>StatusText</c> on the VM.</param>
/// <param name="SetError">Calls <c>SetError(message)</c> on the VM.</param>
/// <param name="ApplyFilter">Triggers the VM's active search/filter after data loads.</param>
/// <param name="SetBusy">Toggles <c>IsBusy</c> on the VM.</param>
/// <param name="FormatError">
/// Formats exceptions for display. Implementations should provide special handling for
/// <c>ODataError</c> to extract HTTP status codes, error codes, and structured messages.
/// </param>
public sealed record CategoryLoadContext(
    ICacheService CacheService,
    string? TenantId,
    Action<string> SetStatus,
    Action<string> SetError,
    Action ApplyFilter,
    Action<bool> SetBusy,
    Func<Exception, string> FormatError);

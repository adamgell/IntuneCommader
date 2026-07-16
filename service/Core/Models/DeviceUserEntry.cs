using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Models;

public sealed record DeviceUserEntry
{
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public string UserId { get; init; } = "";
    public string UserDisplayName { get; init; } = "";
    public string UserPrincipalName { get; init; } = "";
    public string Department { get; init; } = "";
    public string JobTitle { get; init; } = "";
    public string OfficeLocation { get; init; } = "";
    public string UsageLocation { get; init; } = "";
    public string OnPremisesSamAccountName { get; init; } = "";
    public string ExtensionAttribute1 { get; init; } = "";
    public string ExtensionAttribute2 { get; init; } = "";
    public string ExtensionAttribute3 { get; init; } = "";
    public string ExtensionAttribute4 { get; init; } = "";
    public string ExtensionAttribute5 { get; init; } = "";
    public string ExtensionAttribute6 { get; init; } = "";
    public string ExtensionAttribute7 { get; init; } = "";
    public string ExtensionAttribute8 { get; init; } = "";
    public string ExtensionAttribute9 { get; init; } = "";
    public string ExtensionAttribute10 { get; init; } = "";
    public string ExtensionAttribute11 { get; init; } = "";
    public string ExtensionAttribute12 { get; init; } = "";
    public string ExtensionAttribute13 { get; init; } = "";
    public string ExtensionAttribute14 { get; init; } = "";
    public string ExtensionAttribute15 { get; init; } = "";
    public string AccountEnabled { get; init; } = "";
    public string AssignedLicenseCount { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public string ComplianceState { get; init; } = "";
    public string DeviceModel { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string DeviceCategory { get; init; } = "";
    public string Ownership { get; init; } = "";

    public static DeviceUserEntry From(ManagedDevice device, User? user)
    {
        var ext = user?.OnPremisesExtensionAttributes;

        return new DeviceUserEntry
        {
            DeviceId = device.Id ?? "",
            DeviceName = device.DeviceName ?? "",
            UserId = device.UserId ?? "",
            UserDisplayName = user?.DisplayName ?? "",
            UserPrincipalName = user?.UserPrincipalName ?? "",
            Department = user?.Department ?? "",
            JobTitle = user?.JobTitle ?? "",
            OfficeLocation = user?.OfficeLocation ?? "",
            UsageLocation = user?.UsageLocation ?? "",
            OnPremisesSamAccountName = user?.OnPremisesSamAccountName ?? "",
            ExtensionAttribute1 = ext?.ExtensionAttribute1 ?? "",
            ExtensionAttribute2 = ext?.ExtensionAttribute2 ?? "",
            ExtensionAttribute3 = ext?.ExtensionAttribute3 ?? "",
            ExtensionAttribute4 = ext?.ExtensionAttribute4 ?? "",
            ExtensionAttribute5 = ext?.ExtensionAttribute5 ?? "",
            ExtensionAttribute6 = ext?.ExtensionAttribute6 ?? "",
            ExtensionAttribute7 = ext?.ExtensionAttribute7 ?? "",
            ExtensionAttribute8 = ext?.ExtensionAttribute8 ?? "",
            ExtensionAttribute9 = ext?.ExtensionAttribute9 ?? "",
            ExtensionAttribute10 = ext?.ExtensionAttribute10 ?? "",
            ExtensionAttribute11 = ext?.ExtensionAttribute11 ?? "",
            ExtensionAttribute12 = ext?.ExtensionAttribute12 ?? "",
            ExtensionAttribute13 = ext?.ExtensionAttribute13 ?? "",
            ExtensionAttribute14 = ext?.ExtensionAttribute14 ?? "",
            ExtensionAttribute15 = ext?.ExtensionAttribute15 ?? "",
            AccountEnabled = user?.AccountEnabled?.ToString() ?? "",
            AssignedLicenseCount = (user?.AssignedLicenses?.Count ?? 0).ToString(),
            OperatingSystem = device.OperatingSystem ?? "",
            OsVersion = device.OsVersion ?? "",
            ComplianceState = device.ComplianceState?.ToString() ?? "",
            DeviceModel = device.Model ?? "",
            Manufacturer = device.Manufacturer ?? "",
            SerialNumber = device.SerialNumber ?? "",
            DeviceCategory = device.DeviceCategoryDisplayName ?? "",
            Ownership = device.ManagedDeviceOwnerType?.ToString() ?? ""
        };
    }
}

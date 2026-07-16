using Intune.Commander.Core.Services.CaPptExport;
using Microsoft.Graph.Beta.Models;
using SyncPresentation = Syncfusion.Presentation;

namespace Intune.Commander.Core.Services;

/// <summary>
/// Service for exporting Conditional Access policies to PowerPoint format.
/// Generates one slide per policy using the embedded PolicyTemplate.pptx /
/// PolicyTemplateImage.pptx templates, populating named shapes via PowerPointHelper.
/// Resolves directory object GUIDs (users, groups, roles, apps) to display names.
/// </summary>
public class ConditionalAccessPptExportService : IConditionalAccessPptExportService
{
    private readonly IConditionalAccessPolicyService _caPolicyService;
    private readonly INamedLocationService _namedLocationService;
    private readonly IAuthenticationStrengthService _authStrengthService;
    private readonly IAuthenticationContextService _authContextService;
    private readonly IApplicationService _applicationService;
    private readonly IDirectoryObjectResolver? _resolver;
    private readonly ITermsOfUseService? _termsOfUseService;

    public ConditionalAccessPptExportService(
        IConditionalAccessPolicyService caPolicyService,
        INamedLocationService namedLocationService,
        IAuthenticationStrengthService authStrengthService,
        IAuthenticationContextService authContextService,
        IApplicationService applicationService,
        IDirectoryObjectResolver? resolver = null,
        ITermsOfUseService? termsOfUseService = null)
    {
        _caPolicyService = caPolicyService;
        _namedLocationService = namedLocationService;
        _authStrengthService = authStrengthService;
        _authContextService = authContextService;
        _applicationService = applicationService;
        _resolver = resolver;
        _termsOfUseService = termsOfUseService;
    }

    public async Task ExportAsync(
        string outputPath,
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be null, empty, or whitespace.", nameof(outputPath));

        if (string.IsNullOrWhiteSpace(tenantName))
            throw new ArgumentException("Tenant name must not be null, empty, or whitespace.", nameof(tenantName));

        var policies = await _caPolicyService.ListPoliciesAsync(cancellationToken);

        // Batch-resolve all directory object GUIDs across all policies
        var nameLookup = await ResolveAllDirectoryObjectsAsync(policies, cancellationToken);

        // Load both template variants from embedded resources
        // Template structure: Slides[0] = cover page, Slides[1] = per-policy detail template
        var templateBytes = LoadEmbeddedTemplateBytes("PolicyTemplate.pptx");
        var templateImageBytes = LoadEmbeddedTemplateBytes("PolicyTemplateImage.pptx");

        // Open PolicyTemplate.pptx as the output presentation directly
        using var outputMs = new MemoryStream(templateBytes);
        using var presentation = SyncPresentation.Presentation.Open(outputMs);
        var coverSlide = presentation.Slides[0];
        var templateSlide = presentation.Slides[1];

        // Populate the cover slide with tenant info
        var coverHelper = new PowerPointHelper(coverSlide);
        coverHelper.SetText(Shape.TenantName, tenantName);
        coverHelper.SetText(Shape.GenerationDate, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC");
        coverHelper.SetText(Shape.GeneratedBy, "Intune Commander");

        // Open image template for policies that use image-based access type slides
        using var imageMs = new MemoryStream(templateImageBytes);
        using var imagePresentation = SyncPresentation.Presentation.Open(imageMs);
        var templateImageSlide = imagePresentation.Slides[1];

        // Add one cloned detail slide per policy (matching idPowerToys reference approach)
        foreach (var policy in policies.OrderBy(p => p.DisplayName))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var appAction = new AssignedCloudAppAction(policy, nameLookup);
            bool useImageTemplate = appAction.IsSelectedAppO365Only
                || appAction.AccessType == AppAccessType.UserActionsRegSecInfo
                || appAction.AccessType == AppAccessType.UserActionsRegDevice;

            var sourceSlide = useImageTemplate ? templateImageSlide : templateSlide;
            var clonedSlide = sourceSlide.Clone();
            presentation.Slides.Add(clonedSlide);

            var slide = presentation.Slides[presentation.Slides.Count - 1];
            var pptHelper = new PowerPointHelper(slide);
            PopulateSlide(pptHelper, policy, tenantName, appAction, nameLookup);
        }

        // Remove the original per-policy template slide (at index 1; cover stays at index 0)
        presentation.Slides.Remove(templateSlide);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var fileStream = File.Create(outputPath);
        presentation.Save(fileStream);
    }

    private static void PopulateSlide(
        PowerPointHelper ppt,
        ConditionalAccessPolicy policy,
        string tenantName,
        AssignedCloudAppAction appAction,
        IReadOnlyDictionary<string, string> nameLookup)
    {
        // ── Header ──────────────────────────────────────────────────────────────
        ppt.SetText(Shape.PolicyName, policy.DisplayName);
        ppt.SetText(Shape.TenantName, tenantName);
        ppt.SetText(Shape.GenerationDate, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC");
        ppt.SetText(Shape.LastModified, policy.ModifiedDateTime?.ToString("yyyy-MM-dd") ?? "N/A");

        // ── Policy state ─────────────────────────────────────────────────────────
        ppt.Show(policy.State == ConditionalAccessPolicyState.Enabled, Shape.StateEnabled);
        ppt.Show(policy.State == ConditionalAccessPolicyState.Disabled, Shape.StateDisabled);
        ppt.Show(policy.State == ConditionalAccessPolicyState.EnabledForReportingButNotEnforced, Shape.StateReportOnly);

        // ── Users / workload identity ─────────────────────────────────────────
        var userWorkload = new AssignedUserWorkload(policy, nameLookup);
        ppt.SetText(Shape.UserWorkload, userWorkload.Name);
        ppt.SetTextFormatted(Shape.UserWorkloadIncExc, userWorkload.IncludeExclude);
        ppt.Show(userWorkload.IsWorkload, Shape.IconWorkloadIdentity);
        ppt.Show(!userWorkload.IsWorkload, Shape.IconAssignUser, Shape.IconGroupIdentity);
        ppt.Show(userWorkload.HasIncludeRoles, Shape.IconAssignedToRole);
        ppt.Show(userWorkload.HasIncludeExternalUser || userWorkload.HasIncludeExternalTenant, Shape.IconAssignedToGuest);

        // ── Cloud app / action ────────────────────────────────────────────────
        ppt.SetText(Shape.CloudAppAction, appAction.Name);
        ppt.SetTextFormatted(Shape.CloudAppActionIncExc, appAction.IncludeExclude);
        ppt.Show(appAction.AccessType == AppAccessType.AppsAll, Shape.AllCloudApps, Shape.IconAccessAllCloudApps);
        ppt.Show(appAction.AccessType == AppAccessType.AppsSelected
            && !appAction.IsSelectedAppO365Only
            && !appAction.IsSelectedMicrosoftAdminPortalsOnly, Shape.IconAccessSelectedCloudApps);
        ppt.Show(appAction.AccessType == AppAccessType.AuthenticationContext, Shape.IconAccessAuthenticationContext);
        ppt.Show(appAction.AccessType == AppAccessType.AppsNone, Shape.IconAccessAzureAD);
        ppt.Show(appAction.AccessType == AppAccessType.UserActionsRegSecInfo,
            Shape.IconAccessMySecurityInfo, Shape.PicAccessSecurityInfo);
        ppt.Show(appAction.AccessType == AppAccessType.UserActionsRegDevice,
            Shape.IconAccessRegisterOrJoinDevice, Shape.PicAccessRegisterDevice);
        ppt.Show(appAction.IsSelectedAppO365Only, Shape.IconAccessOffice365, Shape.PicAccessOffice365);
        ppt.Show(appAction.IsSelectedMicrosoftAdminPortalsOnly, Shape.IconMicrosoftAdminPortal);

        // ── Conditions: platforms ─────────────────────────────────────────────
        var platforms = new ConditionPlatforms(policy);
        ppt.Show(platforms.HasData, Shape.Platforms, Shape.IconPlatforms, Shape.ShadeDevicePlatforms);
        if (platforms.HasData)
            ppt.SetTextFormatted(Shape.Platforms, platforms.IncludeExclude);

        // ── Conditions: client app types ──────────────────────────────────────
        var clientAppTypes = new ConditionClientAppTypes(policy);
        ppt.Show(clientAppTypes.HasData, Shape.ClientAppTypes, Shape.IconClientAppTypes, Shape.ShadeClientApps);
        if (clientAppTypes.HasData)
            ppt.SetTextFormatted(Shape.ClientAppTypes, clientAppTypes.IncludeExclude);

        // ── Conditions: locations ─────────────────────────────────────────────
        var locations = new ConditionLocations(policy, nameLookup);
        ppt.Show(locations.HasData, Shape.Locations, Shape.IconLocations, Shape.ShadeLocations);
        if (locations.HasData)
            ppt.SetTextFormatted(Shape.Locations, locations.IncludeExclude);

        // ── Conditions: risk levels ───────────────────────────────────────────
        var risks = new ConditionRisks(policy);
        ppt.Show(risks.HasData, Shape.Risks, Shape.IconRiskyUsers, Shape.ShadeRisk);
        if (risks.HasData)
            ppt.SetTextFormatted(Shape.Risks, risks.IncludeExclude);

        // ── Conditions: device filters ────────────────────────────────────────
        var deviceFilters = new ConditionDeviceFilters(policy);
        ppt.Show(deviceFilters.HasData, Shape.DeviceFilters, Shape.IconDeviceFilters, Shape.ShadeFilterForDevices);
        if (deviceFilters.HasData)
            ppt.SetTextFormatted(Shape.DeviceFilters, deviceFilters.IncludeExclude);

        // ── Grant / block controls ────────────────────────────────────────────
        var grantBlock = new ControlGrantBlock(policy, nameLookup);
        ppt.Show(grantBlock.IsGrant, Shape.GrantLabelGrantAccess, Shape.IconGrantAccess);
        ppt.Show(!grantBlock.IsGrant, Shape.GrantLabelBlockAccess, Shape.IconBlockAccess);

        if (grantBlock.IsGrant)
        {
            // Require label only relevant when multiple controls with AND operator
            ppt.Show(grantBlock.GrantControlsCount > 1 && grantBlock.IsGrantRequireAll, Shape.GrantRequireLabel);
            ppt.Show(grantBlock.Mfa,
                Shape.IconGrantMultifactorAuthentication, Shape.IconGrantMultifactorAuthenticationLabel, Shape.ShadeGrantMultifactorAuth);
            ppt.Show(grantBlock.CompliantDevice,
                Shape.IconGrantDeviceCompliance, Shape.IconGrantDeviceComplianceLabel, Shape.ShadeGrantCompliantDevice);
            ppt.Show(grantBlock.DomainJoinedDevice,
                Shape.IconGrantHybridJoined, Shape.IconGrantHybridJoinedLabel, Shape.ShadeGrantHybridAzureADJoined);
            ppt.Show(grantBlock.ApprovedApplication,
                Shape.IconGrantApprovedClientApp, Shape.IconGrantApprovedClientAppLabel, Shape.ShadeGrantApprovedClientApp);
            ppt.Show(grantBlock.CompliantApplication,
                Shape.IconGrantAppProtection, Shape.IconGrantAppProtectionLabel, Shape.ShadeGrantAppProtectionPolicy);
            ppt.Show(grantBlock.PasswordChange,
                Shape.IconGrantChangePassword, Shape.IconGrantChangePasswordLabel, Shape.ShadeGrantChangePassword);
            ppt.Show(grantBlock.TermsOfUse,
                Shape.IconGrantTermsOfUse, Shape.IconGrantTermsOfUseLabel, Shape.ShadeGrantTermsOfUse);
            ppt.Show(grantBlock.CustomAuthenticationFactor,
                Shape.IconGrantCustomAuth, Shape.IconGrantCustomAuthLabel, Shape.ShadeGrantCustomAuthFactor);
            ppt.Show(grantBlock.AuthenticationStrength,
                Shape.IconGrantAuthenticationStrength, Shape.IconGrantAuthenticationStrengthLabel, Shape.ShadeGrantAuthStrength);
        }
        else
        {
            // Block access: hide all grant-specific control shapes
            ppt.Show(false,
                Shape.GrantRequireLabel,
                Shape.IconGrantMultifactorAuthentication, Shape.IconGrantMultifactorAuthenticationLabel, Shape.ShadeGrantMultifactorAuth,
                Shape.IconGrantDeviceCompliance, Shape.IconGrantDeviceComplianceLabel, Shape.ShadeGrantCompliantDevice,
                Shape.IconGrantHybridJoined, Shape.IconGrantHybridJoinedLabel, Shape.ShadeGrantHybridAzureADJoined,
                Shape.IconGrantApprovedClientApp, Shape.IconGrantApprovedClientAppLabel, Shape.ShadeGrantApprovedClientApp,
                Shape.IconGrantAppProtection, Shape.IconGrantAppProtectionLabel, Shape.ShadeGrantAppProtectionPolicy,
                Shape.IconGrantChangePassword, Shape.IconGrantChangePasswordLabel, Shape.ShadeGrantChangePassword,
                Shape.IconGrantTermsOfUse, Shape.IconGrantTermsOfUseLabel, Shape.ShadeGrantTermsOfUse,
                Shape.IconGrantCustomAuth, Shape.IconGrantCustomAuthLabel, Shape.ShadeGrantCustomAuthFactor,
                Shape.IconGrantAuthenticationStrength, Shape.IconGrantAuthenticationStrengthLabel, Shape.ShadeGrantAuthStrength);
        }

        // ── Session controls ──────────────────────────────────────────────────
        var session = new ControlSession(policy);
        ppt.Show(session.UseAppEnforcedRestrictions, Shape.ShadeSessionAppEnforced);
        ppt.Show(session.UseConditionalAccessAppControl, Shape.SessionCasType, Shape.ShadeSessionCas);
        if (session.UseConditionalAccessAppControl)
            ppt.SetText(Shape.SessionCasType, session.CloudAppSecurityType);
        ppt.Show(session.SignInFrequency, Shape.SessionSifInterval, Shape.ShadeSessionSif);
        if (session.SignInFrequency)
            ppt.SetText(Shape.SessionSifInterval, session.SignInFrequencyIntervalLabel);
        ppt.Show(session.PersistentBrowserSession, Shape.SessionPersistenBrowserMode, Shape.ShadeSessionPersistentBrowser);
        if (session.PersistentBrowserSession)
            ppt.SetText(Shape.SessionPersistenBrowserMode, session.PersistentBrowserSessionModeLabel);
        ppt.Show(session.ContinuousAccessEvaluation, Shape.SessionCaeMode, Shape.ShadeSessionCae);
        if (session.ContinuousAccessEvaluation)
            ppt.SetText(Shape.SessionCaeMode, session.ContinuousAccessEvaluationModeLabel);
        ppt.Show(session.DisableResilienceDefaults, Shape.ShadeSessionDisableResilience);
        ppt.Show(session.SecureSignInSession, Shape.ShadeSessionSecureSignIn);
        ppt.Show(session.ContinuousAccessEvaluation, Shape.IconSessionCaeDisable);
    }

    /// <summary>
    /// Collects all GUIDs referenced across all policies, categorises them by type,
    /// and resolves each category via the appropriate service. Named location IDs,
    /// auth context IDs, auth strength IDs, and terms-of-use IDs are NOT sent to
    /// the directory object resolver — they are resolved via their own services.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveAllDirectoryObjectsAsync(
        List<ConditionalAccessPolicy> policies,
        CancellationToken cancellationToken)
    {
        // ── Collect IDs by category ──────────────────────────────────────────
        var directoryObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var locationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var authContextIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var termsOfUseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasAuthStrength = false;

        foreach (var policy in policies)
        {
            var conditions = policy.Conditions;
            if (conditions == null) continue;

            // Users: include/exclude users, groups, roles → directory objects
            var users = conditions.Users;
            if (users != null)
            {
                AddAll(directoryObjectIds, users.IncludeUsers);
                AddAll(directoryObjectIds, users.ExcludeUsers);
                AddAll(directoryObjectIds, users.IncludeGroups);
                AddAll(directoryObjectIds, users.ExcludeGroups);
                AddAll(directoryObjectIds, users.IncludeRoles);
                AddAll(directoryObjectIds, users.ExcludeRoles);
            }

            // Workload identities: service principals → directory objects
            var clientApps = conditions.ClientApplications;
            if (clientApps != null)
            {
                AddAll(directoryObjectIds, clientApps.IncludeServicePrincipals);
                AddAll(directoryObjectIds, clientApps.ExcludeServicePrincipals);
            }

            // Applications: include/exclude app IDs → directory objects
            var apps = conditions.Applications;
            if (apps != null)
            {
                AddAll(directoryObjectIds, apps.IncludeApplications);
                AddAll(directoryObjectIds, apps.ExcludeApplications);

                // Auth context class references → resolved via auth context service
                AddAll(authContextIds, apps.IncludeAuthenticationContextClassReferences);
            }

            // Locations: named location IDs → resolved via named location service
            var locs = conditions.Locations;
            if (locs != null)
            {
                AddAll(locationIds, locs.IncludeLocations);
                AddAll(locationIds, locs.ExcludeLocations);
            }

            // Grant controls: terms of use and auth strength
            var grant = policy.GrantControls;
            if (grant != null)
            {
                AddAll(termsOfUseIds, grant.TermsOfUse);
                if (grant.AuthenticationStrength != null)
                    hasAuthStrength = true;
            }
        }

        // Remove well-known sentinel values that aren't real GUIDs
        directoryObjectIds.Remove(string.Empty);
        locationIds.Remove(string.Empty);
        authContextIds.Remove(string.Empty);
        termsOfUseIds.Remove(string.Empty);

        // ── Resolve each category in parallel ────────────────────────────────
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Directory objects
        var directoryTask = (directoryObjectIds.Count > 0 && _resolver != null)
            ? _resolver.ResolveAsync(directoryObjectIds, cancellationToken)
            : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        // Named locations
        var locationTask = locationIds.Count > 0
            ? ResolveNamedLocationsAsync(cancellationToken)
            : Task.FromResult<Dictionary<string, string>>(new Dictionary<string, string>());

        // Auth strengths
        var authStrengthTask = hasAuthStrength
            ? ResolveAuthStrengthsAsync(cancellationToken)
            : Task.FromResult<Dictionary<string, string>>(new Dictionary<string, string>());

        // Auth contexts
        var authContextTask = authContextIds.Count > 0
            ? ResolveAuthContextsAsync(cancellationToken)
            : Task.FromResult<Dictionary<string, string>>(new Dictionary<string, string>());

        // Terms of use
        var termsOfUseTask = termsOfUseIds.Count > 0 && _termsOfUseService != null
            ? ResolveTermsOfUseAsync(cancellationToken)
            : Task.FromResult<Dictionary<string, string>>(new Dictionary<string, string>());

        await Task.WhenAll(directoryTask, locationTask, authStrengthTask, authContextTask, termsOfUseTask);

        // Merge — directory objects first, then supplementary (TryAdd prevents overwrites)
        foreach (var kvp in await directoryTask)
            result.TryAdd(kvp.Key, kvp.Value);
        foreach (var kvp in await locationTask)
            result.TryAdd(kvp.Key, kvp.Value);
        foreach (var kvp in await authStrengthTask)
            result.TryAdd(kvp.Key, kvp.Value);
        foreach (var kvp in await authContextTask)
            result.TryAdd(kvp.Key, kvp.Value);
        foreach (var kvp in await termsOfUseTask)
            result.TryAdd(kvp.Key, kvp.Value);

        return result;
    }

    private async Task<Dictionary<string, string>> ResolveNamedLocationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var locations = await _namedLocationService.ListNamedLocationsAsync(cancellationToken);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var loc in locations)
            {
                if (!string.IsNullOrEmpty(loc.Id) && !string.IsNullOrEmpty(loc.DisplayName))
                    map.TryAdd(loc.Id, loc.DisplayName);
            }
            return map;
        }
        catch { return new Dictionary<string, string>(); }
    }

    private async Task<Dictionary<string, string>> ResolveAuthStrengthsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var strengths = await _authStrengthService.ListAuthenticationStrengthPoliciesAsync(cancellationToken);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in strengths)
            {
                if (!string.IsNullOrEmpty(s.Id) && !string.IsNullOrEmpty(s.DisplayName))
                    map.TryAdd(s.Id, s.DisplayName);
            }
            return map;
        }
        catch { return new Dictionary<string, string>(); }
    }

    private async Task<Dictionary<string, string>> ResolveAuthContextsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var contexts = await _authContextService.ListAuthenticationContextsAsync(cancellationToken);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ctx in contexts)
            {
                if (!string.IsNullOrEmpty(ctx.Id) && !string.IsNullOrEmpty(ctx.DisplayName))
                    map.TryAdd(ctx.Id, ctx.DisplayName);
            }
            return map;
        }
        catch { return new Dictionary<string, string>(); }
    }

    private async Task<Dictionary<string, string>> ResolveTermsOfUseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var agreements = await _termsOfUseService!.ListTermsOfUseAgreementsAsync(cancellationToken);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in agreements)
            {
                if (!string.IsNullOrEmpty(a.Id) && !string.IsNullOrEmpty(a.DisplayName))
                    map.TryAdd(a.Id, a.DisplayName);
            }
            return map;
        }
        catch { return new Dictionary<string, string>(); }
    }

    private static void AddAll(HashSet<string> target, List<string>? source)
    {
        if (source == null) return;
        foreach (var id in source)
        {
            if (!string.IsNullOrEmpty(id))
                target.Add(id);
        }
    }

    private static byte[] LoadEmbeddedTemplateBytes(string templateName)
    {
        var assembly = typeof(ConditionalAccessPptExportService).Assembly;
        var resourceName = $"Intune.Commander.Core.Assets.{templateName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded template '{resourceName}' not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

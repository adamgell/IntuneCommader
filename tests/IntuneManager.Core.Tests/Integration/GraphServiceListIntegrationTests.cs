using IntuneManager.Core.Services;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Tests.Integration;

/// <summary>
/// Read-only integration tests exercising List + Get operations against live Graph.
/// Safe to run against any tenant — no data is created, modified, or deleted.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Integration")]
public class GraphServiceListIntegrationTests : GraphIntegrationTestBase
{
    #region ConfigurationProfileService

    [Fact]
    public async Task ConfigurationProfile_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ConfigurationProfileService>()!;
        var results = await svc.ListDeviceConfigurationsAsync();
        Assert.NotNull(results);
        Assert.IsType<List<DeviceConfiguration>>(results);
    }

    [Fact]
    public async Task ConfigurationProfile_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ConfigurationProfileService>()!;
        var all = await svc.ListDeviceConfigurationsAsync();
        if (all.Count == 0) return; // tenant has no items — nothing to get

        var item = await svc.GetDeviceConfigurationAsync(all[0].Id!);
        Assert.NotNull(item);
        Assert.Equal(all[0].Id, item!.Id);
    }

    [Fact]
    public async Task ConfigurationProfile_GetAssignments_ReturnsListForExistingItem()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ConfigurationProfileService>()!;
        var all = await svc.ListDeviceConfigurationsAsync();
        if (all.Count == 0) return;

        var assignments = await svc.GetAssignmentsAsync(all[0].Id!);
        Assert.NotNull(assignments);
    }

    #endregion

    #region CompliancePolicyService

    [Fact]
    public async Task CompliancePolicy_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<CompliancePolicyService>()!;
        var results = await svc.ListCompliancePoliciesAsync();
        Assert.NotNull(results);
        Assert.IsType<List<DeviceCompliancePolicy>>(results);
    }

    [Fact]
    public async Task CompliancePolicy_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<CompliancePolicyService>()!;
        var all = await svc.ListCompliancePoliciesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetCompliancePolicyAsync(all[0].Id!);
        Assert.NotNull(item);
        Assert.Equal(all[0].Id, item!.Id);
    }

    [Fact]
    public async Task CompliancePolicy_GetAssignments_ReturnsListForExistingItem()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<CompliancePolicyService>()!;
        var all = await svc.ListCompliancePoliciesAsync();
        if (all.Count == 0) return;

        var assignments = await svc.GetAssignmentsAsync(all[0].Id!);
        Assert.NotNull(assignments);
    }

    #endregion

    #region ApplicationService

    [Fact]
    public async Task Application_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ApplicationService>()!;
        var results = await svc.ListApplicationsAsync();
        Assert.NotNull(results);
        Assert.IsType<List<MobileApp>>(results);
    }

    [Fact]
    public async Task Application_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ApplicationService>()!;
        var all = await svc.ListApplicationsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetApplicationAsync(all[0].Id!);
        Assert.NotNull(item);
        Assert.Equal(all[0].Id, item!.Id);
    }

    [Fact]
    public async Task Application_GetAssignments_ReturnsListForExistingItem()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ApplicationService>()!;
        var all = await svc.ListApplicationsAsync();
        if (all.Count == 0) return;

        var assignments = await svc.GetAssignmentsAsync(all[0].Id!);
        Assert.NotNull(assignments);
    }

    #endregion

    #region GroupService

    [Fact]
    public async Task Group_ListDynamic_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<GroupService>()!;
        var results = await svc.ListDynamicGroupsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Group_ListAssigned_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<GroupService>()!;
        var results = await svc.ListAssignedGroupsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Group_Search_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<GroupService>()!;
        // Search for a common term that should match at least something
        var results = await svc.SearchGroupsAsync("a");
        Assert.NotNull(results);
    }

    #endregion

    #region EndpointSecurityService

    [Fact]
    public async Task EndpointSecurity_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<EndpointSecurityService>()!;
        var results = await svc.ListEndpointSecurityIntentsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task EndpointSecurity_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<EndpointSecurityService>()!;
        var all = await svc.ListEndpointSecurityIntentsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetEndpointSecurityIntentAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region AdministrativeTemplateService

    [Fact]
    public async Task AdministrativeTemplate_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AdministrativeTemplateService>()!;
        var results = await svc.ListAdministrativeTemplatesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task AdministrativeTemplate_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AdministrativeTemplateService>()!;
        var all = await svc.ListAdministrativeTemplatesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetAdministrativeTemplateAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region EnrollmentConfigurationService

    [Fact]
    public async Task Enrollment_ListConfigurations_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<EnrollmentConfigurationService>()!;
        var results = await svc.ListEnrollmentConfigurationsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Enrollment_ListStatusPages_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<EnrollmentConfigurationService>()!;
        var results = await svc.ListEnrollmentStatusPagesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Enrollment_ListRestrictions_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<EnrollmentConfigurationService>()!;
        var results = await svc.ListEnrollmentRestrictionsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Enrollment_ListCoManagement_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<EnrollmentConfigurationService>()!;
        var results = await svc.ListCoManagementSettingsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Enrollment_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<EnrollmentConfigurationService>()!;
        var all = await svc.ListEnrollmentConfigurationsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetEnrollmentConfigurationAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region SettingsCatalogService

    [Fact]
    public async Task SettingsCatalog_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<SettingsCatalogService>()!;
        var results = await svc.ListSettingsCatalogPoliciesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task SettingsCatalog_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<SettingsCatalogService>()!;
        var all = await svc.ListSettingsCatalogPoliciesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetSettingsCatalogPolicyAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    [Fact]
    public async Task SettingsCatalog_GetAssignments_ReturnsListForExistingItem()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<SettingsCatalogService>()!;
        var all = await svc.ListSettingsCatalogPoliciesAsync();
        if (all.Count == 0) return;

        var assignments = await svc.GetAssignmentsAsync(all[0].Id!);
        Assert.NotNull(assignments);
    }

    #endregion

    #region ConditionalAccessPolicyService

    [Fact]
    public async Task ConditionalAccess_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ConditionalAccessPolicyService>()!;
        var results = await svc.ListPoliciesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task ConditionalAccess_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ConditionalAccessPolicyService>()!;
        var all = await svc.ListPoliciesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetPolicyAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region AppProtectionPolicyService

    [Fact]
    public async Task AppProtection_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AppProtectionPolicyService>()!;
        var results = await svc.ListAppProtectionPoliciesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task AppProtection_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AppProtectionPolicyService>()!;
        var all = await svc.ListAppProtectionPoliciesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetAppProtectionPolicyAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region ManagedAppConfigurationService

    [Fact]
    public async Task ManagedDeviceAppConfig_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ManagedAppConfigurationService>()!;
        var results = await svc.ListManagedDeviceAppConfigurationsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task ManagedDeviceAppConfig_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ManagedAppConfigurationService>()!;
        var all = await svc.ListManagedDeviceAppConfigurationsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetManagedDeviceAppConfigurationAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    [Fact]
    public async Task TargetedManagedAppConfig_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ManagedAppConfigurationService>()!;
        var results = await svc.ListTargetedManagedAppConfigurationsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task TargetedManagedAppConfig_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ManagedAppConfigurationService>()!;
        var all = await svc.ListTargetedManagedAppConfigurationsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetTargetedManagedAppConfigurationAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region AssignmentFilterService

    [Fact]
    public async Task AssignmentFilter_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AssignmentFilterService>()!;
        var results = await svc.ListFiltersAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task AssignmentFilter_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AssignmentFilterService>()!;
        var all = await svc.ListFiltersAsync();
        if (all.Count == 0) return;

        var item = await svc.GetFilterAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region PolicySetService

    [Fact]
    public async Task PolicySet_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<PolicySetService>()!;
        var results = await svc.ListPolicySetsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task PolicySet_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<PolicySetService>()!;
        var all = await svc.ListPolicySetsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetPolicySetAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region AutopilotService

    [Fact]
    public async Task Autopilot_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AutopilotService>()!;
        var results = await svc.ListAutopilotProfilesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Autopilot_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AutopilotService>()!;
        var all = await svc.ListAutopilotProfilesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetAutopilotProfileAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region FeatureUpdateProfileService

    [Fact]
    public async Task FeatureUpdate_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<FeatureUpdateProfileService>()!;
        var results = await svc.ListFeatureUpdateProfilesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task FeatureUpdate_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<FeatureUpdateProfileService>()!;
        var all = await svc.ListFeatureUpdateProfilesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetFeatureUpdateProfileAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region DeviceHealthScriptService

    [Fact]
    public async Task DeviceHealthScript_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<DeviceHealthScriptService>()!;
        var results = await svc.ListDeviceHealthScriptsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task DeviceHealthScript_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<DeviceHealthScriptService>()!;
        var all = await svc.ListDeviceHealthScriptsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetDeviceHealthScriptAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region ScopeTagService

    [Fact]
    public async Task ScopeTag_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ScopeTagService>()!;
        var results = await svc.ListScopeTagsAsync();
        Assert.NotNull(results);
        // Every tenant has at least the Default scope tag
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task ScopeTag_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<ScopeTagService>()!;
        var all = await svc.ListScopeTagsAsync();
        Assert.NotEmpty(all);

        var item = await svc.GetScopeTagAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region RoleDefinitionService

    [Fact]
    public async Task RoleDefinition_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<RoleDefinitionService>()!;
        var results = await svc.ListRoleDefinitionsAsync();
        Assert.NotNull(results);
        // Every tenant has built-in role definitions
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task RoleDefinition_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<RoleDefinitionService>()!;
        var all = await svc.ListRoleDefinitionsAsync();
        Assert.NotEmpty(all);

        var item = await svc.GetRoleDefinitionAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region IntuneBrandingService

    [Fact]
    public async Task IntuneBranding_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<IntuneBrandingService>()!;
        var results = await svc.ListIntuneBrandingProfilesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task IntuneBranding_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<IntuneBrandingService>()!;
        var all = await svc.ListIntuneBrandingProfilesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetIntuneBrandingProfileAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region AzureBrandingService

    [Fact]
    public async Task AzureBranding_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AzureBrandingService>()!;
        var results = await svc.ListBrandingLocalizationsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task AzureBranding_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AzureBrandingService>()!;
        var all = await svc.ListBrandingLocalizationsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetBrandingLocalizationAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region MacCustomAttributeService

    [Fact]
    public async Task MacCustomAttribute_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<MacCustomAttributeService>()!;
        var results = await svc.ListMacCustomAttributesAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task MacCustomAttribute_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<MacCustomAttributeService>()!;
        var all = await svc.ListMacCustomAttributesAsync();
        if (all.Count == 0) return;

        var item = await svc.GetMacCustomAttributeAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region TermsAndConditionsService

    [Fact]
    public async Task TermsAndConditions_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<TermsAndConditionsService>()!;
        var results = await svc.ListTermsAndConditionsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task TermsAndConditions_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<TermsAndConditionsService>()!;
        var all = await svc.ListTermsAndConditionsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetTermsAndConditionsAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region NamedLocationService

    [Fact]
    public async Task NamedLocation_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<NamedLocationService>()!;
        var results = await svc.ListNamedLocationsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task NamedLocation_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<NamedLocationService>()!;
        var all = await svc.ListNamedLocationsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetNamedLocationAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region AuthenticationStrengthService

    [Fact]
    public async Task AuthenticationStrength_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AuthenticationStrengthService>()!;
        var results = await svc.ListAuthenticationStrengthPoliciesAsync();
        Assert.NotNull(results);
        // Every tenant has built-in authentication strength policies
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task AuthenticationStrength_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AuthenticationStrengthService>()!;
        var all = await svc.ListAuthenticationStrengthPoliciesAsync();
        Assert.NotEmpty(all);

        var item = await svc.GetAuthenticationStrengthPolicyAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region AuthenticationContextService

    [Fact]
    public async Task AuthenticationContext_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AuthenticationContextService>()!;
        var results = await svc.ListAuthenticationContextsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task AuthenticationContext_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<AuthenticationContextService>()!;
        var all = await svc.ListAuthenticationContextsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetAuthenticationContextAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion

    #region TermsOfUseService

    [Fact]
    public async Task TermsOfUse_List_Returns_Results()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<TermsOfUseService>()!;
        var results = await svc.ListTermsOfUseAgreementsAsync();
        Assert.NotNull(results);
    }

    [Fact]
    public async Task TermsOfUse_Get_ReturnsItem_WhenExists()
    {
        if (ShouldSkip()) return;
        var svc = CreateService<TermsOfUseService>()!;
        var all = await svc.ListTermsOfUseAgreementsAsync();
        if (all.Count == 0) return;

        var item = await svc.GetTermsOfUseAgreementAsync(all[0].Id!);
        Assert.NotNull(item);
    }

    #endregion
}

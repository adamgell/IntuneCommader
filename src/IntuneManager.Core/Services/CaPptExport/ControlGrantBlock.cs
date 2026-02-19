using System.Text;
using Microsoft.Graph.Beta.Models;

namespace IntuneManager.Core.Services.CaPptExport;

/// <summary>
/// Parses grant controls from a CA policy.
/// </summary>
public class ControlGrantBlock
{
    public string? Name { get; private set; } = string.Empty;
    public string? IncludeExclude { get; private set; }
    public bool IsGrant { get; set; }
    public bool ApprovedApplication { get; set; }
    public bool TermsOfUse { get; set; }
    public bool CustomAuthenticationFactor { get; set; }
    public bool CompliantApplication { get; set; }
    public bool CompliantDevice { get; set; }
    public bool DomainJoinedDevice { get; set; }
    public bool Mfa { get; set; }
    public bool PasswordChange { get; set; }
    public bool AuthenticationStrength { get; set; }
    public bool IsGrantRequireAll { get; set; }
    public bool IsGrantRequireOne { get; set; }
    public int GrantControlsCount { get; set; }
    public string? CustomAuthenticationFactorName { get; set; }
    public string? TermsOfUseName { get; set; }
    public string? AuthenticationStrengthName { get; set; }

    public ControlGrantBlock(ConditionalAccessPolicy policy)
    {
        var grantControls = policy.GrantControls;
        if (grantControls == null) return;

        IsGrant = !grantControls.BuiltInControls?.Contains(ConditionalAccessGrantControl.Block) ?? true;
        IncludeExclude = GetIncludes(grantControls, policy);
    }

    private string GetIncludes(ConditionalAccessGrantControls grantControls, ConditionalAccessPolicy policy)
    {
        var sb = new StringBuilder();
        IsGrantRequireAll = grantControls.Operator == "AND";
        IsGrantRequireOne = grantControls.Operator == "OR";
        GrantControlsCount = 0;

        if (grantControls.BuiltInControls?.Count > 0)
        {
            foreach (var control in grantControls.BuiltInControls)
            {
                switch (control)
                {
                    case ConditionalAccessGrantControl.ApprovedApplication:
                        Name += "-Approved App";
                        ApprovedApplication = true;
                        GrantControlsCount++;
                        break;
                    case ConditionalAccessGrantControl.Block:
                        break; // Block is shown in header
                    case ConditionalAccessGrantControl.CompliantApplication:
                        Name += "-Compliant App";
                        CompliantApplication = true;
                        GrantControlsCount++;
                        break;
                    case ConditionalAccessGrantControl.CompliantDevice:
                        Name += "-Compliant Device";
                        CompliantDevice = true;
                        GrantControlsCount++;
                        break;
                    case ConditionalAccessGrantControl.DomainJoinedDevice:
                        Name += "-HAADJ";
                        DomainJoinedDevice = true;
                        GrantControlsCount++;
                        break;
                    case ConditionalAccessGrantControl.Mfa:
                        Name += "-MFA";
                        Mfa = true;
                        GrantControlsCount++;
                        break;
                    case ConditionalAccessGrantControl.PasswordChange:
                        Name += "-Password Change";
                        PasswordChange = true;
                        GrantControlsCount++;
                        break;
                }
            }
        }

        if (grantControls.CustomAuthenticationFactors?.Count > 0)
        {
            Name += "-3PMFA";
            CustomAuthenticationFactor = true;
            var names = new List<string>();
            foreach (var caf in grantControls.CustomAuthenticationFactors)
            {
                names.Add(caf);
                GrantControlsCount++;
            }
            CustomAuthenticationFactorName = string.Join(", ", names);
        }

        if (grantControls.TermsOfUse?.Count > 0)
        {
            Name += "-ToU";
            TermsOfUse = true;
            var names = new List<string>();
            foreach (var tou in grantControls.TermsOfUse)
            {
                names.Add(tou);
                GrantControlsCount++;
            }
            TermsOfUseName = string.Join(", ", names);
        }

        var authStrength = policy.GrantControls?.AuthenticationStrength;
        if (authStrength != null)
        {
            Name += "-MFA Strength";
            AuthenticationStrength = true;
            AuthenticationStrengthName = $"Auth strength:{authStrength.DisplayName}";
        }

        return sb.ToString();
    }
}

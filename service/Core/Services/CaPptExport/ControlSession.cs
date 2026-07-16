using System.Text;
using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Services.CaPptExport;

/// <summary>
/// Parses session controls from a CA policy.
/// </summary>
public class ControlSession
{
    public bool UseAppEnforcedRestrictions { get; set; }
    public bool UseConditionalAccessAppControl { get; set; }
    public bool SignInFrequency { get; set; }
    public bool PersistentBrowserSession { get; set; }
    public bool ContinuousAccessEvaluation { get; set; }
    public bool DisableResilienceDefaults { get; set; }
    public bool SecureSignInSession { get; set; }
    public string? SignInFrequencyIntervalLabel { get; set; }
    public string? CloudAppSecurityType { get; set; }
    public string? PersistentBrowserSessionModeLabel { get; set; }
    public string? ContinuousAccessEvaluationModeLabel { get; set; }

    public ControlSession(ConditionalAccessPolicy policy)
    {
        var session = policy.SessionControls;
        if (session == null) return;

        UpdateProps(session);
    }

    private void UpdateProps(ConditionalAccessSessionControls session)
    {
        UseAppEnforcedRestrictions = session.ApplicationEnforcedRestrictions?.IsEnabled == true;
        UseConditionalAccessAppControl = session.CloudAppSecurity?.IsEnabled == true;
        if (UseConditionalAccessAppControl)
            CloudAppSecurityType = session.CloudAppSecurity?.CloudAppSecurityType?.ToString();

        var sif = session.SignInFrequency;
        SignInFrequency = sif?.IsEnabled == true;
        if (SignInFrequency)
        {
            if (sif?.FrequencyInterval == SignInFrequencyInterval.EveryTime)
                SignInFrequencyIntervalLabel = "Every time";
            else if (sif?.FrequencyInterval == SignInFrequencyInterval.TimeBased)
            {
                var frequency = sif.Type?.ToString()?.ToLower() ?? "";
                if (sif.Value == 1 && frequency.Length > 2)
                    frequency = frequency[..^1]; // Remove trailing 's'
                SignInFrequencyIntervalLabel = $"{sif.Value} {frequency}";
            }
        }

        var pb = session.PersistentBrowser;
        PersistentBrowserSession = pb?.IsEnabled == true;
        if (PersistentBrowserSession)
            PersistentBrowserSessionModeLabel = pb?.Mode?.ToString();

        var cae = session.ContinuousAccessEvaluation;
        ContinuousAccessEvaluation = cae != null;
        if (ContinuousAccessEvaluation)
            ContinuousAccessEvaluationModeLabel = cae?.Mode?.ToString();

        DisableResilienceDefaults = session.DisableResilienceDefaults == true;
        SecureSignInSession = session.SecureSignInSession?.IsEnabled == true;
    }
}

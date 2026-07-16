function Test-MtIntuneWindowsComplianceRequireEncryption {
    <#
    .SYNOPSIS
    Checks that a Windows device compliance policy requires storage (BitLocker) encryption.

    .DESCRIPTION
    CIS Microsoft Intune for Windows and the OIB hardening baselines require device
    storage to be encrypted. This test passes when at least one Windows 10/11 device
    compliance policy sets "Require encryption of data storage on device"
    (storageRequireEncryption = true), so non-compliant devices are flagged and can be
    gated by Conditional Access.

    Derived from the embedded OIB / CIS baseline knowledge in cmProjectX
    (intunecommander) as an upstream give-back to Maester.

    .EXAMPLE
    Test-MtIntuneWindowsComplianceRequireEncryption

    Returns true if at least one Windows compliance policy requires storage encryption,
    false if Windows policies exist but none require it, and $null (Skipped) when Intune
    is not licensed or no Windows compliance policies exist.

    .LINK
    https://maester.dev/docs/commands/Test-MtIntuneWindowsComplianceRequireEncryption
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param()

    if (-not (Get-MtLicenseInformation -Product Intune)) {
        Add-MtTestResultDetail -SkippedBecause NotLicensedIntune
        return $null
    }

    try {
        Write-Verbose 'Retrieving Windows device compliance policies...'
        $policies = Invoke-MtGraphRequest -RelativeUri 'deviceManagement/deviceCompliancePolicies' -ApiVersion beta
        $windows = $policies | Where-Object { $_.'@odata.type' -eq '#microsoft.graph.windows10CompliancePolicy' }

        if (-not $windows) {
            Add-MtTestResultDetail -SkippedBecause Custom -SkippedCustomReason 'No Windows 10/11 device compliance policies exist.'
            return $null
        }

        $enforcing = @($windows | Where-Object { $_.storageRequireEncryption -eq $true })

        $testResultMarkdown = "Windows device compliance policies and their storage-encryption requirement:`n`n"
        $testResultMarkdown += "| Policy | storageRequireEncryption |`n| --- | --- |`n"
        foreach ($p in $windows) {
            $icon = if ($p.storageRequireEncryption -eq $true) { '✅' } else { '❌' }
            $testResultMarkdown += "| $($p.displayName) | $icon $($p.storageRequireEncryption) |`n"
        }
        Add-MtTestResultDetail -Result $testResultMarkdown

        return $enforcing.Count -gt 0
    } catch {
        Add-MtTestResultDetail -SkippedBecause Error -SkippedError $_
        return $null
    }
}

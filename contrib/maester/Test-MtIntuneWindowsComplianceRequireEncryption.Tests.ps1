# Pester wrapper mirroring Maester's maester-tests/*.Tests.ps1 convention. Invoke-Maester
# discovers this Describe/It; the test id + tags feed the report and -Tag filtering.
Describe 'Intune' -Tag 'Intune', 'Compliance', 'CIS', 'Security', 'All' {
    It 'MT.CONTRIB.1001: A Windows compliance policy requires storage (BitLocker) encryption' -Tag 'MT.CONTRIB.1001', 'Intune' {
        $result = Test-MtIntuneWindowsComplianceRequireEncryption

        # $null = Skipped (not licensed / no Windows policies); Pester treats it as skipped
        # via Add-MtTestResultDetail, so only assert when we have a boolean verdict.
        if ($null -ne $result) {
            $result | Should -Be $true -Because 'at least one Windows 10/11 device compliance policy should require storage (BitLocker) encryption (CIS Intune / OIB baseline)'
        }
    }
}

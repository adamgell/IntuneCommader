<#
.SYNOPSIS
    Creates or updates an Entra ID app registration for Intune Commander
    integration tests and grants all required Microsoft Graph application
    permissions.

.DESCRIPTION
    This script:
    1. Connects to Azure (interactive login or existing session)
    2. Creates a new app registration -- or finds one that already exists
    3. Compares the current permissions against the required set
    4. Adds any missing permissions and removes stale ones
    5. Creates a client secret (skip with -SkipSecretCreation)
    6. Ensures a service principal exists
    7. Grants admin consent for every required permission
    8. Outputs the values needed for GitHub Actions secrets

    Running the script a second time is safe -- it will only apply changes
    where the current state differs from the desired state.

.NOTES
    Prerequisites:
      - Az PowerShell module: Install-Module Az -Scope CurrentUser
      - Entra ID role: Global Admin or Application Admin + Privileged Role Admin (for consent)
      - Run in PowerShell 5.1+

    After running, add these GitHub Environment secrets:
      Settings -> Environments -> integration-test -> Add secret
      AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET

.EXAMPLE
    # First run -- creates the app and outputs secrets
    .\Setup-IntegrationTestApp.ps1

.EXAMPLE
    # Re-run after adding new services -- updates permissions, no new secret
    .\Setup-IntegrationTestApp.ps1 -SkipSecretCreation

.EXAMPLE
    # Target a specific tenant
    .\Setup-IntegrationTestApp.ps1 -TenantId "00000000-0000-0000-0000-000000000000"
#>

[CmdletBinding()]
param(
    # Display name for the app registration
    [string]$AppDisplayName = "IntuneCommander-IntegrationTests",

    # How long the client secret is valid (default: 180 days)
    [int]$SecretExpirationDays = 180,

    # Tenant ID -- leave empty to use the current Az context tenant
    [string]$TenantId = "",

    # Subscription ID -- leave empty to use the current Az context subscription
    [string]$SubscriptionId = "",

    # Skip client secret creation (useful for permission-only updates)
    [switch]$SkipSecretCreation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- 0. Check and install required modules -------------------------------------

Write-Host "`n=== Step 0: Checking required PowerShell modules ===" -ForegroundColor Cyan

$requiredModules = @("Az.Accounts", "Az.Resources")

foreach ($mod in $requiredModules) {
    if (Get-Module -ListAvailable -Name $mod) {
        $ver = (Get-Module -ListAvailable -Name $mod | Sort-Object Version -Descending | Select-Object -First 1).Version
        Write-Host "  [OK] $mod ($ver) is installed" -ForegroundColor Green
    } else {
        Write-Host "  [X] $mod not found -- installing..." -ForegroundColor Yellow
        try {
            Install-Module -Name $mod -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop
            $ver = (Get-Module -ListAvailable -Name $mod | Sort-Object Version -Descending | Select-Object -First 1).Version
            Write-Host "  [OK] $mod ($ver) installed successfully" -ForegroundColor Green
        } catch {
            Write-Error "Failed to install $mod. Run 'Install-Module $mod -Scope CurrentUser' manually and retry."
            exit 1
        }
    }
}

# Import modules
foreach ($mod in $requiredModules) {
    Import-Module $mod -ErrorAction Stop
}

Write-Host "  All required modules are available." -ForegroundColor Green

# Helper: Get a plain-text access token (works with both old and new Az.Accounts)
# Az.Accounts 2.13+ returns .Token as SecureString; older versions return a string.
function Get-PlainGraphToken {
    $tokenResult = Get-AzAccessToken -ResourceUrl "https://graph.microsoft.com"
    $raw = $tokenResult.Token
    if ($raw -is [System.Security.SecureString]) {
        # PS 5.1 does not have ConvertFrom-SecureString -AsPlainText
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($raw)
        try {
            return [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
        } finally {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
    return [string]$raw
}

# --- 1. Connect to Azure -------------------------------------------------------

Write-Host "`n=== Step 1: Connecting to Azure ===" -ForegroundColor Cyan

$connectParams = @{}
if ($TenantId)       { $connectParams.TenantId = $TenantId }
if ($SubscriptionId) { $connectParams.SubscriptionId = $SubscriptionId }

$context = Get-AzContext
if (-not $context) {
    Connect-AzAccount @connectParams
    $context = Get-AzContext
} elseif ($TenantId -and $context.Tenant.Id -ne $TenantId) {
    Connect-AzAccount @connectParams
    $context = Get-AzContext
}

$resolvedTenantId = $context.Tenant.Id
Write-Host "  Tenant:       $resolvedTenantId" -ForegroundColor Green
Write-Host "  Subscription: $($context.Subscription.Name)" -ForegroundColor Green

# --- 2. Get Microsoft Graph service principal ----------------------------------

Write-Host "`n=== Step 2: Looking up Microsoft Graph service principal ===" -ForegroundColor Cyan

$graphSpn = Get-AzADServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
if (-not $graphSpn) {
    throw "Microsoft Graph service principal not found in tenant. Is this a valid Entra ID tenant?"
}
Write-Host "  Found: $($graphSpn.DisplayName) ($($graphSpn.AppId))" -ForegroundColor Green

# --- 3. Define required permissions -------------------------------------------

# These map to the permissions documented in docs/GRAPH-PERMISSIONS.md
$requiredPermissions = @(
    # Intune -- Device Management
    "DeviceManagementConfiguration.ReadWrite.All"
    "DeviceManagementApps.ReadWrite.All"
    "DeviceManagementServiceConfig.ReadWrite.All"
    "DeviceManagementRBAC.ReadWrite.All"
    "DeviceManagementManagedDevices.Read.All"
    "DeviceManagementScripts.ReadWrite.All"

    # Entra ID -- Conditional Access & Identity
    "Policy.ReadWrite.ConditionalAccess"
    "Policy.Read.All"

    # Entra ID -- Terms of Use
    "Agreement.ReadWrite.All"

    # Entra ID -- Organization & Branding
    "Organization.Read.All"
    "OrganizationalBranding.ReadWrite.All"

    # Entra ID -- Groups
    "Group.Read.All"
    "GroupMember.Read.All"
)

Write-Host "`n=== Step 3: Resolving permission IDs ===" -ForegroundColor Cyan

# Build a lookup of app role name -> ID from the Graph service principal
$appRoleLookup = @{}
foreach ($role in $graphSpn.AppRole) {
    $appRoleLookup[$role.Value] = $role.Id
}

$permissionIds = @()
foreach ($perm in $requiredPermissions) {
    if ($appRoleLookup.ContainsKey($perm)) {
        $permissionIds += @{
            Name = $perm
            Id   = $appRoleLookup[$perm]
        }
        Write-Host "  [OK] $perm -> $($appRoleLookup[$perm])" -ForegroundColor Green
    } else {
        Write-Warning "  [X] Permission '$perm' not found on Microsoft Graph service principal -- skipping"
    }
}

if ($permissionIds.Count -eq 0) {
    throw "No valid permissions resolved. Cannot proceed."
}

# --- 4. Find or create the app registration ------------------------------------

Write-Host "`n=== Step 4: App registration '$AppDisplayName' ===" -ForegroundColor Cyan

$existingApp = Get-AzADApplication -Filter "displayName eq '$AppDisplayName'" -ErrorAction SilentlyContinue
$isUpdate = $false

if ($existingApp) {
    $isUpdate = $true
    $app = $existingApp
    Write-Host "  [~] Found existing app: $($app.AppId)" -ForegroundColor Yellow
    Write-Host "      Object ID: $($app.Id)" -ForegroundColor DarkGray
} else {
    Write-Host "  Creating new app registration..." -ForegroundColor White

    # Build required resource access (Microsoft Graph permissions)
    $resourceAccess = $permissionIds | ForEach-Object {
        @{
            Id   = $_.Id
            Type = "Role"   # "Role" = Application permission; "Scope" = Delegated
        }
    }

    $app = New-AzADApplication `
        -DisplayName $AppDisplayName `
        -SignInAudience "AzureADMyOrg" `
        -RequiredResourceAccess @(
            @{
                ResourceAppId  = "00000003-0000-0000-c000-000000000000"  # Microsoft Graph
                ResourceAccess = $resourceAccess
            }
        )

    Write-Host "  [OK] Created: $($app.AppId)" -ForegroundColor Green
}

$appId = $app.AppId

# --- 5. Diff and update permissions (existing apps) ---------------------------

if ($isUpdate) {
    Write-Host "`n=== Step 5: Comparing permissions ===" -ForegroundColor Cyan

    # Get current required resource access for Microsoft Graph
    $graphResourceAppId = "00000003-0000-0000-c000-000000000000"

    # Fetch fresh app details including RequiredResourceAccess
    $graphToken = Get-PlainGraphToken
    $headers = @{
        "Authorization" = "Bearer $graphToken"
        "Content-Type"  = "application/json"
    }
    $appDetails = Invoke-RestMethod -Uri "https://graph.microsoft.com/v1.0/applications/$($app.Id)" -Headers $headers -Method GET

    $currentIds = @()
    foreach ($rra in $appDetails.requiredResourceAccess) {
        if ($rra.resourceAppId -eq $graphResourceAppId) {
            foreach ($ra in $rra.resourceAccess) {
                $currentIds += $ra.id
            }
        }
    }

    $desiredIds = $permissionIds | ForEach-Object { $_.Id }

    # Compute diff
    $toAdd    = @($desiredIds | Where-Object { $currentIds -notcontains $_ })
    $toRemove = @($currentIds | Where-Object { $desiredIds -notcontains $_ })
    $unchanged = @($desiredIds | Where-Object { $currentIds -contains $_ })

    Write-Host "  Current permissions : $($currentIds.Count)" -ForegroundColor DarkGray
    Write-Host "  Desired permissions : $($desiredIds.Count)" -ForegroundColor DarkGray
    Write-Host "  Unchanged           : $($unchanged.Count)" -ForegroundColor Green

    # Map IDs back to names for display
    $idToName = @{}
    foreach ($perm in $permissionIds) {
        $idToName[$perm.Id] = $perm.Name
    }
    foreach ($role in $graphSpn.AppRole) {
        if (-not $idToName.ContainsKey($role.Id)) {
            $idToName[$role.Id] = $role.Value
        }
    }

    if ($toAdd.Count -gt 0) {
        Write-Host "  Adding             : $($toAdd.Count)" -ForegroundColor Yellow
        foreach ($id in $toAdd) {
            $name = if ($idToName.ContainsKey($id)) { $idToName[$id] } else { $id }
            Write-Host "    [+] $name" -ForegroundColor Green
        }
    }
    if ($toRemove.Count -gt 0) {
        Write-Host "  Removing (stale)   : $($toRemove.Count)" -ForegroundColor Yellow
        foreach ($id in $toRemove) {
            $name = if ($idToName.ContainsKey($id)) { $idToName[$id] } else { $id }
            Write-Host "    [-] $name" -ForegroundColor Red
        }
    }

    if ($toAdd.Count -eq 0 -and $toRemove.Count -eq 0) {
        Write-Host "  No permission changes needed." -ForegroundColor Green
    } else {
        Write-Host "`n  Updating app registration permissions..." -ForegroundColor White

        # Build the full desired resource access list
        $resourceAccess = $permissionIds | ForEach-Object {
            @{
                id   = $_.Id
                type = "Role"
            }
        }

        $body = @{
            requiredResourceAccess = @(
                @{
                    resourceAppId  = $graphResourceAppId
                    resourceAccess = @($resourceAccess)
                }
            )
        } | ConvertTo-Json -Depth 5

        Invoke-RestMethod `
            -Uri "https://graph.microsoft.com/v1.0/applications/$($app.Id)" `
            -Method PATCH `
            -Headers $headers `
            -Body $body

        Write-Host "  [OK] App registration permissions updated" -ForegroundColor Green
    }
} else {
    Write-Host "`n=== Step 5: Skipped (new app -- permissions set at creation) ===" -ForegroundColor DarkGray
}

# --- 6. Create a client secret ------------------------------------------------

Write-Host "`n=== Step 6: Client secret ===" -ForegroundColor Cyan

$clientSecret = $null
$endDate = (Get-Date).AddDays($SecretExpirationDays)

if ($SkipSecretCreation) {
    Write-Host "  Skipped (-SkipSecretCreation flag set)" -ForegroundColor Yellow
    Write-Host "  Existing secrets are not affected." -ForegroundColor DarkGray
} else {
    $secret = New-AzADAppCredential -ApplicationId $appId -EndDate $endDate

    # The secret value is only available immediately after creation
    $clientSecret = $secret.SecretText
    if (-not $clientSecret) {
        Write-Warning "  Could not retrieve secret value. You may need to create one manually in the Azure portal."
    } else {
        $expiryStr = $endDate.ToString("yyyy-MM-dd")
        Write-Host "  [OK] Secret created (expires: $expiryStr)" -ForegroundColor Green
        if ($isUpdate) {
            Write-Host "  NOTE: Previous secrets are still valid. Revoke old ones in Azure Portal if no longer needed." -ForegroundColor Yellow
        }
    }
}

# --- 7. Create the service principal (enterprise app) -------------------------

Write-Host "`n=== Step 7: Service principal ===" -ForegroundColor Cyan

$spn = Get-AzADServicePrincipal -Filter "appId eq '$appId'" -ErrorAction SilentlyContinue
if (-not $spn) {
    $spn = New-AzADServicePrincipal -ApplicationId $appId
    Write-Host "  [OK] Created service principal: $($spn.Id)" -ForegroundColor Green
} else {
    Write-Host "  [~] Service principal already exists: $($spn.Id)" -ForegroundColor Yellow
}

# --- 8. Grant admin consent (assign app roles) --------------------------------

Write-Host "`n=== Step 8: Granting admin consent for $($permissionIds.Count) permissions ===" -ForegroundColor Cyan

# Ensure we have a fresh token (step 5 may have used one earlier for new apps)
if (-not $headers) {
    $graphToken = Get-PlainGraphToken
    $headers = @{
        "Authorization" = "Bearer $graphToken"
        "Content-Type"  = "application/json"
    }
}

$consentUrl = "https://graph.microsoft.com/v1.0/servicePrincipals/$($spn.Id)/appRoleAssignments"

$consentedCount = 0
$alreadyCount   = 0

foreach ($perm in $permissionIds) {
    $body = @{
        principalId = $spn.Id
        resourceId  = $graphSpn.Id
        appRoleId   = $perm.Id
    } | ConvertTo-Json

    try {
        $null = Invoke-RestMethod -Uri $consentUrl -Method POST -Headers $headers -Body $body
        Write-Host "  [OK] Consented: $($perm.Name)" -ForegroundColor Green
        $consentedCount++
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 409) {
            Write-Host "  [~] Already consented: $($perm.Name)" -ForegroundColor DarkGray
            $alreadyCount++
        } else {
            Write-Warning "  [X] Failed to consent '$($perm.Name)': $($_.Exception.Message)"
        }
    }
}

# Revoke consent for removed permissions (if updating)
if ($isUpdate -and $toRemove.Count -gt 0) {
    Write-Host "`n  Revoking consent for $($toRemove.Count) removed permission(s)..." -ForegroundColor Yellow

    # Get current app role assignments
    $currentAssignments = Invoke-RestMethod -Uri $consentUrl -Method GET -Headers $headers

    foreach ($id in $toRemove) {
        $name = if ($idToName.ContainsKey($id)) { $idToName[$id] } else { $id }
        $assignment = $currentAssignments.value | Where-Object { $_.appRoleId -eq $id }

        if ($assignment) {
            $revokeUrl = "https://graph.microsoft.com/v1.0/servicePrincipals/$($spn.Id)/appRoleAssignments/$($assignment.id)"
            try {
                Invoke-RestMethod -Uri $revokeUrl -Method DELETE -Headers $headers
                Write-Host "  [OK] Revoked: $name" -ForegroundColor Green
            } catch {
                Write-Warning "  [X] Failed to revoke '$name': $($_.Exception.Message)"
            }
        } else {
            Write-Host "  [~] No active consent to revoke: $name" -ForegroundColor DarkGray
        }
    }
}

Write-Host "`n  Consent summary: $consentedCount new, $alreadyCount already granted" -ForegroundColor White

# --- 9. Output summary --------------------------------------------------------

$action = if ($isUpdate) { "Update" } else { "Setup" }

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "  App Registration $action Complete!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan

Write-Host "`n  Add these as GitHub Environment secrets:" -ForegroundColor White
Write-Host "  Settings -> Environments -> integration-test -> Add secret" -ForegroundColor DarkGray
Write-Host "  -----------------------------------------" -ForegroundColor DarkGray
Write-Host "  AZURE_TENANT_ID      = $resolvedTenantId" -ForegroundColor Yellow
Write-Host "  AZURE_CLIENT_ID      = $appId" -ForegroundColor Yellow

if ($clientSecret) {
    Write-Host "  AZURE_CLIENT_SECRET  = $clientSecret" -ForegroundColor Yellow
} elseif ($SkipSecretCreation) {
    Write-Host "  AZURE_CLIENT_SECRET  = <unchanged -- existing secret still valid>" -ForegroundColor DarkGray
} else {
    Write-Host "  AZURE_CLIENT_SECRET  = <create manually in Azure Portal>" -ForegroundColor Red
}

Write-Host "`n  App details:" -ForegroundColor White
Write-Host "  -----------------------------------------" -ForegroundColor DarkGray
$expiryStr = $endDate.ToString("yyyy-MM-dd")
Write-Host "  Display Name : $AppDisplayName"
Write-Host "  App (client) ID : $appId"
Write-Host "  Object ID    : $($app.Id)"
if (-not $SkipSecretCreation) {
    Write-Host "  Secret Expiry: $expiryStr"
}
Write-Host "  Permissions  : $($permissionIds.Count) application roles"
if ($isUpdate) {
    if ($toAdd.Count -gt 0 -or $toRemove.Count -gt 0) {
        Write-Host "  Changes      : +$($toAdd.Count) added, -$($toRemove.Count) removed" -ForegroundColor Yellow
    } else {
        Write-Host "  Changes      : No changes needed" -ForegroundColor Green
    }
}
Write-Host ""

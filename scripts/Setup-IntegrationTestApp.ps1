<#
.SYNOPSIS
    Creates an Entra ID app registration for Intune Commander integration tests
    and grants all required Microsoft Graph application permissions.

.DESCRIPTION
    This script:
    1. Connects to Azure (interactive login or existing session)
    2. Creates an app registration with a client secret
    3. Assigns all required Microsoft Graph application permissions
    4. Triggers admin consent for the permissions
    5. Outputs the values needed for GitHub Actions secrets

.NOTES
    Prerequisites:
      - Az PowerShell module: Install-Module Az -Scope CurrentUser
      - Entra ID role: Global Admin or Application Admin + Privileged Role Admin (for consent)
      - Run in PowerShell 5.1+

    After running, add these GitHub repository secrets:
      AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET
#>

[CmdletBinding()]
param(
    # ---------- PLACEHOLDER VARIABLES -- fill these in ----------

    # Display name for the app registration
    [string]$AppDisplayName = "IntuneCommander-IntegrationTests",

    # How long the client secret is valid (default: 180 days)
    [int]$SecretExpirationDays = 180,

    # Tenant ID -- leave empty to use the current Az context tenant
    [string]$TenantId = "",

    # Subscription ID -- leave empty to use the current Az context subscription
    [string]$SubscriptionId = ""
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

# --- 4. Create the app registration -------------------------------------------

Write-Host "`n=== Step 4: Creating app registration '$AppDisplayName' ===" -ForegroundColor Cyan

# Check if it already exists
$existingApp = Get-AzADApplication -Filter "displayName eq '$AppDisplayName'" -ErrorAction SilentlyContinue
if ($existingApp) {
    Write-Host "  App registration already exists: $($existingApp.AppId)" -ForegroundColor Yellow
    Write-Host "  Reusing existing app. Delete it first if you want a fresh start." -ForegroundColor Yellow
    $app = $existingApp
} else {
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

    Write-Host "  Created: $($app.AppId)" -ForegroundColor Green
}

$appId = $app.AppId

# --- 5. Create a client secret ------------------------------------------------

Write-Host "`n=== Step 5: Creating client secret ===" -ForegroundColor Cyan

$endDate = (Get-Date).AddDays($SecretExpirationDays)
$secret = New-AzADAppCredential -ApplicationId $appId -EndDate $endDate

# The secret value is only available immediately after creation
$clientSecret = $secret.SecretText
if (-not $clientSecret) {
    Write-Warning "  Could not retrieve secret value. You may need to create one manually in the Azure portal."
} else {
    $expiryStr = $endDate.ToString("yyyy-MM-dd")
    Write-Host "  Secret created (expires: $expiryStr)" -ForegroundColor Green
}

# --- 6. Create the service principal (enterprise app) -------------------------

Write-Host "`n=== Step 6: Creating service principal ===" -ForegroundColor Cyan

$spn = Get-AzADServicePrincipal -Filter "appId eq '$appId'" -ErrorAction SilentlyContinue
if (-not $spn) {
    $spn = New-AzADServicePrincipal -ApplicationId $appId
    Write-Host "  Created service principal: $($spn.Id)" -ForegroundColor Green
} else {
    Write-Host "  Service principal already exists: $($spn.Id)" -ForegroundColor Yellow
}

# --- 7. Grant admin consent (assign app roles) --------------------------------

Write-Host "`n=== Step 7: Granting admin consent for $($permissionIds.Count) permissions ===" -ForegroundColor Cyan

# Get an access token for the Microsoft Graph API to assign app roles
$graphToken = (Get-AzAccessToken -ResourceUrl "https://graph.microsoft.com").Token
$headers = @{
    "Authorization" = "Bearer $graphToken"
    "Content-Type"  = "application/json"
}

$consentUrl = "https://graph.microsoft.com/v1.0/servicePrincipals/$($spn.Id)/appRoleAssignments"

foreach ($perm in $permissionIds) {
    $body = @{
        principalId = $spn.Id
        resourceId  = $graphSpn.Id
        appRoleId   = $perm.Id
    } | ConvertTo-Json

    try {
        $null = Invoke-RestMethod -Uri $consentUrl -Method POST -Headers $headers -Body $body
        Write-Host "  [OK] Consented: $($perm.Name)" -ForegroundColor Green
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 409) {
            Write-Host "  [~] Already consented: $($perm.Name)" -ForegroundColor Yellow
        } else {
            Write-Warning "  [X] Failed to consent '$($perm.Name)': $($_.Exception.Message)"
        }
    }
}

# --- 8. Output summary --------------------------------------------------------

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "  App Registration Setup Complete!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan

Write-Host "`n  Add these as GitHub repository secrets:" -ForegroundColor White
Write-Host "  -----------------------------------------" -ForegroundColor DarkGray
Write-Host "  AZURE_TENANT_ID      = $resolvedTenantId" -ForegroundColor Yellow
Write-Host "  AZURE_CLIENT_ID      = $appId" -ForegroundColor Yellow

if ($clientSecret) {
    Write-Host "  AZURE_CLIENT_SECRET  = $clientSecret" -ForegroundColor Yellow
} else {
    Write-Host "  AZURE_CLIENT_SECRET  = <create manually in Azure Portal>" -ForegroundColor Red
}

Write-Host "`n  GitHub -> Settings -> Secrets and variables -> Actions -> New repository secret" -ForegroundColor DarkGray

Write-Host "`n  App details:" -ForegroundColor White
Write-Host "  -----------------------------------------" -ForegroundColor DarkGray
$expiryStr = $endDate.ToString("yyyy-MM-dd")
Write-Host "  Display Name : $AppDisplayName"
Write-Host "  App (client) ID : $appId"
Write-Host "  Object ID    : $($app.Id)"
Write-Host "  Secret Expiry: $expiryStr"
Write-Host "  Permissions  : $($permissionIds.Count) application roles granted"
Write-Host ""

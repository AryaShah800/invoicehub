<#
.SYNOPSIS
  Creates the Azure resources InvoiceHub runs on and configures the web app. Run it yourself, once.

.DESCRIPTION
  Creates: a resource group, an Azure SQL server and database, an App Service plan (Linux) and a web app on .NET 8.
  Then sets the app up for production: HTTPS only, TLS 1.2, FTP off, WebSockets on (SignalR), the connection string,
  and two generated secrets (the JWT signing key and the payment webhook secret).

  It does not deploy code. The Azure DevOps pipeline (azure-pipelines.yml) does that.

  Prerequisites: the Azure CLI, then `az login` and `az account set --subscription <id>`.

  Cost: the default plan is the free F1 App Service tier and the Basic SQL database, which is about US$5 a month
  and comes out of your Azure credit. Delete everything when you are done with:
    az group delete --name <prefix>-rg --yes

  Secrets: the SQL admin password is read from the INVOICEHUB_SQL_ADMIN_PASSWORD environment variable, or prompted
  for. The generated secrets are written straight to App Service settings through a temporary file and never printed.
  Re-running keeps existing secrets, so a second run does not sign everyone out.

.EXAMPLE
  $env:INVOICEHUB_SQL_ADMIN_PASSWORD = '<a strong password>'
  ./infra/provision.ps1 -Prefix aryainvoice
#>
[CmdletBinding()]
param(
    # Lower-case letters and digits. Becomes part of public names (<prefix>-api.azurewebsites.net), so it must be unique.
    [Parameter(Mandatory)]
    [ValidatePattern('(?-i)^[a-z][a-z0-9]{2,15}$')]
    [string]$Prefix,

    # Azure for Students subscriptions can restrict regions. If creation fails with a policy error, try another region.
    [string]$Location = 'eastasia',

    [string]$SqlAdminUser = 'invoicehubadmin',

    # F1 is free but has no Always On and a small WebSocket limit. Use B1 for something steadier.
    [string]$AppServiceSku = 'F1',

    [string]$SqlServiceObjective = 'Basic'
)

$ErrorActionPreference = 'Stop'

$resourceGroup = "$Prefix-rg"
$sqlServer = "$Prefix-sql"
$database = 'invoicehub'
$plan = "$Prefix-plan"
$webApp = "$Prefix-api"

function Invoke-Az {
    # Runs an az command and stops the script if it fails. az reports errors through its exit code, not exceptions.
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed with exit code $LASTEXITCODE" }
    return $output
}

function New-Secret([int]$bytes = 48) {
    $buffer = New-Object byte[] $bytes
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($buffer)
    $rng.Dispose()
    return [Convert]::ToBase64String($buffer)
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'The Azure CLI (az) is not installed.' }
$account = Invoke-Az account show --query '{name:name, id:id}' -o json | ConvertFrom-Json
Write-Host "Subscription: $($account.name)"

$sqlPassword = $env:INVOICEHUB_SQL_ADMIN_PASSWORD
if ([string]::IsNullOrEmpty($sqlPassword)) {
    $secure = Read-Host -AsSecureString -Prompt 'SQL admin password (at least 16 characters)'
    $sqlPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
if ($sqlPassword.Length -lt 16) { throw 'Use a SQL admin password of at least 16 characters.' }

Write-Host "Resource group $resourceGroup in $Location"
Invoke-Az group create --name $resourceGroup --location $Location --output none

Write-Host "Azure SQL server $sqlServer"
Invoke-Az sql server create --name $sqlServer --resource-group $resourceGroup --location $Location `
    --admin-user $SqlAdminUser --admin-password $sqlPassword --minimal-tls-version 1.2 --output none
# Lets Azure services, including the Azure DevOps hosted agents that run the migrations, reach the server.
Invoke-Az sql server firewall-rule create --name AllowAzureServices --server $sqlServer --resource-group $resourceGroup `
    --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none

Write-Host "Database $database ($SqlServiceObjective)"
Invoke-Az sql db create --name $database --server $sqlServer --resource-group $resourceGroup `
    --edition Basic --service-objective $SqlServiceObjective --output none

Write-Host "App Service plan $plan ($AppServiceSku, Linux) and web app $webApp"
Invoke-Az appservice plan create --name $plan --resource-group $resourceGroup --location $Location `
    --is-linux --sku $AppServiceSku --output none
Invoke-Az webapp create --name $webApp --resource-group $resourceGroup --plan $plan --runtime 'DOTNETCORE:8.0' --output none

Write-Host 'Hardening the web app'
Invoke-Az webapp update --name $webApp --resource-group $resourceGroup --https-only true --output none
Invoke-Az webapp config set --name $webApp --resource-group $resourceGroup `
    --min-tls-version 1.2 --ftps-state Disabled --web-sockets-enabled true --http20-enabled true --output none
if ($AppServiceSku -notin @('F1', 'D1')) {
    Invoke-Az webapp config set --name $webApp --resource-group $resourceGroup --always-on true --output none
}

# The connection string is stored as a SQLAzure connection string. .NET reads it as ConnectionStrings:InvoiceHub.
$connectionString = "Server=tcp:$sqlServer.database.windows.net,1433;Database=$database;User ID=$SqlAdminUser;Password=$sqlPassword;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
Invoke-Az webapp config connection-string set --name $webApp --resource-group $resourceGroup `
    --connection-string-type SQLAzure --settings "InvoiceHub=$connectionString" --output none

# Keep secrets that already exist, so re-running this script does not rotate them.
$existing = (Invoke-Az webapp config appsettings list --name $webApp --resource-group $resourceGroup -o json | ConvertFrom-Json).name
$settings = @(
    @{ name = 'ASPNETCORE_ENVIRONMENT'; value = 'Production'; slotSetting = $false },
    # App Service terminates TLS in front of the app. This makes the app see the real scheme and client address,
    # which HTTPS redirection, HSTS and the per-address rate limits rely on.
    @{ name = 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'; value = 'true'; slotSetting = $false },
    @{ name = 'AllowedHosts'; value = "$webApp.azurewebsites.net"; slotSetting = $false }
)
if ($existing -notcontains 'Jwt__SigningKey') { $settings += @{ name = 'Jwt__SigningKey'; value = (New-Secret); slotSetting = $false } }
if ($existing -notcontains 'PaymentWebhook__Secret') { $settings += @{ name = 'PaymentWebhook__Secret'; value = (New-Secret); slotSetting = $false } }

$file = [System.IO.Path]::GetTempFileName()
try {
    ConvertTo-Json -InputObject $settings | Set-Content -Path $file -Encoding utf8
    Invoke-Az webapp config appsettings set --name $webApp --resource-group $resourceGroup --settings "@$file" --output none
}
finally {
    Remove-Item $file -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host 'Done. Put these in the Azure DevOps variable group "invoicehub-prod":'
Write-Host "  webAppName        $webApp"
Write-Host "  sqlServerName     $sqlServer"
Write-Host "  sqlDatabaseName   $database"
Write-Host "  sqlAdminUser      $SqlAdminUser"
Write-Host '  sqlAdminPassword  (the password you entered; mark it secret)'
Write-Host '  azureServiceConnection  (the name of your Azure service connection)'
Write-Host ''
Write-Host "Site (after the first deployment): https://$webApp.azurewebsites.net/health/ready"
Write-Host 'To read the webhook secret, to give to a payment provider:'
Write-Host "  az webapp config appsettings list --name $webApp --resource-group $resourceGroup --query ""[?name=='PaymentWebhook__Secret'].value"" -o tsv"

param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceName,

    [Parameter(Mandatory = $true)]
    [string]$InstallPath,

    [string]$DisplayName = "DaemonQueueQuery Worker",
    [string]$Description = "DaemonQueueQuery .NET Worker Service",
    [string]$Environment = "Production",
    [string]$ConnectionString = ""
)

$exePath = Join-Path $InstallPath "DaemonQueueQuery.exe"
if (-not (Test-Path $exePath)) {
    $exePath = Join-Path $InstallPath "DaemonQueueQuery.dll"
}

$binPath = "`"$exePath`""
if ($exePath.EndsWith(".dll")) {
    $dotnet = "$Env:ProgramFiles\dotnet\dotnet.exe"
    $binPath = "`"$dotnet`" `"$exePath`""
}

sc.exe create $ServiceName binPath= $binPath start= auto | Out-Null
sc.exe description $ServiceName "$Description" | Out-Null
sc.exe config $ServiceName DisplayName= "$DisplayName" | Out-Null

# Set service-specific environment variables
$envKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $envKey -Name "Environment" -PropertyType MultiString -Value @(
    "DOTNET_ENVIRONMENT=$Environment"
) -Force | Out-Null

if ($ConnectionString) {
    New-ItemProperty -Path $envKey -Name "Environment" -PropertyType MultiString -Value @(
        "DOTNET_ENVIRONMENT=$Environment",
        "ConnectionStrings__DefaultConnection=$ConnectionString"
    ) -Force | Out-Null
}

Write-Host "Service $ServiceName installed. Run: sc.exe start $ServiceName"

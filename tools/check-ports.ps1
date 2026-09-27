<#
    Fails when a port this solution depends on cannot actually be bound on this machine.

    Why this exists
    ---------------
    Windows hands out "excluded port ranges" to Hyper-V, WSL, Docker and to services that reserve
    ranges. A socket opened inside one of them fails with

        SocketException (10013): An attempt was made to access a socket in a way forbidden by its
        access permissions.

    Kestrel treats that as fatal. The host prints its startup banner and then dies, which is exactly
    what "the app starts and then stops working" looked like from the outside. It is also invisible
    in a diff, because the port numbers themselves look perfectly reasonable.

    On the machine this was written for, the exclusions covered 5141-5240 and 5241-5340, which
    contained every port the project had configured: 5199 and 5210 in the documentation, 5270 and
    5273 in the launch profiles.

    Usage
    -----
        powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-ports.ps1

    Exit 0 when every configured port is bindable, exit 1 otherwise.
    The ranges move between reboots, so run this whenever the app "starts and then stops".
#>
[CmdletBinding()]
param(
    [string]$Root
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is empty in a param() default on PowerShell 5.1, so resolve it in the body.
if ([string]::IsNullOrWhiteSpace($Root)) {
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Root = Split-Path -Parent $here
}

function Get-ExcludedRanges {
    $ranges = @()
    $lines = netsh interface ipv4 show excludedportrange protocol=tcp 2>$null
    foreach ($line in $lines) {
        if ($line -match '^\s*(\d{1,5})\s+(\d{1,5})\s*(\*?)\s*$') {
            $ranges += [pscustomobject]@{
                Start    = [int]$Matches[1]
                End      = [int]$Matches[2]
                AdminSet = ($Matches[3] -eq '*')
            }
        }
    }
    return $ranges
}

function Add-Port([hashtable]$Ports, [int]$Port, [string]$Source) {
    if ($Ports.ContainsKey($Port)) { $Ports[$Port] = @($Ports[$Port]) + $Source }
    else { $Ports[$Port] = @($Source) }
}

function Get-ConfiguredPorts {
    # A plain hashtable, not [ordered]: an OrderedDictionary resolves an int indexer to its
    # positional overload, so $ports[5680] would throw instead of adding a key.
    $ports = @{}
    $sources = @(
        (Join-Path $Root 'WebApi\Properties\launchSettings.json'),
        (Join-Path $Root 'WebApp\WebApp\Properties\launchSettings.json')
    )
    foreach ($file in $sources) {
        if (-not (Test-Path $file)) { continue }
        $text = Get-Content -Raw $file
        foreach ($m in [regex]::Matches($text, '"applicationUrl"\s*:\s*"([^"]+)"')) {
            foreach ($u in [regex]::Matches($m.Groups[1].Value, ':(\d{2,5})')) {
                Add-Port $ports ([int]$u.Groups[1].Value) $file.Replace($Root, '')
            }
        }
    }

    # The UI's fallback address, which must also be reachable.
    $program = Join-Path $Root 'WebApp\WebApp\Program.cs'
    if (Test-Path $program) {
        $m = [regex]::Match((Get-Content -Raw $program), 'return published \?\? "http://localhost:(\d+)"')
        if ($m.Success) { Add-Port $ports ([int]$m.Groups[1].Value) 'WebApp\WebApp\Program.cs (fallback)' }
    }
    return $ports
}

function Test-Bindable([int]$Port) {
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        return $true
    }
    catch { return $false }
    finally { if ($listener) { $listener.Stop() } }
}

$excluded = Get-ExcludedRanges
$ports = Get-ConfiguredPorts

Write-Host ''
Write-Host 'Windows excluded TCP ranges' -ForegroundColor Cyan
if ($excluded.Count -eq 0) { Write-Host '  (none reported)' }
foreach ($range in $excluded) {
    $suffix = if ($range.AdminSet) { '  (administered)' } else { '' }
    Write-Host ("  {0,6} - {1,-6}{2}" -f $range.Start, $range.End, $suffix)
}

Write-Host ''
Write-Host 'Configured ports' -ForegroundColor Cyan
$failed = 0
foreach ($port in ($ports.Keys | Sort-Object)) {
    $inRange = $excluded | Where-Object { $port -ge $_.Start -and $port -le $_.End } | Select-Object -First 1
    $bindable = Test-Bindable $port
    $verdict = if ($bindable) { 'bindable' } else { 'CANNOT BIND' }
    $colour = if ($bindable) { 'Green' } else { 'Red' }
    $note = if ($inRange) { " [inside excluded range $($inRange.Start)-$($inRange.End)]" } else { '' }
    Write-Host ("  {0,-6} {1,-12}{2}" -f $port, $verdict, $note) -ForegroundColor $colour
    foreach ($source in $ports[$port]) {
        Write-Host ("         {0}" -f $source) -ForegroundColor DarkGray
    }
    if (-not $bindable) { $failed++ }
}

Write-Host ''
if ($failed -gt 0) {
    Write-Host "$failed port(s) cannot be bound. Move them out of the excluded ranges," -ForegroundColor Red
    Write-Host 'or run the API with an explicit free port:' -ForegroundColor Red
    Write-Host "  dotnet run --project WebApi --urls http://127.0.0.1:<free-port>" -ForegroundColor Yellow
    Write-Host "  `$env:ApiBaseUrl='http://127.0.0.1:<free-port>'" -ForegroundColor Yellow
    exit 1
}

Write-Host 'Every configured port is bindable.' -ForegroundColor Green
exit 0

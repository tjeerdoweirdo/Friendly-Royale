param(
    [int]$MaxPlayers = 2,
    [string]$JoinFile = "join_code.txt",
    [string]$Exe = "Friendly Royale.exe",
    [int]$TimeoutSec = 60,
    [switch]$Relay
)

Write-Host "Starting host-for-all..."

# Resolve exe path relative to this script
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

if (!(Test-Path $Exe)) {
    Write-Error "Executable not found: $Exe"
    exit 1
}

# Clean previous join file
if (Test-Path $JoinFile) { Remove-Item -Force $JoinFile }

if ($Relay.IsPresent) { $relayFlag = "-relay" } else { $relayFlag = "" }

# Launch the game in headless/batch as a host and write join code to file
$ArgsList = @(
    '-batchmode','-nographics','-hostForAll',"-maxPlayers",$MaxPlayers.ToString(),"-joinFile","$JoinFile",$relayFlag,
    '-logFile','host_cli.log'
) | Where-Object { $_ -ne '' }

Write-Host "Launching: $Exe $($ArgsList -join ' ')"

$proc = Start-Process -FilePath $Exe -ArgumentList $ArgsList -PassThru -NoNewWindow

# Wait for join file to appear and contain a code
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$code = $null
while ((Get-Date) -lt $deadline) {
    if (Test-Path $JoinFile) {
        try {
            $content = Get-Content -Path $JoinFile -Raw -ErrorAction Stop
            if ($content -and $content.Trim().Length -gt 0) {
                $code = $content.Trim()
                break
            }
        } catch {}
    }
    Start-Sleep -Milliseconds 500
}

if ($null -eq $code) {
    Write-Warning "Timed out waiting for join code. Check host_cli.log for details."
} else {
    Write-Host "JOIN CODE: $code"
    Write-Host "Share this code with players to join via Relay."
}

Write-Host "Process Id: $($proc.Id). Use Stop-Process -Id $($proc.Id) to terminate the host."

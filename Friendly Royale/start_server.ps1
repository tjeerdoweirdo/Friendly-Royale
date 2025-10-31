param(
    [switch]$Tail
)

Write-Host "Starting Friendly Royale Dedicated Server..."

# Resolve paths relative to this script
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$Exe = Join-Path $ScriptDir 'Bulds/Friendly Royale.exe'
$Log = Join-Path $ScriptDir 'Bulds/server.log'

if (!(Test-Path $Exe)) {
    Write-Error "Executable not found: $Exe"
    exit 1
}

# Clean previous log to make tailing clearer
if (Test-Path $Log) { Remove-Item -Force $Log }

# Start headless server and write logs
$ArgsList = @('-batchmode','-nographics','-server','-logFile',$Log)
Write-Host "Launching: $Exe $($ArgsList -join ' ')"
$proc = Start-Process -FilePath $Exe -ArgumentList $ArgsList -PassThru -NoNewWindow
Write-Host "Process Id: $($proc.Id). Use 'Stop-Process -Id $($proc.Id)' to stop it."

if ($Tail) {
    # Wait until the log is created, then tail it
    $deadline = (Get-Date).AddSeconds(15)
    while (!(Test-Path $Log) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (Test-Path $Log) {
        Write-Host "=== Tailing $Log (Ctrl+C to stop) ==="
        Get-Content -Path $Log -Wait -Tail 80
    } else {
        Write-Warning "Log file not found yet: $Log"
    }
}

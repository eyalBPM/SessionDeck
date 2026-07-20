# SessionDeck uninstaller. Removes the app, the Claude Code hooks, the VSCode extension,
# the user-PATH entry and the auto-start registry value. User settings in
# %APPDATA%\SessionDeck are kept unless -PurgeConfig is passed.
# PowerShell 5.1 compatible.
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\SessionDeck",
    [switch]$PurgeConfig
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallDir 'SessionDeck.exe'

# SessionDeck.exe is a GUI-subsystem binary: PowerShell 5.1 neither waits for it nor
# captures its output via `&`. Start-Process -Wait with redirected streams does both.
function Invoke-SessionDeck([string]$ExePath, [string]$Arguments) {
    $outFile = [IO.Path]::GetTempFileName()
    $errFile = [IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath $ExePath -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $outFile -RedirectStandardError $errFile
        $text = @()
        foreach ($f in $outFile, $errFile) {
            if ((Get-Item $f).Length -gt 0) { $text += (Get-Content $f -Encoding UTF8) }
        }
        return @{ ExitCode = $p.ExitCode; Output = $text }
    } finally {
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
    }
}

# --- 1. Stop a running instance cleanly (see install.ps1 for why not Stop-Process first) ---
if (Get-Process SessionDeck -ErrorAction SilentlyContinue) {
    Write-Host "Stopping the running SessionDeck instance..."
    if (Test-Path $exe) { Invoke-SessionDeck $exe 'quit' | Out-Null }
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline -and (Get-Process SessionDeck -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 250
    }
    Get-Process SessionDeck -ErrorAction SilentlyContinue | Stop-Process -Force
}

# --- 2. Remove the Claude Code hooks (while the exe still exists) ---
if (Test-Path $exe) {
    Write-Host "Removing the Claude Code hooks..."
    (Invoke-SessionDeck $exe 'uninstall-hooks').Output | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Warning "$exe not found - if hooks are still registered, remove the SessionDeck entries from ~/.claude/settings.json manually."
}

# --- 3. Remove the VSCode extension ---
if (Get-Command code -ErrorAction SilentlyContinue) {
    & code --uninstall-extension eyal-sinay.sessiondeck-connector 2>&1 | Out-Null
    Write-Host "VSCode extension removed (if it was installed)."
} else {
    Write-Warning "VSCode 'code' command not found - remove the 'SessionDeck Connector' extension from VSCode manually if present."
}

# --- 4. Remove the user-PATH entry ---
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath) {
    $kept = ($userPath -split ';') | Where-Object { $_ -and ($_.TrimEnd('\') -ine $InstallDir.TrimEnd('\')) }
    [Environment]::SetEnvironmentVariable('Path', ($kept -join ';'), 'User')
}

# --- 5. Remove the auto-start value ---
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SessionDeck' -ErrorAction SilentlyContinue

# --- 6. Delete the install dir (move out of it first in case we run from inside it) ---
if (Test-Path $InstallDir) {
    Set-Location $env:USERPROFILE
    Remove-Item -Path $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir"
}

# --- 7. User settings ---
$configDir = Join-Path $env:APPDATA 'SessionDeck'
if ($PurgeConfig) {
    if (Test-Path $configDir) { Remove-Item -Path $configDir -Recurse -Force; Write-Host "Removed $configDir" }
} elseif (Test-Path $configDir) {
    Write-Host "User settings kept at $configDir (re-run with -PurgeConfig to remove them too)."
}

Write-Host ""
Write-Host "SessionDeck uninstalled."

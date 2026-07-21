# SessionDeck release script - one command from committed code to a published GitHub release.
# Usage:
#   .\release.ps1            # full release of the version in SessionDeck.csproj
#   .\release.ps1 -DryRun    # everything except the sync-commit and the actual release
#
# The version in SessionDeck.csproj is the single source of truth. The script:
#   guards (clean tree, main, unreleased version) -> syncs the hook-script version header
#   -> publishes self-contained -> runs the install-hooks tests against the published exe
#   -> packages the vsix only if the extension changed -> zips -> gh release create.
# PowerShell 5.1 compatible.
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
Set-Location $repo
$gh = "C:\Program Files\GitHub CLI\gh.exe"
if (-not (Test-Path $gh)) { $gh = 'gh' }   # fall back to PATH on other machines

function Fail([string]$msg) { Write-Host "RELEASE BLOCKED: $msg" -ForegroundColor Red; exit 1 }
function Step([string]$msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }

# --- Guards -------------------------------------------------------------------
Step "Preflight checks"

if (git status --porcelain) { Fail "working tree is not clean - commit or stash first." }
$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') { Fail "on branch '$branch' - releases are cut from main." }

$csproj = Get-Content (Join-Path $repo 'SessionDeck.csproj') -Raw
if ($csproj -notmatch '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>') { Fail "no <Version> in SessionDeck.csproj" }
$ver = $Matches[1]
$tag = "v$ver"

git fetch --tags --quiet
if (git tag --list $tag) { Fail "tag $tag already exists - bump <Version> in SessionDeck.csproj first." }

$prevTag = git tag --list 'v*' --sort=-v:refname | Select-Object -First 1
Write-Host "  version: $ver  (previous release: $(if ($prevTag) { $prevTag } else { 'none' }))"

# --- Sync the hook script's version header (BOM must survive - PS 5.1 + Hebrew) ---
Step "Hook script version header"
$hookPath = Join-Path $repo 'hooks\sessiondeck-hook.ps1'
$hookText = [IO.File]::ReadAllText($hookPath)
if ($hookText -notmatch '(?m)^# Version: (\S+)') { Fail "no '# Version:' header in the hook script." }
$hookVer = $Matches[1]
if ($hookVer -ne $ver) {
    if ($DryRun) {
        Write-Host "  would sync $hookVer -> $ver (+ commit)"
    } else {
        $hookText = $hookText -replace '(?m)^# Version: \S+', "# Version: $ver"
        [IO.File]::WriteAllText($hookPath, $hookText, [Text.UTF8Encoding]::new($true))
        $bytes = [IO.File]::ReadAllBytes($hookPath)
        if (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) { Fail "BOM lost while syncing the hook header." }
        git add $hookPath
        git commit -m "chore: sync hook script version header to $ver" --quiet
        Write-Host "  synced $hookVer -> $ver and committed."
    }
} else {
    Write-Host "  already $ver."
}

# --- Build & test ---------------------------------------------------------------
Step "Publish (self-contained single file)"
dotnet publish -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed." }
$pubDir = Join-Path $repo 'bin\Release\net10.0-windows\win-x64\publish'
$pubExe = Join-Path $pubDir 'SessionDeck.exe'

$exeVer = ((Get-Item $pubExe).VersionInfo.ProductVersion -split '\+')[0]
if ($exeVer -ne $ver) { Fail "published exe reports $exeVer, expected $ver." }
$bytes = [IO.File]::ReadAllBytes((Join-Path $pubDir 'hooks\sessiondeck-hook.ps1'))
if (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) { Fail "published hook script lost its BOM." }

Step "install-hooks tests against the published exe"
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tests\install-hooks.tests.ps1') -Exe $pubExe
if ($LASTEXITCODE -ne 0) { Fail "tests failed." }

# --- VSCode extension ------------------------------------------------------------
Step "VSCode extension"
$extDir = Join-Path $repo 'vscode-extension'
$pkg = Get-Content (Join-Path $extDir 'package.json') -Raw | ConvertFrom-Json
$extVer = $pkg.version
$vsix = Join-Path $extDir "sessiondeck-connector-$extVer.vsix"

$extChanged = $true
if ($prevTag) {
    git diff --quiet $prevTag HEAD -- vscode-extension/src vscode-extension/package.json
    $extChanged = ($LASTEXITCODE -ne 0)
}
if ($extChanged -and $prevTag -and -not $DryRun) {
    # The extension changed but its version didn't -> the installed extension would look
    # identical to the old one. Block: bump vscode-extension/package.json first.
    $prevPkg = git show "${prevTag}:vscode-extension/package.json" | ConvertFrom-Json
    if ($prevPkg.version -eq $extVer) { Fail "vscode-extension changed since $prevTag but its package.json version is still $extVer - bump it." }
}
if ($extChanged -or -not (Test-Path $vsix)) {
    Write-Host "  packaging vsix $extVer..."
    Push-Location $extDir
    npm install --silent 2>&1 | Out-Null
    npx @vscode/vsce package 2>&1 | Select-Object -Last 1
    Pop-Location
    if (-not (Test-Path $vsix)) { Fail "vsce did not produce $vsix" }
} else {
    Write-Host "  unchanged since $prevTag - reusing sessiondeck-connector-$extVer.vsix"
}

# --- Stage & zip -----------------------------------------------------------------
Step "Package zip"
$stage = Join-Path $repo "publish\SessionDeck-$ver-win-x64"
$zip = "$stage.zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $pubExe $stage
Copy-Item (Join-Path $pubDir 'hooks') $stage -Recurse
Copy-Item $vsix $stage
Copy-Item (Join-Path $repo 'install.ps1'), (Join-Path $repo 'uninstall.ps1') $stage
Compress-Archive -Path "$stage\*" -DestinationPath $zip -Force
Write-Host "  $zip ($([math]::Round((Get-Item $zip).Length/1MB)) MB)"

# --- Release notes + publish -------------------------------------------------------
Step "Release notes"
$notesFile = Join-Path $env:TEMP "sessiondeck-release-notes-$ver.md"
$changes = if ($prevTag) { git log "$prevTag..HEAD" --no-merges --format='- %s' } else { @('- initial packaged release') }
@"
**Install:** download the zip, extract, run ``install.ps1`` (no admin, no .NET needed). **Upgrade** from any previous version the same way. Details in the [README](https://github.com/eyalBPM/SessionDeck#getting-started).

Versions in this zip: app $ver | extension $extVer | hooks $ver

### Changes since $(if ($prevTag) { $prevTag } else { 'the beginning' })

$($changes -join "`n")
"@ | Set-Content -Path $notesFile -Encoding UTF8
Get-Content $notesFile | ForEach-Object { "  | $_" }

if ($DryRun) {
    Step "DryRun - stopping before: git push, gh release create $tag"
    exit 0
}

Step "Publish"
git push origin main
& $gh release create $tag $zip --title $tag --notes-file $notesFile
if ($LASTEXITCODE -ne 0) { Fail "gh release create failed." }
Write-Host "`nDone." -ForegroundColor Green

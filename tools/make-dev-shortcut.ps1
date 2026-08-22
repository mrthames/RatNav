<#
.SYNOPSIS
    Puts a shortcut to your working copy of RatNav on the Desktop and in the Start Menu.

.DESCRIPTION
    A clone gives you source, not a thing you can click. `dotnet run --project src/RatNav.App`
    works and is what you want while changing code, but it holds a terminal open and it is a lot to
    remember when you only want to look at something.

    The shortcut is named "RatNav (dev)" and the name is doing real work. Once you have also
    installed a release there are two RatNavs on the machine, they look identical in the Start
    Menu, and they behave differently: this one is whatever you last compiled, and it does not
    rebuild itself. Launching the stale one and wondering why your change is missing is a bad
    twenty minutes.

.PARAMETER Configuration
    Debug by default, which is what you get from `dotnet build`. Pass Release if that is what you
    have built.

.PARAMETER Remove
    Deletes the shortcuts instead of creating them.

.EXAMPLE
    pwsh tools/make-dev-shortcut.ps1

.EXAMPLE
    pwsh tools/make-dev-shortcut.ps1 -Remove
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $Remove
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$name = 'RatNav (dev).lnk'

$places = @(
    [System.Environment]::GetFolderPath('Desktop'),
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
)

if ($Remove) {
    foreach ($place in $places) {
        $path = Join-Path $place $name
        if (Test-Path $path) { Remove-Item $path -Force; "removed: $path" }
    }
    return
}

# RatNav.App targets a Windows-specific framework, and the folder is named for it. Found rather
# than hardcoded, because that moniker changes with the SDK and a hardcoded one goes stale quietly.
$binaries = Join-Path $repo "src\RatNav.App\bin\$Configuration"

if (-not (Test-Path $binaries)) {
    throw "No $Configuration build found. Run: dotnet build src/RatNav.App -c $Configuration"
}

$exe = Get-ChildItem -Path $binaries -Filter 'RatNav.exe' -Recurse -File |
       Sort-Object LastWriteTime -Descending |
       Select-Object -First 1

if (-not $exe) {
    throw "No RatNav.exe under $binaries. Run: dotnet build src/RatNav.App -c $Configuration"
}

# The web app is built into the service's wwwroot and copied beside the executable. Without it the
# app starts and every page is blank, which looks like a bug in RatNav rather than a missing step.
$wwwroot = Join-Path $exe.DirectoryName 'wwwroot\index.html'
if (-not (Test-Path $wwwroot)) {
    Write-Warning "No web app in this build. Run: cd web; npm install; npm run build"
    Write-Warning "The shortcut will still be made, but RatNav's pages will be empty until you do."
}

# Is this build older than the code it was built from?
#
# This is the failure the shortcut's name exists to prevent, and naming it is not enough: a Release
# folder from last week looks exactly like one from ten minutes ago in Explorer. Saying so here is
# the difference between "my change is missing" and "oh, I need to rebuild".
#
# bin and obj are skipped because they are build output and always newer than the source; counting
# them would make every build look current.
$newest = Get-ChildItem -Path (Join-Path $repo 'src'), (Join-Path $repo 'web\src') -Recurse -File `
              -Include *.cs, *.xaml, *.ts, *.tsx, *.csproj -ErrorAction SilentlyContinue |
          Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules)\\' } |
          Sort-Object LastWriteTime -Descending |
          Select-Object -First 1

if ($newest -and $newest.LastWriteTime -gt $exe.LastWriteTime) {
    Write-Warning "This $Configuration build is older than the source it was built from."
    Write-Warning "  built:  $($exe.LastWriteTime)"
    Write-Warning "  newest: $($newest.LastWriteTime)  $($newest.Name)"
    Write-Warning "The shortcut will run the old one. Rebuild with:"
    Write-Warning "  dotnet build src/RatNav.App -c $Configuration"
}

$shell = New-Object -ComObject WScript.Shell

foreach ($place in $places) {
    $path = Join-Path $place $name

    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe.FullName
    $link.WorkingDirectory = $exe.DirectoryName
    $link.IconLocation = "$($exe.FullName),0"
    $link.Description = 'RatNav, built from your working copy. Not the installed release.'
    $link.Save()

    "created: $path"
}

""
"Points at: $($exe.FullName)"
"Built:     $($exe.LastWriteTime)"
""
"This does not rebuild itself. After changing code:"
"  dotnet build src/RatNav.App -c $Configuration"
""
"RatNav sits in the system tray once it starts. Right-click it for Open panel, or go to"
"http://localhost:8722/."

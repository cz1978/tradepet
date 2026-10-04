param([switch]$RepairShortcuts)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$installDirectory = Join-Path $repoRoot 'artifacts\TradePet-win-x64'
$executable = Join-Path $installDirectory 'TradePet.exe'

if (Get-Process -Name TradePet -ErrorAction SilentlyContinue) {
    throw 'Exit TradePet from its tray menu before updating the local installation.'
}
if (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'Runtime\python-runtime\python.exe'))) {
    throw 'Run scripts/build-release.ps1 first to create the bundled local installation.'
}

dotnet publish (Join-Path $repoRoot 'src\TradePet.App\TradePet.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --property:PublishSingleFile=false `
    --no-restore `
    --output $installDirectory
if ($LASTEXITCODE -ne 0) { throw 'Local TradePet update failed.' }

foreach ($file in @('TradePet.exe', 'TradePet.dll', 'TradePet.Core.dll', 'TradePet.Infrastructure.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory $file))) {
        throw "Local installation is missing $file."
    }
}

if ($RepairShortcuts) {
    $shortcutShell = New-Object -ComObject WScript.Shell
    $shortcutDirectories = @(
        [Environment]::GetFolderPath('Desktop'),
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'),
        (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar')
    )
    $repoPrefix = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    foreach ($directory in $shortcutDirectories) {
        if (-not (Test-Path -LiteralPath $directory)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $directory -Filter *.lnk -Recurse -File) {
            $shortcut = $shortcutShell.CreateShortcut($file.FullName)
            if ([IO.Path]::GetFileName($shortcut.TargetPath) -ine 'TradePet.exe' -or
                -not $shortcut.TargetPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            $shortcut.TargetPath = $executable
            $shortcut.WorkingDirectory = $installDirectory
            $shortcut.IconLocation = "$executable,0"
            $shortcut.Save()
            $verified = $shortcutShell.CreateShortcut($file.FullName)
            if ($verified.TargetPath -ine $executable -or $verified.WorkingDirectory -ine $installDirectory) {
                throw "Shortcut verification failed: $($file.FullName)"
            }
            Write-Output "Shortcut updated: $($file.FullName)"
        }
    }
}

Write-Output "Local installation updated: $executable"
Write-Output "Application SHA256: $((Get-FileHash -LiteralPath (Join-Path $installDirectory 'TradePet.dll')).Hash)"

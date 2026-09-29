param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$PythonPath = 'python'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
$runtimeDirectory = Join-Path $publishRoot 'Runtime\python-runtime'
$cacheDirectory = Join-Path $repoRoot 'artifacts\python-cache'
$stagingDirectory = Join-Path $cacheDirectory ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

# Official Windows x64 embeddable distribution; verify before extracting/executing.
# https://www.python.org/downloads/release/python-31315/
$archive = Join-Path $cacheDirectory 'python-3.13.15-embed-amd64.zip'
$expectedHash = 'd1f04d990aee1253d8569e8e5104e30fa9f5fa830899f14843448872d936a2cf'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://www.python.org/ftp/python/3.13.15/python-3.13.15-embed-amd64.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    throw "Python archive checksum mismatch. Remove $archive and rebuild."
}
Expand-Archive -LiteralPath $archive -DestinationPath $stagingDirectory

# Install wheels at build time, never into the builder's Python or on the user's PC.
& $PythonPath -m pip --isolated install --disable-pip-version-check --no-compile `
    --only-binary=:all: --platform win_amd64 --implementation cp --python-version 3.13 --abi cp313 `
    --index-url https://pypi.org/simple --target (Join-Path $stagingDirectory 'Lib\site-packages') `
    -r (Join-Path $repoRoot 'python\requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Unable to bundle the TradePet Python requirements.' }

# Keep imports isolated from the machine, while allowing the sibling worker modules.
@('python313.zip', '.', 'Lib\site-packages', '..\python', 'import site') |
    Set-Content -LiteralPath (Join-Path $stagingDirectory 'python313._pth') -Encoding ASCII
& (Join-Path $stagingDirectory 'python.exe') -X utf8 -c "import sys, struct, MetaTrader5, numpy; assert sys.version_info[:2] == (3, 13) and struct.calcsize('P') == 8; print('Bundled Python ready:', sys.version.split()[0], 'MetaTrader5', MetaTrader5.__version__, 'NumPy', numpy.__version__)"
if ($LASTEXITCODE -ne 0) { throw 'Bundled Python validation failed.' }

# Replace only this generated runtime beneath the explicitly selected publish root.
$resolvedRuntime = [IO.Path]::GetFullPath($runtimeDirectory)
if (-not $resolvedRuntime.StartsWith($publishRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Python runtime destination is outside the publish directory.'
}
if (Test-Path -LiteralPath $resolvedRuntime) { Remove-Item -LiteralPath $resolvedRuntime -Recurse -Force }
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedRuntime) -Force | Out-Null
Move-Item -LiteralPath $stagingDirectory -Destination $resolvedRuntime
& (Join-Path $resolvedRuntime 'python.exe') -X utf8 -c "import tradepet_mt5_worker, tradepet_mt5_history_worker; print('Bundled worker imports ready')"
if ($LASTEXITCODE -ne 0) { throw 'Bundled worker validation failed.' }

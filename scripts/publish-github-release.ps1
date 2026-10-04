param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$ChecksumPath,
    [Parameter(Mandatory = $true)][string]$NotesPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$repository = 'cz1978/tradepet'
$tag = "v$Version"
if ($Version -notmatch '^1\.0\.0-rc\.[0-9]+$') { throw 'Expected a TradePet candidate version.' }
if ((git -C $repoRoot remote get-url origin) -ne "https://github.com/$repository.git") { throw 'Unexpected Git remote.' }
if ((git -C $repoRoot branch --show-current) -ne 'main') { throw 'Publish from main.' }
if (git -C $repoRoot status --porcelain) { throw 'Commit the release changes before publishing.' }
$commit = git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the release commit.' }

$package = Get-Item -LiteralPath $PackagePath
$checksums = Get-Item -LiteralPath $ChecksumPath
$notes = Get-Item -LiteralPath $NotesPath
if ($package.Name -ne "TradePet-$Version-win-x64.zip" -or $checksums.Name -ne 'SHA256SUMS.txt') { throw 'Unexpected release asset names.' }
$packageHash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-Content -LiteralPath $checksums.FullName -Raw).Trim() -ne "$packageHash  $($package.Name)") { throw 'ZIP checksum does not match.' }

$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
$verificationDirectory = Join-Path $repoRoot ('artifacts\release-verify-' + [Guid]::NewGuid().ToString('N'))
try {
    $entries = @($archive.Entries.FullName)
    foreach ($required in @('TradePet.exe', 'TradePet.dll', 'README.md', 'README.en.md', 'INSTALL.md', 'INSTALL.en.md',
        'CHANGELOG.md', 'CHANGELOG.en.md', 'Runtime/python-runtime/python.exe', 'Runtime/TradePetBridge.ex5', 'Runtime/mt4/TradePetBridge.ex4')) {
        if ($required -notin $entries) { throw "Package is missing $required." }
    }
    if ($entries -match '(?i)(^|/)(\.git|bin|obj|TestResults)(/|$)|\.(db|sqlite|log|pfx|pem|key)$|(^|/)(\.env|appsettings\.Local\.json)$') {
        throw 'Package contains user data or development files.'
    }
    New-Item -ItemType Directory -Path $verificationDirectory | Out-Null
    $assemblyPath = Join-Path $verificationDirectory 'TradePet.dll'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry('TradePet.dll'), $assemblyPath)
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion -ne "$Version+$commit") {
        throw 'Package version/source commit does not match this release.'
    }
} finally { $archive.Dispose() }

# Use the existing Git credential helper without printing or persisting its token.
$env:GCM_INTERACTIVE = 'Never'
$credential = "protocol=https`nhost=github.com`n`n" | git -c credential.interactive=false credential fill 2>$null
if ($LASTEXITCODE -ne 0) { throw 'Existing GitHub authentication is unavailable.' }
$passwordLine = $credential | Where-Object { $_.StartsWith('password=') } | Select-Object -First 1
if (-not $passwordLine) { throw 'GitHub authentication did not provide a credential.' }
$handler = [Net.Http.HttpClientHandler]::new()
$handler.UseProxy = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMinutes(15)
$client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $passwordLine.Substring(9))
$client.DefaultRequestHeaders.UserAgent.ParseAdd('TradePet-release-publisher')
$client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
$client.DefaultRequestHeaders.Add('X-GitHub-Api-Version', '2022-11-28')
$credential = $passwordLine = $null

function Invoke-ReleaseApi([string]$Method, [string]$Url, $Body = $null, [switch]$AllowNotFound) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), $Url)
    try {
        if ($Body) { $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 5), [Text.Encoding]::UTF8, 'application/json') }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($AllowNotFound -and [int]$response.StatusCode -eq 404) { return $null }
            if (-not $response.IsSuccessStatusCode) { throw "GitHub request failed: $([int]$response.StatusCode)." }
            return $text | ConvertFrom-Json
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

try {
    $api = "https://api.github.com/repos/$repository"
    $release = Invoke-ReleaseApi GET "$api/releases/tags/$tag" -AllowNotFound
    if ($release -and -not $release.draft) { throw 'This version is already published; use a new version.' }
    $existingTag = git -C $repoRoot rev-parse --verify "refs/tags/$tag^{commit}" 2>$null
    if ($LASTEXITCODE -eq 0) {
        if ($existingTag -ne $commit) { throw 'The tag points to a different commit.' }
    } else {
        git -C $repoRoot tag -a $tag -m "TradePet $Version"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create the release tag.' }
    }
    $env:HTTP_PROXY = $env:HTTPS_PROXY = $env:ALL_PROXY = $null
    git -C $repoRoot -c http.proxy= -c http.https://github.com.proxy= push --atomic origin main $tag
    if ($LASTEXITCODE -ne 0) { throw 'Source/tag push failed.' }
    $releaseBody = @{ tag_name=$tag; target_commitish=$commit; name="TradePet $Version";
        body=(Get-Content -LiteralPath $notes.FullName -Raw -Encoding UTF8); draft=$true; prerelease=$true }
    if ($release) { $release = Invoke-ReleaseApi PATCH "$api/releases/$($release.id)" $releaseBody }
    else { $release = Invoke-ReleaseApi POST "$api/releases" $releaseBody }
    foreach ($assetFile in @($package, $checksums)) {
        $expectedHash = 'sha256:' + (Get-FileHash -LiteralPath $assetFile.FullName).Hash.ToLowerInvariant()
        $existingAsset = $release.assets | Where-Object { $_.name -eq $assetFile.Name } | Select-Object -First 1
        if ($existingAsset) {
            if ($existingAsset.digest -ne $expectedHash) { throw 'An existing draft asset differs from the prepared file.' }
            continue
        }
        Write-Output "Uploading $($assetFile.Name) ($($assetFile.Length) bytes)..."
        $stream = [IO.File]::OpenRead($assetFile.FullName)
        $content = [Net.Http.StreamContent]::new($stream)
        try {
            $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new(
                $(if ($assetFile.Extension -eq '.zip') { 'application/zip' } else { 'text/plain' }))
            $url = "https://uploads.github.com/repos/$repository/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($assetFile.Name))"
            $response = $client.PostAsync($url, $content).GetAwaiter().GetResult()
            try {
                if (-not $response.IsSuccessStatusCode) { throw "Asset upload failed: $([int]$response.StatusCode)." }
                $uploaded = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                if ($uploaded.state -ne 'uploaded' -or $uploaded.size -ne $assetFile.Length -or $uploaded.digest -ne $expectedHash) {
                    throw 'Uploaded asset verification failed.'
                }
            } finally { $response.Dispose() }
        } finally { $content.Dispose(); $stream.Dispose() }
    }
    $release = Invoke-ReleaseApi PATCH "$api/releases/$($release.id)" @{ draft=$false }
    if ($release.draft -or @($release.assets).Count -ne 2) { throw 'Published release verification failed.' }
    Write-Output "Published: $($release.html_url)"
} finally { $client.Dispose(); $handler.Dispose() }

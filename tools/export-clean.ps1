<#
.SYNOPSIS
    Изнася чисто копие на кода в отделна папка — това, което отива в GitHub.

.DESCRIPTION
    Работната папка носи и шум: сглобки (bin, obj, .vs), пакети за тестващите
    (dist, над 150 MB), бази и Excel файлове от проба, артефакти от издания,
    двоични близнаци на документи. Тук се копира САМО изброеното по-долу —
    всичко останало просто не идва. Списък на позволеното, а не на забраненото:
    нов шум в работната папка не може да се промъкне сам.

    В целевата папка .git не се пипа. При повторно изнасяне старите файлове се
    махат, а историята остава — изнасянето може да се повтаря, докато работата
    не се премести изцяло в хранилището.

    Хранилището с изданията (README и news.json) се прави само веднъж, ако го
    няма: после news.json се пише там, през GitHub, и не бива да се затрие.

.PARAMETER Destination
    Къде отива кодът. По подразбиране — GitHub\RiosvImports до работната папка.

.PARAMETER ReleasesDestination
    Къде се прави хранилището с изданията. По подразбиране —
    GitHub\RiosvImports-releases до работната папка.

.EXAMPLE
    .\tools\export-clean.ps1
#>
[CmdletBinding()]
param(
    [string] $Destination,
    [string] $ReleasesDestination
)

$ErrorActionPreference = 'Stop'

$source = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\')
$parent = Split-Path -Parent $source

if (-not $Destination) { $Destination = Join-Path $parent 'GitHub\RiosvImports' }
if (-not $ReleasesDestination) { $ReleasesDestination = Join-Path $parent 'GitHub\RiosvImports-releases' }

$Destination = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\')
$ReleasesDestination = [System.IO.Path]::GetFullPath($ReleasesDestination).TrimEnd('\')

# ------------------------------------------------------------------ пазачи

# Целта не е изворът, не е вътре в него и не е над него.
foreach ($target in @($Destination, $ReleasesDestination)) {
    if ($target -ieq $source) { throw "Целта е самата работна папка: $target" }
    if ($target.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Целта е вътре в работната папка: $target" }
    if ($source.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Целта съдържа работната папка: $target" }
}

# Пипа се само папка, която е празна или вече е изнесено копие.
if (Test-Path -LiteralPath $Destination) {
    $existing = @(Get-ChildItem -LiteralPath $Destination -Force)
    $isExport = Test-Path -LiteralPath (Join-Path $Destination 'RiosvImports.sln')

    if ($existing.Count -gt 0 -and -not $isExport) {
        throw "$Destination не е празна и не прилича на изнесено копие (няма RiosvImports.sln). Нищо не е пипнато."
    }

    # Старото копие си отива — освен историята.
    foreach ($item in $existing) {
        if ($item.Name -eq '.git') { continue }
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }
}
else {
    New-Item -ItemType Directory -Path $Destination | Out-Null
}

# ------------------------------------------------------------------ какво влиза

# Файловете в корена — поименно.
$rootFiles = @(
    '.gitignore'
    '.gitattributes'
    'README.md'
    'CHANGELOG.md'
    'RiosvImports.sln'
    'Directory.Build.props'
)

# Папките — с вида файлове, които се пускат от тях.
$folders = @(
    @{ Path = '.github'; Files = @('*.yml', '*.md') }
    # Без .docx: двоичен близнак на .txt, който git не може да покаже като разлика.
    @{ Path = 'docs';    Files = @('*.md', '*.txt') }
    @{ Path = 'schemas'; Files = @('*.xsd') }
    @{ Path = 'src';     Files = @('*.cs', '*.csproj', '*.json', '*.resx', '*.ico', '*.png', '*.manifest') }
    @{ Path = 'tests';   Files = @('*.cs', '*.csproj') }
    @{ Path = 'tools';   Files = @('*.ps1', '*.cs', '*.csproj', '*.md', '*.json') }
)

# Никъде и никога.
$skipDirs = @('bin', 'obj', '.vs', 'artifacts', 'publish', 'TestResults')

# Мъртвият код отпреди преработката. Не се вика отникъде. В работната папка
# още стои — махането му там чака изрично „да“ (КЪМ_GITHUB.md, т. 3), — а в
# хранилището не влиза: нов човек би го чел като жив код.
$dead = @(
    'src\RiosvImports.App\MainForm.cs'
    'src\RiosvImports.App\Controls\DropZone.cs'
    'src\RiosvImports.App\Controls\ResultsView.cs'
    'src\RiosvImports.App\Controls\ReportPicker.cs'
)

# ------------------------------------------------------------------ копиране

$copied = New-Object System.Collections.Generic.List[string]

function Copy-One([string] $relative) {
    $from = Join-Path $source $relative
    $to = Join-Path $Destination $relative

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
    Copy-Item -LiteralPath $from -Destination $to -Force

    $copied.Add($relative)
}

foreach ($file in $rootFiles) {
    if (Test-Path -LiteralPath (Join-Path $source $file)) { Copy-One $file }
}

foreach ($folder in $folders) {
    $base = Join-Path $source $folder.Path
    if (-not (Test-Path -LiteralPath $base)) { continue }

    Get-ChildItem -Path $base -Recurse -File -Force -Include $folder.Files | ForEach-Object {
        $relative = $_.FullName.Substring($source.Length).TrimStart('\')
        $parts = $relative.Split('\')

        if (@($parts | Where-Object { $skipDirs -contains $_ }).Count -gt 0) { return }
        if ($dead -contains $relative) { return }

        Copy-One $relative
    }
}

# ------------------------------------------------------------------ проверка

# Нищо, което прилича на ключ. Публичният ключ за правото на ползване е в кода
# нарочно и не е „PRIVATE KEY“.
$suspicious = @(Get-ChildItem -LiteralPath $Destination -Recurse -File -Force |
    Where-Object { $_.FullName -notmatch '\\\.git\\' } |
    Where-Object {
        $_.Extension -in @('.pem', '.pfx', '.p12', '.key', '.env') -or
        (Select-String -LiteralPath $_.FullName -Pattern 'BEGIN (EC |RSA |OPENSSH )?PRIVATE KEY' -Quiet)
    })

if ($suspicious.Count -gt 0) {
    throw "Изнесено е нещо, което прилича на ключ — копието НЕ е за качване:`n  " +
        (($suspicious | ForEach-Object { $_.FullName }) -join "`n  ")
}

# ------------------------------------------------------------------ изданията

$releasesTemplate = Join-Path $source 'tools\releases-repo'

if ((Test-Path -LiteralPath $releasesTemplate) -and -not (Test-Path -LiteralPath $ReleasesDestination)) {
    New-Item -ItemType Directory -Path $ReleasesDestination | Out-Null
    Copy-Item -Path (Join-Path $releasesTemplate '*') -Destination $ReleasesDestination -Recurse -Force
    $releasesMade = $true
}
else {
    $releasesMade = $false
}

# ------------------------------------------------------------------ отчет

$size = ($copied | ForEach-Object { (Get-Item -LiteralPath (Join-Path $Destination $_)).Length } |
    Measure-Object -Sum).Sum

Write-Host ''
Write-Host "Кодът е изнесен: $Destination" -ForegroundColor Green
Write-Host ("  {0} файла, {1:N1} MB" -f $copied.Count, ($size / 1MB))

$copied | Group-Object { ($_ -split '\\')[0] } | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-24} {1,4}" -f $_.Name, $_.Count)
}

Write-Host "  мъртъв код, оставен извън копието: $($dead.Count) файла" -ForegroundColor DarkGray

if ($releasesMade) {
    Write-Host ''
    Write-Host "Хранилището с изданията е направено: $ReleasesDestination" -ForegroundColor Green
}

if (-not (Test-Path -LiteralPath (Join-Path $Destination '.git'))) {
    Write-Host ''
    Write-Host 'Следва (КЪМ_GITHUB.md):' -ForegroundColor Yellow
    Write-Host "  cd `"$Destination`""
    Write-Host '  git init -b main'
    Write-Host '  git add -A'
    Write-Host '  git status          # чете се ред по ред, преди първия коммит'
}

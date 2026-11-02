[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('test', 'dev', 'prod', 'fast', 'inst', 'clean', 'folder')]
    [string]$Mode,

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'Continue'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:VersionFile = Join-Path $script:RepoRoot 'Directory.Build.props'
$script:ReleaseRoot = Join-Path $script:RepoRoot 'Release'
$script:AppProject = Join-Path $script:RepoRoot 'src\UberDashboard.App\UberDashboard.App.csproj'
$script:InstallerScript = Join-Path $script:RepoRoot 'installer\UberDashboard.iss'
$script:StepIndex = 0
$script:StepTotal = 1

function Set-StepPlan {
    param([int]$Total)

    $script:StepIndex = 0
    $script:StepTotal = [Math]::Max($Total, 1)
}

function Write-Step {
    param([string]$Message)

    $script:StepIndex++
    $percent = [int](($script:StepIndex / $script:StepTotal) * 100)
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Write-Host "[$timestamp] $Message"
    Write-Progress -Activity 'UberDashboard build pipeline' -Status $Message -PercentComplete $percent
}

function Invoke-Checked {
    param(
        [string]$Description,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory = $script:RepoRoot
    )

    Write-Step $Description

    if ($DryRun) {
        Write-Host "DRY-RUN: $FilePath $($Arguments -join ' ')"
        return
    }

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "Polecenie nie powiodło się: $FilePath $($Arguments -join ' ') (exit $exitCode)."
    }
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        if ($DryRun) {
            Write-Host "DRY-RUN: New-Item -ItemType Directory -Path `"$Path`""
            return
        }

        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-CurrentVersion {
    $content = Get-Content $script:VersionFile -Raw
    if ($content -notmatch '<Version>([^<]+)</Version>') {
        throw "Nie znaleziono znacznika <Version> w $script:VersionFile."
    }

    return [Version]$matches[1]
}

function Get-NextPatchVersion {
    $current = Get-CurrentVersion
    $patch = if ($current.Build -ge 0) { $current.Build + 1 } else { 0 }
    return [Version]::new($current.Major, $current.Minor, $patch)
}

function Set-Version {
    param([Version]$Version)

    $newVersion = $Version.ToString(3)
    $content = Get-Content $script:VersionFile -Raw
    $updated = $content -replace '<Version>[^<]+</Version>', "<Version>$newVersion</Version>"

    if ($updated -eq $content) {
        throw "Nie udało się zaktualizować wersji w $script:VersionFile."
    }

    if ($DryRun) {
        Write-Host "DRY-RUN: zapis wersji $newVersion do $script:VersionFile"
        return $newVersion
    }

    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($script:VersionFile, $updated, $utf8)
    return $newVersion
}

function New-BuildContext {
    param(
        [string]$Channel,
        [string]$Suffix = ''
    )

    $version = Get-NextPatchVersion
    $versionText = Set-Version -Version $version
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $buildId = "v{0}_{1}{2}" -f $versionText, $timestamp, $Suffix
    $channelRoot = Join-Path $script:ReleaseRoot $Channel
    $outputDirectory = Join-Path $channelRoot $buildId

    Ensure-Directory $script:ReleaseRoot
    Ensure-Directory $channelRoot
    Ensure-Directory $outputDirectory

    return [pscustomobject]@{
        Version = $versionText
        BuildId = $buildId
        Channel = $Channel
        OutputDirectory = $outputDirectory
    }
}

function Write-BuildMetadata {
    param(
        [string]$OutputDirectory,
        [string]$ModeName,
        [string]$Version,
        [bool]$Verified
    )

    Write-Step "Zapis metadanych builda ($ModeName)."

    $content = @(
        "product=UberDashboard"
        "mode=$ModeName"
        "version=$Version"
        "verified=$Verified"
        "timestamp=$(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')"
    ) -join [Environment]::NewLine

    $path = Join-Path $OutputDirectory 'build-info.txt'
    if ($DryRun) {
        Write-Host "DRY-RUN: zapis $path"
        return
    }

    Set-Content -Path $path -Value $content -Encoding utf8
}

function Run-TestSuite {
    Invoke-Checked -Description 'Build rozwiązania w konfiguracji Release.' -FilePath 'dotnet' -Arguments @(
        'build',
        'UberDashboard.slnx',
        '-c',
        'Release',
        '--nologo'
    )

    Invoke-Checked -Description 'Uruchomienie testów rozwiązania.' -FilePath 'dotnet' -Arguments @(
        'test',
        'UberDashboard.slnx',
        '-c',
        'Release',
        '--no-build',
        '--nologo',
        '-v',
        'q'
    )
}

function Publish-App {
    param(
        [string]$Configuration,
        [string]$OutputDirectory,
        [bool]$SelfContained,
        [string]$RuntimeIdentifier,
        [string]$ModeName
    )

    $arguments = @(
        'publish',
        $script:AppProject,
        '-c',
        $Configuration,
        '-o',
        $OutputDirectory,
        '--nologo',
        '--self-contained',
        $SelfContained.ToString().ToLowerInvariant()
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $arguments += @('-r', $RuntimeIdentifier)
    }

    Invoke-Checked -Description "Publikacja aplikacji ($ModeName)." -FilePath 'dotnet' -Arguments $arguments
}

function Start-BuildOutput {
    param([string]$OutputDirectory)

    $exePath = Join-Path $OutputDirectory 'UberDashboard.exe'
    if ($DryRun) {
        Write-Step "Uruchomienie aplikacji z $exePath."
        Write-Host "DRY-RUN: Start-Process `"$exePath`""
        return
    }

    if (-not (Test-Path $exePath)) {
        throw "Nie znaleziono pliku wykonywalnego: $exePath."
    }

    Write-Step "Uruchomienie aplikacji z $exePath."
    Start-Process -FilePath $exePath | Out-Null
}

function Resolve-IsccPath {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Build-Installer {
    param([pscustomobject]$Context)

    $iscc = Resolve-IsccPath
    if (-not $iscc) {
        throw 'Nie znaleziono ISCC.exe. Zainstaluj Inno Setup 6 i uruchom budowę instalatora ponownie.'
    }

    $appStage = Join-Path $Context.OutputDirectory 'app'
    Ensure-Directory $appStage

    Publish-App -Configuration 'Release' -OutputDirectory $appStage -SelfContained $true -RuntimeIdentifier 'win-x64' -ModeName 'installer-staging'

    Invoke-Checked -Description 'Budowa instalatora Inno Setup.' -FilePath $iscc -Arguments @(
        "/DAppVersion=$($Context.Version)",
        "/DBuildId=$($Context.BuildId)",
        "/DSourceDir=$appStage",
        "/DOutputDir=$($Context.OutputDirectory)",
        $script:InstallerScript
    )
}

function Remove-OldBuilds {
    param([switch]$WhatIf)

    Ensure-Directory $script:ReleaseRoot

    foreach ($channel in @('Dev', 'Prod', 'Instal')) {
        $channelRoot = Join-Path $script:ReleaseRoot $channel
        Ensure-Directory $channelRoot

        Write-Step "Retencja katalogu $channel."
        $directories = @(Get-ChildItem -Path $channelRoot -Directory | Sort-Object LastWriteTimeUtc -Descending)
        if ($directories.Count -le 1) {
            continue
        }

        $toRemove = $directories | Select-Object -Skip 1
        foreach ($directory in $toRemove) {
            if ($WhatIf -or $DryRun) {
                Write-Host "DRY-RUN: Remove-Item -Recurse -Force `"$($directory.FullName)`""
                continue
            }

            Remove-Item -Path $directory.FullName -Recurse -Force
        }
    }
}

function Open-ReleaseFolder {
    Ensure-Directory $script:ReleaseRoot
    Write-Step 'Otwieranie katalogu Release.'
    if ($DryRun) {
        Write-Host "DRY-RUN: explorer.exe `"$script:ReleaseRoot`""
        return
    }

    Start-Process -FilePath 'explorer.exe' -ArgumentList $script:ReleaseRoot | Out-Null
}

switch ($Mode) {
    'test' {
        Set-StepPlan -Total 2
        Run-TestSuite
    }
    'dev' {
        Set-StepPlan -Total 5
        $context = New-BuildContext -Channel 'Dev'
        Run-TestSuite
        Publish-App -Configuration 'Debug' -OutputDirectory $context.OutputDirectory -SelfContained $false -RuntimeIdentifier '' -ModeName 'dev'
        Write-BuildMetadata -OutputDirectory $context.OutputDirectory -ModeName 'dev' -Version $context.Version -Verified $true
        Start-BuildOutput -OutputDirectory $context.OutputDirectory
    }
    'prod' {
        Set-StepPlan -Total 5
        $context = New-BuildContext -Channel 'Prod'
        Run-TestSuite
        Publish-App -Configuration 'Release' -OutputDirectory $context.OutputDirectory -SelfContained $true -RuntimeIdentifier 'win-x64' -ModeName 'prod'
        Write-BuildMetadata -OutputDirectory $context.OutputDirectory -ModeName 'prod' -Version $context.Version -Verified $true
        Start-BuildOutput -OutputDirectory $context.OutputDirectory
    }
    'fast' {
        Set-StepPlan -Total 4
        $context = New-BuildContext -Channel 'Dev' -Suffix '-fast'
        Publish-App -Configuration 'Release' -OutputDirectory $context.OutputDirectory -SelfContained $false -RuntimeIdentifier '' -ModeName 'fast'
        Write-BuildMetadata -OutputDirectory $context.OutputDirectory -ModeName 'fast' -Version $context.Version -Verified $false
        Start-BuildOutput -OutputDirectory $context.OutputDirectory
    }
    'inst' {
        Set-StepPlan -Total 5
        $context = New-BuildContext -Channel 'Instal'
        Run-TestSuite
        Build-Installer -Context $context
        Write-BuildMetadata -OutputDirectory $context.OutputDirectory -ModeName 'installer' -Version $context.Version -Verified $true
    }
    'clean' {
        Set-StepPlan -Total 3
        Remove-OldBuilds -WhatIf:$DryRun
    }
    'folder' {
        Set-StepPlan -Total 1
        Open-ReleaseFolder
    }
}

Write-Progress -Activity 'UberDashboard build pipeline' -Completed

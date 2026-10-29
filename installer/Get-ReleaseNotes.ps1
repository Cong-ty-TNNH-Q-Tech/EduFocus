#Requires -Version 5.1
<#
.SYNOPSIS
    Builds GitHub Release notes from the matching CHANGELOG.md section.

.DESCRIPTION
    The section can be named "## [<version>]" or use an Unreleased heading that names
    the version, for example "## [Unreleased] - EduFocus 2.0.0 (2026-10-26)".

    The section must contain a "### Breaking changes" subsection so every release
    states compatibility impact explicitly. A shared footer with system requirements
    and verification instructions is appended.

.PARAMETER Version
    Release version without the leading "v", for example 2.0.0 or 2.0.0-rc.1.

.PARAMETER OutputPath
    Destination markdown file.

.PARAMETER ChangelogPath
    CHANGELOG.md path (defaults to the repository root).

.PARAMETER FooterPath
    Footer template appended to the extracted section.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$ChangelogPath = (Join-Path $PSScriptRoot "..\CHANGELOG.md"),

    [string]$FooterPath = (Join-Path $PSScriptRoot "release-notes-footer.md")
)

$ErrorActionPreference = "Stop"

$version = $Version.Trim() -replace "^[vV]", ""
if ($version -notmatch "^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$") {
    throw "Version '$version' is not a valid SemVer value."
}

if (-not (Test-Path -LiteralPath $ChangelogPath -PathType Leaf)) {
    throw "CHANGELOG was not found at '$ChangelogPath'."
}
$changelog = Get-Content -LiteralPath $ChangelogPath -Raw -Encoding UTF8

$sections = [regex]::Matches(
    $changelog,
    "(?ms)^## \[(?<label>[^\]]+)\](?<rest>[^\r\n]*)\r?\n(?<body>.*?)(?=^## \[|\z)")
if ($sections.Count -eq 0) {
    throw "CHANGELOG.md does not contain any version sections."
}

$escapedVersion = [regex]::Escape($version)
$section = $sections | Where-Object {
    $label = $_.Groups["label"].Value.Trim()
    $label -eq $version -or
    ($label -eq "Unreleased" -and $_.Groups["rest"].Value -match ("(?i)EduFocus\s+" + $escapedVersion + "(\s|$)"))
} | Select-Object -First 1

if (-not $section) {
    throw "CHANGELOG.md has no section for version '$version'. Add '## [$version] - YYYY-MM-DD' or name the Unreleased section with this version before releasing."
}

$body = $section.Groups["body"].Value.Trim()
if ([string]::IsNullOrWhiteSpace($body)) {
    throw "The CHANGELOG section for '$version' is empty."
}

if ($body -notmatch "(?im)^###\s+Breaking") {
    throw "The CHANGELOG section for '$version' must contain a '### Breaking changes' subsection. State that there are none when nothing breaks."
}

if (-not (Test-Path -LiteralPath $FooterPath -PathType Leaf)) {
    throw "Release notes footer was not found at '$FooterPath'."
}
$footer = (Get-Content -LiteralPath $FooterPath -Raw -Encoding UTF8).Trim()
$footer = $footer.Replace("<version>", $version)

$notes = $body + "`r`n`r`n---`r`n`r`n" + $footer + "`r`n"

$fullPath = [IO.Path]::GetFullPath($OutputPath)
$directory = [IO.Path]::GetDirectoryName($fullPath)
if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

[IO.File]::WriteAllText($fullPath, $notes, [Text.UTF8Encoding]::new($false))
Write-Host "Release notes written to '$fullPath' ($($notes.Length) characters)."

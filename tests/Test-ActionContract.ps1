$ErrorActionPreference = 'Stop'

$actionPath = Join-Path $PSScriptRoot '..\action.yml'
$readmePath = Join-Path $PSScriptRoot '..\README.md'
$actionText = Get-Content -LiteralPath $actionPath -Raw
$readmeText = Get-Content -LiteralPath $readmePath -Raw

function Assert-Contains {
  param(
    [string] $Text,
    [string] $Expected,
    [string] $Message
  )

  if (-not $Text.Contains($Expected)) {
    throw "$Message Expected to find: $Expected"
  }
}

function Assert-DoesNotContain {
  param(
    [string] $Text,
    [string] $Unexpected,
    [string] $Message
  )

  if ($Text.Contains($Unexpected)) {
    throw "$Message Found unexpected text: $Unexpected"
  }
}

Assert-Contains -Text $actionText -Expected 'name: Require .NET 10 SDK' -Message 'The action must check the .NET 10 prerequisite before running the validator.'
Assert-Contains -Text $actionText -Expected 'dotnet --list-sdks' -Message 'The action must inspect installed SDKs for the fail-fast prerequisite check.'
Assert-Contains -Text $actionText -Expected "Install .NET 10 or newer before invoking codebeltnet/nuget-release-validate." -Message 'The prerequisite diagnostic must tell callers how to fix a missing SDK.'
Assert-Contains -Text $actionText -Expected "[int]`$Matches.major -ge 10" -Message 'The action must accept compatible SDKs that are newer than .NET 10.'
Assert-Contains -Text $actionText -Expected 'Push-Location $env:ACTION_PATH' -Message 'The validator must run from the action directory so repository SDK pinning does not select the wrong SDK.'
Assert-Contains -Text $actionText -Expected "dotnet run --file" -Message 'The action must continue using the file-based validator.'
Assert-DoesNotContain -Text $actionText -Unexpected 'install-dotnet:' -Message 'The action must not expose an install-dotnet toggle.'
Assert-DoesNotContain -Text $readmeText -Unexpected 'install-dotnet:' -Message 'The README must not document an install-dotnet input.'
Assert-Contains -Text $readmeText -Expected 'requires the .NET 10 SDK or newer' -Message 'The README must document the external SDK prerequisite.'
Assert-Contains -Text $readmeText -Expected 'fails immediately with an explicit diagnostic' -Message 'The README must describe the fail-fast prerequisite behavior.'

Write-Host 'NuGet release validator action contract passed.'

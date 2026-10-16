param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
# Compile the actual production class against explicitly limited API test doubles.
# This exercises ordering/retries; it cannot establish native Revit API behavior.
Add-Type -Path @((Join-Path $RepositoryRoot 'src\AreaReinforcementHosts\ParameterValue.cs'), (Join-Path $PSScriptRoot 'ParameterApiDoubles.cs'))
[ParameterRegression]::Run()
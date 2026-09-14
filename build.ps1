param([string]$OutputDirectory = (Join-Path $PSScriptRoot "dist"))
$ErrorActionPreference = "Stop"

$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The .NET Framework C# compiler was not found at $compiler"
}

$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$icon = Join-Path $PSScriptRoot "assets\CommStudio.ico"
if (-not (Test-Path -LiteralPath $icon)) {
    throw "The application icon is missing. Run .\tools\generate-icon.ps1 first."
}

& $compiler `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /win32icon:"$icon" `
    /out:"$outputDirectory\CommStudio.exe" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Runtime.Serialization.dll `
    /reference:System.Security.dll `
    /reference:System.Windows.Forms.dll `
    /reference:"$PSScriptRoot\vendor\MQTTnet\MQTTnet.dll" `
    "/resource:$PSScriptRoot\vendor\MQTTnet\MQTTnet.dll,CommStudio.MQTTnet.dll" `
    "/resource:$PSScriptRoot\vendor\MQTTnet\LICENSE.txt,CommStudio.MQTTnet.LICENSE.txt" `
    "$PSScriptRoot\AssemblyInfo.cs" `
    "$PSScriptRoot\ByteCodec.cs" `
    "$PSScriptRoot\CommandRowControl.cs" `
    "$PSScriptRoot\LogRichTextBox.cs" `
    "$PSScriptRoot\MainForm.cs" `
    "$PSScriptRoot\ConnectionPanel.cs" `
    "$PSScriptRoot\SerialBaudControl.cs" `
    "$PSScriptRoot\Models.cs" `
    "$PSScriptRoot\MqttProfiles.cs" `
    "$PSScriptRoot\MqttProfilesControl.cs" `
    "$PSScriptRoot\EmbeddedDependencies.cs" `
    "$PSScriptRoot\MqttSession.cs" `
    "$PSScriptRoot\MqttWorkspaceControl.cs" `
    "$PSScriptRoot\MqttLogControl.cs" `
    "$PSScriptRoot\MqttFilter.cs" `
    "$PSScriptRoot\MqttPayloadTextBox.cs" `
    "$PSScriptRoot\MqttHistoryStore.cs" `
    "$PSScriptRoot\JsonMessageTextBox.cs" `
    "$PSScriptRoot\SavedMqttMessages.cs" `
    "$PSScriptRoot\Program.cs" `
    "$PSScriptRoot\SettingsStore.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Built $outputDirectory\CommStudio.exe"

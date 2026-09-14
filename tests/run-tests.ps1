$ErrorActionPreference = "Stop"

$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$testOutput = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Force -Path $testOutput | Out-Null

& $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\CodecTests.exe" `
    "$PSScriptRoot\..\ByteCodec.cs" "$PSScriptRoot\CodecTests.cs"
if ($LASTEXITCODE -ne 0) { throw "Codec test compilation failed." }
& "$testOutput\CodecTests.exe"
if ($LASTEXITCODE -ne 0) { throw "Codec tests failed." }

& $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\UiSmokeTests.exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    "$PSScriptRoot\UiSmokeTests.cs"
if ($LASTEXITCODE -ne 0) { throw "UI smoke test compilation failed." }

Push-Location (Join-Path $PSScriptRoot "..")
try {
    & "$testOutput\UiSmokeTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "UI smoke test failed." }

    & $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\NetworkIntegrationTests.exe" `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
        "$PSScriptRoot\NetworkIntegrationTests.cs"
    if ($LASTEXITCODE -ne 0) { throw "TCP integration test compilation failed." }

    & "$testOutput\NetworkIntegrationTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "TCP integration test failed." }

    & $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\SerialTests.exe" `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll `
        "$PSScriptRoot\SerialTests.cs"
    if ($LASTEXITCODE -ne 0) { throw "Serial test compilation failed." }
    & "$testOutput\SerialTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "Serial tests failed." }

    & $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\BaudSwitchTests.exe" `
        /reference:System.dll /reference:System.Core.dll `
        "$PSScriptRoot\..\SerialBaudControl.cs" "$PSScriptRoot\..\ByteCodec.cs" "$PSScriptRoot\BaudSwitchTests.cs"
    if ($LASTEXITCODE -ne 0) { throw "Baud switch test compilation failed." }
    & "$testOutput\BaudSwitchTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "Baud switch tests failed." }

    & $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\MqttProfileTests.exe" `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /reference:System.Security.dll `
        "$PSScriptRoot\..\MqttProfiles.cs" "$PSScriptRoot\..\MqttProfilesControl.cs" "$PSScriptRoot\MqttProfileTests.cs"
    if ($LASTEXITCODE -ne 0) { throw "MQTT profile test compilation failed." }
    & "$testOutput\MqttProfileTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "MQTT profile tests failed." }

    Copy-Item -LiteralPath "$PSScriptRoot\..\vendor\MQTTnet\MQTTnet.dll" -Destination "$testOutput\MQTTnet.dll" -Force
    & $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\MqttIntegrationTests.exe" `
        /reference:System.dll /reference:System.Core.dll /reference:System.Runtime.Serialization.dll /reference:System.Security.dll `
        /reference:"$testOutput\MQTTnet.dll" `
        "$PSScriptRoot\..\MqttSession.cs" "$PSScriptRoot\..\MqttProfiles.cs" "$PSScriptRoot\MqttIntegrationTests.cs"
    if ($LASTEXITCODE -ne 0) { throw "MQTT integration test compilation failed." }
    & "$testOutput\MqttIntegrationTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "MQTT integration tests failed." }

    & $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\MqttMessageTests.exe" `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
        /reference:System.Runtime.Serialization.dll /reference:System.Security.dll /reference:"$testOutput\MQTTnet.dll" `
        "$PSScriptRoot\..\MqttSession.cs" "$PSScriptRoot\..\MqttProfiles.cs" "$PSScriptRoot\..\MqttProfilesControl.cs" `
        "$PSScriptRoot\..\MqttWorkspaceControl.cs" "$PSScriptRoot\..\MqttLogControl.cs" "$PSScriptRoot\..\MqttFilter.cs" "$PSScriptRoot\..\MqttPayloadTextBox.cs" "$PSScriptRoot\..\MqttHistoryStore.cs" "$PSScriptRoot\..\JsonMessageTextBox.cs" "$PSScriptRoot\..\SavedMqttMessages.cs" "$PSScriptRoot\MqttMessageTests.cs"
    if ($LASTEXITCODE -ne 0) { throw "MQTT message test compilation failed." }
    & "$testOutput\MqttMessageTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "MQTT message tests failed." }

    & $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\MqttPreferencesTests.exe" `
        /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
        "$PSScriptRoot\MqttPreferencesTests.cs"
    if ($LASTEXITCODE -ne 0) { throw "MQTT preferences test compilation failed." }
    & "$testOutput\MqttPreferencesTests.exe"
    if ($LASTEXITCODE -ne 0) { throw "MQTT preferences tests failed." }
}
finally {
    Pop-Location
}

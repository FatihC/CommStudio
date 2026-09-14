$ErrorActionPreference = "Stop"

$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$toolOutput = Join-Path $PSScriptRoot "bin"
New-Item -ItemType Directory -Force -Path $toolOutput | Out-Null

& $compiler /nologo /unsafe /target:exe /platform:x64 `
    /out:"$toolOutput\IconBuilder.exe" `
    /reference:System.dll /reference:System.Drawing.dll `
    "$PSScriptRoot\IconBuilder.cs"
if ($LASTEXITCODE -ne 0) { throw "Icon builder compilation failed." }

& "$toolOutput\IconBuilder.exe" `
    "$PSScriptRoot\..\assets\CommStudio-source.png" `
    "$PSScriptRoot\..\assets\CommStudio.png" `
    "$PSScriptRoot\..\assets\CommStudio.ico"
if ($LASTEXITCODE -ne 0) { throw "Icon generation failed." }

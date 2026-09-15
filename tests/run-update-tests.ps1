$ErrorActionPreference = 'Stop'
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$testOutput = Join-Path $PSScriptRoot 'bin'
foreach ($variant in @('update-old', 'update-new')) {
    $fixtureOutput = Join-Path $testOutput $variant
    New-Item -ItemType Directory -Force -Path $fixtureOutput | Out-Null
    $fixtureDefines = if ($variant -eq 'update-new') { '/define:UPDATED' } else { '/define:ORIGINAL' }
    & $compiler /nologo /target:winexe /platform:x64 $fixtureDefines /out:"$fixtureOutput\CommStudio.exe" "$PSScriptRoot\UpdateFixture.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Update fixture compilation failed.' }
}
& $compiler /nologo /target:exe /platform:x64 /out:"$testOutput\UpdateTests.exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll `
    "$PSScriptRoot\..\UpdateService.cs" "$PSScriptRoot\..\UpdateForm.cs" "$PSScriptRoot\UpdateTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Update test compilation failed.' }
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & "$testOutput\UpdateTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Update tests failed.' }
} finally { Pop-Location }

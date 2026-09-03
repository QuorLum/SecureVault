# Activates the isolated .NET 8 SDK environment for SecureVault
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
Write-Host "SecureVault environment activated: .NET $($(& "$env:DOTNET_ROOT\dotnet.exe" --version))"

# Compila o launcher com o compilador C# que já vem com o Windows (.NET Framework 4.x, WPF)
param([string]$Out = '')
$ErrorActionPreference = 'Stop'
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = Join-Path $fw 'WPF'
$csc = Join-Path $fw 'csc.exe'
$src = Join-Path $PSScriptRoot 'src'
$out = if ($Out) { $Out } else { Join-Path $PSScriptRoot 'RecompHub.exe' }

$refs = @(
  'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
  'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Web.Extensions.dll', 'System.Security.dll', 'System.Xml.dll', 'System.Xml.Linq.dll',
  (Join-Path $wpf 'WindowsBase.dll'), (Join-Path $wpf 'PresentationCore.dll'), (Join-Path $wpf 'PresentationFramework.dll'), (Join-Path $wpf 'UIAutomationProvider.dll'), (Join-Path $wpf 'UIAutomationTypes.dll')
) | ForEach-Object { "/r:$_" }

# Tema e fontes vão embutidos no .exe
$res = @("/resource:$(Join-Path $src 'Theme.xaml'),Theme.xaml")
Get-ChildItem (Join-Path $PSScriptRoot 'fonts') -Filter *.ttf | ForEach-Object { $res += "/resource:$($_.FullName),$($_.Name)" }

$icon = Join-Path $PSScriptRoot 'icon.ico'   # gerado por tools\make_icon.ps1
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" "/win32icon:$icon" @refs @res `
    (Join-Path $src 'Core.cs') (Join-Path $src 'Games.cs') (Join-Path $src 'Effects.cs') (Join-Path $src 'Media.cs') (Join-Path $src 'Xex.cs') (Join-Path $src 'Notify.cs') (Join-Path $src 'Saves.cs') (Join-Path $src 'Pad.cs') (Join-Path $src 'Shell.cs') (Join-Path $src 'Hub.cs') (Join-Path $src 'News.cs') (Join-Path $src 'Updates.cs') (Join-Path $src 'GameFiles.cs') (Join-Path $src 'Discord.cs') (Join-Path $src 'HubUpdate.cs') (Join-Path $src 'Welcome.cs') (Join-Path $src 'Sessions.cs') (Join-Path $src 'Diagnose.cs') (Join-Path $src 'Hardware.cs') (Join-Path $src 'Profile.cs') (Join-Path $src 'IconPack.cs') (Join-Path $src 'WinCheck.cs') (Join-Path $src 'Ps4Keys.cs') (Join-Path $src 'Ps4Pkg.cs') (Join-Path $src 'Xiso.cs') (Join-Path $src 'App.cs')

if ($LASTEXITCODE -ne 0) { throw "Falha na compilação" }
Write-Host "OK -> $out"

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# --- Detect native machine architecture (not process arch, which may be emulated) ---
$nativeArch = (Get-Item "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment").GetValue("PROCESSOR_ARCHITECTURE")
$rid = switch ($nativeArch) {
    "AMD64" { "win-x64" }
    "ARM64" { "win-arm64" }
    "x86"   { "win-x86" }
    "ARM"   { "win-arm" }
    default { throw "Unsupported architecture: $nativeArch" }
}
Write-Host "Platform: $rid" -ForegroundColor Cyan

# --- Generate app.ico if missing ---
$ico = Join-Path $root "app.ico"
if (-not (Test-Path $ico)) {
    Write-Host "Generating app.ico..." -ForegroundColor Cyan

    $svgSrc = Join-Path $root "assets/sidebar-pin-pinned-dark.svg"
    $tmp    = Join-Path ([IO.Path]::GetTempPath()) "wpt-ico-gen"

    New-Item -ItemType Directory -Force -Path $tmp | Out-Null

    Set-Content "$tmp/ico-gen.csproj" @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Svg" Version="3.4.6" />
  </ItemGroup>
</Project>
"@

    Set-Content "$tmp/Program.cs" @'
using Svg;
using System.Drawing.Imaging;

string svgPath = args[0], outPath = args[1];
var doc = SvgDocument.Open(svgPath);
int[] sizes = [48, 32, 16];
var blobs = sizes.Select(s => {
    using var bmp = doc.Draw(s, s);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}).ToArray();
using var fs = File.Create(outPath);
using var bw = new BinaryWriter(fs);
bw.Write((short)0); bw.Write((short)1); bw.Write((short)blobs.Length);
int offset = 6 + blobs.Length * 16;
for (int i = 0; i < blobs.Length; i++) {
    bw.Write((byte)sizes[i]); bw.Write((byte)sizes[i]);
    bw.Write((byte)0); bw.Write((byte)0);
    bw.Write((short)1); bw.Write((short)32);
    bw.Write(blobs[i].Length); bw.Write(offset);
    offset += blobs[i].Length;
}
foreach (var b in blobs) bw.Write(b);
'@

    dotnet run --project $tmp -- $svgSrc $ico
    Remove-Item -Recurse -Force $tmp
    Write-Host "app.ico generated." -ForegroundColor Green
}

# --- Build ---
Write-Host "Building ($Configuration, $rid)..." -ForegroundColor Cyan

dotnet publish $root `
    -c $Configuration `
    -r $rid `
    --self-contained false `
    -p:RuntimeIdentifier=$rid `
    --nologo

$out = Join-Path $root "bin/$Configuration/net8.0-windows/$rid/publish/win-pin-taskbar.exe"
Write-Host ""
Write-Host "Done: $out" -ForegroundColor Green

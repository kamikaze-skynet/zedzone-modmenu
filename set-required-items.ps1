# Marks Mod Menu as a "Required item" on each of our other Steam Workshop items, so anyone
# who subscribes to one of them gets Steam's "this item requires... subscribe to all?" prompt.
#
#   .\set-required-items.ps1              add Mod Menu to every item in $ItemIds
#   .\set-required-items.ps1 -Remove      take it off again
#
# Needs: the Steam client running and logged in as the account that owns the items (admare1).
# Close the game first. Nothing is uploaded and no password is asked for: it talks to the
# running Steam client through the game's own steam_api64.dll, the same way the game does.
# If it fails, do it by hand: open each item's Workshop page > Owner controls (right side) >
# "Add/Remove Required Items" > pick Mod Menu.
param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\ZED ZONE",
    [string]$MenuId = "",
    [string[]]$ItemIds = @(
        "3837584386",  # AmmoStacks
        "3837154256",  # DogShare
        "3837767458",  # Pack Rat
        "3838789704",  # Boltgun
        "3838721872",  # Build Anywhere
        "3839956754",  # Heavy Horde
        "3839284592"   # Spawn Menu
        # Supply Drop: add its id here once it is on the Workshop
    ),
    [switch]$Remove
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not $MenuId) {
    $idFile = Join-Path $PSScriptRoot "workshop\publishedfileid.txt"
    if (Test-Path $idFile) { $MenuId = (Get-Content $idFile -Raw).Trim() }
}
if (-not $MenuId -or $MenuId -eq "0") { throw "Upload Mod Menu first (publish-workshop.ps1), so workshop\publishedfileid.txt has its item id." }
if (-not [Environment]::Is64BitProcess) { throw "Run this from 64-bit PowerShell." }

$dll = Join-Path $GameDir "ZEDZONE_Data\Plugins\x86_64\steam_api64.dll"
if (-not (Test-Path $dll)) { throw "Can't find $dll - pass -GameDir" }

# steam_api64 looks for steam_appid.txt in the working directory (or the SteamAppId variable).
$work = Join-Path $env:TEMP "zedzone-required-items"
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item $dll (Join-Path $work "steam_api64.dll") -Force
Set-Content -Path (Join-Path $work "steam_appid.txt") -Value "1211600" -Encoding ASCII
[Environment]::CurrentDirectory = $work
$env:SteamAppId = "1211600"
$env:SteamGameId = "1211600"
$lib = (Join-Path $work "steam_api64.dll") -replace '\\', '\\'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ZzSteam {
    const string L = "$lib";
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SteamAPI_Init();
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern void SteamAPI_Shutdown();
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern void SteamAPI_RunCallbacks();
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SteamAPI_SteamUGC_v016();
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SteamAPI_SteamUtils_v010();
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SteamAPI_ISteamUGC_AddDependency(IntPtr ugc, ulong parent, ulong child);
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SteamAPI_ISteamUGC_RemoveDependency(IntPtr ugc, ulong parent, ulong child);
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SteamAPI_ISteamUtils_IsAPICallCompleted(IntPtr utils, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [DllImport(L, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool SteamAPI_ISteamUtils_GetAPICallResult(IntPtr utils, ulong call, byte[] data, int size, int expected, [MarshalAs(UnmanagedType.I1)] out bool failed);
}
"@

if (-not [ZzSteam]::SteamAPI_Init()) { throw "Steam didn't answer. Start the Steam client, log in as the items' owner, close the game, and try again." }
try {
    $ugc = [ZzSteam]::SteamAPI_SteamUGC_v016()
    $utils = [ZzSteam]::SteamAPI_SteamUtils_v010()
    # AddUGCDependencyResult_t = 3412, RemoveUGCDependencyResult_t = 3413: { int result; ulong parent; ulong child }
    $expected = if ($Remove) { 3413 } else { 3412 }
    $ok = 0
    foreach ($id in $ItemIds) {
        $call = if ($Remove) { [ZzSteam]::SteamAPI_ISteamUGC_RemoveDependency($ugc, [uint64]$id, [uint64]$MenuId) }
                else         { [ZzSteam]::SteamAPI_ISteamUGC_AddDependency($ugc, [uint64]$id, [uint64]$MenuId) }
        $failed = $false
        $deadline = (Get-Date).AddSeconds(30)
        while (-not [ZzSteam]::SteamAPI_ISteamUtils_IsAPICallCompleted($utils, $call, [ref]$failed)) {
            [ZzSteam]::SteamAPI_RunCallbacks()
            if ((Get-Date) -gt $deadline) { break }
            Start-Sleep -Milliseconds 100
        }
        $buf = New-Object byte[] 24
        $got = [ZzSteam]::SteamAPI_ISteamUtils_GetAPICallResult($utils, $call, $buf, 24, $expected, [ref]$failed)
        $result = [BitConverter]::ToInt32($buf, 0)
        # 1 = OK, 29 = already done (duplicate request)
        if ($got -and -not $failed -and ($result -eq 1 -or $result -eq 29)) {
            Write-Host ("  {0}: done" -f $id) -ForegroundColor Green; $ok++
        } else {
            Write-Host ("  {0}: failed (Steam result {1}). You must own the item; or set it by hand on its page." -f $id, $result) -ForegroundColor Red
        }
    }
    $verb = if ($Remove) { "removed from" } else { "added to" }
    Write-Host ""
    Write-Host " Mod Menu ($MenuId) $verb $ok of $($ItemIds.Count) items." -ForegroundColor Cyan
    Write-Host " Check one: https://steamcommunity.com/sharedfiles/filedetails/?id=$($ItemIds[0]) (Required items, right side)"
}
finally { [ZzSteam]::SteamAPI_Shutdown() }

# Windows runner (PowerShell 5+). Use run.bat. Runs every line of plans\default.plan with SimHost.exe (.NET Framework, already in Windows).
param([string]$Plan = "plans\default.plan", [string]$Only = "", [int]$Jobs = 0)
Set-Location $PSScriptRoot
$cpus = [Environment]::ProcessorCount
if ($Jobs -le 0) { $Jobs = [Math]::Max(1, $cpus - 1) }
$exe = Join-Path $PSScriptRoot "out\sim\SimHost.exe"
if (-not (Test-Path $exe)) { Write-Host "SimHost.exe not found - did you unzip the whole pack?"; exit 1 }
New-Item -ItemType Directory -Force -Path results | Out-Null
$cpuName = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
"machine: $env:COMPUTERNAME; cpu: $cpuName; logical processors: $cpus; jobs: $Jobs" | Set-Content results\machine.txt
$onlySet = @(); if ($Only) { $onlySet = $Only.Split(",") | ForEach-Object { $_.Trim() } }

function Split-Args([string]$s) {   # tokenizer: spaces separate, double quotes group, quotes removed
  $tok = New-Object System.Collections.Generic.List[string]; $cur = New-Object System.Text.StringBuilder; $inq = $false; $has = $false
  foreach ($ch in $s.ToCharArray()) {
    if ($ch -eq '"') { $inq = -not $inq; $has = $true }
    elseif ($ch -eq ' ' -and -not $inq) { if ($has -or $cur.Length -gt 0) { $tok.Add($cur.ToString()); [void]$cur.Clear(); $has = $false } }
    else { [void]$cur.Append($ch) }
  }
  if ($has -or $cur.Length -gt 0) { $tok.Add($cur.ToString()) }
  return ,$tok.ToArray()
}

foreach ($line in Get-Content $Plan) {
  if ($line.Trim() -eq "" -or $line.TrimStart().StartsWith("#")) { continue }
  $i = $line.IndexOf("|"); $name = $line.Substring(0, $i).Trim(); $argl = $line.Substring($i + 1).Trim()
  if ($onlySet.Count -gt 0 -and ($onlySet -notcontains $name)) { continue }
  $out = "results\$name"; New-Item -ItemType Directory -Force -Path $out | Out-Null
  $argl = $argl.Replace("{OUT}", $out.Replace("\", "/"))
  $tokens = Split-Args $argl
  if ($tokens[0] -eq "run") { $tokens += @("--jobs", "$Jobs") }
  Write-Host ("=== {0} ({1}) ===" -f $name, (Get-Date -Format HH:mm:ss))
  "SimHost.exe $($tokens -join ' ')" | Set-Content "$out\command.txt"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  & $exe @tokens *> "$out\log.txt"
  $rc = $LASTEXITCODE
  "rc=$rc seconds=$([int]$sw.Elapsed.TotalSeconds)" | Tee-Object "$out\status.txt"
  Get-Content "$out\log.txt" -Tail 2
}
Write-Host "All done. Now double-click pack_results.bat and send me SE_results.zip."

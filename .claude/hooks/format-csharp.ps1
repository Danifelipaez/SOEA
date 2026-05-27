# Hook: auto-format C# files after Edit/Write using dotnet format
$json = [Console]::In.ReadToEnd() | ConvertFrom-Json
$f = $json.file_path
if ($f -and $f.EndsWith('.cs')) {
    $cwd  = (Get-Location).Path
    $rel  = [System.IO.Path]::GetRelativePath($cwd, $f)
    dotnet format SOEA.sln --include $rel 2>$null
}

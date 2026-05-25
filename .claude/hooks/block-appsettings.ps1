# Hook: block direct edits to appsettings.json (contains plaintext DB credentials)
$json = [Console]::In.ReadToEnd() | ConvertFrom-Json
$f = $json.file_path
if ($f -and $f -match 'appsettings\.json$' -and $f -notmatch 'appsettings\.Development\.json') {
    [Console]::Error.WriteLine("BLOCKED: Edit appsettings.json manually — it contains database credentials.")
    exit 1
}

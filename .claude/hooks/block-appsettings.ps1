# Hook: block direct edits to appsettings.json (contains plaintext DB credentials)
$json = [Console]::In.ReadToEnd() | ConvertFrom-Json
# El payload real anida en tool_input.file_path; el campo plano quedaba sin usar (nunca bloqueaba).
$f = $json.tool_input.file_path
if (-not $f) { $f = $json.file_path }
if ($f -and $f -match 'appsettings\.json$' -and $f -notmatch 'appsettings\.Development\.json') {
    # Sin caracteres no-ASCII: powershell.exe (5.1) sin BOM UTF-8 rompe el parseo del .ps1 con ellos.
    [Console]::Error.WriteLine("BLOCKED: Edit appsettings.json manually - it contains database credentials.")
    exit 1
}

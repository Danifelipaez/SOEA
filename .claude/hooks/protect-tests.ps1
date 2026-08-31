# Hook: disciplina de tests (PreToolUse sobre Edit|Write).
#   A. No se modifican tests ya trackeados en git — impide relajar un aserto hasta ponerlo verde.
#      Un test untracked (recien creado) si se puede editar: todavia se esta escribiendo.
#   B. No se edita codigo si no hay ningun test nuevo o modificado en el working tree.
# Escape unico y visible en el comando: SOEA_TESTS_UNLOCK=1
#
# ponytail: la regla B mira el working tree entero, asi que un WIP que ya toco tests la
# satisface de entrada. Techo conocido; subir a "test tocado despues del ultimo commit de
# codigo" solo si la disciplina se relaja en la practica.

if ($env:SOEA_TESTS_UNLOCK -eq '1') { exit 0 }

$raw = [Console]::In.ReadToEnd()
if (-not $raw) { exit 0 }
try { $json = $raw | ConvertFrom-Json } catch { exit 0 }

# El payload actual anida en tool_input; se deja el fallback plano por compatibilidad.
$path = $json.tool_input.file_path
if (-not $path) { $path = $json.file_path }
if (-not $path) { exit 0 }

if ($json.cwd) { Set-Location -LiteralPath $json.cwd }

# [char]92 = backslash — evita escribir el caracter literal (el transporte del hook lo puede colapsar).
$rel = $path.Replace([char]92, '/')

function Deny([string]$reason) {
    $out = @{ hookSpecificOutput = @{
        hookEventName            = 'PreToolUse'
        permissionDecision       = 'deny'
        permissionDecisionReason = $reason
    } } | ConvertTo-Json -Depth 5 -Compress
    [Console]::Out.Write($out)
    exit 0
}

$esTest = ($rel -match '(Tests\.cs|\.spec\.ts)$') -or ($rel -match '/test/')

if ($esTest) {
    git ls-files --error-unmatch -- "$path" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Deny "BLOQUEADO - test existente: $rel ya esta commiteado y no se modifica. Si el test falla, se arregla el codigo, no el aserto. Para un cambio deliberado del test: SOEA_TESTS_UNLOCK=1"
    }
    exit 0
}

$esCodigo = ($rel -match '/src/') -or ($rel -match '/frontend/.*/app/')
if (-not $esCodigo) { exit 0 }

$testsTocados = git status --porcelain -- '*Tests.cs' '*.spec.ts' 'test/' 2>$null
if (-not $testsTocados) {
    Deny "BLOQUEADO - test primero: $rel es codigo y no hay ningun test nuevo o modificado en el working tree. Escribe primero el test que falla. Para saltar: SOEA_TESTS_UNLOCK=1"
}
exit 0

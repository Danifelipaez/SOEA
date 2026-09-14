using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Exceptions;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Crea una sesión manual sin re-ejecutar el modelo de optimización.
    /// Valida HC-I01 (docente libre), HC-S01 (espacio libre) y HC-S05 (espacio fijo)
    /// contra el estado actual de la BD antes de persistir.
    /// </summary>
    public class CrearSesionManualService
    {
        private readonly IBloqueTiempoRepositorio     _bloques;
        private readonly ISesionRepositorio            _sesiones;
        private readonly IAsignacionSemanalRepositorio _asignaciones;
        private readonly IUnitOfWork                   _uow;
        private readonly IAsignaturaRepositorio?       _asignaturas;
        private readonly IGrupoRepositorio?            _grupos;

        // asignaturas/grupos son opcionales (default null → mensaje degradado sin nombre de
        // asignatura, y HC-S05 no se valida) para no romper la firma del constructor en tests
        // existentes que no los proveen.
        public CrearSesionManualService(
            IBloqueTiempoRepositorio     bloques,
            ISesionRepositorio            sesiones,
            IAsignacionSemanalRepositorio asignaciones,
            IUnitOfWork                   uow,
            IAsignaturaRepositorio?       asignaturas = null,
            IGrupoRepositorio?            grupos = null)
        {
            _bloques      = bloques;
            _sesiones     = sesiones;
            _asignaciones = asignaciones;
            _uow          = uow;
            _asignaturas  = asignaturas;
            _grupos       = grupos;
        }

        private async Task<string> NombreAsignaturaAsync(Guid asignaturaId) =>
            _asignaturas is not null ? (await _asignaturas.GetByIdAsync(asignaturaId))?.Nombre ?? "asignatura sin nombre" : "asignatura sin nombre";

        /// <returns>
        /// Las 1 o 2 filas (<see cref="SesionGeneradaDto"/>) creadas, listas para el frontend.
        /// En caso de violación de hard constraint lanza <see cref="BusinessRuleViolationException"/>
        /// con mensaje descriptivo en español; un request mal formado lanza <see cref="ArgumentException"/>.
        /// </returns>
        public async Task<List<SesionGeneradaDto>> EjecutarAsync(CrearSesionManualRequest req)
        {
            // ── Mapear día ────────────────────────────────────────────────────────
            var dia = MapearDia(req.Dia)
                ?? throw new ArgumentException($"Día no reconocido: '{req.Dia}'. Use lunes, martes, miercoles, jueves, viernes o sabado.");

            // ── Buscar BloqueTiempo ───────────────────────────────────────────────
            if (!TimeOnly.TryParse(req.HoraInicio, out var horaInicio))
                throw new ArgumentException($"Hora no válida: '{req.HoraInicio}'. Formato esperado HH:mm.");

            // ERR2 auditoría: una franja que no existe en la grilla es un problema del REQUEST
            // (400), no un conflicto con datos ya persistidos (409) — a diferencia de los 4 throws
            // de más abajo (HC-I01/HC-S01/HC-S05/HC-SEP), que sí lo son.
            var bloque = await _bloques.FindByDiaHoraAsync(dia, horaInicio)
                ?? throw new ArgumentException(
                    $"No existe franja horaria para {req.Dia} a las {req.HoraInicio}. " +
                    "Verifique que la hora esté dentro del horario académico (06:00–20:00 L-V, 06:00–13:00 Sáb).");

            // ── Resolver tipo de sesión, modalidad y alternancia ──────────────────
            var tipoFlujo = CalculadorEspaciosSesion.ParseTipoFlujo(req.TipoFlujo);
            // Teoría virtual es fija e independiente de Alternancia (decisión de diseño): solo el
            // track de laboratorio alterna. Espacio nunca aplica a una sesión virtual (regla 9).
            var modalidad = tipoFlujo == TipoFlujo.AulaVirtual && req.EsVirtual
                ? Modalidad.Virtual
                : Modalidad.Presencial;

            if (!Enum.TryParse<TipoAlternancia>(req.Alternancia, ignoreCase: true, out var alternancia))
                alternancia = TipoAlternancia.SinAlternancia;
            var alternanciaFinal = tipoFlujo == TipoFlujo.Laboratorio ? alternancia : TipoAlternancia.SinAlternancia;

            Guid? espacioFinal = modalidad == Modalidad.Virtual ? null : req.EspacioId;

            // ── Grilla e índices — compartidos por los dos chequeos de solape de abajo ──
            // MAN1 auditoría: fuente única de "¿estos dos spans se cruzan?" (BloquesPlanner.Solapan,
            // la misma que usan los motores), en vez de la comparación de solo bloque-de-INICIO que
            // había aquí antes: una sesión de 4h a las 07:00 y otra a las 09:00 no comparten bloque
            // de inicio pero sí se solapan durante dos horas — antes eso pasaba limpio y devolvía
            // 201; el docente o el aula quedaban con dos clases simultáneas.
            var bloquesGrid  = GrillaInstitucional.GenerarBloques();
            var idxPorBloque = Enumerable.Range(0, bloquesGrid.Count).ToDictionary(i => bloquesGrid[i].Id, i => i);
            var diaPorIdx    = BloquesPlanner.DiaPorBloqueIdx(bloquesGrid);
            int inicioNueva  = idxPorBloque[bloque.Id];
            int durNueva     = Math.Max(1, (int)Math.Ceiling(req.DuracionHoras));

            // PERF4 auditoría: antes _sesiones.GetAllAsync() se llamaba 3 veces (HC-I01, HC-S01,
            // HC-SEP) — la tabla completa de Sesiones, tres round trips por request. Una sola
            // consulta basta: ninguno de los tres chequeos escribe antes de leer.
            var todasSesiones = (await _sesiones.GetAllAsync()).ToList();
            // bloquesGrid ya trae los mismos bloques que persiste BloqueTiempoSeeder (ids
            // deterministas por día/hora, ver GrillaInstitucional) — no hace falta otra consulta
            // a _bloques para HC-SEP más abajo.
            var bloquePorId  = bloquesGrid.ToDictionary(b => b.Id);

            // ── HC-I01: conflicto de docente ──────────────────────────────────────
            // Sin agrupar por semana (ALT-05): la franja de una sesión es la misma todas las
            // semanas, así que un solape de horario del docente es real sin importar la semana.
            // Sin docente en la nueva sesión (CR-02: opcional) no hay nada que solape — antes,
            // con DocenteId no-nullable, esto habría comparado contra Guid.Empty y podría chocar
            // con otra sesión igualmente fantasma en vez de simplemente no aplicar.
            Sesion? choqueDocente = null;
            if (req.DocenteId.HasValue)
            {
                var sesionesDocente = todasSesiones
                    .Where(s => s.DocenteId == req.DocenteId.Value)
                    .ToList();

                foreach (var s in sesionesDocente)
                {
                    if (!idxPorBloque.TryGetValue(s.BloqueTiempoId, out var inicioOtra)) continue;
                    int durOtra = Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));
                    if (BloquesPlanner.Solapan(inicioNueva, durNueva, inicioOtra, durOtra, diaPorIdx))
                    {
                        choqueDocente = s;
                        break;
                    }
                }
            }
            if (choqueDocente is not null)
            {
                var nombreOtra = await NombreAsignaturaAsync(choqueDocente.AsignaturaId);
                var bloqueOtra = bloquesGrid[idxPorBloque[choqueDocente.BloqueTiempoId]];
                throw new BusinessRuleViolationException(
                    $"HC-I01: el docente ya tiene otra sesión que se solapa con esta franja horaria. " +
                    $"Sesión 1: {await NombreAsignaturaAsync(req.AsignaturaId)} ({req.Dia} {req.HoraInicio}). " +
                    $"Sesión 2: {nombreOtra} ({GenerarHorarioService.DiaToString(bloqueOtra.Dia)} {bloqueOtra.HoraInicio:HH\\:mm}). " +
                    "Elija una hora diferente o cambie el docente de una de las dos sesiones.");
            }

            // ── HC-S01: conflicto de espacio (solo filas presenciales) ────────────
            if (espacioFinal.HasValue)
            {
                // Candidatas = cualquier sesión cuyo span se solape con el de la nueva — antes solo
                // se miraban las que EMPEZABAN en el mismo bloque, así que una sesión de 3h que ya
                // ocupaba el aula desde antes no se detectaba si la nueva empezaba a mitad de esa
                // franja.
                var sesionesEnConflictoDeHorario = todasSesiones
                    .Where(s => idxPorBloque.TryGetValue(s.BloqueTiempoId, out var inicioOtra) &&
                                BloquesPlanner.Solapan(inicioNueva, durNueva, inicioOtra,
                                    Math.Max(1, (int)Math.Ceiling(s.DuracionHoras)), diaPorIdx))
                    .ToList();

                if (sesionesEnConflictoDeHorario.Count > 0)
                {
                    var asignacionesBD = await _asignaciones.GetBySesionIdsAsync(
                        sesionesEnConflictoDeHorario.Select(s => s.Id));

                    // Solo es conflicto si las dos ocupan el aula ALGUNA semana en común: dos
                    // sesiones que alternan en semanas opuestas comparten aula y bloque a propósito.
                    var sesionPorIdBloque = sesionesEnConflictoDeHorario.ToDictionary(x => x.Id);
                    var ocupadaPor = asignacionesBD.FirstOrDefault(a =>
                        a.EspacioId == espacioFinal &&
                        a.Modalidad == Modalidad.Presencial &&
                        sesionPorIdBloque.TryGetValue(a.SesionId, out var ocupante) &&
                        ModalidadSemanal.CompartenSemanaDeEspacio(
                            alternanciaFinal, modalidad, ocupante));

                    if (ocupadaPor is not null)
                    {
                        var sesionOcupante = sesionesEnConflictoDeHorario.First(s => s.Id == ocupadaPor.SesionId);
                        var nombreOcupante = await NombreAsignaturaAsync(sesionOcupante.AsignaturaId);
                        var bloqueOcupante = bloquesGrid[idxPorBloque[sesionOcupante.BloqueTiempoId]];
                        throw new BusinessRuleViolationException(
                            $"HC-S01: el espacio ya está ocupado por otra sesión presencial que se solapa con esta franja horaria. " +
                            $"Sesión 1: {await NombreAsignaturaAsync(req.AsignaturaId)} ({req.Dia} {req.HoraInicio}). " +
                            $"Sesión 2: {nombreOcupante} ({GenerarHorarioService.DiaToString(bloqueOcupante.Dia)} {bloqueOcupante.HoraInicio:HH\\:mm}). " +
                            "Elija otro espacio u otra hora.");
                    }
                }
            }

            // ── Crear Sesion ──────────────────────────────────────────────────────
            var sesion = new Sesion(
                id:           Guid.NewGuid(),
                asignaturaId: req.AsignaturaId,
                docenteId:    req.DocenteId,
                bloqueId:     bloque.Id,
                espacioId:    espacioFinal,
                grupoId:      req.GrupoId,
                alternancia:  alternanciaFinal,
                modalidad:    modalidad,
                duracionHoras: req.DuracionHoras,
                esBloque:     false,
                estaDividida: false,
                tipoFlujo:    tipoFlujo);

            var tipoSesionNueva = CalculadorEspaciosSesion.TipoSesionDe(sesion);

            // ── HC-S05: espacio fijo exigido por el grupo para este tipo de sesión ──────
            // R2 auditoría: sin GrupoId este chequeo no podía existir (comentario ponytail
            // original) — Grupo.RequisitosEspacio reemplazó a Asignatura.EspacioFijoId.
            if (req.GrupoId.HasValue && _grupos is not null && espacioFinal.HasValue)
            {
                var grupo = await _grupos.GetByIdAsync(req.GrupoId.Value);
                var requisito = grupo?.RequisitosEspacio.FirstOrDefault(r => r.TipoSesion == tipoSesionNueva);
                if (requisito?.EspacioId is Guid espacioExigido && espacioExigido != espacioFinal.Value)
                    throw new BusinessRuleViolationException(
                        $"HC-S05: el grupo '{grupo!.Nombre}' exige un espacio fijo para esta sesión y el " +
                        "elegido no es ese espacio. Seleccione el aula fija del grupo, o quite el requisito " +
                        "de espacio del grupo en el catálogo si ya no aplica.");
            }

            // ── HC-SEP: separación mínima de días entre sesiones semanales (petición 11) ──
            // R2 auditoría: ahora agrupa por (grupo, asignatura, tipo) cuando el request trae
            // GrupoId — antes agrupaba solo por (asignatura, tipo) sobre TODAS las cohortes, así
            // que crear la sesión del lunes del grupo B se rechazaba porque el grupo A ya tenía
            // una el lunes. Sin GrupoId (llamador que no lo provee) se omite: no hay forma
            // correcta de acotar la comparación sin él, y aplicar la vieja regla amplia
            // reintroduciría el mismo falso rechazo entre cohortes.
            if (req.GrupoId.HasValue)
            {
                var mismasDelGrupo = todasSesiones
                    .Where(s => s.GrupoId == req.GrupoId.Value && s.AsignaturaId == req.AsignaturaId &&
                                CalculadorEspaciosSesion.TipoSesionDe(s) == tipoSesionNueva)
                    .ToList();
                if (mismasDelGrupo.Count > 0)
                {
                    foreach (var otra in mismasDelGrupo)
                    {
                        if (!bloquePorId.TryGetValue(otra.BloqueTiempoId, out var otroBloque)) continue;
                        if (!ReglasSesion.SeparacionDiasOk(dia, otroBloque.Dia))
                            throw new BusinessRuleViolationException(
                                $"HC-SEP: este grupo ya tiene otra sesión de esta asignatura/tipo el {otroBloque.Dia}. " +
                                "Las sesiones semanales repetidas necesitan al menos un día de separación.");
                    }
                }
            }

            // M5 auditoría: la Sesion y su AsignacionSemanal se guardaban con dos escrituras
            // independientes (cada AddAsync/AddRangeAsync confirma por su cuenta) — si la segunda
            // fallaba, la sesión ya persistida quedaba huérfana, sin ninguna fila que la ubique.
            var asignacionesList = CrearAsignaciones(sesion, bloque.Id);
            await _uow.BeginTransactionAsync();
            try
            {
                await _sesiones.AddAsync(sesion);
                await _asignaciones.AddRangeAsync(asignacionesList);
                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }

            // ── Construir respuesta ───────────────────────────────────────────────
            var horaFin = horaInicio.AddHours((double)req.DuracionHoras).ToString("HH:mm");
            var diaStr  = req.Dia.ToLowerInvariant();
            var labId   = espacioFinal?.ToString();

            return asignacionesList.Select(a => new SesionGeneradaDto
            {
                Id           = sesion.Id.ToString(),
                AsignaturaId = sesion.AsignaturaId.ToString(),
                DocenteId    = sesion.DocenteId?.ToString() ?? string.Empty,
                EspacioId    = a.EspacioId?.ToString(),
                EspacioIdHogar = labId,           // lab de origen (aunque sea virtual)
                Dia          = diaStr,
                HoraInicio   = req.HoraInicio,
                HoraFin      = horaFin,
                DuracionHoras = req.DuracionHoras,
                Alternancia  = alternanciaFinal.ToString(),
                Virtual      = a.Modalidad == Modalidad.Virtual,
                Semana       = alternanciaFinal == TipoAlternancia.SinAlternancia ? string.Empty : a.Semana.ToString(),
                TipoFlujo    = sesion.TipoFlujo.ToString(),
                MotivoConflicto = sesion.MotivoConflicto
            }).ToList();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        // IMP2/DUP7 auditoría: ParseTipoFlujo vive ahora en CalculadorEspaciosSesion (Domain) —
        // única definición, usada también por GenerarHorarioService y el import.

        /// <summary>
        /// UNA fila por sesión, en su semana canónica (ver <see cref="ModalidadSemanal"/>): la
        /// franja y el aula son un dato único que aplica a todas las semanas (regla 9 / ALT-05).
        /// internal (no private): reutilizado por <see cref="ReacomodarHorarioService"/> (P5) para
        /// reconstruir la fila tras mover una sesión.
        /// </summary>
        internal static List<AsignacionSemanal> CrearAsignaciones(Sesion sesion, Guid bloqueId)
        {
            var modalidad = ModalidadSemanal.ModalidadCanonica(sesion);
            return new List<AsignacionSemanal>
            {
                new(Guid.NewGuid(), sesion.Id, ModalidadSemanal.SemanaCanonica(sesion), bloqueId,
                    modalidad == Modalidad.Presencial ? sesion.EspacioId : null, modalidad)
            };
        }

        /// <summary>internal (no private): reutilizado por <see cref="ReacomodarHorarioService"/> (P5).</summary>
        internal static DiaDeSemana? MapearDia(string dia) =>
            dia.ToLowerInvariant().Trim() switch
            {
                "lunes"     => DiaDeSemana.Lunes,
                "martes"    => DiaDeSemana.Martes,
                "miercoles" => DiaDeSemana.Miercoles,
                "jueves"    => DiaDeSemana.Jueves,
                "viernes"   => DiaDeSemana.Viernes,
                "sabado"    => DiaDeSemana.Sábado,
                _           => null
            };
    }
}

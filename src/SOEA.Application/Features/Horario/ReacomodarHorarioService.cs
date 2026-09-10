using SOEA.Application.Features.Horario.Requests;
using SOEA.Application.Features.Horario.Responses;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Application.Features.Horario
{
    /// <summary>
    /// Petición 13 — recálculo mínimo al editar: mueve una sesión ya generada a un nuevo
    /// (día, hora, espacio) sin volver a ejecutar el pipeline completo. Solo la sesión editada
    /// y las que ahora chocan con ella (mismo grupo o mismo espacio, franjas solapadas) se
    /// recalculan; el resto conserva EXACTAMENTE su AsignacionSemanal previa — esto se garantiza
    /// aquí mismo (no confiando en que CP-SAT respete la igualdad de espacio de las fijas, que
    /// solo fija el bloque — ver MotorConstraintProgramming), tomando del resultado del solver
    /// únicamente las filas de la editada y las liberadas.
    /// </summary>
    public class ReacomodarHorarioService
    {
        private readonly IHorarioRepositorio           _horarioRepo;
        private readonly ISesionRepositorio             _sesionRepo;
        private readonly IAsignacionSemanalRepositorio  _asignacionRepo;
        private readonly IEspacioRepositorio            _espacioRepo;
        private readonly IDocenteRepositorio            _docenteRepo;
        private readonly IGrupoRepositorio              _grupoRepo;
        private readonly IAsignaturaRepositorio         _asignaturaRepo;
        private readonly IMotorConstraintProgramming    _fase2;
        private readonly IUnitOfWork                    _uow;

        public ReacomodarHorarioService(
            IHorarioRepositorio          horarioRepo,
            ISesionRepositorio           sesionRepo,
            IAsignacionSemanalRepositorio asignacionRepo,
            IEspacioRepositorio          espacioRepo,
            IDocenteRepositorio          docenteRepo,
            IGrupoRepositorio            grupoRepo,
            IAsignaturaRepositorio       asignaturaRepo,
            IMotorConstraintProgramming  fase2,
            IUnitOfWork                  uow)
        {
            _horarioRepo    = horarioRepo;
            _sesionRepo     = sesionRepo;
            _asignacionRepo = asignacionRepo;
            _espacioRepo    = espacioRepo;
            _docenteRepo    = docenteRepo;
            _grupoRepo      = grupoRepo;
            _asignaturaRepo = asignaturaRepo;
            _fase2          = fase2;
            _uow            = uow;
        }

        public async Task<ReacomodarHorarioResponse> EjecutarAsync(ReacomodarHorarioRequest req, CancellationToken ct = default)
        {
            var horario = await _horarioRepo.GetByIdAsync(req.HorarioId)
                ?? throw new KeyNotFoundException("No se encontró el horario indicado. Puede haber sido regenerado — recargue la vista de horario.");

            var sesiones = new List<Sesion>();
            foreach (var id in horario.SesioneIds)
            {
                var s = await _sesionRepo.GetByIdAsync(id);
                if (s != null) sesiones.Add(s);
            }

            var sesionEditada = sesiones.FirstOrDefault(s => s.Id == req.SesionEditadaId)
                ?? throw new KeyNotFoundException("La sesión editada no pertenece al horario actual. Puede haber sido regenerado — recargue la vista de horario.");

            var dia = CrearSesionManualService.MapearDia(req.Dia)
                ?? throw new ArgumentException($"Día no reconocido: '{req.Dia}'.");
            if (!TimeOnly.TryParse(req.HoraInicio, out var horaInicio))
                throw new ArgumentException($"Hora no válida: '{req.HoraInicio}'. Formato esperado HH:mm.");

            // ponytail: mover a un espacio concreto está soportado; "vaciar" el espacio de una
            // sesión presencial no (Sesion no expone esa mutación) — EspacioId vacío/null aquí
            // significa "no tocar el espacio actual", no "volverla virtual".
            Guid? espacioNuevo = Guid.TryParse(req.EspacioId, out var eid) ? eid : null;

            // Grilla canónica completa, regenerada aquí igual que en GenerarHorarioService — sus
            // ids son determinísticos por (día, hora) (GrillaInstitucional.IdDeterministico), así
            // que coinciden con los BloqueTiempoId que ya quedaron persistidos en una corrida previa.
            var bloquesGrid = GrillaInstitucional.GenerarBloques();
            var idxPorBloque = Enumerable.Range(0, bloquesGrid.Count).ToDictionary(i => bloquesGrid[i].Id, i => i);
            var bloqueNuevo = bloquesGrid.FirstOrDefault(b => b.Dia == dia && b.HoraInicio == horaInicio)
                ?? throw new ArgumentException($"({req.Dia} {req.HoraInicio}) no coincide con ningún bloque de la grilla canónica.");
            var inicioEditada = idxPorBloque[bloqueNuevo.Id];

            var asignacionesActuales = await _asignacionRepo.GetBySesionIdsAsync(sesiones.Select(s => s.Id));
            var asignPorSesion = asignacionesActuales.GroupBy(a => a.SesionId).ToDictionary(g => g.Key, g => g.ToList());

            // Se cargan una sola vez y sin condición: antes espacios/gruposMotor/asignaturas solo
            // se pedían dentro de la rama "hay choque" (para CP-SAT), y `grupos` se volvía a pedir
            // al final para la respuesta — dos round-trips a `_grupoRepo` en el camino con choque.
            // Ahora también alimentan el post-chequeo (ver más abajo), que necesita las tres listas
            // sin importar qué rama se tome.
            var espacios    = await _espacioRepo.GetAllAsync();
            var gruposMotor = await _grupoRepo.GetAllAsync();
            var asignaturas = await _asignaturaRepo.GetAllAsync();
            var ventanaPorAsig = asignaturas.ToDictionary(
                a => a.Id, a => ((TimeOnly?)a.HoraInicioMin, (TimeOnly?)a.HoraFinMax));

            // ── Detectar SOLO las sesiones que ahora chocan con la editada en su nuevo slot ──
            int duracionEditada = Math.Max(1, (int)Math.Ceiling(sesionEditada.DuracionHoras));
            int finEditada = inicioEditada + duracionEditada;
            bool Solapa(int inicio, int duracion) => inicio < finEditada && inicioEditada < inicio + duracion;

            // El espacio que la sesión editada va a OCUPAR tras este movimiento: el nuevo si la
            // petición trae uno, o el que YA TENÍA ASIGNADO si "EspacioId vacío" significa "no
            // tocarlo" (ver comentario arriba). Ese "ya tenía" vive en su AsignacionSemanal
            // actual — NO en Sesion.EspacioId, que es el requisito de aula FIJA (HC-S05, casi
            // siempre null) y no el aula que CP-SAT le asignó al generar.
            // Bug (1/2): comparar solo contra `espacioNuevo` significaba que mover una sesión de
            // hora sin especificar espacio nunca comprobaba si SU PROPIO espacio actual ya estaba
            // ocupado por otra sesión en la franja de destino — "reacomodar" podía aterrizar dos
            // sesiones en la misma aula a la misma hora y devolver EsFactible=true igual.
            var espacioActualEditada = asignPorSesion.TryGetValue(sesionEditada.Id, out var asigsEditadaActual)
                ? asigsEditadaActual.FirstOrDefault(a => a.EspacioId.HasValue)?.EspacioId
                : null;
            var espacioParaChoque = espacioNuevo ?? espacioActualEditada;

            var freedIds = new HashSet<Guid>();
            foreach (var s in sesiones.Where(s => s.Id != sesionEditada.Id))
            {
                if (!asignPorSesion.TryGetValue(s.Id, out var asigs)) continue;
                int dur = Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));
                bool choca = asigs.Any(a =>
                    idxPorBloque.TryGetValue(a.BloqueTiempoId, out var inicio) &&
                    Solapa(inicio, dur) &&
                    // Cohorte: independiente de la semana (la sesión ocupa el tiempo del grupo
                    // todas las semanas, presencial o virtualmente).
                    ((s.GrupoId.HasValue && s.GrupoId == sesionEditada.GrupoId) ||
                     // Aula: solo si ambas la ocupan alguna semana en común. Una pareja de
                     // alternancia comparte aula y bloque a propósito y no es conflicto.
                     (espacioParaChoque.HasValue && a.EspacioId == espacioParaChoque &&
                      ModalidadSemanal.CompartenSemanaDeEspacio(s, sesionEditada))));
                if (choca) freedIds.Add(s.Id);
            }

            sesionEditada.AsignarBloqueTiempo(bloqueNuevo.Id);
            if (espacioNuevo.HasValue) sesionEditada.AsignarEspacio(espacioNuevo.Value);

            var advertencias = new List<string>();
            List<AsignacionSemanal> nuevasAsignaciones;

            if (freedIds.Count == 0)
            {
                // Camino común: nada choca, no hace falta invocar CP-SAT — se reconstruye
                // directamente la fila de la editada. NO se delega en
                // CrearSesionManualService.CrearAsignaciones: esa construye el aula desde
                // Sesion.EspacioId, que es el REQUISITO fijo de aula (casi siempre null salvo
                // HC-S05), no el aula que la sesión ya tenía asignada — esa vive únicamente en su
                // AsignacionSemanal actual. Usarlo aquí borraba el aula en silencio cada vez que
                // se movía una sesión de hora sin volver a especificar espacio explícitamente.
                var modalidadEditada = ModalidadSemanal.ModalidadCanonica(sesionEditada);
                nuevasAsignaciones = new List<AsignacionSemanal>
                {
                    new(Guid.NewGuid(), sesionEditada.Id, ModalidadSemanal.SemanaCanonica(sesionEditada), bloqueNuevo.Id,
                        modalidadEditada == Modalidad.Presencial ? espacioParaChoque : null, modalidadEditada)
                };
            }
            else
            {
                // Regla 8 (horario base): todo lo que NO chocó queda fijo por igualdad — CP-SAT
                // resuelve casi al instante porque solo las liberadas tienen dominio real.
                var sesionesFijasIds = sesiones.Select(s => s.Id).Where(id => !freedIds.Contains(id)).ToHashSet();

                var resultado = await _fase2.ResolverFactibilidadAsync(
                    sesiones, bloquesGrid, espacios,
                    grupos: gruposMotor,
                    sesionesFijasIds: sesionesFijasIds,
                    ventanaPorAsignatura: ventanaPorAsig,
                    ct: ct);

                if (!resultado.EsFactible)
                    return new ReacomodarHorarioResponse
                    {
                        EsFactible = false,
                        MensajeError = resultado.MensajeError
                    };

                // Postcondición dura: de la salida del solver solo se toman la editada y las
                // liberadas — el resto del horario NO se toca, pase lo que pase con el solver.
                nuevasAsignaciones = resultado.Asignaciones
                    .Where(a => a.SesionId == sesionEditada.Id || freedIds.Contains(a.SesionId))
                    .ToList();

                foreach (var fid in freedIds)
                {
                    var libre    = sesiones.First(s => s.Id == fid);
                    var asigLibre = nuevasAsignaciones.FirstOrDefault(a => a.SesionId == fid);
                    if (asigLibre == null) continue;
                    libre.AsignarBloqueTiempo(asigLibre.BloqueTiempoId);
                    if (asigLibre.EspacioId.HasValue) libre.AsignarEspacio(asigLibre.EspacioId.Value);
                }
                advertencias.Add($"{freedIds.Count} sesión(es) en conflicto se reubicaron automáticamente.");
            }

            var idsAReemplazar = new HashSet<Guid>(freedIds) { sesionEditada.Id };
            var asignacionesFinal = asignacionesActuales
                .Where(a => !idsAReemplazar.Contains(a.SesionId))
                .Concat(nuevasAsignaciones)
                .ToList();

            // ── Post-chequeo de restricciones duras — ANTES de persistir ────────────────
            // Ésta es la única ruta de edición que conduce directamente una persona (a diferencia
            // de GenerarHorarioService, que sí valida su salida antes de publicar): sin esto, un
            // movimiento que el detector de choques de arriba no cubre (p. ej. HC-VH, HC-G01,
            // HC-CAP, HC-S03) se persistía igual con EsFactible=true.
            var sesionPorId = sesiones.ToDictionary(s => s.Id);
            var contextoValidacion = new ContextoValidacion(
                Bloques: bloquesGrid,
                VentanaPorAsignatura: ventanaPorAsig,
                DisponibilidadPorGrupo: gruposMotor
                    .GroupBy(g => g.Id)
                    .ToDictionary(g => g.Key, g => g.First().ObtenerDisponibilidadSemanal()),
                EstudiantesPorGrupo: gruposMotor
                    .GroupBy(g => g.Id)
                    .ToDictionary(g => g.Key, g => g.First().EstudiantesInscritos),
                EspacioPorId: espacios.ToDictionary(e => e.Id),
                RequisitosPorGrupo: gruposMotor
                    .GroupBy(g => g.Id)
                    .ToDictionary(g => g.Key, g => g.First().RequisitosEspacio),
                NombrePorAsignatura: asignaturas.ToDictionary(a => a.Id, a => a.Nombre),
                NombrePorGrupo: gruposMotor
                    .GroupBy(g => g.Id)
                    .ToDictionary(g => g.Key, g => g.First().Nombre));

            var conflictos = ValidadorRestriccionesDuras.Validar(
                asignacionesFinal, sesionPorId, idxPorBloque, contextoValidacion);
            if (conflictos.Count > 0)
                return new ReacomodarHorarioResponse
                {
                    EsFactible   = false,
                    MensajeError = $"El movimiento produce {conflictos.Count} violación(es) de restricción(es) dura(s) " +
                                   "y no se aplicó. " + string.Join(" ", conflictos.Take(5))
                };

            // ── Persistir: reemplazar solo las filas de la editada + liberadas ──
            var asignacionesViejasAEliminar = asignacionesActuales.Where(a => idsAReemplazar.Contains(a.SesionId)).ToList();

            await _uow.BeginTransactionAsync();
            try
            {
                // B4 auditoría: delete uno por uno en vez de un DeleteRangeAsync — ineficiencia, no
                // bug (misma transacción, y freedIds es siempre un puñado de sesiones en conflicto,
                // nunca la tabla completa). No se agrega DeleteRangeAsync a IAsignacionSemanalRepositorio
                // solo para esto: el costo de tocar los 11 fakes de test que implementan la interfaz
                // no se justifica para este volumen.
                foreach (var vieja in asignacionesViejasAEliminar)
                    await _asignacionRepo.DeleteAsync(vieja.Id);
                await _asignacionRepo.AddRangeAsync(nuevasAsignaciones);

                await _sesionRepo.UpdateAsync(sesionEditada);
                foreach (var fid in freedIds)
                    await _sesionRepo.UpdateAsync(sesiones.First(s => s.Id == fid));

                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }

            // ── Respuesta: horario completo refrescado (fijas intactas + las que se movieron) ──
            return new ReacomodarHorarioResponse
            {
                EsFactible   = true,
                Advertencias = advertencias,
                Sesiones     = GenerarHorarioService.ConstruirSesionesDto(sesiones, asignacionesFinal, gruposMotor, bloquesGrid)
            };
        }
    }
}

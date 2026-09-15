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
    /// Agrega una sesión manual al horario vigente sin re-ejecutar el modelo de optimización.
    /// Valida contra las asignaciones reales de ese horario antes de persistir: HC-I01 (docente
    /// libre) aquí, y el resto de restricciones duras con <see cref="ValidadorRestriccionesDuras"/>,
    /// la misma fuente que usan la generación y /reacomodar.
    /// </summary>
    public class CrearSesionManualService
    {
        private readonly IBloqueTiempoRepositorio      _bloques;
        private readonly IHorarioRepositorio           _horarios;
        private readonly ISesionRepositorio            _sesiones;
        private readonly IAsignacionSemanalRepositorio _asignaciones;
        private readonly IAsignaturaRepositorio        _asignaturas;
        private readonly IGrupoRepositorio             _grupos;
        private readonly IEspacioRepositorio           _espacios;
        private readonly IUnitOfWork                   _uow;

        public CrearSesionManualService(
            IBloqueTiempoRepositorio      bloques,
            IHorarioRepositorio           horarios,
            ISesionRepositorio            sesiones,
            IAsignacionSemanalRepositorio asignaciones,
            IAsignaturaRepositorio        asignaturas,
            IGrupoRepositorio             grupos,
            IEspacioRepositorio           espacios,
            IUnitOfWork                   uow)
        {
            _bloques      = bloques;
            _horarios     = horarios;
            _sesiones     = sesiones;
            _asignaciones = asignaciones;
            _asignaturas  = asignaturas;
            _grupos       = grupos;
            _espacios     = espacios;
            _uow          = uow;
        }

        /// <returns>
        /// La fila (<see cref="SesionGeneradaDto"/>) creada, lista para el frontend. Violación de hard
        /// constraint → <see cref="BusinessRuleViolationException"/> (409); request mal formado →
        /// <see cref="ArgumentException"/> (400); horario inexistente → <see cref="KeyNotFoundException"/> (404).
        /// </returns>
        public async Task<List<SesionGeneradaDto>> EjecutarAsync(CrearSesionManualRequest req)
        {
            // ── Día y bloque ──────────────────────────────────────────────────────
            var dia = MapearDia(req.Dia)
                ?? throw new ArgumentException("Día no válido.");

            if (!TimeOnly.TryParse(req.HoraInicio, out var horaInicio))
                throw new ArgumentException("Hora no válida. Use el formato 08:00.");

            // ERR2 auditoría: una franja que no existe en la grilla es un problema del REQUEST (400),
            // no un conflicto con datos ya persistidos (409).
            var bloque = await _bloques.FindByDiaHoraAsync(dia, horaInicio)
                ?? throw new ArgumentException(
                    $"Esa hora está fuera de la jornada (lunes a viernes " +
                    $"{GrillaInstitucional.HoraAperturaLunesAViernes:HH\\:mm}–{GrillaInstitucional.HoraCierreLunesAViernes:HH\\:mm}, " +
                    $"sábado {GrillaInstitucional.HoraAperturaSabado:HH\\:mm}–{GrillaInstitucional.HoraCierreSabado:HH\\:mm}).");

            // ── Tipo de sesión, modalidad y alternancia ───────────────────────────
            var tipoFlujo = CalculadorEspaciosSesion.ParseTipoFlujo(req.TipoFlujo);
            // Teoría virtual es fija e independiente de Alternancia (decisión de diseño): solo el
            // track de laboratorio alterna. Espacio nunca aplica a una sesión virtual (regla 9).
            var modalidad = tipoFlujo == TipoFlujo.AulaVirtual && req.EsVirtual
                ? Modalidad.Virtual
                : Modalidad.Presencial;

            if (!Enum.TryParse<TipoAlternancia>(req.Alternancia, ignoreCase: true, out var alternancia))
                alternancia = TipoAlternancia.SinAlternancia;
            var alternanciaFinal = tipoFlujo == TipoFlujo.Laboratorio ? alternancia : TipoAlternancia.SinAlternancia;

            // P0-5 auditoría: la sesión pertenece al horario vigente. Antes no entraba en ninguno:
            // respondía 201, desaparecía al recargar y seguía bloqueando docentes sin que nadie la viera.
            var horario = await _horarios.GetByIdAsync(req.HorarioId)
                ?? throw new KeyNotFoundException("El horario cambió mientras trabajaba. Recargue la página.");

            // Sesion.EspacioId es el aula FIJA exigida (HC-S05), no el aula elegida: esa vive solo en la
            // asignación, igual que en las sesiones generadas.
            var sesion = new Sesion(
                id:            Guid.NewGuid(),
                asignaturaId:  req.AsignaturaId,
                docenteId:     req.DocenteId,
                bloqueId:      bloque.Id,
                espacioId:     null,
                grupoId:       req.GrupoId,
                alternancia:   alternanciaFinal,
                modalidad:     modalidad,
                duracionHoras: req.DuracionHoras,
                esBloque:      false,
                estaDividida:  false,
                tipoFlujo:     tipoFlujo);

            // UNA fila por sesión, en su semana canónica (ALT-05).
            var modalidadFila = ModalidadSemanal.ModalidadCanonica(sesion);
            Guid? espacioFinal = modalidadFila == Modalidad.Presencial ? req.EspacioId : null;
            var asignacion = new AsignacionSemanal(Guid.NewGuid(), sesion.Id, ModalidadSemanal.SemanaCanonica(sesion),
                bloque.Id, espacioFinal, modalidadFila);

            // P0-4 auditoría: los chequeos leían Sesion.BloqueTiempoId, que en las sesiones generadas era
            // la pista de Fase 1 y no el bloque final: una sesión 1 h después de otra en la misma aula
            // pasaba con 201. La posición real es la de la asignación, y solo cuentan las del horario.
            var existentes   = await _sesiones.GetByIdsAsync(horario.SesioneIds);
            var asignaciones = await _asignaciones.GetBySesionIdsAsync(existentes.Select(s => s.Id));
            var sesionPorId  = existentes.Append(sesion).ToDictionary(s => s.Id);
            var bloquesGrid  = GrillaInstitucional.GenerarBloques();
            var idxPorBloque = Enumerable.Range(0, bloquesGrid.Count).ToDictionary(i => bloquesGrid[i].Id, i => i);
            var asignaturas  = await _asignaturas.GetAllAsync();

            // ── HC-I01: docente ocupado ── fuera del validador: el docente no es eje de generación (CR-08).
            if (req.DocenteId is Guid docenteId)
            {
                static int Bloques(Sesion s) => Math.Max(1, (int)Math.Ceiling(s.DuracionHoras));
                var diaPorIdx = BloquesPlanner.DiaPorBloqueIdx(bloquesGrid);
                var choque = asignaciones.FirstOrDefault(a =>
                    sesionPorId[a.SesionId].DocenteId == docenteId &&
                    idxPorBloque.TryGetValue(a.BloqueTiempoId, out var inicioOtra) &&
                    BloquesPlanner.Solapan(idxPorBloque[bloque.Id], Bloques(sesion), inicioOtra, Bloques(sesionPorId[a.SesionId]), diaPorIdx));
                if (choque is not null)
                {
                    string Nombre(Guid asignaturaId) => asignaturas.FirstOrDefault(x => x.Id == asignaturaId)?.Nombre ?? "asignatura sin nombre";
                    var bloqueOtra = bloquesGrid[idxPorBloque[choque.BloqueTiempoId]];
                    throw new BusinessRuleViolationException(
                        "El docente ya tiene clase a esa hora — " +
                        $"Sesión 1: {Nombre(req.AsignaturaId)} ({req.Dia} {req.HoraInicio}). " +
                        $"Sesión 2: {Nombre(sesionPorId[choque.SesionId].AsignaturaId)} " +
                        $"({GenerarHorarioService.DiaToString(bloqueOtra.Dia)} {bloqueOtra.HoraInicio:HH\\:mm}). " +
                        "Elija una hora diferente o cambie el docente de una de las dos sesiones. [HC-I01]");
                }
            }

            // ── Resto de restricciones duras (HC-C01, HC-S01, HC-S03, HC-S04, HC-S05, HC-CAP, HC-VH, HC-G01, HC-SEP) ──
            // Solo cuentan las violaciones que introduce la sesión nueva: una preexistente (p. ej. un grupo
            // editado en el catálogo después de generar) no debe impedir agregar otra sesión.
            var contexto = ContextoValidacion.DesdeCatalogo(bloquesGrid, asignaturas,
                await _grupos.GetAllAsync(), await _espacios.GetAllAsync());
            var previas = ValidadorRestriccionesDuras.Validar(asignaciones, sesionPorId, idxPorBloque, contexto).ToHashSet();
            var nuevas = ValidadorRestriccionesDuras.Validar(asignaciones.Append(asignacion), sesionPorId, idxPorBloque, contexto)
                .Where(c => !previas.Contains(c))
                .ToList();
            if (nuevas.Count > 0)
                throw new BusinessRuleViolationException(string.Join(" ", nuevas));

            // M5 auditoría: sesión, asignación y horario en una sola transacción.
            await _uow.BeginTransactionAsync();
            try
            {
                await _sesiones.AddAsync(sesion);
                await _asignaciones.AddRangeAsync(new[] { asignacion });
                horario.AgregarSesion(sesion.Id);
                await _horarios.UpdateAsync(horario);
                await _uow.CommitAsync();
            }
            catch
            {
                await _uow.RollbackAsync();
                throw;
            }

            return new List<SesionGeneradaDto>
            {
                GenerarHorarioService.MapearSesionDto(asignacion, sesion, bloquesGrid.ToDictionary(b => b.Id), espacioFinal?.ToString())
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOEA.Domain.Interfaces;
using SOEA.Domain.Services;

namespace SOEA.Application.Features.Docentes
{
    /// <summary>
    /// Fusiona docentes que son la misma persona pero quedaron fragmentados en varios registros
    /// por variantes de nombre en el Excel — causa raíz del síntoma "un docente con 2 sesiones a
    /// la misma hora" (el motor los ve como personas distintas y los agenda en paralelo).
    /// La fusión es MANUAL: el usuario elige el registro canónico y cuáles absorber. Reasigna las
    /// asignaturas de los duplicados al canónico y elimina los duplicados. NO toca las sesiones:
    /// son transitorias (se regeneran en cada corrida — regla 8) y su columna docente_id no tiene
    /// FK, así que no hay riesgo de integridad referencial.
    /// La disponibilidad del canónico se conserva tal cual; por eso el usuario debe elegir como
    /// canónico el registro con la disponibilidad correcta.
    /// </summary>
    public class FusionDocentesService
    {
        private readonly IDocenteRepositorio _docenteRepo;
        private readonly IAsignaturaRepositorio _asignaturaRepo;
        private readonly DocenteService _docenteService;

        public FusionDocentesService(
            IDocenteRepositorio docenteRepo,
            IAsignaturaRepositorio asignaturaRepo,
            DocenteService docenteService)
        {
            _docenteRepo = docenteRepo;
            _asignaturaRepo = asignaturaRepo;
            _docenteService = docenteService;
        }

        /// <summary>
        /// Grupos de docentes que probablemente son la misma persona, para revisión/fusión manual.
        /// Cada grupo trae 2+ docentes mapeados al DTO de UI.
        /// </summary>
        public async Task<List<List<DocenteUiDto>>> SugerirDuplicadosAsync()
        {
            var docentes = await _docenteService.GetAllAsync();
            var porId = docentes.ToDictionary(d => d.Id);

            var grupos = DetectorDocentesDuplicados.AgruparPosiblesDuplicados(
                docentes.Select(d => (d.Id, d.Nombre)));

            return grupos
                .Select(g => g.Where(x => porId.ContainsKey(x.Id)).Select(x => porId[x.Id]).ToList())
                .Where(g => g.Count > 1)
                .ToList();
        }

        /// <summary>
        /// Fusiona los <paramref name="duplicadosIds"/> en <paramref name="canonicoId"/>: mueve sus
        /// asignaturas al canónico y elimina los registros duplicados.
        /// </summary>
        public async Task<FusionResultado> FusionarAsync(Guid canonicoId, IReadOnlyCollection<Guid> duplicadosIds)
        {
            if (await _docenteRepo.GetByIdAsync(canonicoId) is null)
                throw new ArgumentException("El docente canónico no existe.");

            var aAbsorber = (duplicadosIds ?? Array.Empty<Guid>())
                .Where(id => id != canonicoId)
                .Distinct()
                .ToHashSet();

            if (aAbsorber.Count == 0)
                throw new ArgumentException("Debe indicar al menos un docente duplicado distinto del canónico.");

            // Reasignar asignaturas de los duplicados al canónico.
            var asignaturas = await _asignaturaRepo.GetAllAsync();
            int reasignadas = 0;
            foreach (var a in asignaturas)
            {
                if (a.DocenteId.HasValue && aAbsorber.Contains(a.DocenteId.Value))
                {
                    a.AsignarDocente(canonicoId);
                    await _asignaturaRepo.UpdateAsync(a);
                    reasignadas++;
                }
            }

            // Eliminar los registros duplicados.
            int eliminados = 0;
            foreach (var dupId in aAbsorber)
            {
                if (await _docenteRepo.GetByIdAsync(dupId) is null) continue;
                await _docenteRepo.DeleteAsync(dupId);
                eliminados++;
            }

            return new FusionResultado(canonicoId, eliminados, reasignadas);
        }
    }

    public readonly record struct FusionResultado(Guid CanonicoId, int DocentesEliminados, int AsignaturasReasignadas);
}

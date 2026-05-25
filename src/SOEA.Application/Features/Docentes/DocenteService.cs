using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;

namespace SOEA.Application.Features.Docentes
{
    public class DocenteService
    {
        private readonly IDocenteRepositorio _repo;

        private static readonly Dictionary<DiaDeSemana, string> DiaKey = new()
        {
            { DiaDeSemana.Lunes,     "lunes"     },
            { DiaDeSemana.Martes,    "martes"    },
            { DiaDeSemana.Miercoles, "miercoles" },
            { DiaDeSemana.Jueves,    "jueves"    },
            { DiaDeSemana.Viernes,   "viernes"   },
            { DiaDeSemana.Sábado,    "sabado"    },
        };

        private static readonly TimeOnly TodoStart = new(6, 0);
        private static readonly TimeOnly TodoEnd = new(22, 0);
        private static readonly TimeOnly OfficeStart = new(6, 0);
        private static readonly TimeOnly OfficeEnd = new(18, 0);
        private static readonly TimeOnly MatutinoStart = new(6, 0);
        private static readonly TimeOnly MatutinoEnd = new(12, 0);
        private static readonly TimeOnly VespertinoStart = new(12, 0);
        private static readonly TimeOnly VespertinoEnd = new(18, 0);
        private static readonly TimeOnly NocturnoStart = new(18, 0);
        private static readonly TimeOnly NocturnoEnd = new(22, 0);

        private const string TipoFranjaGeneral = "Franja general";
        private const string TipoFranjaEspecifica = "Franja específica";

        private const string LabelTodo = "Todo el día";
        private const string LabelOffice = "Horario de oficina (06:00–18:00)";
        private const string LabelMatutino = "Matutino (06:00–12:00)";
        private const string LabelVespertino = "Vespertino (12:00–18:00)";
        private const string LabelNocturno = "Nocturno (18:00–22:00)";

        public DocenteService(IDocenteRepositorio repo) => _repo = repo;

        public async Task<List<DocenteUiDto>> GetAllAsync()
        {
            var list = await _repo.GetAllAsync();
            return list.Select(MapToDto).ToList();
        }

        public async Task<DocenteUiDto> CreateAsync(DocenteUiDto dto)
        {
            var id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id;
            var docente = new Docente(
                id,
                dto.Nombre,
                "",
                $"{id}@soea.local",
                (decimal)dto.MaxHoras,
                new List<FranjaHoraria> { FranjaHoraria.Matutino, FranjaHoraria.Vespertino }
            );
            docente.ActualizarPersistenciaUi(dto.Cedula, dto.Disponibilidad?.GetRawText());
            await _repo.AddAsync(docente);
            return MapToDto(docente);
        }

        public async Task<DocenteUiDto?> UpdateAsync(Guid id, DocenteUiDto dto)
        {
            var existing = await _repo.GetByIdAsync(id);
            if (existing is null) return null;

            existing.ActualizarDatos(dto.Nombre, "", existing.Correo, (decimal)dto.MaxHoras);
            existing.ActualizarPersistenciaUi(dto.Cedula, dto.Disponibilidad?.GetRawText());
            await _repo.UpdateAsync(existing);

            return MapToDto(existing);
        }

        public Task DeleteAsync(Guid id) => _repo.DeleteAsync(id);

        private static DocenteUiDto MapToDto(Docente docente)
        {
            return new DocenteUiDto
            {
                Id = docente.Id,
                Nombre = docente.NombreCompleto.Trim(),
                Cedula = docente.CedulaIdentidad ?? "",
                MaxHoras = (double)docente.MaximoHorasSemanales,
                Disponibilidad = BuildDisponibilidad(docente)
            };
        }

        private static JsonElement? BuildDisponibilidad(Docente docente)
        {
            if (!string.IsNullOrEmpty(docente.DisponibilidadUiJson))
            {
                try { return JsonSerializer.Deserialize<JsonElement>(docente.DisponibilidadUiJson); }
                catch { return null; }
            }

            if (docente.BloquesDisponibles.Count == 0)
            {
                return null;
            }

            var bloquesPorDia = docente.BloquesDisponibles
                .GroupBy(b => b.Dia)
                .ToDictionary(g => g.Key, g => g.ToList());

            var obj = new JsonObject();

            foreach (var (dia, key) in DiaKey)
            {
                if (!bloquesPorDia.TryGetValue(dia, out var bloques) || bloques.Count == 0)
                {
                    obj[key] = new JsonObject { ["noDisponible"] = true };
                    continue;
                }

                var tipoGeneral = ResolveGeneralTipo(bloques);
                if (tipoGeneral is not null)
                {
                    obj[key] = new JsonObject
                    {
                        ["noDisponible"] = false,
                        ["tipo"] = TipoFranjaGeneral,
                        ["franjaGeneral"] = tipoGeneral
                    };
                    continue;
                }

                // Fallback: derive a specific range using min/max of the blocks for the day.
                var desde = bloques.Min(b => b.HoraInicio);
                var hasta = bloques.Max(b => b.HoraFin);

                obj[key] = new JsonObject
                {
                    ["noDisponible"] = false,
                    ["tipo"] = TipoFranjaEspecifica,
                    ["desde"] = desde.ToString("HH:mm"),
                    ["hasta"] = hasta.ToString("HH:mm")
                };
            }

            return JsonSerializer.Deserialize<JsonElement>(obj.ToJsonString());
        }

        private static string? ResolveGeneralTipo(List<BloqueTiempo> bloques)
        {
            if (CoversRange(bloques, TodoStart, TodoEnd)) return LabelTodo;
            if (CoversRange(bloques, OfficeStart, OfficeEnd)) return LabelOffice;
            if (CoversRange(bloques, MatutinoStart, MatutinoEnd)) return LabelMatutino;
            if (CoversRange(bloques, VespertinoStart, VespertinoEnd)) return LabelVespertino;
            if (CoversRange(bloques, NocturnoStart, NocturnoEnd)) return LabelNocturno;
            return null;
        }

        private static bool CoversRange(List<BloqueTiempo> bloques, TimeOnly start, TimeOnly end)
        {
            var ranges = bloques
                .Select(b => (b.HoraInicio, b.HoraFin))
                .OrderBy(b => b.HoraInicio)
                .ToList();

            if (ranges.Count == 0 || ranges[0].HoraInicio > start)
            {
                return false;
            }

            var currentEnd = ranges[0].HoraFin;
            for (var i = 1; i < ranges.Count; i++)
            {
                var next = ranges[i];
                if (next.HoraInicio > currentEnd)
                {
                    return false;
                }

                if (next.HoraFin > currentEnd)
                {
                    currentEnd = next.HoraFin;
                }
            }

            return currentEnd >= end;
        }
    }
}

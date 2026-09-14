using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SOEA.Domain.Enums;

namespace SOEA.Domain.ValueObjects
{
    /// <summary>
    /// Disponibilidad horaria declarada por día (JSON crudo de la UI, <c>DisponibilidadUiJson</c>).
    /// Fuente única de "¿puede este bloque [día, horaInicio, horaFin] recibir clase?" para
    /// <see cref="Entities.Docente"/> y <see cref="Entities.Grupo"/> — antes este parseo por día vivía
    /// solo para docentes (<c>GenerarHorarioService.MapearDocentes</c>); ahora ambos lo derivan de aquí.
    /// JSON vacío/ausente/sin entradas = sin restricción (todo bloque permitido).
    /// </summary>
    public sealed class DisponibilidadSemanal
    {
        /// <summary>Entrada cruda de un día tal como la envía la UI (mismo shape que usa Docente y Grupo).</summary>
        public sealed record DiaEntradaCruda(
            [property: JsonPropertyName("noDisponible")] bool NoDisponible,
            [property: JsonPropertyName("tipo")] string? Tipo,
            [property: JsonPropertyName("franjaGeneral")] string? FranjaGeneral,
            [property: JsonPropertyName("desde")] string? Desde,
            [property: JsonPropertyName("hasta")] string? Hasta);

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly Dictionary<DiaDeSemana, (TimeOnly Desde, TimeOnly Hasta)>? _ventanasPorDia;
        private readonly HashSet<DiaDeSemana>? _diasCerrados;

        private DisponibilidadSemanal(
            Dictionary<DiaDeSemana, (TimeOnly, TimeOnly)>? ventanasPorDia,
            HashSet<DiaDeSemana>? diasCerrados)
        {
            _ventanasPorDia = ventanasPorDia;
            _diasCerrados = diasCerrados;
        }

        /// <summary>Sin ninguna restricción declarada: todo bloque, de cualquier día, está permitido.</summary>
        public static readonly DisponibilidadSemanal SinRestriccion = new(null, null);

        public static DisponibilidadSemanal DesdeJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return SinRestriccion;

            Dictionary<string, DiaEntradaCruda>? crudo;
            try
            {
                crudo = JsonSerializer.Deserialize<Dictionary<string, DiaEntradaCruda>>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return SinRestriccion;
            }
            return Desde(crudo);
        }

        /// <summary>
        /// True si <paramref name="json"/> parsea sin error (o está ausente). H3 auditoría:
        /// <see cref="DesdeJson"/> traga cualquier JSON malformado y cae a
        /// <see cref="SinRestriccion"/> en silencio — comportamiento a prueba de fallos correcto
        /// para los muchos llamadores del pipeline que solo necesitan "algo usable", pero eso hace
        /// indistinguible "el grupo no declaró disponibilidad" de "la disponibilidad declarada
        /// llegó corrupta y se ignoró entera". SOEA.Domain no puede depender de ILogger (regla 2 de
        /// arquitectura), así que este helper solo informa; el llamador (GenerarHorarioService, que
        /// sí tiene logs) decide si lo reporta como advertencia.
        /// </summary>
        public static bool JsonEsValido(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;
            try
            {
                JsonSerializer.Deserialize<Dictionary<string, DiaEntradaCruda>>(json, JsonOptions);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static DisponibilidadSemanal Desde(IReadOnlyDictionary<string, DiaEntradaCruda>? porDia)
        {
            if (porDia is null || porDia.Count == 0) return SinRestriccion;

            var ventanas = new Dictionary<DiaDeSemana, (TimeOnly, TimeOnly)>();
            var cerrados = new HashSet<DiaDeSemana>();
            foreach (var (diaNombre, entrada) in porDia)
            {
                var dia = ParseDia(diaNombre);
                if (dia is null) continue;

                if (entrada.NoDisponible) { cerrados.Add(dia.Value); continue; }
                ventanas[dia.Value] = VentanaDe(entrada);
            }
            return new DisponibilidadSemanal(ventanas, cerrados);
        }

        /// <summary>
        /// True si el bloque [horaInicio, horaFin) del día cae dentro de la ventana declarada.
        /// Sin restricción global, o día sin entrada declarada (falta de información) → permitido.
        /// Día declarado explícitamente no-disponible, o fuera de la ventana → rechazado.
        /// </summary>
        public bool PermiteBloque(DiaDeSemana dia, TimeOnly horaInicio, TimeOnly horaFin)
        {
            if (_ventanasPorDia is null) return true;
            if (_diasCerrados!.Contains(dia)) return false;
            if (!_ventanasPorDia.TryGetValue(dia, out var ventana)) return true;
            return horaInicio >= ventana.Desde && horaFin <= ventana.Hasta;
        }

        /// <summary>
        /// Reduce la disponibilidad por día a las 2 franjas del modelo legacy
        /// (Matutino/Vespertino sobre el inicio) que todavía usa <see cref="Entities.Docente"/>
        /// (su <c>Disponibilidad</c> sigue siendo <c>List&lt;FranjaHoraria&gt;</c> — fuera del
        /// pipeline de generación, CR-08). <see cref="Services.CalculadorDominioSesion"/> (HC-G01
        /// de grupo) ya NO pasa por este puente desde P2: consume esta clase directamente vía
        /// <c>PermiteBloque</c>, con precisión por día. Pierde la dimensión "día" — solo usar para
        /// Docente. Lista vacía = sin restricción (misma semántica que <see cref="FranjaHoraria"/> hoy).
        /// </summary>
        public List<FranjaHoraria> ComoFranjasCoarse()
        {
            var franjas = new List<FranjaHoraria>();
            if (_ventanasPorDia is null) return franjas;

            bool matutino = false, vespertino = false;
            foreach (var ventana in _ventanasPorDia.Values)
            {
                if (ventana.Desde.Hour < 12) matutino = true;
                if (ventana.Hasta.Hour > 12 || (ventana.Hasta.Hour == 12 && ventana.Hasta.Minute > 0)) vespertino = true;
            }
            if (matutino) franjas.Add(FranjaHoraria.Matutino);
            if (vespertino) franjas.Add(FranjaHoraria.Vespertino);
            return franjas;
        }

        private static (TimeOnly Desde, TimeOnly Hasta) VentanaDe(DiaEntradaCruda entrada)
        {
            // Franja específica: ventana explícita [desde, hasta).
            if (entrada.Tipo == "Franja específica"
                && TimeOnly.TryParse(entrada.Desde, out var desdeEspecifico)
                && TimeOnly.TryParse(entrada.Hasta, out var hastaEspecifico))
            {
                return (desdeEspecifico, hastaEspecifico);
            }

            // DUP6 auditoría: el prefijo de la etiqueta decide la ventana fija — y esta ventana
            // debe coincidir con la que el frontend le mostró al docente al elegirla
            // (FRANJAS_DEFECTO en disponibilidad-editor.component.ts: Matutino 06:00–12:00,
            // Vespertino 12:00–18:00, Nocturno 18:00–22:00). Antes Matutino/Vespertino tenían aquí
            // 06:00–13:00/13:00–20:00 — un docente que eligió "Matutino (06:00–12:00)" quedaba
            // disponible para HC-G01/CP-SAT/GA hasta la 13:00, una hora más de lo que el label le
            // prometió. DocenteService (que solo renderiza la etiqueta de vuelta a la UI) ya usaba
            // los límites correctos; esta es la fuente que de verdad aplica la restricción dura.
            var franja = entrada.FranjaGeneral ?? string.Empty;
            if (franja.StartsWith("Matutino", StringComparison.OrdinalIgnoreCase))
                return (new TimeOnly(6, 0), new TimeOnly(12, 0));
            if (franja.StartsWith("Vespertino", StringComparison.OrdinalIgnoreCase))
                return (new TimeOnly(12, 0), new TimeOnly(18, 0));
            if (franja.StartsWith("Nocturno", StringComparison.OrdinalIgnoreCase))
                return (new TimeOnly(18, 0), new TimeOnly(22, 0));

            // "Todo el día" u otro valor no reconocido: rango operativo completo, sin acotar.
            return (new TimeOnly(6, 0), new TimeOnly(22, 0));
        }

        private static DiaDeSemana? ParseDia(string dia) => dia.Trim().ToLowerInvariant() switch
        {
            "lunes"     => DiaDeSemana.Lunes,
            "martes"    => DiaDeSemana.Martes,
            "miercoles" => DiaDeSemana.Miercoles,
            "miércoles" => DiaDeSemana.Miercoles,
            "jueves"    => DiaDeSemana.Jueves,
            "viernes"   => DiaDeSemana.Viernes,
            "sabado"    => DiaDeSemana.Sábado,
            "sábado"    => DiaDeSemana.Sábado,
            _           => null
        };
    }
}

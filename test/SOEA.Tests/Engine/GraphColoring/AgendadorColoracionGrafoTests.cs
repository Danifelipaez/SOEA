using Microsoft.Extensions.Logging.Abstractions;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Engine.GraphColoring;

namespace SOEA.Tests.Engine.GraphColoring
{
    /// <summary>
    /// Fase 1 (Welsh-Powell duration-aware): verifica que el warm-start respeta HC-G01/HC-VH
    /// (B3 — misma fuente que CP-SAT/GA: CalculadorDominioSesion) y que el orden round-robin por
    /// día evita amontonar todas las sesiones al inicio de la semana cuando el grafo de conflictos
    /// es completo (cohorte única).
    /// </summary>
    public class AgendadorColoracionGrafoTests
    {
        private static AgendadorColoracionGrafo Motor() =>
            new(new ConstructorGrafoConflictos(), NullLogger<AgendadorColoracionGrafo>.Instance);

        private static List<BloqueTiempo> Grilla(int bloquesPorDia, params DiaDeSemana[] dias)
        {
            var bloques = new List<BloqueTiempo>();
            foreach (var dia in (dias.Length == 0
                         ? new[] { DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles }
                         : dias))
                for (int h = 0; h < bloquesPorDia; h++)
                    bloques.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(6 + h, 0), new TimeOnly(7 + h, 0)));
            return bloques;
        }

        private static Sesion Sesion(Guid grupoId, decimal dur = 1m, Guid? asignaturaId = null) =>
            new(Guid.NewGuid(), asignaturaId ?? Guid.NewGuid(), null, Guid.NewGuid(), null, grupoId,
                TipoAlternancia.SinAlternancia, Modalidad.Virtual, dur, false, false);

        // HC-G01: un grupo Matutino nunca debe recibir un bloque en la tarde, aunque el warm-start
        // sea solo una pista para CP-SAT — antes ignoraba la franja por completo.
        [Fact]
        public async Task GrupoMatutino_NingunBloqueAsignadoCaeEnLaTarde()
        {
            var grupoId = Guid.NewGuid();
            var grupo = new Grupo(grupoId, "Cohorte", Guid.Empty, estudiantesInscritos: 20,
                disponibilidad: new List<FranjaHoraria> { FranjaHoraria.Matutino });
            // 12 bloques/día (06:00–18:00): la mitad matutina, la mitad vespertina.
            var bloques = Grilla(12);
            var sesiones = Enumerable.Range(0, 6).Select(_ => Sesion(grupoId)).ToList();

            var resultado = (await Motor().AsignarBloquesDeTiempoAsync(
                sesiones, bloques, new List<Grupo> { grupo })).ToList();

            var bloquePorId = bloques.ToDictionary(b => b.Id);
            Assert.All(resultado.Where(s => s.BloqueTiempoId != Guid.Empty && bloquePorId.ContainsKey(s.BloqueTiempoId)),
                s => Assert.True(bloquePorId[s.BloqueTiempoId].HoraInicio.Hour < 12,
                    $"Sesión {s.Id} cayó en {bloquePorId[s.BloqueTiempoId].HoraInicio}, fuera de la franja Matutino."));
        }

        // HC-VH: ninguna sesión debe caer fuera de la ventana horaria de su asignatura.
        [Fact]
        public async Task ConVentanaHoraria_NingunaSesionCaeFueraDeLaVentana()
        {
            var grupoId = Guid.NewGuid();
            var asigId  = Guid.NewGuid();
            var bloques = Grilla(12); // 06:00–18:00
            var sesiones = Enumerable.Range(0, 3)
                .Select(_ => Sesion(grupoId, 1m, asigId)).ToList();
            var ventanas = new Dictionary<Guid, (TimeOnly? min, TimeOnly? max)>
            {
                [asigId] = (new TimeOnly(9, 0), new TimeOnly(11, 0))
            };

            var resultado = (await Motor().AsignarBloquesDeTiempoAsync(
                sesiones, bloques, ventanaPorAsignatura: ventanas)).ToList();

            var bloquePorId = bloques.ToDictionary(b => b.Id);
            Assert.All(resultado.Where(s => s.BloqueTiempoId != Guid.Empty && bloquePorId.ContainsKey(s.BloqueTiempoId)),
                s =>
                {
                    var inicio = bloquePorId[s.BloqueTiempoId].HoraInicio;
                    Assert.True(inicio >= new TimeOnly(9, 0) && inicio.AddHours(1) <= new TimeOnly(11, 0),
                        $"Sesión {s.Id} inició a las {inicio}, fuera de la ventana [09:00–11:00].");
                });
        }

        // B3 anti-amontonamiento: con cohorte única (grafo completo), el orden round-robin por día
        // reparte el warm-start entre varios días en vez de apilarlo todo el lunes por la mañana.
        [Fact]
        public async Task CohorteUnica_ElWarmStartSeRepartaEntreVariosDias()
        {
            var grupoId = Guid.NewGuid();
            var bloques = Grilla(8, DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles,
                DiaDeSemana.Jueves, DiaDeSemana.Viernes); // 5 días × 8 bloques
            var sesiones = Enumerable.Range(0, 5).Select(_ => Sesion(grupoId)).ToList();

            var resultado = (await Motor().AsignarBloquesDeTiempoAsync(sesiones, bloques)).ToList();

            var bloquePorId = bloques.ToDictionary(b => b.Id);
            var diasUsados = resultado
                .Where(s => s.BloqueTiempoId != Guid.Empty && bloquePorId.ContainsKey(s.BloqueTiempoId))
                .Select(s => bloquePorId[s.BloqueTiempoId].Dia)
                .Distinct()
                .Count();

            Assert.True(diasUsados >= 3,
                $"El warm-start solo usó {diasUsados} día(s); el round-robin debería repartir entre varios.");
        }
    }
}

using System;
using System.Collections.Generic;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Fuente ÚNICA del rango horario institucional y de la grilla canónica de bloques de tiempo.
    /// Antes existían tres valores distintos para el mismo dato: esta grilla usaba sábado hasta las
    /// 13:00, <see cref="BloqueTiempo"/> validaba hasta las 14:00, y el lector de Excel también
    /// asumía 14:00 (auditoría de saneamiento, hallazgo H1) — una fila de Excel entre 13:00 y 14:00
    /// pasaba la validación de <see cref="BloqueTiempo"/> pero no encontraba bloque en esta grilla,
    /// y el lector fabricaba un <see cref="BloqueTiempo"/> con Guid aleatorio que quedaba como
    /// referencia colgante en <c>Sesiones.bloque_tiempo_id</c>. <see cref="BloqueTiempo.Validar"/> y
    /// <c>LectorExcel</c> leen ahora las constantes de aquí; no vuelvan a declarar el rango por su
    /// cuenta.
    ///
    /// <c>ATENCIÓN — dato bloqueante (CLAUDE.md §4):</c> el rango horario real de la institución NO
    /// está confirmado por Rosa (coordinadora académica). Se fijó el corte de sábado en 14:00 — el
    /// más amplio de los tres valores en conflicto — porque es el que no descarta datos reales: un
    /// corte más estrecho rechazaría filas de Excel válidas, y ampliarlo nunca pierde información.
    /// Si Rosa confirma otro valor, cambiar la constante de abajo; no hay más sitios que tocar.
    /// </summary>
    public static class GrillaInstitucional
    {
        public static readonly TimeOnly HoraAperturaLunesAViernes = new(6, 0);
        public static readonly TimeOnly HoraCierreLunesAViernes   = new(22, 0);
        public static readonly TimeOnly HoraAperturaSabado        = new(6, 0);
        public static readonly TimeOnly HoraCierreSabado          = new(14, 0);

        private static readonly DiaDeSemana[] DiasLunesAViernes =
        {
            DiaDeSemana.Lunes, DiaDeSemana.Martes, DiaDeSemana.Miercoles,
            DiaDeSemana.Jueves, DiaDeSemana.Viernes
        };

        /// <summary>Genera la grilla canónica: bloques de 1 hora, L–V y Sábado, en el rango declarado arriba.</summary>
        public static List<BloqueTiempo> GenerarBloques()
        {
            var bloques = new List<BloqueTiempo>();

            foreach (var dia in DiasLunesAViernes)
                for (int h = HoraAperturaLunesAViernes.Hour; h < HoraCierreLunesAViernes.Hour; h++)
                {
                    var horaInicio = new TimeOnly(h, 0);
                    bloques.Add(new BloqueTiempo(IdDeterministico(dia, horaInicio), dia, horaInicio, new TimeOnly(h + 1, 0)));
                }

            for (int h = HoraAperturaSabado.Hour; h < HoraCierreSabado.Hour; h++)
            {
                var horaInicio = new TimeOnly(h, 0);
                bloques.Add(new BloqueTiempo(IdDeterministico(DiaDeSemana.Sábado, horaInicio), DiaDeSemana.Sábado, horaInicio, new TimeOnly(h + 1, 0)));
            }

            return bloques;
        }

        /// <summary>
        /// Id estable por (día, hora) — no un Guid.NewGuid() al vuelo. La grilla se regenera en cada
        /// request (GenerarHorarioService, ReacomodarHorarioService); sin un id determinístico, un
        /// BloqueTiempoId persistido en una corrida no se puede volver a encontrar en la siguiente
        /// (P5: reacomodar necesita correlacionar el bloque de una sesión ya generada).
        /// </summary>
        private static Guid IdDeterministico(DiaDeSemana dia, TimeOnly horaInicio)
        {
            var key = $"bloque-{dia}-{horaInicio:HH\\:mm}";
            var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key));
            return new Guid(hash);
        }
    }
}

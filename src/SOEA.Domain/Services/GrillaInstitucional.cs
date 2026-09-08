using System;
using System.Collections.Generic;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;

namespace SOEA.Domain.Services
{
    /// <summary>
    /// Fuente única de la grilla canónica de bloques de tiempo institucional. Antes se generaba
    /// inline en <c>GenerarHorarioService</c> con literales sin nombre; C1 auditoría la extrae aquí
    /// para que exista un solo lugar que editar si cambia el horario real.
    ///
    /// <c>ATENCIÓN — dato bloqueante (CLAUDE.md §4):</c> el rango horario real de la institución
    /// NO está confirmado. El código usa 06:00–22:00 (L–V) / 06:00–13:00 (Sáb); la documentación
    /// histórica menciona 07:00–20:00 y 07:00–21:30 en distintos lugares — tres valores distintos
    /// para el mismo dato. Esta clase preserva el valor que ya corría en producción (06:00–22:00 /
    /// 06:00–13:00) sin adivinar cuál es el correcto: hay que confirmarlo con Rosa (coordinadora
    /// académica) y actualizar las constantes de abajo, no los call sites.
    /// </summary>
    public static class GrillaInstitucional
    {
        public static readonly TimeOnly HoraAperturaLunesAViernes = new(6, 0);
        public static readonly TimeOnly HoraCierreLunesAViernes   = new(22, 0);
        public static readonly TimeOnly HoraAperturaSabado        = new(6, 0);
        public static readonly TimeOnly HoraCierreSabado          = new(13, 0);

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

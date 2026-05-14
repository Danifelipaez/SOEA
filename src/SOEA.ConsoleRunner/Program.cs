using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SOEA.Domain.Entities;
using SOEA.Domain.Enums;
using SOEA.Domain.Interfaces;
using SOEA.Engine.GraphColoring;
using SOEA.Infrastructure.Excel;

namespace SOEA.ConsoleRunner
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .ConfigureServices((context, services) =>
                {
                    // Registrar Infraestructura Excel
                    services.AddExcelInfrastructure();
                    // Registrar Motor de Coloración de Grafos
                    services.AddGraphColoringEngine();
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            
            Console.WriteLine("=====================================================");
            Console.WriteLine("   SOEA - PIPELINE DE AGENDAMIENTO (TEST TERMINAL)   ");
            Console.WriteLine("=====================================================");

            Console.WriteLine();
            Console.Write("Por favor, ingresa la ruta del archivo Excel de Asignaturas (Currículum): ");
            var rutaAsignaturas = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(rutaAsignaturas) || !File.Exists(rutaAsignaturas))
            {
                logger.LogError("El archivo de asignaturas no existe o la ruta es inválida: {Ruta}", rutaAsignaturas);
                return;
            }

            var lectorExcel = host.Services.GetRequiredService<ILectorExcel>();
            var motorColoracion = host.Services.GetRequiredService<IMotorColoracionGrafo>();

            try
            {
                logger.LogInformation("Iniciando Pipeline de Procesamiento...");

                // 1. Ingesta de Datos desde el Excel del horario existente
                using var streamAsignaturas = new FileStream(rutaAsignaturas, FileMode.Open, FileAccess.Read);
                var curriculum = await lectorExcel.LeerCurriculumAsync(streamAsignaturas);

                var asignaturas = curriculum.Asignaturas;
                var docentes    = curriculum.Docentes;

                // 2. Preparación de Sesiones (una por cada SesionesPorSemana de la Asignatura)
                logger.LogInformation("Preparando sesiones para ser agendadas basadas en las asignaturas...");
                var sesiones = new List<Sesion>();
                var docenteList = docentes.ToList();
                int docenteIdx = 0;

                foreach (var asignatura in asignaturas)
                {
                    var docenteAsignado = docenteList.Count > 0 ? docenteList[docenteIdx % docenteList.Count] : null;

                    for (int i = 0; i < asignatura.SesionesPorSemana; i++)
                    {
                        var sesion = new Sesion(
                            Guid.NewGuid(),
                            asignatura.Id,
                            docenteAsignado?.Id ?? Guid.NewGuid(),
                            Guid.Empty, // Bloque vacío inicial (será asignado por Welsh-Powell)
                            null,
                            null, // Grupo — desactivado para el piloto
                            asignatura.Alternancia,
                            Modalidad.Presencial,
                            asignatura.HorasPorSesion > 0 ? (decimal)asignatura.HorasPorSesion : 2m,
                            false,
                            false
                        );
                        sesiones.Add(sesion);
                    }
                    docenteIdx++;
                }

                // Bloques de tiempo: usando los bloques de disponibilidad extraídos del Excel,
                // o un set ficticio si no hay docentes con disponibilidad registrada.
                var bloquesDisponibles = docentes
                    .SelectMany(d => d.BloquesDisponibles)
                    .GroupBy(b => (b.Dia, b.HoraInicio))
                    .Select(g => g.First())
                    .ToList<BloqueTiempo>();

                if (!bloquesDisponibles.Any())
                {
                    logger.LogWarning("No se encontraron bloques de disponibilidad en el Excel. Usando bloques ficticios de prueba.");
                    foreach (DiaDeSemana dia in Enum.GetValues(typeof(DiaDeSemana)))
                    {
                        bloquesDisponibles.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(7, 0), new TimeOnly(9, 0)));
                        bloquesDisponibles.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(9, 0), new TimeOnly(11, 0)));
                        bloquesDisponibles.Add(new BloqueTiempo(Guid.NewGuid(), dia, new TimeOnly(11, 0), new TimeOnly(13, 0)));
                    }
                }

                // 3. Fase de Coloración de Grafos
                logger.LogInformation("Enviando {Cantidad} sesiones al Motor de Coloración de Grafos.", sesiones.Count);
                var sesionesColoreadas = await motorColoracion.AsignarBloquesDeTiempoAsync(sesiones, bloquesDisponibles);

                var asignadas  = sesionesColoreadas.Count(s => s.Estado == EstadoSesion.Asignada);
                var conflictos = sesionesColoreadas.Count(s => s.Estado == EstadoSesion.Conflicto);

                Console.WriteLine();
                Console.WriteLine("=====================================================");
                Console.WriteLine("                RESULTADOS DEL PIPELINE              ");
                Console.WriteLine("=====================================================");
                Console.WriteLine($"Facultades detectadas           : {curriculum.Facultades.Count}");
                Console.WriteLine($"Programas detectados            : {curriculum.Programas.Count}");
                Console.WriteLine($"Total de Asignaturas Ingestadas : {asignaturas.Count}");
                Console.WriteLine($"Total de Docentes Ingestados    : {docentes.Count}");
                Console.WriteLine($"Total de Sesiones Creadas       : {sesiones.Count}");
                Console.WriteLine($"Sesiones Agendadas Exitosamente : {asignadas}");
                Console.WriteLine($"Sesiones Sin Bloque (Conflicto) : {conflictos}");
                Console.WriteLine("=====================================================");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ocurrió un error inesperado durante la ejecución del pipeline.");
            }
        }
    }
}

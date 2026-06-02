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
using SOEA.Engine.ConstraintProg;
using SOEA.Engine.Genetic;
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
                    // Registrar Motor de Coloración de Grafos (Fase 1)
                    services.AddGraphColoringEngine();
                    // Registrar Motor CP-SAT (Fase 2)
                    services.AddConstraintProgEngine();
                    // Registrar Motor Genético (Fase 3)
                    services.AddGeneticEngine();
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            
            Console.WriteLine("=====================================================");
            Console.WriteLine("   SOEA - PIPELINE DE AGENDAMIENTO (TEST TERMINAL)   ");
            Console.WriteLine("=====================================================");

            var lectorExcel = host.Services.GetRequiredService<ILectorExcel>();
            var motorColoracion = host.Services.GetRequiredService<IMotorColoracionGrafo>();
            var motorCP = host.Services.GetRequiredService<IMotorConstraintProgramming>();
            var motorGenetico = host.Services.GetRequiredService<IMotorGenetico>();

            string rutaModo1 = "";
            string rutaModo2Asignaturas = "";
            string rutaModo2Docentes = "";

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("--- MODO DE INGRESO DE DATOS ---");
                Console.WriteLine($"1. Ingresar Excel de Horario Funcional (Ruta actual: {rutaModo1 ?? "Ninguna"})");
                Console.WriteLine($"2. Ingresar Excel de Asignaturas (Modo 2) (Ruta actual: {rutaModo2Asignaturas ?? "Ninguna"})");
                Console.WriteLine($"3. Ingresar Excel Disponibilidad Docentes (Modo 2) (Ruta actual: {rutaModo2Docentes ?? "Ninguna"})");
                Console.WriteLine("4. Ejecutar Pipeline");
                Console.WriteLine("5. Salir");
                Console.Write("Selecciona una opción: ");
                var opcionMenu = Console.ReadLine();

                if (opcionMenu == "1")
                {
                    Console.Write("Ruta: ");
                    rutaModo1 = Console.ReadLine()?.Replace("\"", "");
                }
                else if (opcionMenu == "2")
                {
                    Console.Write("Ruta: ");
                    rutaModo2Asignaturas = Console.ReadLine()?.Replace("\"", "");
                }
                else if (opcionMenu == "3")
                {
                    Console.Write("Ruta: ");
                    rutaModo2Docentes = Console.ReadLine()?.Replace("\"", "");
                }
                else if (opcionMenu == "5")
                {
                    return;
                }
                else if (opcionMenu == "4")
                {
                    if (string.IsNullOrWhiteSpace(rutaModo1) && (string.IsNullOrWhiteSpace(rutaModo2Asignaturas) || string.IsNullOrWhiteSpace(rutaModo2Docentes)))
                    {
                        Console.WriteLine("Por favor, ingresa la ruta 1 O las rutas 2 y 3 antes de ejecutar.");
                        continue;
                    }
                    break;
                }
            }

            try
            {
                logger.LogInformation("Iniciando Pipeline de Procesamiento...");
                CurriculumExcelResult curriculum = null;

                if (!string.IsNullOrWhiteSpace(rutaModo1))
                {
                    if (!File.Exists(rutaModo1)) { logger.LogError("Archivo no encontrado: {Ruta}", rutaModo1); return; }
                    logger.LogInformation("Usando Modo 1: Horario Funcional Completo");
                    using var stream = new FileStream(rutaModo1, FileMode.Open, FileAccess.Read);
                    curriculum = await lectorExcel.LeerCurriculumAsync(stream);
                }
                else
                {
                    if (!File.Exists(rutaModo2Asignaturas)) { logger.LogError("Archivo no encontrado: {Ruta}", rutaModo2Asignaturas); return; }
                    if (!File.Exists(rutaModo2Docentes)) { logger.LogError("Archivo no encontrado: {Ruta}", rutaModo2Docentes); return; }
                    
                    logger.LogInformation("Usando Modo 2: Archivos Separados");
                    using var streamAsig = new FileStream(rutaModo2Asignaturas, FileMode.Open, FileAccess.Read);
                    curriculum = await lectorExcel.LeerAsignaturasModo2Async(streamAsig);

                    using var streamDoc = new FileStream(rutaModo2Docentes, FileMode.Open, FileAccess.Read);
                    var docentesConDisp = await lectorExcel.LeerDisponibilidadDocentesAsync(streamDoc, curriculum.Docentes);
                    
                    // Recrear CurriculumExcelResult con los docentes que ahora tienen disponibilidad real
                    curriculum = new CurriculumExcelResult(
                        curriculum.Facultades,
                        curriculum.Programas,
                        curriculum.Asignaturas,
                        docentesConDisp.ToList().AsReadOnly(),
                        curriculum.SesionesPredefinidas,
                        curriculum.Espacios,
                        curriculum.Grupos
                    );
                }

                var asignaturas = curriculum.Asignaturas;
                var docentes    = curriculum.Docentes;

                // 2. Usar las Sesiones Predefinidas leídas exactamente del Excel
                logger.LogInformation("Preparando sesiones para ser agendadas basadas en las asignaturas y docentes del Excel...");
                var sesiones = curriculum.SesionesPredefinidas.ToList();


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

                // 4. Fase 2 — Constraint Programming (CP-SAT)
                Console.WriteLine();
                Console.WriteLine("--- FASE 2: CONSTRAINT PROGRAMMING (CP-SAT) ---");
                var resultadoCP = await motorCP.ResolverFactibilidadAsync(
                    sesionesColoreadas, bloquesDisponibles, curriculum.Espacios, docentes);

                // Fase 2 ahora devuelve asignaciones bi-semanales (dos AsignacionSemanal por sesión).
                // La sesión lógica (colorada en Fase 1) sigue siendo la unidad que alimenta la Fase 3.
                IEnumerable<Sesion> sesionesFase2 = sesionesColoreadas;
                if (resultadoCP.EsFactible)
                {
                    Console.WriteLine($"Fase 2: FACTIBLE — {resultadoCP.Asignaciones.Count} asignaciones semanales (Semana A/B).");
                }
                else
                {
                    Console.WriteLine($"Fase 2: NO FACTIBLE — {resultadoCP.MensajeError}");
                    Console.WriteLine("Se continuará con la asignación de la Fase 1.");
                }

                // 5. Fase 3 — Algoritmo Genético
                Console.WriteLine();
                Console.WriteLine("--- FASE 3: ALGORITMO GENÉTICO ---");
                var asignacionesFase2 = resultadoCP.EsFactible
                    ? resultadoCP.Asignaciones
                    : (IReadOnlyList<AsignacionSemanal>)Array.Empty<AsignacionSemanal>();

                var resultadoGA = await motorGenetico.OptimizarAsync(
                    sesionesFase2, asignacionesFase2, bloquesDisponibles, curriculum.Espacios, docentes);

                Console.WriteLine($"Fase 3: Fitness final = {resultadoGA.PuntajeFitness} | Generaciones = {resultadoGA.Generaciones} | Fallback = {resultadoGA.UsoFallback}");

                // El GA ya no muta las sesiones: la unidad lógica sigue siendo la sesión coloreada;
                // las asignaciones optimizadas (A/B) son el resultado a mostrar.
                var sesionesFinales = sesionesFase2.ToList();
                var asignacionesFinales = resultadoGA.AsignacionesOptimizadas;
                var sesionPorId = sesionesFinales.ToDictionary(s => s.Id);

                Console.WriteLine();
                Console.WriteLine("=====================================================");
                Console.WriteLine("            RESULTADOS FINALES DEL PIPELINE          ");
                Console.WriteLine("=====================================================");
                Console.WriteLine($"Sesiones lógicas                : {sesionesFinales.Count}");
                Console.WriteLine($"Asignaciones semanales (A/B)    : {asignacionesFinales.Count}");
                Console.WriteLine($"Fallback a Fase 2               : {(resultadoGA.UsoFallback ? "Sí" : "No")}");
                Console.WriteLine($"Puntaje Fitness (Fase 3)        : {resultadoGA.PuntajeFitness}");
                Console.WriteLine("=====================================================");

                bool exitMenu = false;
                while (!exitMenu)
                {
                    Console.WriteLine("\n--- MENÚ INTERACTIVO ---");
                    Console.WriteLine("1. Ver Asignaturas");
                    Console.WriteLine("2. Ver Docentes");
                    Console.WriteLine("3. Ver Todas las Sesiones (Final)");
                    Console.WriteLine("4. Ver Sesiones con Conflicto");
                    Console.WriteLine("5. Ver Resultados por Fase");
                    Console.WriteLine("6. Salir");
                    Console.Write("Selecciona una opción: ");
                    var opcion = Console.ReadLine();

                    switch (opcion)
                    {
                        case "1":
                            Console.WriteLine("\n--- Asignaturas ---");
                            foreach(var a in asignaturas) Console.WriteLine($"- {a.Nombre} (Código: {a.Codigo})");
                            break;
                        case "2":
                            Console.WriteLine("\n--- Docentes ---");
                            foreach(var d in docentes) Console.WriteLine($"- {d.NombreCompleto}");
                            break;
                        case "3":
                            Console.WriteLine("\n--- Todas las Asignaciones Semanales (Post Fase 3) ---");
                            foreach(var asg in asignacionesFinales.OrderBy(x => x.SesionId).ThenBy(x => x.Semana))
                            {
                                sesionPorId.TryGetValue(asg.SesionId, out var s);
                                var a = s != null ? (asignaturas.FirstOrDefault(x => x.Id == s.AsignaturaId)?.Nombre ?? "Desconocida") : "Desconocida";
                                var d = s != null ? (docentes.FirstOrDefault(x => x.Id == s.DocenteId)?.NombreCompleto ?? "Desconocido") : "Desconocido";
                                var e = curriculum.Espacios.FirstOrDefault(x => x.Id == asg.EspacioId)?.Nombre ?? "Virtual";
                                var bloque = bloquesDisponibles.FirstOrDefault(b => b.Id == asg.BloqueTiempoId);
                                var bloqueStr = bloque != null ? $"{bloque.Dia} {bloque.HoraInicio}-{bloque.HoraFin}" : "Sin bloque";
                                Console.WriteLine($"- {a} | {d} | Semana {asg.Semana} | {asg.Modalidad} | {e} | {bloqueStr}");
                            }
                            break;
                        case "4":
                            Console.WriteLine("\n--- Estado de factibilidad ---");
                            Console.WriteLine(resultadoGA.UsoFallback
                                ? "La Fase 3 hizo fallback a la solución factible de la Fase 2 (no pudo mejorar de forma factible)."
                                : "La Fase 3 optimizó sobre la solución factible sin introducir violaciones de restricciones duras.");
                            break;
                        case "5":
                            Console.WriteLine("\n--- Resultados por Fase ---");
                            Console.WriteLine($"Fase 1 (Graph Coloring): {asignadas} asignadas, {conflictos} en conflicto");
                            Console.WriteLine($"Fase 2 (CP-SAT): {(resultadoCP.EsFactible ? "FACTIBLE" : "NO FACTIBLE")}");
                            Console.WriteLine($"Fase 3 (Genético): Fitness = {resultadoGA.PuntajeFitness}, Generaciones = {resultadoGA.Generaciones}");
                            break;
                        case "6":
                            exitMenu = true;
                            break;
                        default:
                            Console.WriteLine("Opción no válida.");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ocurrió un error inesperado durante la ejecución del pipeline.");
            }
        }
    }
}

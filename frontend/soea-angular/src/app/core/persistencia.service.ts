import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Asignatura, CriterioCesionAlternancia, DisponibilidadUi, Docente, Espacio, Facultad, Grupo, Programa, RequisitoEspacio, Sesion } from './models';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class PersistenciaService {
  private http = inject(HttpClient);
  private base = environment.apiBaseUrl;

  // ── Facultades y Programas ─────────────────────────────────────────────────────

  cargarFacultades(): Observable<Facultad[]> {
    return this.http.get<Facultad[]>(`${this.base}/facultades`);
  }

  guardarFacultad(f: Facultad): Observable<Facultad> {
    return this.http.post<Facultad>(`${this.base}/facultades`, { id: f.id, nombre: f.nombre });
  }

  actualizarFacultad(f: Facultad): Observable<Facultad> {
    return this.http.put<Facultad>(`${this.base}/facultades/${f.id}`, { id: f.id, nombre: f.nombre });
  }

  eliminarFacultadBD(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/facultades/${id}`);
  }

  cargarProgramas(): Observable<Programa[]> {
    return this.http.get<Programa[]>(`${this.base}/programas`);
  }

  guardarPrograma(p: Programa): Observable<Programa> {
    return this.http.post<Programa>(`${this.base}/programas`, { id: p.id, nombre: p.nombre, facultadId: p.facultadId });
  }

  actualizarPrograma(p: Programa): Observable<Programa> {
    return this.http.put<Programa>(`${this.base}/programas/${p.id}`, { id: p.id, nombre: p.nombre, facultadId: p.facultadId });
  }

  eliminarProgramaBD(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/programas/${id}`);
  }

  // ── Docentes ───────────────────────────────────────────────────────────────────

  cargarDocentes(): Observable<DocenteApi[]> {
    return this.http.get<DocenteApi[]>(`${this.base}/docentes`);
  }

  guardarDocente(d: Docente): Observable<DocenteApi> {
    const body = { id: d.id, nombre: d.nombre, cedula: d.cedula, maxHoras: d.maxHoras, disponibilidad: d.disponibilidad };
    return this.http.post<DocenteApi>(`${this.base}/docentes`, body);
  }

  actualizarDocente(d: Docente): Observable<DocenteApi> {
    const body = { id: d.id, nombre: d.nombre, cedula: d.cedula, maxHoras: d.maxHoras, disponibilidad: d.disponibilidad };
    return this.http.put<DocenteApi>(`${this.base}/docentes/${d.id}`, body);
  }

  eliminarDocenteBD(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/docentes/${id}`);
  }

  /** Grupos de docentes que probablemente son la misma persona (variantes de nombre). */
  detectarDuplicadosDocentes(): Observable<Docente[][]> {
    return this.http.get<Docente[][]>(`${this.base}/docentes/duplicados`);
  }

  /** Fusiona los duplicados en el canónico: reasigna sus asignaturas y los elimina de la BD. */
  fusionarDocentes(canonicoId: string, duplicadosIds: string[]): Observable<FusionDocentesResultado> {
    return this.http.post<FusionDocentesResultado>(`${this.base}/docentes/fusionar`,
      { canonicoId, duplicadosIds });
  }

  // ── Espacios ───────────────────────────────────────────────────────────────────

  cargarEspacios(): Observable<EspacioApi[]> {
    return this.http.get<EspacioApi[]>(`${this.base}/espacios`);
  }

  guardarEspacio(e: Espacio): Observable<EspacioApi> {
    const body = { id: e.id, nombre: e.nombre, tipo: e.tipo, capacidad: e.capacidad, edificio: e.edificio, piso: e.piso };
    return this.http.post<EspacioApi>(`${this.base}/espacios`, body);
  }

  actualizarEspacio(e: Espacio): Observable<EspacioApi> {
    const body = { id: e.id, nombre: e.nombre, tipo: e.tipo, capacidad: e.capacidad, edificio: e.edificio, piso: e.piso };
    return this.http.put<EspacioApi>(`${this.base}/espacios/${e.id}`, body);
  }

  eliminarEspacioBD(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/espacios/${id}`);
  }

  // ── Grupos ────────────────────────────────────────────────────────────────────

  cargarGrupos(): Observable<GrupoApi[]> {
    return this.http.get<GrupoApi[]>(`${this.base}/grupos`);
  }

  guardarGrupo(g: Grupo): Observable<GrupoApi> {
    return this.http.post<GrupoApi>(`${this.base}/grupos`, {
      id: g.id, asignaturaId: g.asignaturaId, nombre: g.nombre,
      estudiantesInscritos: g.estudiantesInscritos,
      programaId: g.programaId, facultadId: g.facultadId ?? null,
      docenteId: g.docenteId ?? null,
      codigo: g.codigo ?? null, disponibilidadUiJson: g.disponibilidadUiJson ?? null,
      requisitosEspacio: g.requisitosEspacio ?? []
    });
  }

  actualizarGrupo(g: Grupo): Observable<GrupoApi> {
    return this.http.put<GrupoApi>(`${this.base}/grupos/${g.id}`, {
      id: g.id, asignaturaId: g.asignaturaId, nombre: g.nombre,
      estudiantesInscritos: g.estudiantesInscritos,
      programaId: g.programaId, facultadId: g.facultadId ?? null,
      docenteId: g.docenteId ?? null,
      codigo: g.codigo ?? null, disponibilidadUiJson: g.disponibilidadUiJson ?? null,
      requisitosEspacio: g.requisitosEspacio ?? []
    });
  }

  eliminarGrupoBD(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/grupos/${id}`);
  }

  // ── Asignaturas (usa el AsignaturasController existente) ──────────────────────

  cargarAsignaturas(): Observable<AsignaturaApi[]> {
    return this.http.get<AsignaturaApi[]>(`${this.base}/asignaturas`);
  }

  crearAsignatura(a: Asignatura): Observable<AsignaturaApi> {
    const body = {
      id: a.id,
      nombre: a.nombre,
      codigo: a.codigo,
      sesionesTeoriaPresencialSemana: a.sesionesTeoriaPresencialSemana,
      horasTeoriaPresencial: a.horasTeoriaPresencial,
      sesionesTeoriaVirtualSemana: a.sesionesTeoriaVirtualSemana,
      horasTeoriaVirtual: a.horasTeoriaVirtual,
      sesionesLaboratorioSemana: a.sesionesLaboratorioSemana,
      horasLaboratorio: a.horasLaboratorio,
      sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre,
      programaId: a.programaId,
      alternancia: a.alternancia,
      categoria: a.categoria ?? null,
      horaInicioMin: a.horaInicioMin ?? null,
      horaFinMax: a.horaFinMax ?? null
    };
    return this.http.post<AsignaturaApi>(`${this.base}/asignaturas`, body);
  }

  actualizarAsignatura(a: Asignatura): Observable<AsignaturaApi> {
    const body = {
      nombre: a.nombre,
      codigo: a.codigo,
      sesionesTeoriaPresencialSemana: a.sesionesTeoriaPresencialSemana,
      horasTeoriaPresencial: a.horasTeoriaPresencial,
      sesionesTeoriaVirtualSemana: a.sesionesTeoriaVirtualSemana,
      horasTeoriaVirtual: a.horasTeoriaVirtual,
      sesionesLaboratorioSemana: a.sesionesLaboratorioSemana,
      horasLaboratorio: a.horasLaboratorio,
      sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre,
      programaId: a.programaId,
      alternancia: a.alternancia,
      categoria: a.categoria ?? null,
      horaInicioMin: a.horaInicioMin ?? null,
      horaFinMax: a.horaFinMax ?? null
    };
    return this.http.put<AsignaturaApi>(`${this.base}/asignaturas/${a.id}`, body);
  }

  actualizarElegibilidadAlternancia(id: string, elegible: boolean): Observable<void> {
    return this.http.patch<void>(`${this.base}/asignaturas/${id}/elegibilidad-alternancia`, { elegible });
  }

  // ── Criterios de cesión a alternancia (cesión por saturación de espacio) ────────

  cargarCriteriosCesion(): Observable<CriterioCesionAlternancia[]> {
    return this.http.get<CriterioCesionAlternancia[]>(`${this.base}/criterioscesionalternancia`);
  }

  actualizarCriterioCesion(id: string, cambios: { orden?: number; activo?: boolean }): Observable<CriterioCesionAlternancia[]> {
    return this.http.patch<CriterioCesionAlternancia[]>(`${this.base}/criterioscesionalternancia/${id}`, cambios);
  }

  eliminarAsignatura(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/asignaturas/${id}`);
  }

  // ── Horario: sesiones ─────────────────────────────────────────────────────────

  /** Asigna (o desasigna con null) el docente a una sesión ya generada (HU-04, Etapa 4). */
  asignarDocente(sesionId: string, docenteId: string | null): Observable<{ sesionId: string; docenteId: string | null; advertencias: string[] }> {
    return this.http.patch<{ sesionId: string; docenteId: string | null; advertencias: string[] }>(
      `${this.base}/sesiones/${sesionId}/docente`,
      { docenteId }
    );
  }

  crearSesionManual(payload: {
    /** Horario vigente al que se agrega la sesión. */
    horarioId: string;
    asignaturaId: string;
    docenteId: string | null;
    espacioId: string | null;
    // R2 auditoría: el diálogo de creación manual ya obliga a elegir un grupo, pero el dato se
    // descartaba antes de llegar aquí — sin él, HC-SEP se evaluaba sobre TODAS las cohortes
    // (rechazaba una sesión válida por chocar con la de otro grupo) y HC-S05 (aula fija del
    // grupo) no se podía aplicar en absoluto. Obligatorio: toda sesión pertenece a un grupo de la asignatura.
    grupoId: string;
    dia: string;
    horaInicio: string;
    duracionHoras: number;
    tipoFlujo: 'Laboratorio' | 'AulaVirtual';
    esVirtual: boolean;
  }): Observable<Sesion[]> {
    return this.http.post<Sesion[]>(`${this.base}/horario/sesion-manual`, payload);
  }

  /** Paso 1 del import: lee el Excel y devuelve sus filas con las incoherencias. No guarda nada. */
  revisarExcel(file: File): Observable<RevisionImport> {
    const form = new FormData();
    form.append('archivo', file, file.name);
    return this.http.post<RevisionImport>(`${this.base}/import/excel/revisar`, form);
  }

  /** Vuelve a revisar las filas tras editarlas o borrarlas. No guarda nada. */
  revisarFilas(filas: FilaCurriculum[]): Observable<RevisionImport> {
    return this.http.post<RevisionImport>(`${this.base}/import/filas/revisar`, filas);
  }

  /** Paso 2: importa las filas. 422 (con un RevisionImport en el cuerpo) si queda algún error. */
  importarFilas(filas: FilaCurriculum[]): Observable<ImportExcelStatsDto> {
    return this.http.post<ImportExcelStatsDto>(`${this.base}/import/filas`, filas);
  }
}

/** Una fila del Excel tal cual (texto). `fila` = número de fila en el archivo. */
export interface FilaCurriculum {
  fila: number;
  facultad: string; programa: string; asignatura: string; codigo: string; tipoEspacio: string;
  espacio: string; duracion: string; dia: string; hora: string; final: string; docente: string; grupo: string;
}

/** `campo` = propiedad de FilaCurriculum afectada, o '' si es la fila entera. */
export interface IncoherenciaFila { fila: number; campo: string; esError: boolean; mensaje: string; }

export interface RevisionImport { filas: FilaCurriculum[]; incoherencias: IncoherenciaFila[]; avisos: string[]; }

export interface ImportExcelStatsDto {
  facultadesCreadas: number;
  programasCreados: number;
  docentesCreados: number;
  docentesActualizados: number;
  espaciosCreados: number;
  espaciosActualizados: number;
  asignaturasCreadas: number;
  asignaturasActualizadas: number;
  gruposCreados: number;
  /** Bug FE (auditoría UI 2026-09): el DTO decía `asignaturasSinDocente` pero el backend envía
   *  `gruposSinDocente` (ImportDtos.cs) — el campo nunca coincidía y esta fila nunca se mostraba. */
  gruposSinDocente: number;
  advertencias: string[];
}

export interface FusionDocentesResultado {
  canonicoId: string;
  docentesEliminados: number;
  gruposReasignados: number;
}
// ── Respuestas del API ──────────────────────────────────────────────────────────
// Forma real de los DTOs del backend: lo opcional llega como null, no como ausente. Los map* de
// CatalogoService las convierten al modelo del frontend (models.ts).

/** AsignaturaResponse (C#). */
export interface AsignaturaApi {
  id: string; nombre: string; codigo: string;
  sesionesTeoriaPresencialSemana: number; horasTeoriaPresencial: number;
  sesionesTeoriaVirtualSemana: number; horasTeoriaVirtual: number;
  sesionesLaboratorioSemana: number; horasLaboratorio: number; sesionesLaboratorioSemestre: number;
  alternancia: Asignatura['alternancia']; categoria: NonNullable<Asignatura['categoria']>;
  programaId: string; esCandidataAlternancia: boolean;
  horaInicioMin: string | null; horaFinMax: string | null;
}

/** DocenteUiDto (C#). */
export interface DocenteApi { id: string; nombre: string; cedula: string | null; maxHoras: number; disponibilidad: DisponibilidadUi | null; }

/** EspacioDto (C#). */
export interface EspacioApi { id: string; nombre: string; tipo: Espacio['tipo']; capacidad: number; edificio: string | null; piso: number | null; }

/** GrupoDto (C#). */
export interface GrupoApi {
  id: string; asignaturaId: string | null; programaId: string; facultadId: string | null; docenteId: string | null;
  nombre: string; codigo: string | null; estudiantesInscritos: number; disponibilidadUiJson: string | null;
  requisitosEspacio: RequisitoEspacio[];
}

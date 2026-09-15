import { Injectable, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, forkJoin, of, throwError } from 'rxjs';
import { map, catchError, tap } from 'rxjs/operators';
import { PersistenciaService } from './persistencia.service';
import { StateService } from './state.service';
import { HorarioApiService } from './horario-api.service';
import { mensajeErrorHttp } from './http-error.util';
import { Asignatura, Docente, Espacio, Facultad, Grupo, Programa } from './models';

export interface ResumenCatalogo {
  facultades: number;
  programas: number;
  asignaturas: number;
  docentes: number;
  espacios: number;
  grupos: number;
}

export type EntidadCatalogo = 'asignatura' | 'docente' | 'espacio' | 'grupo' | 'facultad' | 'programa';

/**
 * Fuente de verdad única para hidratar el StateService desde la BD.
 *
 * Antes cada componente (tabs de Ingesta, Horario, Configuración de alternancia)
 * cargaba su propio subconjunto de endpoints, dejando el estado a medias
 * (facultades/programas ausentes → nombres en '—') y los `bdIds` privados de cada
 * tab desincronizados (la papelera tomaba la rama "eliminar solo localmente").
 * `cargarTodo()` centraliza la carga completa y la pertenencia a BD.
 */
@Injectable({ providedIn: 'root' })
export class CatalogoService {
  private persistencia = inject(PersistenciaService);
  private state = inject(StateService);
  private horarioApi = inject(HorarioApiService);

  // ── Ids que existen en la BD ─────────────────────────────────────────────────
  // Deciden si guardar/eliminar deben ir contra el backend o son solo locales.
  readonly asignaturasEnBd = signal<ReadonlySet<string>>(new Set());
  readonly docentesEnBd    = signal<ReadonlySet<string>>(new Set());
  readonly espaciosEnBd    = signal<ReadonlySet<string>>(new Set());
  readonly gruposEnBd      = signal<ReadonlySet<string>>(new Set());
  readonly facultadesEnBd  = signal<ReadonlySet<string>>(new Set());
  readonly programasEnBd   = signal<ReadonlySet<string>>(new Set());

  private bdIds(tipo: EntidadCatalogo) {
    switch (tipo) {
      case 'asignatura': return this.asignaturasEnBd;
      case 'docente':    return this.docentesEnBd;
      case 'espacio':    return this.espaciosEnBd;
      case 'grupo':      return this.gruposEnBd;
      case 'facultad':   return this.facultadesEnBd;
      case 'programa':   return this.programasEnBd;
    }
  }

  estaEnBd(tipo: EntidadCatalogo, id: string): boolean {
    return this.bdIds(tipo)().has(id);
  }

  /** Registrar que una entidad quedó persistida (tras POST/PUT exitoso). */
  marcarEnBd(tipo: EntidadCatalogo, id: string) {
    this.bdIds(tipo).update(s => new Set(s).add(id));
  }

  /** Registrar que una entidad dejó de existir en BD (tras DELETE exitoso). */
  quitarDeBd(tipo: EntidadCatalogo, id: string) {
    this.bdIds(tipo).update(s => {
      const n = new Set(s);
      n.delete(id);
      return n;
    });
  }

  /**
   * Único camino de persistencia (B1): escribe el `StateService` de inmediato (optimista) y
   * dispara POST (si la entidad no está en BD) o PUT (si ya lo está) contra el backend. Al
   * resolver con éxito, re-sincroniza el state con la respuesta del servidor (que puede traer
   * un id distinto, p. ej. si el cliente no pudo o no quiso fijar uno) y marca la entidad como
   * persistida. En error, revierte el cambio optimista (G6 auditoría: antes quedaba en el
   * state como si existiera — "se creó… y luego no está" al recargar desde BD) y el caller
   * sigue siendo quien decide cómo mostrar el fallo (snackbar, estado por fila, etc.).
   */
  guardar(tipo: 'asignatura', entidad: Asignatura): Observable<Asignatura>;
  guardar(tipo: 'docente', entidad: Docente): Observable<Docente>;
  guardar(tipo: 'espacio', entidad: Espacio): Observable<Espacio>;
  guardar(tipo: 'grupo', entidad: Grupo): Observable<Grupo>;
  guardar(tipo: 'facultad', entidad: Facultad): Observable<Facultad>;
  guardar(tipo: 'programa', entidad: Programa): Observable<Programa>;
  guardar(tipo: EntidadCatalogo, entidad: any): Observable<any> {
    const anterior = this.buscarEnState(tipo, entidad.id);
    this.actualizarEnState(tipo, entidad);
    const esNueva = !this.estaEnBd(tipo, entidad.id);
    return this.peticionGuardar(tipo, entidad, esNueva).pipe(
      map(raw => this.mapearRespuesta(tipo, raw, entidad)),
      tap(guardada => {
        this.marcarEnBd(tipo, guardada.id);
        this.actualizarEnState(tipo, guardada);
      }),
      catchError(err => {
        if (esNueva) this.eliminarDeState(tipo, entidad.id);
        else if (anterior) this.actualizarEnState(tipo, anterior);
        return throwError(() => err);
      })
    );
  }

  private buscarEnState(tipo: EntidadCatalogo, id: string): any {
    switch (tipo) {
      case 'asignatura': return this.state.asignaturas().find(x => x.id === id);
      case 'docente':    return this.state.docentes().find(x => x.id === id);
      case 'espacio':    return this.state.espacios().find(x => x.id === id);
      case 'grupo':      return this.state.grupos().find(x => x.id === id);
      case 'facultad':   return this.state.facultades().find(x => x.id === id);
      case 'programa':   return this.state.programas().find(x => x.id === id);
    }
  }

  /** Elimina en backend y, si tiene éxito, en el `StateService` y en `*EnBd`. */
  eliminar(tipo: EntidadCatalogo, id: string): Observable<void> {
    const peticion$ = tipo === 'asignatura' ? this.persistencia.eliminarAsignatura(id)
      : tipo === 'docente' ? this.persistencia.eliminarDocenteBD(id)
      : tipo === 'espacio' ? this.persistencia.eliminarEspacioBD(id)
      : tipo === 'grupo' ? this.persistencia.eliminarGrupoBD(id)
      : tipo === 'facultad' ? this.persistencia.eliminarFacultadBD(id)
      : this.persistencia.eliminarProgramaBD(id);
    return peticion$.pipe(tap(() => {
      this.quitarDeBd(tipo, id);
      this.eliminarDeState(tipo, id);
    }));
  }

  private peticionGuardar(tipo: EntidadCatalogo, entidad: any, esNueva: boolean): Observable<any> {
    switch (tipo) {
      case 'asignatura': return esNueva ? this.persistencia.crearAsignatura(entidad) : this.persistencia.actualizarAsignatura(entidad);
      case 'docente':    return esNueva ? this.persistencia.guardarDocente(entidad) : this.persistencia.actualizarDocente(entidad);
      case 'espacio':    return esNueva ? this.persistencia.guardarEspacio(entidad) : this.persistencia.actualizarEspacio(entidad);
      case 'grupo':      return esNueva ? this.persistencia.guardarGrupo(entidad) : this.persistencia.actualizarGrupo(entidad);
      case 'facultad':   return esNueva ? this.persistencia.guardarFacultad(entidad) : this.persistencia.actualizarFacultad(entidad);
      case 'programa':   return esNueva ? this.persistencia.guardarPrograma(entidad) : this.persistencia.actualizarPrograma(entidad);
    }
  }

  private mapearRespuesta(tipo: EntidadCatalogo, raw: any, entidad: any): any {
    switch (tipo) {
      case 'asignatura': return this.mapAsignatura({ ...entidad, ...raw });
      case 'docente':    return this.mapDocente(raw);
      case 'espacio':    return this.mapEspacio(raw);
      case 'grupo':      return this.mapGrupo(raw);
      case 'facultad':
      case 'programa':   return raw;
    }
  }

  private actualizarEnState(tipo: EntidadCatalogo, entidad: any) {
    switch (tipo) {
      case 'asignatura':
        this.state.asignaturas().some(x => x.id === entidad.id) ? this.state.updateAsignatura(entidad) : this.state.addAsignatura(entidad);
        break;
      case 'docente':
        this.state.docentes().some(x => x.id === entidad.id) ? this.state.updateDocente(entidad) : this.state.addDocente(entidad);
        break;
      case 'espacio':
        this.state.espacios().some(x => x.id === entidad.id) ? this.state.updateEspacio(entidad) : this.state.addEspacio(entidad);
        break;
      case 'grupo':
        this.state.grupos().some(x => x.id === entidad.id) ? this.state.updateGrupo(entidad) : this.state.addGrupo(entidad);
        break;
      case 'facultad':
        this.state.facultades().some(x => x.id === entidad.id) ? this.state.updateFacultad(entidad) : this.state.addFacultad(entidad);
        break;
      case 'programa':
        this.state.programas().some(x => x.id === entidad.id) ? this.state.updatePrograma(entidad) : this.state.addPrograma(entidad);
        break;
    }
  }

  private eliminarDeState(tipo: EntidadCatalogo, id: string) {
    switch (tipo) {
      case 'asignatura': this.state.deleteAsignatura(id); break;
      case 'docente':    this.state.deleteDocente(id); break;
      case 'espacio':    this.state.deleteEspacio(id); break;
      case 'grupo':      this.state.deleteGrupo(id); break;
      case 'facultad':   this.state.deleteFacultad(id); break;
      case 'programa':   this.state.deletePrograma(id); break;
    }
  }

  /**
   * Carga los 5 catálogos desde la BD, hidrata el StateService completo y
   * sincroniza la pertenencia a BD. Único punto de entrada para "Cargar desde BD",
   * la carga inicial de Ingesta y la sincronización de Horario/Configuración.
   */
  cargarTodo(): Observable<ResumenCatalogo> {
    return forkJoin({
      facultades:  this.persistencia.cargarFacultades(),
      programas:   this.persistencia.cargarProgramas(),
      asignaturas: this.persistencia.cargarAsignaturas(),
      docentes:    this.persistencia.cargarDocentes(),
      espacios:    this.persistencia.cargarEspacios(),
      // M7: solo un 404 (sin grupos creados aún) se trata como "lista vacía" — cualquier otro
      // error (backend caído, 500) debe propagarse en vez de disfrazarse de "no hay grupos".
      grupos:      this.persistencia.cargarGrupos().pipe(
        catchError((err: HttpErrorResponse) => err.status === 404 ? of([]) : throwError(() => err))
      ),
      // FE13 auditoría: antes solo HorarioComponent rehidrataba el horario persistido (P6), así
      // que aterrizar directo en /revisar tras un F5 mostraba "aún no hay horario" aunque siguiera
      // intacto en BD — el KPI dependía de por dónde había entrado el usuario a la app. Se
      // centraliza aquí, la única fuente de hidratación del StateService. Un fallo real al
      // traer el horario no debe tumbar la carga del catálogo (asignaturas/docentes/espacios) —
      // se guarda en errorHorarioActual (FE6) para que quien lo necesite lo muestre.
      horario: this.horarioApi.obtenerActual('2026-1').pipe(catchError(err => {
        this.state.errorHorarioActual.set(mensajeErrorHttp(err));
        return of(null);
      }))
    }).pipe(
      map(({ facultades, programas, asignaturas, docentes, espacios, grupos, horario }) => {
        this.state.facultades.set(facultades.map((f: any) => ({ id: f.id, nombre: f.nombre })));
        this.state.programas.set(programas.map((p: any) => ({
          id: p.id, nombre: p.nombre, facultadId: p.facultadId
        })));
        this.state.docentes.set(docentes.map(d => this.mapDocente(d)));
        this.state.espacios.set(espacios.map(e => this.mapEspacio(e)));
        this.state.setAsignaturas(asignaturas.map((a: any) => this.mapAsignatura(a)));
        this.state.grupos.set((grupos as any[]).map(g => this.mapGrupo(g)));
        if (horario) {
          this.state.errorHorarioActual.set(null);
          this.state.setSesiones(this.horarioApi.mapearSesiones(horario.sesiones));
          this.state.setExecutionLogs(horario.logs || []);
          this.state.horarioId.set(horario.horarioId);
        }

        this.asignaturasEnBd.set(new Set(asignaturas.map((a: any) => a.id as string)));
        this.docentesEnBd.set(new Set(docentes.map(d => d.id)));
        this.espaciosEnBd.set(new Set(espacios.map(e => e.id)));
        this.gruposEnBd.set(new Set((grupos as any[]).map(g => g.id as string)));
        this.facultadesEnBd.set(new Set(facultades.map((f: any) => f.id as string)));
        this.programasEnBd.set(new Set(programas.map((p: any) => p.id as string)));

        return {
          facultades: facultades.length,
          programas: programas.length,
          asignaturas: asignaturas.length,
          docentes: docentes.length,
          espacios: espacios.length,
          grupos: (grupos as any[]).length
        };
      })
    );
  }

  // ── Mapeos canónicos DTO → modelo del frontend ───────────────────────────────

  private mapDocente(d: any): Docente {
    return {
      id: d.id,
      nombre: d.nombre,
      cedula: d.cedula ?? '',
      maxHoras: d.maxHoras ?? 40,
      disponibilidad: d.disponibilidad ?? {}
    };
  }

  private mapEspacio(e: any): Espacio {
    return {
      id: e.id,
      nombre: e.nombre,
      capacidad: e.capacidad,
      tipo: e.tipo,
      edificio: e.edificio ?? undefined,
      piso: e.piso ?? undefined
    };
  }

  private mapAsignatura(a: any): Asignatura {
    return {
      id: a.id,
      codigo: a.codigo ?? '',
      nombre: a.nombre,
      sesionesTeoriaPresencialSemana: a.sesionesTeoriaPresencialSemana ?? 0,
      horasTeoriaPresencial: a.horasTeoriaPresencial ?? 2,
      sesionesTeoriaVirtualSemana: a.sesionesTeoriaVirtualSemana ?? 0,
      horasTeoriaVirtual: a.horasTeoriaVirtual ?? 2,
      sesionesLaboratorioSemana: a.sesionesLaboratorioSemana ?? 0,
      horasLaboratorio: a.horasLaboratorio ?? 2,
      sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre ?? 0,
      alternancia: a.alternancia ?? 'SinAlternancia',
      categoria: a.categoria ?? undefined,
      programaId: a.programaId,
      grupoNumero: a.grupoNumero ?? undefined,
      esCandidataAlternancia: a.esCandidataAlternancia ?? false,
      // Sin esto la ventana HC-VH se pierde al recargar desde BD: la UI la edita y el backend
      // la guarda, pero el mapeo de vuelta la tiraba (mismo patrón de mapeo silencioso).
      horaInicioMin: a.horaInicioMin ?? undefined,
      horaFinMax: a.horaFinMax ?? undefined
    };
  }

  private mapGrupo(g: any): Grupo {
    return {
      id: g.id,
      asignaturaId: g.asignaturaId ?? '',
      nombre: g.nombre,
      estudiantesInscritos: g.estudiantesInscritos ?? 0,
      programaId: g.programaId ?? '',
      facultadId: g.facultadId ?? undefined,
      docenteId: g.docenteId ?? undefined,
      codigo: g.codigo ?? undefined,
      disponibilidadUiJson: g.disponibilidadUiJson ?? undefined,
      requisitosEspacio: g.requisitosEspacio ?? []
    };
  }
}

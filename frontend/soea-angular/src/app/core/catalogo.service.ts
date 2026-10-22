import { Injectable, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, forkJoin, of, throwError } from 'rxjs';
import { map, catchError, tap } from 'rxjs/operators';
import { AsignaturaApi, DocenteApi, EspacioApi, GrupoApi, PersistenciaService } from './persistencia.service';
import { StateService } from './state.service';
import { HorarioApiService } from './horario-api.service';
import { mensajeErrorHttp } from './http-error.util';
import { ordenar } from './orden';
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

/** Modelo del frontend de cada entidad del catálogo. */
export interface EntidadesCatalogo {
  asignatura: Asignatura; docente: Docente; espacio: Espacio; grupo: Grupo; facultad: Facultad; programa: Programa;
}

/** Tipo y entidad juntos: un switch sobre `tipo` acota `entidad` a su modelo, sin any. */
type ParCatalogo = { [K in EntidadCatalogo]: { tipo: K; entidad: EntidadesCatalogo[K] } }[EntidadCatalogo];

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
  guardar<K extends EntidadCatalogo>(tipo: K, entidad: EntidadesCatalogo[K]): Observable<EntidadesCatalogo[K]> {
    // La firma ya ata `entidad` a `tipo`; ParCatalogo se lo hace visible a los switch de abajo.
    const par = { tipo, entidad } as ParCatalogo;
    const anterior = this.buscarEnState(tipo, entidad.id);
    this.actualizarEnState(par);
    const esNueva = !this.estaEnBd(tipo, entidad.id);
    return this.guardarEnApi(par, esNueva).pipe(
      tap(guardada => {
        this.marcarEnBd(tipo, guardada.entidad.id);
        this.actualizarEnState(guardada);
        // El backend mueve en cascada las sesiones de un grupo que cambia de asignatura (FK
        // compuesta): sin esto el horario en memoria las seguía mostrando con la asignatura vieja.
        if (guardada.tipo === 'grupo') {
          const g = guardada.entidad;
          this.state.sesiones.update(v => v.some(s => s.grupoId === g.id && s.asignaturaId !== g.asignaturaId)
            ? v.map(s => s.grupoId === g.id ? { ...s, asignaturaId: g.asignaturaId } : s) : v);
        }
      }),
      map(guardada => guardada.entidad as EntidadesCatalogo[K]),
      catchError(err => {
        if (esNueva) this.eliminarDeState(tipo, entidad.id);
        else if (anterior) this.actualizarEnState({ tipo, entidad: anterior } as ParCatalogo);
        return throwError(() => err);
      })
    );
  }

  private buscarEnState(tipo: EntidadCatalogo, id: string): EntidadesCatalogo[EntidadCatalogo] | undefined {
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
      // GrupoService.DeleteAsync borra sus sesiones en cascada: el horario en memoria las seguía
      // pintando hasta recargar la página.
      if (tipo === 'grupo') this.state.sesiones.update(v => v.filter(s => s.grupoId !== id));
    }));
  }

  /** POST (nueva) o PUT contra el backend, con la respuesta ya convertida al modelo del frontend. */
  private guardarEnApi(par: ParCatalogo, esNueva: boolean): Observable<ParCatalogo> {
    const p = this.persistencia;
    switch (par.tipo) {
      case 'asignatura': {
        const a = par.entidad;
        return (esNueva ? p.crearAsignatura(a) : p.actualizarAsignatura(a))
          .pipe(map(raw => ({ tipo: par.tipo, entidad: this.mapAsignatura({ ...a, ...raw }) })));
      }
      case 'docente':
        return (esNueva ? p.guardarDocente(par.entidad) : p.actualizarDocente(par.entidad))
          .pipe(map(raw => ({ tipo: par.tipo, entidad: this.mapDocente(raw) })));
      case 'espacio':
        return (esNueva ? p.guardarEspacio(par.entidad) : p.actualizarEspacio(par.entidad))
          .pipe(map(raw => ({ tipo: par.tipo, entidad: this.mapEspacio(raw) })));
      case 'grupo':
        return (esNueva ? p.guardarGrupo(par.entidad) : p.actualizarGrupo(par.entidad))
          .pipe(map(raw => ({ tipo: par.tipo, entidad: this.mapGrupo(raw) })));
      case 'facultad':
        return (esNueva ? p.guardarFacultad(par.entidad) : p.actualizarFacultad(par.entidad))
          .pipe(map(raw => ({ tipo: par.tipo, entidad: raw })));
      case 'programa':
        return (esNueva ? p.guardarPrograma(par.entidad) : p.actualizarPrograma(par.entidad))
          .pipe(map(raw => ({ tipo: par.tipo, entidad: raw })));
    }
  }

  private actualizarEnState(par: ParCatalogo) {
    const s = this.state;
    switch (par.tipo) {
      case 'asignatura':
        s.asignaturas().some(x => x.id === par.entidad.id) ? s.updateAsignatura(par.entidad) : s.addAsignatura(par.entidad);
        break;
      case 'docente':
        s.docentes().some(x => x.id === par.entidad.id) ? s.updateDocente(par.entidad) : s.addDocente(par.entidad);
        break;
      case 'espacio':
        s.espacios().some(x => x.id === par.entidad.id) ? s.updateEspacio(par.entidad) : s.addEspacio(par.entidad);
        break;
      case 'grupo':
        s.grupos().some(x => x.id === par.entidad.id) ? s.updateGrupo(par.entidad) : s.addGrupo(par.entidad);
        break;
      case 'facultad':
        s.facultades().some(x => x.id === par.entidad.id) ? s.updateFacultad(par.entidad) : s.addFacultad(par.entidad);
        break;
      case 'programa':
        s.programas().some(x => x.id === par.entidad.id) ? s.updatePrograma(par.entidad) : s.addPrograma(par.entidad);
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
        catchError((err: HttpErrorResponse) => err.status === 404 ? of<GrupoApi[]>([]) : throwError(() => err))
      ),
      // FE13 auditoría: antes solo HorarioComponent rehidrataba el horario persistido (P6), así
      // que aterrizar directo en /revisar tras un F5 mostraba "aún no hay horario" aunque siguiera
      // intacto en BD — el KPI dependía de por dónde había entrado el usuario a la app. Se
      // centraliza aquí, la única fuente de hidratación del StateService. Un fallo real al
      // traer el horario no debe tumbar la carga del catálogo (asignaturas/docentes/espacios) —
      // se guarda en errorHorarioActual (FE6) para que quien lo necesite lo muestre.
      horario: this.horarioApi.obtenerActual(this.state.semestre()).pipe(catchError(err => {
        this.state.errorHorarioActual.set(mensajeErrorHttp(err));
        return of(null);
      }))
    }).pipe(
      map(({ facultades, programas, asignaturas, docentes, espacios, grupos, horario }) => {
        this.state.facultades.set(ordenar(facultades.map(f => ({ id: f.id, nombre: f.nombre }))));
        this.state.programas.set(ordenar(programas.map(p => ({
          id: p.id, nombre: p.nombre, facultadId: p.facultadId
        }))));
        this.state.docentes.set(ordenar(docentes.map(d => this.mapDocente(d))));
        this.state.espacios.set(ordenar(espacios.map(e => this.mapEspacio(e))));
        this.state.setAsignaturas(asignaturas.map(a => this.mapAsignatura(a)));
        this.state.grupos.set(ordenar(grupos.map(g => this.mapGrupo(g))));
        if (horario) {
          this.state.errorHorarioActual.set(null);
          this.state.setSesiones(this.horarioApi.mapearSesiones(horario.sesiones));
          this.state.setExecutionLogs(horario.logs || []);
          this.state.horarioId.set(horario.horarioId);
        }

        this.asignaturasEnBd.set(new Set(asignaturas.map(a => a.id)));
        this.docentesEnBd.set(new Set(docentes.map(d => d.id)));
        this.espaciosEnBd.set(new Set(espacios.map(e => e.id)));
        this.gruposEnBd.set(new Set(grupos.map(g => g.id)));
        this.facultadesEnBd.set(new Set(facultades.map(f => f.id)));
        this.programasEnBd.set(new Set(programas.map(p => p.id)));

        return {
          facultades: facultades.length,
          programas: programas.length,
          asignaturas: asignaturas.length,
          docentes: docentes.length,
          espacios: espacios.length,
          grupos: grupos.length
        };
      })
    );
  }

  // ── Mapeos canónicos DTO → modelo del frontend ───────────────────────────────

  private mapDocente(d: DocenteApi): Docente {
    return {
      id: d.id,
      nombre: d.nombre,
      cedula: d.cedula ?? '',
      maxHoras: d.maxHoras ?? 40,
      disponibilidad: normalizarClavesDia(d.disponibilidad ?? {})
    };
  }

  private mapEspacio(e: EspacioApi): Espacio {
    return {
      id: e.id,
      nombre: e.nombre,
      capacidad: e.capacidad,
      tipo: e.tipo,
      edificio: e.edificio ?? undefined,
      piso: e.piso ?? undefined
    };
  }

  /** Tras guardar llega mezclada con la entidad local, que aporta grupoNumero (el API no lo devuelve). */
  private mapAsignatura(a: AsignaturaApi & { grupoNumero?: number }): Asignatura {
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

  private mapGrupo(g: GrupoApi): Grupo {
    return {
      id: g.id,
      asignaturaId: g.asignaturaId ?? '',
      nombre: g.nombre,
      estudiantesInscritos: g.estudiantesInscritos ?? 0,
      programaId: g.programaId ?? '',
      facultadId: g.facultadId ?? undefined,
      docenteId: g.docenteId ?? undefined,
      codigo: g.codigo ?? undefined,
      disponibilidadUiJson: normalizarClavesDiaJson(g.disponibilidadUiJson) ?? undefined,
      requisitosEspacio: g.requisitosEspacio ?? []
    };
  }
}

const DIAS_CANONICOS = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];

/** Clave de día del contrato de la UI: minúscula y sin tilde ("Miércoles" → "miercoles"). */
function claveDia(clave: string): string {
  return clave.trim().normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
}

/**
 * L-3 auditoría 2026-09-28: el import de Excel guardó la disponibilidad por grupo con el nombre del
 * enum del backend ("Martes", "Sábado"), pero el editor, el chequeo de "sin días disponibles" y el
 * resto de la UI leen las claves en minúscula y sin tilde. Con claves capitalizadas todos los días
 * aparecían cerrados y guardar el grupo destruía la disponibilidad importada. Se normalizan aquí,
 * donde entra todo dato de la API, para no depender de que cada lector lo recuerde. Las claves que
 * no son un día se dejan tal cual.
 */
export function normalizarClavesDia<T>(disp: Record<string, T>): Record<string, T> {
  const salida: Record<string, T> = {};
  for (const [clave, valor] of Object.entries(disp ?? {})) {
    const c = claveDia(clave);
    salida[DIAS_CANONICOS.includes(c) ? c : clave] = valor;
  }
  return salida;
}

/** Igual que {@link normalizarClavesDia} sobre el JSON crudo de un grupo; si no parsea, se devuelve intacto. */
export function normalizarClavesDiaJson(json: string | null | undefined): string | null | undefined {
  if (!json) return json;
  try {
    const obj = JSON.parse(json);
    return obj && typeof obj === 'object' && !Array.isArray(obj) ? JSON.stringify(normalizarClavesDia(obj)) : json;
  } catch { return json; }
}

import { Injectable, inject, signal } from '@angular/core';
import { Observable, forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';
import { PersistenciaService } from './persistencia.service';
import { StateService } from './state.service';
import { Asignatura, Docente, Espacio } from './models';

export interface ResumenCatalogo {
  facultades: number;
  programas: number;
  asignaturas: number;
  docentes: number;
  espacios: number;
}

export type EntidadCatalogo = 'asignatura' | 'docente' | 'espacio';

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

  // ── Ids que existen en la BD ─────────────────────────────────────────────────
  // Deciden si guardar/eliminar deben ir contra el backend o son solo locales.
  readonly asignaturasEnBd = signal<ReadonlySet<string>>(new Set());
  readonly docentesEnBd    = signal<ReadonlySet<string>>(new Set());
  readonly espaciosEnBd    = signal<ReadonlySet<string>>(new Set());

  private bdIds(tipo: EntidadCatalogo) {
    switch (tipo) {
      case 'asignatura': return this.asignaturasEnBd;
      case 'docente':    return this.docentesEnBd;
      case 'espacio':    return this.espaciosEnBd;
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
      espacios:    this.persistencia.cargarEspacios()
    }).pipe(
      map(({ facultades, programas, asignaturas, docentes, espacios }) => {
        this.state.facultades.set(facultades.map((f: any) => ({ id: f.id, nombre: f.nombre })));
        this.state.programas.set(programas.map((p: any) => ({
          id: p.id, nombre: p.nombre, facultadId: p.facultadId
        })));
        this.state.docentes.set(docentes.map(d => this.mapDocente(d)));
        this.state.espacios.set(espacios.map(e => this.mapEspacio(e)));
        this.state.setAsignaturas(asignaturas.map((a: any) => this.mapAsignatura(a)));

        this.asignaturasEnBd.set(new Set(asignaturas.map((a: any) => a.id as string)));
        this.docentesEnBd.set(new Set(docentes.map(d => d.id)));
        this.espaciosEnBd.set(new Set(espacios.map(e => e.id)));

        return {
          facultades: facultades.length,
          programas: programas.length,
          asignaturas: asignaturas.length,
          docentes: docentes.length,
          espacios: espacios.length
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
      horasPorSesion: a.horasPorSesion ?? 2,
      sesionesPorSemana: a.sesionesPorSemana ?? 1,
      sesionesLaboratorioSemestre: a.sesionesLaboratorioSemestre ?? 0,
      alternancia: a.alternancia ?? 'SinAlternancia',
      programaId: a.programaId,
      docenteId: a.docenteId ?? undefined,
      grupoNumero: a.grupoNumero ?? undefined,
      espacioFijoId: a.espacioFijoId ?? undefined
    };
  }
}

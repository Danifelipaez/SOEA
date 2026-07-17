import { Component, inject, computed, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StateService } from '../../core/state.service';
import { CatalogoService } from '../../core/catalogo.service';
import { RouterModule } from '@angular/router';

/** Paso 4 del journey (HF-4) — KPIs de solo lectura sobre el horario generado. */
@Component({
  selector: 'app-dashboard-admin',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="rev-head">
      <span class="soea-tag">Paso 4</span>
      <h1 class="rev-title">Revisar</h1>
      <span class="text-muted rev-sub">Solo lectura, tras generar/ajustar el horario.</span>
    </div>

    @if (state.sesiones().length === 0) {
      <div class="blueprint elev-md empty">
        <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
        <p>Aún no hay un horario generado. Ve a <a routerLink="/horario">Horario</a> y genera uno para ver sus métricas.</p>
      </div>
    } @else {
      <!-- KPIs -->
      <div class="kpis">
        <div class="blueprint kpi">
          <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
          <span class="klabel">Ocupación de espacios</span>
          <span class="kval accent">{{ ocupacionPct() }}%</span>
          <div class="bar"><i [style.width.%]="ocupacionPct()"></i></div>
        </div>
        <div class="blueprint kpi">
          <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
          <span class="klabel">Presencial / Virtual</span>
          <span class="kval">{{ totalPresenciales() }} / {{ totalVirtuales() }}</span>
          <div class="bar split"><i class="pres" [style.width.%]="presencialPct()"></i><i class="virt"></i></div>
        </div>
        <div class="blueprint kpi">
          <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
          <span class="klabel">Franjas ociosas</span>
          <span class="kval warn">{{ franjasOciosas() }}</span>
          <span class="text-muted knote">huecos entre sesiones por espacio</span>
        </div>
      </div>

      <!-- Carga docente -->
      <div class="blueprint carga">
        <i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i>
        <div class="carga-head"><h3 class="sec">Carga docente</h3><span class="text-muted">rango de color según carga vs. máximo declarado</span></div>
        @if (docentesData().length === 0) {
          <p class="text-muted" style="margin:0">Sin docentes asignados a sesiones.</p>
        }
        @for (d of docentesData(); track d.docente) {
          <div class="crow">
            <span class="cname">{{ d.docente }}</span>
            <div class="bar"><i [style.width.%]="d.porcentaje > 100 ? 100 : d.porcentaje" [style.background]="d.color"></i></div>
            <span class="dpill" [ngClass]="d.pill"><span class="stat" [style.background]="d.color"></span>{{ d.estado }} · {{ d.horasAsignadas }}/{{ d.maxHoras }}</span>
          </div>
        }
      </div>

      <!-- Catálogo cargado -->
      <div class="counts">
        <div class="blueprint cnt"><i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i><span class="klabel">Asignaturas</span><span class="cval">{{ state.asignaturas().length }}</span></div>
        <div class="blueprint cnt"><i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i><span class="klabel">Docentes</span><span class="cval">{{ state.docentes().length }}</span></div>
        <div class="blueprint cnt"><i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i><span class="klabel">Espacios</span><span class="cval">{{ state.espacios().length }}</span></div>
        <div class="blueprint cnt"><i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i><span class="klabel">Grupos</span><span class="cval">{{ state.grupos().length }}</span></div>
        <div class="blueprint cnt"><i class="corner tl"></i><i class="corner tr"></i><i class="corner bl"></i><i class="corner br"></i><span class="klabel">Programas</span><span class="cval">{{ state.programas().length }}</span></div>
      </div>
    }
  `,
  styles: [`
    .rev-head { display: flex; align-items: baseline; gap: 12px; margin-bottom: 18px; flex-wrap: wrap; }
    .soea-tag { font: 600 11px var(--font-heading); letter-spacing: .1em; text-transform: uppercase; background: var(--color-accent-800); color: #fff; padding: 4px 10px; }
    .rev-title { margin: 0; font-size: 26px; } .rev-sub { font-size: 13px; }
    .empty { padding: 40px; text-align: center; }

    .kpis { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; margin-bottom: 18px; }
    .kpi { padding: 14px 16px; display: flex; flex-direction: column; gap: 9px; }
    .klabel { font-size: 11px; letter-spacing: .06em; text-transform: uppercase; color: var(--color-neutral-600); }
    .kval { font: 600 32px var(--font-heading); line-height: 1; }
    .kval.accent { color: var(--color-accent); } .kval.warn { color: var(--warn-bd); }
    .knote { font-size: 11.5px; }
    .bar { height: 12px; border: 1px solid var(--color-neutral-700); position: relative; overflow: hidden; }
    .bar > i { position: absolute; inset: 0 auto 0 0; background: var(--color-accent); }
    .bar.split { display: flex; }
    .bar.split > i { position: static; }
    .bar.split .pres { background: var(--color-accent); border-right: 1px solid var(--color-neutral-700); }
    .bar.split .virt { flex: 1; background: repeating-linear-gradient(-45deg, transparent 0 4px, color-mix(in srgb, var(--color-accent) 25%, transparent) 4px 6px); }

    .carga { padding: 16px 18px; margin-bottom: 18px; display: flex; flex-direction: column; gap: 11px; }
    .carga-head { display: flex; align-items: baseline; gap: 10px; margin-bottom: 4px; }
    .carga-head .text-muted { font-size: 12px; }
    .crow { display: grid; grid-template-columns: 130px 1fr 150px; gap: 12px; align-items: center; font-size: 13px; }
    .cname { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

    .counts { display: grid; grid-template-columns: repeat(5, 1fr); gap: 12px; }
    .cnt { padding: 12px 14px; display: flex; flex-direction: column; gap: 6px; }
    .cval { font: 600 24px var(--font-heading); line-height: 1; }
  `]
})
export class DashboardAdminComponent implements OnInit {
  state = inject(StateService);
  catalogo = inject(CatalogoService);

  ngOnInit() {
    if (this.state.espacios().length === 0) this.catalogo.cargarTodo().subscribe({ error: () => {} });
  }

  totalPresenciales = computed(() => this.state.sesiones().filter(s => !s.virtual).length);
  totalVirtuales = computed(() => this.state.sesiones().filter(s => s.virtual).length);
  presencialPct = computed(() => {
    const t = this.totalPresenciales() + this.totalVirtuales();
    return t ? Math.round((this.totalPresenciales() / t) * 100) : 0;
  });

  private totalSlots = computed(() => this.state.espacios().length * 13 * 6);
  ocupacionPct = computed(() => { const s = this.totalSlots(); return s ? Math.round((this.totalPresenciales() / s) * 100) : 0; });
  franjasOciosas = computed(() => Math.max(0, this.totalSlots() - this.totalPresenciales()));

  docentesData = computed(() => {
    const sesiones = this.state.sesiones();
    return this.state.docentes()
      .map(d => {
        const sesDoc = sesiones.filter(s => s.docenteId === d.id);
        const horas = sesDoc.reduce((acc, s) => {
          const [hI, mI] = s.horaInicio.split(':').map(Number);
          const [hF, mF] = s.horaFin.split(':').map(Number);
          return acc + ((hF * 60 + mF) - (hI * 60 + mI)) / 60;
        }, 0);
        const maxHoras = d.maxHoras || 28;
        const porcentaje = maxHoras > 0 ? Math.round((horas / maxHoras) * 100) : 0;
        const estado = porcentaje >= 100 ? 'Límite' : porcentaje >= 85 ? 'Alerta' : 'Normal';
        const pill = porcentaje >= 100 ? 'err' : porcentaje >= 85 ? 'warn' : 'ok';
        const color = porcentaje >= 100 ? 'var(--err-bd)' : porcentaje >= 85 ? 'var(--warn-bd)' : 'var(--ok-bd)';
        return { docente: d.nombre, horasAsignadas: Math.round(horas * 10) / 10, maxHoras, porcentaje, estado, pill, color, tiene: sesDoc.length > 0 };
      })
      .filter(d => d.tiene);
  });
}

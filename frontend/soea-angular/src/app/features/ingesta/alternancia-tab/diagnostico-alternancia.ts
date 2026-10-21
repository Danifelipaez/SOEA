import { Asignatura, CriterioCesionAlternancia, Espacio, Grupo, RequisitoEspacio, TipoEspacioAlternancia } from '../../../core/models';

/** Sesión presencial que podría alternar, con lo que decide si forma pareja con otra. */
interface Candidata { asignaturaId: string; grupoId: string; horas: number; tipoEspacio: RequisitoEspacio['tipoEspacio']; requisito?: RequisitoEspacio; }

export interface DiagnosticoAlternancia {
  /** Sesiones que cumplen los criterios y el límite de espacio. */
  sesiones: number;
  asignaturas: number;
  /** true si existe al menos una pareja posible. */
  hayPareja: boolean;
  /** Por qué no se podría mandar a alternancia ningún grupo ('' si sí se puede). */
  motivo: string;
}

/**
 * Espejo (cliente) de CederSiguientePareja + EvaluadorParejaAlternancia, solo con las compuertas que
 * se pueden saber sin generar: criterios activos, límite de tipo de espacio, asignatura y grupo
 * distintos, misma duración y mismo requisito de aula. Franja común y aula con aforo las decide el
 * servidor al generar. Sirve para avisar ANTES de generar que una combinación de parámetros deja la
 * alternancia sin ninguna pareja posible.
 */
export function diagnosticarAlternancia(
  asignaturas: Asignatura[], grupos: Grupo[], espacios: Espacio[],
  criterios: CriterioCesionAlternancia[], tipo: TipoEspacioAlternancia
): DiagnosticoAlternancia {
  const activos = new Set(criterios.filter(c => c.activo).map(c => c.criterio));
  const elegible = (a: Asignatura) =>
    (activos.has('Electiva') && a.categoria === 'Electiva') ||
    (activos.has('Optativa') && a.categoria === 'Optativa') ||
    (activos.has('Elegible') && !!a.esCandidataAlternancia);

  const vacio = (motivo: string, sesiones = 0, asigs = 0): DiagnosticoAlternancia => ({ sesiones, asignaturas: asigs, hayPareja: false, motivo });
  if (!activos.has('Electiva') && !activos.has('Optativa') && !activos.has('Elegible'))
    return vacio('No hay ningún criterio activo en «Prioridad para alternar» (el de «2 o más sesiones» solo desempata). Active al menos uno.');

  const asigs = asignaturas.filter(elegible);
  if (!asigs.length)
    return vacio('Ninguna asignatura cumple los criterios activos: marque asignaturas en «Puede alternar» o active el criterio de electivas u optativas si las hay.');

  const candidatas: Candidata[] = [];
  for (const a of asigs) {
    for (const g of grupos.filter(x => x.asignaturaId === a.id)) {
      const tracks: [RequisitoEspacio['tipoSesion'], number, number][] =
        [['TeoriaPresencial', a.sesionesTeoriaPresencialSemana, a.horasTeoriaPresencial], ['Laboratorio', a.sesionesLaboratorioSemana, a.horasLaboratorio]];
      for (const [tipoSesion, n, horas] of tracks) {
        const requisito = g.requisitosEspacio?.find(r => r.tipoSesion === tipoSesion);
        const tipoEspacio = tipoEspacioDe(tipoSesion, requisito, espacios);
        // Laboratorio vs. aula de teoría (salón o auditorio), igual que CederSiguientePareja.
        if (tipo !== 'Todos' && (tipoEspacio === 'Laboratorio') !== (tipo === 'Laboratorio')) continue;
        for (let i = 0; i < n; i++) candidatas.push({ asignaturaId: a.id, grupoId: g.id, horas, tipoEspacio, requisito });
      }
    }
  }
  const nAsigs = new Set(candidatas.map(c => c.asignaturaId)).size;
  if (!candidatas.length)
    return vacio(tipo === 'Todos'
      ? `Las ${asigs.length} asignatura(s) que pueden alternar no tienen grupos con clases presenciales.`
      : `Ninguna de las ${asigs.length} asignatura(s) que pueden alternar tiene clases en ${tipo === 'Laboratorio' ? 'laboratorio' : 'salón o auditorio'}. Cambie el tipo de espacio o marque otras asignaturas.`);

  const hayPareja = candidatas.some((a, i) => candidatas.some((b, j) => j > i && formanPareja(a, b)));
  return hayPareja
    ? { sesiones: candidatas.length, asignaturas: nAsigs, hayPareja, motivo: '' }
    : vacio(`Hay ${candidatas.length} clase(s) que podrían alternar, pero ninguna pareja posible: hacen falta dos clases de asignaturas y grupos distintos, con la misma duración y el mismo requisito de aula.`, candidatas.length, nAsigs);
}

function formanPareja(a: Candidata, b: Candidata): boolean {
  if (a.asignaturaId === b.asignaturaId || a.grupoId === b.grupoId || a.horas !== b.horas) return false;
  if (!a.requisito && !b.requisito) return a.tipoEspacio === b.tipoEspacio;
  return !!a.requisito && !!b.requisito && a.requisito.espacioId === b.requisito.espacioId && a.requisito.tipoEspacio === b.requisito.tipoEspacio;
}

/** Espejo de CalculadorEspaciosSesion.TipoEspacioDe: espacio fijo → tipo exigido → por defecto del tipo de sesión. */
function tipoEspacioDe(tipoSesion: RequisitoEspacio['tipoSesion'], requisito: RequisitoEspacio | undefined, espacios: Espacio[]): RequisitoEspacio['tipoEspacio'] {
  const fijo = requisito?.espacioId && espacios.find(e => e.id === requisito.espacioId);
  if (fijo) return fijo.tipo === 'Salón' ? 'Salon' : fijo.tipo;
  if (requisito?.tipoEspacio) return requisito.tipoEspacio;
  return tipoSesion === 'Laboratorio' ? 'Laboratorio' : 'Salon';
}

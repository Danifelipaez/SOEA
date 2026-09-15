import { seSolapanHorarios, nuncaCoexisteEnSemana, finDeJornadaOk, espaciosPermitidosPara } from './horario.component';
import { Sesion, Grupo, Espacio } from '../../core/models';

const HORAS = ['06:00','07:00','08:00','09:00','10:00','11:00','12:00','13:00','14:00','15:00','16:00','17:00','18:00','19:00','20:00','21:00'];

function sesion(overrides: Partial<Sesion> = {}): Sesion {
  return {
    id: 's1', asignaturaId: 'a1', dia: 'lunes', horaInicio: '08:00', horaFin: '10:00',
    duracionHoras: 2, alternancia: 'SinAlternancia', tipoFlujo: 'AulaVirtual', virtual: false,
    ...overrides,
  };
}

/**
 * FE5/FE16/FE17 auditoría: estas reglas vivían duplicadas —con divergencias reales— entre
 * los diálogos de crear, editar y fijar sesión. Aquí se prueban una vez, en la fuente única.
 */
describe('seSolapanHorarios', () => {
  it('detecta solape cuando los spans se cruzan', () => {
    const s = sesion({ horaInicio: '08:00', duracionHoras: 2 }); // 08:00-10:00
    expect(seSolapanHorarios(s, HORAS.indexOf('09:00'), HORAS.indexOf('11:00'), HORAS)).toBe(true);
  });

  it('no hay solape cuando los spans solo se tocan (fin === inicio)', () => {
    const s = sesion({ horaInicio: '08:00', duracionHoras: 2 }); // 08:00-10:00
    expect(seSolapanHorarios(s, HORAS.indexOf('10:00'), HORAS.indexOf('12:00'), HORAS)).toBe(false);
  });

  it('MAN1: una sesión de 4h a las 07:00 solapa con una nueva a las 09:00 (no comparten bloque de inicio)', () => {
    const s = sesion({ horaInicio: '07:00', duracionHoras: 4 }); // 07:00-11:00
    const inicio = HORAS.indexOf('09:00');
    expect(seSolapanHorarios(s, inicio, inicio + 2, HORAS)).toBe(true);
  });
});

describe('nuncaCoexisteEnSemana', () => {
  it('TipoA y TipoB nunca coexisten (pueden compartir aula/bloque)', () => {
    expect(nuncaCoexisteEnSemana('A', 'B')).toBe(true);
  });
  it('la misma semana sí coexiste', () => {
    expect(nuncaCoexisteEnSemana('A', 'A')).toBe(false);
  });
  it('sin semana declarada en alguna de las dos, se asume que coexisten (SinAlternancia ocupa siempre)', () => {
    expect(nuncaCoexisteEnSemana(undefined, 'A')).toBe(false);
  });
});

describe('finDeJornadaOk', () => {
  it('sábado se acota a las 14:00 (espejo de GrillaInstitucional.cs)', () => {
    const startIdx = HORAS.indexOf('12:00');
    expect(finDeJornadaOk('sabado', startIdx + 2, HORAS)).toBe(true); // 12:00-14:00, dentro
    const startIdx2 = HORAS.indexOf('13:00');
    expect(finDeJornadaOk('sabado', startIdx2 + 2, HORAS)).toBe(false); // 13:00-15:00, fuera
  });
  it('FE16: un día L-V que se pasa del final de la grilla se rechaza (antes daba fin = inicio)', () => {
    const startIdx = HORAS.indexOf('20:00');
    expect(finDeJornadaOk('lunes', startIdx + 3, HORAS)).toBe(false);
  });
  it('un día L-V dentro de rango se acepta', () => {
    const startIdx = HORAS.indexOf('08:00');
    expect(finDeJornadaOk('lunes', startIdx + 2, HORAS)).toBe(true);
  });
});

describe('espaciosPermitidosPara', () => {
  const lab: Espacio = { id: 'e-lab', nombre: 'Lab 1', tipo: 'Laboratorio', capacidad: 20 };
  const salon: Espacio = { id: 'e-salon', nombre: 'Salón 1', tipo: 'Salón', capacidad: 40 };
  const espacios = [lab, salon];

  it('TeoriaVirtual no necesita ningún espacio', () => {
    expect(espaciosPermitidosPara('TeoriaVirtual', undefined, espacios)).toEqual([]);
  });

  it('Laboratorio sin requisito de grupo solo ofrece laboratorios', () => {
    expect(espaciosPermitidosPara('Laboratorio', undefined, espacios)).toEqual([lab]);
  });

  it('TeoriaPresencial sin requisito de grupo excluye laboratorios', () => {
    expect(espaciosPermitidosPara('TeoriaPresencial', undefined, espacios)).toEqual([salon]);
  });

  it('FE17: un requisito de aula fija del grupo restringe a esa única aula, aunque no sea del tipo por defecto', () => {
    const grupo: Grupo = {
      id: 'g1', asignaturaId: 'a1', nombre: 'G1', estudiantesInscritos: 20, programaId: 'p1',
      requisitosEspacio: [{ tipoSesion: 'TeoriaPresencial', espacioId: 'e-lab', tipoEspacio: 'Laboratorio', sesiones: 1 }],
    };
    expect(espaciosPermitidosPara('TeoriaPresencial', grupo, espacios)).toEqual([lab]);
  });
});

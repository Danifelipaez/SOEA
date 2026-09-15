import { ubicarEnGrilla, distribuirEnCarriles, MAX_CARRILES } from './horario.component';

/** Convierte "HH:mm" a minutos, para construir los items de prueba sin repetir el cálculo. */
function min(hhmm: string): number {
  const [h, m] = hhmm.split(':').map(Number);
  return h * 60 + m;
}

/** Rango [inicio, fin) de subcolumnas que ocupa una tarjeta en carril `c` de `k` carriles —
 *  espejo de la fórmula usada en la plantilla (grid-column: c*(6/k)+1 / span 6/k). */
function columnas(carril: number, carriles: number): [number, number] {
  const ancho = 6 / carriles;
  return [carril * ancho, carril * ancho + ancho];
}

/** Ninguna tarjeta devuelta debe compartir subcolumnas con otra que se solape en el tiempo —
 *  la propiedad central de la cuadrícula por carriles: nunca ocultar ni encimar. */
function sinEncimar<T extends { ini: number; fin: number }>(tarjetas: { item: T; carril: number; carriles: number }[]) {
  for (let i = 0; i < tarjetas.length; i++) {
    for (let j = i + 1; j < tarjetas.length; j++) {
      const a = tarjetas[i], b = tarjetas[j];
      const seSolapanEnTiempo = a.item.ini < b.item.fin && b.item.ini < a.item.fin;
      if (!seSolapanEnTiempo) continue;
      const [a0, a1] = columnas(a.carril, a.carriles);
      const [b0, b1] = columnas(b.carril, b.carriles);
      expect(a0 < b1 && b0 < a1).toBe(false);
    }
  }
}

describe('distribuirEnCarriles', () => {
  it('6 sesiones simultáneas: 2 tarjetas + un grupo "+4" — nada se pierde', () => {
    const items = Array.from({ length: 6 }, (_, i) => ({ id: i, ini: min('08:00'), fin: min('10:00') }));
    const { tarjetas, mas } = distribuirEnCarriles(items);
    expect(tarjetas.length).toBe(2);
    expect(mas).toHaveLength(1);
    expect(mas[0].items.length).toBe(4);
    expect(tarjetas.length + mas.reduce((n, m) => n + m.items.length, 0)).toBe(6);
    sinEncimar(tarjetas);
  });

  it('escalonadas (09-12 / 10-12 / 11-13): 3 carriles, sin desborde', () => {
    const items = [
      { id: 'a', ini: min('09:00'), fin: min('12:00') },
      { id: 'b', ini: min('10:00'), fin: min('12:00') },
      { id: 'c', ini: min('11:00'), fin: min('13:00') },
    ];
    const { tarjetas, mas } = distribuirEnCarriles(items);
    expect(tarjetas).toHaveLength(3);
    expect(mas).toHaveLength(0);
    expect(new Set(tarjetas.map(t => t.carril))).toEqual(new Set([0, 1, 2]));
    tarjetas.forEach(t => expect(t.carriles).toBe(3));
    sinEncimar(tarjetas);
  });

  it('contiguas (08-10 / 10-12): no se solapan, comparten el carril 0 a ancho completo', () => {
    const items = [
      { id: 'a', ini: min('08:00'), fin: min('10:00') },
      { id: 'b', ini: min('10:00'), fin: min('12:00') },
    ];
    const { tarjetas, mas } = distribuirEnCarriles(items);
    expect(tarjetas).toHaveLength(2);
    expect(mas).toHaveLength(0);
    tarjetas.forEach(t => { expect(t.carril).toBe(0); expect(t.carriles).toBe(1); });
  });

  it('una sesión de 1h dentro de otra de 2h: 2 carriles', () => {
    const items = [
      { id: 'larga', ini: min('08:00'), fin: min('10:00') },
      { id: 'corta', ini: min('09:00'), fin: min('10:00') },
    ];
    const { tarjetas, mas } = distribuirEnCarriles(items);
    expect(tarjetas).toHaveLength(2);
    expect(mas).toHaveLength(0);
    tarjetas.forEach(t => expect(t.carriles).toBe(2));
    sinEncimar(tarjetas);
  });

  it('respeta un `max` de carriles distinto al default', () => {
    const items = Array.from({ length: 5 }, (_, i) => ({ id: i, ini: 0, fin: 60 }));
    const { tarjetas, mas } = distribuirEnCarriles(items, 2);
    expect(tarjetas.filter(t => t.carril < 1)).toHaveLength(1);
    expect(mas[0]?.items.length).toBe(4);
    expect(MAX_CARRILES).toBe(3); // default sin tocar
  });
});

describe('ubicarEnGrilla', () => {
  it('06:30–07:30 no empieza en hora en punto: fuera de la cuadrícula', () => {
    expect(ubicarEnGrilla('lunes', '06:30', '07:30')).toEqual({ motivo: expect.any(String) });
  });

  it('domingo no existe en la cuadrícula', () => {
    expect(ubicarEnGrilla('domingo', '08:00', '10:00')).toEqual({ motivo: expect.any(String) });
  });

  it('05:00–07:00 empieza antes de la apertura', () => {
    expect(ubicarEnGrilla('lunes', '05:00', '07:00')).toEqual({ motivo: expect.any(String) });
  });

  it('lunes 20:00–22:00 cabe justo hasta el cierre', () => {
    const pos = ubicarEnGrilla('lunes', '20:00', '22:00');
    expect(pos).toEqual({ ini: min('20:00'), fin: min('22:00'), fila0: 15, fila1: 17 });
  });

  it('lunes 21:00–23:00 se pasa del cierre (22:00)', () => {
    expect(ubicarEnGrilla('lunes', '21:00', '23:00')).toEqual({ motivo: expect.any(String) });
  });

  it('sábado 12:00–14:00 cabe (cierre 14:00, espejo de GrillaInstitucional.cs)', () => {
    const pos = ubicarEnGrilla('sabado', '12:00', '14:00');
    expect(pos).toEqual({ ini: min('12:00'), fin: min('14:00'), fila0: 7, fila1: 9 });
  });

  it('sábado 13:00–15:00 se pasa del cierre de sábado', () => {
    expect(ubicarEnGrilla('sabado', '13:00', '15:00')).toEqual({ motivo: expect.any(String) });
  });

  it('horario invertido (fin antes que inicio) es inválido', () => {
    expect(ubicarEnGrilla('lunes', '10:00', '09:00')).toEqual({ motivo: expect.any(String) });
  });
});

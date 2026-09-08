import { nuevoId } from './id.util';

describe('nuevoId', () => {
  it('genera un UUID v4 válido usando crypto.randomUUID cuando está disponible', () => {
    const id = nuevoId();
    expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
  });

  it('no revienta y sigue generando un UUID v4 válido si crypto.randomUUID lanza (contexto inseguro)', () => {
    const original = crypto.randomUUID;
    crypto.randomUUID = () => { throw new TypeError('crypto.randomUUID is not a function'); };
    try {
      const id = nuevoId();
      expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
    } finally {
      crypto.randomUUID = original;
    }
  });

  it('genera ids distintos en llamadas sucesivas', () => {
    expect(nuevoId()).not.toBe(nuevoId());
  });
});

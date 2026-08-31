import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { SearchableSelectComponent, SearchableOption } from './searchable-select.component';

/**
 * G5 (bug reportado "no deja guardar"): onBlur() comparaba el texto tecleado (una etiqueta)
 * contra lastValid (un id) — nunca podían coincidir, así que cualquier texto tecleado se
 * revertía a vacío, incluso si coincidía exactamente con una opción real. El usuario escribía
 * la facultad, salía del campo, y el control quedaba vacío sin ningún aviso.
 */
describe('SearchableSelectComponent — onBlur', () => {
  const opciones: SearchableOption[] = [
    { value: 'f1', label: 'Ingeniería' },
    { value: 'f2', label: 'Ciencias' },
  ];

  function crear() {
    TestBed.configureTestingModule({
      imports: [SearchableSelectComponent],
      providers: [provideNoopAnimations()],
    });
    const fixture = TestBed.createComponent(SearchableSelectComponent);
    fixture.componentRef.setInput('options', opciones);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('texto tecleado que coincide con una etiqueta se confirma como esa opción, no se revierte', () => {
    const c = crear();
    const onChange = vi.fn();
    c.registerOnChange(onChange);

    c.ctrl.setValue('Ingeniería');
    c.onBlur();

    expect(onChange).toHaveBeenCalledWith('f1');
    expect(c.ctrl.value).toBe('f1');
  });

  it('texto tecleado que coincide ignorando mayúsculas/espacios también se confirma', () => {
    const c = crear();
    const onChange = vi.fn();
    c.registerOnChange(onChange);

    c.ctrl.setValue('  ciencias  ');
    c.onBlur();

    expect(onChange).toHaveBeenCalledWith('f2');
  });

  it('texto tecleado sin coincidencia real revierte al último valor válido', () => {
    const c = crear();
    c.writeValue('f1'); // último válido = f1 (Ingeniería)
    const onChange = vi.fn();
    c.registerOnChange(onChange);

    c.ctrl.setValue('texto que no existe');
    c.onBlur();

    expect(onChange).not.toHaveBeenCalled();
    expect(c.ctrl.value).toBe('f1');
  });

  it('seleccionar una opción por clic sigue funcionando (regresión)', () => {
    const c = crear();
    const onChange = vi.fn();
    c.registerOnChange(onChange);

    c.onSelected({ option: { value: 'f2' } } as any);

    expect(onChange).toHaveBeenCalledWith('f2');
  });

  it('reset en cascada (writeValue) refresca filtered() con las opciones nuevas, no deja "Sin resultados" con el término viejo', () => {
    const c = crear();
    c.onSelected({ option: { value: 'f2' } } as any); // selección previa, p. ej. bajo otra facultad
    expect(c.filtered().length).toBe(2);

    c.writeValue(''); // p. ej. Facultad → Programa: reset en cascada del campo dependiente
    expect(c.filtered().length).toBe(2); // debe mostrar todas las opciones nuevas, no quedar vacío
  });
});

import { TestBed } from '@angular/core/testing';
import { DisponibilidadEditorComponent } from './disponibilidad-editor.component';

/**
 * FE2 auditoría: writeValue normalizaba `disp` en memoria pero nunca llamaba a onChange. Un
 * docente NUEVO, guardado sin tocar ningún día del editor (la tabla ya muestra "Todo el día"
 * para los seis), persistía disponibilidad = {} (o null) porque el FormControl del padre nunca
 * se actualizaba con el valor normalizado — la fila quedaba "sin disponibilidad declarada"
 * aunque la UI mostrara los seis días disponibles.
 */
describe('DisponibilidadEditorComponent — FE2 (emitir el valor inicial sin interacción)', () => {
  it('writeValue(null) emite los seis días normalizados sin esperar a que el usuario toque nada', () => {
    TestBed.configureTestingModule({ imports: [DisponibilidadEditorComponent] });
    const fixture = TestBed.createComponent(DisponibilidadEditorComponent);
    const component = fixture.componentInstance;

    let emitido: Record<string, any> | undefined;
    component.registerOnChange((v: Record<string, any>) => { emitido = v; });
    component.registerOnTouched(() => {});

    component.writeValue(null);

    expect(emitido).toBeDefined();
    const dias = ['lunes', 'martes', 'miercoles', 'jueves', 'viernes', 'sabado'];
    for (const dia of dias) {
      expect(emitido![dia]).toBeDefined();
      expect(emitido![dia].noDisponible).toBe(false);
    }
  });

  it('setDisp sigue emitiendo en cada interacción (sin regresión)', () => {
    TestBed.configureTestingModule({ imports: [DisponibilidadEditorComponent] });
    const fixture = TestBed.createComponent(DisponibilidadEditorComponent);
    const component = fixture.componentInstance;

    let llamadas = 0;
    component.registerOnChange(() => { llamadas++; });
    component.registerOnTouched(() => {});
    component.writeValue(null); // 1ª llamada (FE2)

    component.setDisp('lunes', 'noDisponible', true);

    expect(llamadas).toBe(2);
  });
});

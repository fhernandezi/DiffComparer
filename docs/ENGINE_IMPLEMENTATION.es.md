# Motor de comparación implementado

Fecha: 21 de octubre de 2026.

## Arquitectura y exactitud

`ComparisonSession` mantiene dos documentos AvaloniaEdit con identidades y revisiones independientes. Sus capturas inmutables alimentan el cálculo en segundo plano. La vista filtrada contiene proyecciones de lectura; sus filas vacías nunca entran en los documentos ni en los archivos guardados.

`DiffEngine.Analyze` produce un `ComparisonResult`: documentos originales, opciones, ediciones exactas, bloques con rangos de origen y filas de presentación. `RawEdits` permite reconstruir el documento derecho aun cuando la política visual ignore espacios o el estilo de terminadores. El salto final es significativo. Los rangos de copia son independientes del emparejamiento visual.

`SequenceDiffer` combina prefijos/sufijos comunes, LCS para regiones pequeñas y Myers bidireccional con fronteras de memoria lineal. El modo Legible añade anclas únicas con subsecuencia creciente y anclas de baja frecuencia. El modo Mínimo prescinde de esas anclas heurísticas. No se corta el guion final por una distancia de edición prefijada.

`LinePairer` utiliza programación dinámica ponderada para elegir conjuntamente parejas y huecos, con un umbral de similitud. En bloques grandes utiliza una banda. Esa aproximación afecta la presentación, no los bloques exactos. `TextDiffer` refina el bloque completo por grafemas Unicode y proyecta los rangos a las líneas, preservando emojis, marcas combinadas y coincidencias al dividir/unir líneas.

## Copias e historial

`CopyPlanner` recibe identidad del resultado, bloque y dirección. Valida identidad/revisión de ambos documentos y genera un reemplazo exacto; `ComparisonSession` verifica también el contenido destino. Los filtros deben incluir el bloque completo. Cada copia usa una transacción del historial nativo; deshacer/rehacer sigue el orden de copias en ambos lados.

El recálculo se valida antes de publicarse. Si se cancela, el documento editado permanece y el resultado anterior queda obsoleto, con copia deshabilitada hasta recalcular. Las revisiones solo se transfieren cuando el contenido y contexto inmediato identifican un bloque único sin cambios.

La política de copia conserva por defecto el primer estilo de terminador encontrado en el destino. Puede desactivarse; comparar EOL exige copiar los originales. No se intenta inferir estilos locales de documentos mixtos. Guardar como conserva la codificación/BOM cargados, valida caracteres representables y guarda el documento real. La escritura del proveedor de almacenamiento no es una transacción atómica frente a fallos de disco.

## Referencias y procedencia

Implementación propia basada en patrones publicados; no se incorporó código fuente de estos proyectos al motor:

- [VS Code: algoritmo de diferencias](https://github.com/microsoft/vscode/tree/main/src/vs/editor/common/diff): separación de alineación de líneas, refinamiento interno y presentación.
- [DiffPlex](https://github.com/mmanela/diffplex): referencia para Myers y validación diferencial. DiffPlex 1.9.0, licencia Apache-2.0, se utiliza exclusivamente como dependencia de pruebas; no se agrega a la aplicación.
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit): documentos, capturas inmutables y agrupación de deshacer; se utiliza la biblioteca ya presente en el proyecto.
- [Beyond Compare: alineación](https://www.scootersoftware.com/v5help/sessiontextalignment.html): referencia funcional para estrategias y legibilidad. No se afirma reproducir su motor propietario ni superarlo sin evaluación comparativa.

## Validación y límites

Las pruebas contrastan el coste mínimo con LCS exhaustivo y DiffPlex sobre 600 secuencias generadas; comprueban reconstrucción de 500 pares con Unicode, espacios y terminadores mezclados, grafemas, líneas largas, islas repetidas y uniones/divisiones de líneas. Otras 300 comparaciones generadas verifican que copiar sucesivamente bloques converge al documento de origen en ambas direcciones, conservando los separadores. También ejercitan invalidación de solicitudes antiguas, filtros parciales, historial, guardado, clic real en flechas y atajos en ventanas headless.

`PerformanceSmokeTests` mide el motor canónico con 20.000 líneas y 50 cambios separados. Los tiempos se exportan mediante `DIFFCOMPARER_TEST_ARTIFACTS`; dependen del equipo y del corpus. No constituyen una garantía para documentos totalmente distintos ni una comparación contra Beyond Compare.

El recálculo todavía es completo. Myers puede ser costoso con distancia de edición elevada. La banda de emparejamiento puede omitir parejas visualmente preferibles fuera de ella. No hay análisis semántico ni merge de tres versiones. El diagnóstico anterior y sus archivos `engine-probes` se mantienen como evidencia histórica, no como medición de este motor.

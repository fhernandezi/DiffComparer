# Merge por secciones · 0.2.0

**Herramientas → Merge…** abre un espacio independiente del comparador: original a la izquierda, entrante a la derecha y resultado combinado debajo.

La barra del merge agrupa **Archivo** (abrir fuentes y guardar), **Edición** (historial, incluir ambos lados y edición manual), **Vista** (texto y zoom) y **Ayuda**. La franja rápida conserva Anterior, Siguiente y Guardar resultado. Las cabeceras de los documentos muestran solo sus nombres; los checks permanecen junto a cada sección.

## Uso

1. Abre o pega los dos textos superiores. Ambos siguen editables. Las diferencias se recalculan automáticamente después de una pausa de 250 ms; no hay que preparar la comparación ni cargar una base.
2. El resaltado identifica líneas agregadas, eliminadas y modificadas, incluidos fragmentos y cambios de terminadores. Cada sección distinta tiene su propio **check y corchete en el margen de ambos editores**. Las líneas iguales quedan fuera de los bloques.
3. El resultado parte del **original completo**. Los checks son independientes: marca el entrante manteniendo el original marcado para incluir ambos bloques, **primero original y después entrante**. Para conservar solo uno, desmarca el otro. Ninguna elección modifica los textos superiores. Una sección vacía representa una inserción o eliminación y también tiene un check en su punto de inserción.
4. Desmarcar un lado quita únicamente su bloque; desmarcar ambos omite la sección completa. **Ambas en esta sección** equivale a marcar los dos checks. Se conservan los saltos existentes; si los bloques no tienen un salto entre ellos, se agrega uno para evitar pegar el final de un método al inicio del siguiente. El resto del documento se conserva.
5. **Anterior/Siguiente** desplaza los tres editores hasta la sección correspondiente. Los checks funcionan directamente sin tener que navegar primero a la sección.
6. **Deshacer/Rehacer selección** revierte las decisiones. Una edición de las fuentes cancela el cálculo anterior y deshabilita sus controles de inmediato. Solo se conservan decisiones de bloques únicos cuyo texto y contexto inmediato no cambiaron; el historial de decisiones comienza de nuevo al recalcular.
7. Puedes **Guardar resultado como…** directamente. **Editar resultado manualmente** permite ajustes libres y bloquea temporalmente las fuentes y los checks para proteger tus ediciones. Ctrl+Z/Ctrl+Y actúan sobre el editor de resultado. **Usar resultado como original** permite continuar seleccionando secciones con tus ajustes manuales como nuevo original.

Los separadores permiten ajustar el ancho de las fuentes y la altura del resultado. **Vista → Texto** comparte fuente, tamaño, interlineado, ajuste de líneas y zoom con el comparador. El guardado usa UTF-8 sin BOM y conserva literalmente los terminadores del contenido elegido; el resultado puede contener EOL mixtos.

## Arquitectura

| Pieza | Responsabilidad |
| --- | --- |
| `DiffComparer.Core` | Comparación exacta de dos documentos, bloques y fragmentos. No se modifica su sesión de comparación. |
| `DiffComparer.Merge/SectionMergeSession` | Decisiones por sección, ensamblado del resultado, historial y conservación prudente de decisiones al editar. No depende de Avalonia. |
| `DiffComparer.UI/Merge/MergeWorkspaceViewModel` | Documentos independientes, recálculo con pausa, cancelación y rechazo de acciones obsoletas. |
| `DiffComparer.UI/Merge/MergeSectionGutter` | Checks y corchetes por sección, ubicados usando las coordenadas reales del editor; siguen el desplazamiento, zoom e interlineado. |
| `DiffComparer.UI/Merge/MergeWindow` | Dos fuentes superiores editables y resultado inferior; resaltado por líneas y fragmentos. |
| `DiffComparer.Merge/ThreeWayMerge` y `MergeSession` | Motor de tres entradas con base común, independiente del flujo interactivo original/entrante. Sigue disponible en el módulo; esta ventana no requiere una base. |

Los cálculos trabajan en segundo plano con capturas del texto. Cada cambio invalida la revisión anterior antes de iniciar el nuevo cálculo. Los checks llevan la identidad de la sesión y de la sección, por lo que un control de una comparación anterior no puede aplicar cambios a otra revisión.

## Verificación

`SectionMergeSessionTests` verifica inserciones, eliminaciones, CRLF/LF/CR, Unicode, ensamblado exacto, decisiones independientes e historial. Incluye reconstrucciones aleatorias donde elegir todas las secciones entrantes debe producir exactamente el entrante.

`MergeWorkspaceTests` comprueba cambios automáticos, cancelación, rechazo de checks obsoletos, protección de edición manual y clics reales en distintos checks. Verifica que los controles estén junto a sus líneas y que cambiar un bloque no reemplace las fuentes. Las capturas se generan al definir `DIFFCOMPARER_TEST_ARTIFACTS`.

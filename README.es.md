# DiffComparer

Comparador de texto y código en .NET 10 y Avalonia. Permite analizar diferencias, copiar bloques entre paneles y guardar el resultado con una alineación legible.

## Interfaz y apariencia

El selector superior permite elegir Claro, Oscuro o Sistema. Los dos textos permanecen editables; las ayudas explican cada acción al pasar el puntero. Las opciones menos frecuentes están en **Opciones de comparación y vista**; **Listo** o **Esc** cierran ese panel.

Cada lado permite abrir, guardar, limpiar y copiar su texto. **Copiar resultado** copia todo el panel derecho al portapapeles. **Intercambiar** cambia los textos de lado; **Restablecer** vacía ambos y devuelve las opciones iniciales. **Recuperar textos** restaura los textos y opciones anteriores a la última de estas acciones. No modifica los archivos guardados.

El diseño adapta los principios de Material Design 3 y las heurísticas de Nielsen a Avalonia. Decisiones, referencias, pruebas y límites: [diseño de interfaz](docs/UI_DESIGN.es.md).

## Comparar y analizar

1. Abre los archivos izquierdo y derecho o pega texto en los editores. Cada cabecera muestra el nombre, la codificación y los finales de línea; la ruta completa aparece al pasar el cursor.
2. La lectura automática reconoce BOM de UTF-8, UTF-16 y UTF-32; sin BOM usa UTF-8 estricto. Si el archivo utiliza otra codificación, selecciona UTF-16 o Windows-1252 antes de abrirlo. Los errores de decodificación se muestran sin sustituir silenciosamente caracteres.
3. Por defecto se compara el contenido exacto, incluidos los espacios. **Ignorar espacios** activa la normalización de espacios y tabulaciones, incluso dentro de cadenas. **Comparar EOL** incluye diferencias CRLF/LF/CR; la presencia del salto final siempre se compara. **Legible** utiliza anclas y Myers; **Mínimo** minimiza inserciones y eliminaciones de líneas.
4. Ambos paneles permanecen editables. Las diferencias se actualizan automáticamente tras una pausa de 250 ms al escribir, pegar o cambiar las opciones. Las líneas agregadas tienen fondo verde; las eliminadas, rojo; las modificadas, amarillo. Los fragmentos cambiados usan un tono más intenso del mismo color. La izquierda se considera el original. No se insertan filas vacías ni se mueve el cursor al actualizar.
5. La barra inferior muestra líneas agregadas, eliminadas y modificadas, además de los bloques movidos detectados. Un bloque agrupa cambios consecutivos; no equivale a una línea modificada.

## Vista y revisión

- **Resaltar solo diferencias:** atenúa el texto fuera de los cambios y su contexto, y limita la búsqueda y la navegación a esa selección. El control Contexto permite ampliar el texto destacado alrededor de los cambios. Los documentos completos siguen visibles y editables.
- **Agregadas / Eliminadas / Modificadas:** filtran los tipos visibles sin alterar los totales globales. El contexto no vuelve a mostrar tipos excluidos.
- **Solo movidas:** muestra movimientos de texto idéntico y sin ambigüedad, identificados como agregados y eliminados. Los movimientos conservan sus filas de eliminación e inserción y no se cuentan como una categoría adicional de líneas.
- **Buscar:** busca sin distinguir mayúsculas en ambos paneles visibles. **Resaltar coincidencias** limita el resaltado y la navegación al texto encontrado y su contexto. Los resultados se limitan a 50.000, con aviso cuando se alcanza el límite.
- **Marcar revisado:** registra la revisión del bloque actual y muestra ✓ junto a sus números de línea; **Sin revisar** permite trabajar con los pendientes. Al recalcular se conserva únicamente para bloques únicos cuyo contenido y contexto inmediato no cambiaron. Se incluye en el informe HTML.
- **Espacios visibles:** muestra espacios, tabulaciones y finales de línea. El selector de lenguaje aplica resaltado de sintaxis; **Automático** usa la extensión del archivo.

| Atajo | Acción |
| --- | --- |
| F7 / Shift+F7 | Bloque siguiente / anterior, entre los visibles |
| Ctrl+F | Enfocar la búsqueda |
| F3 / Shift+F3 | Coincidencia siguiente / anterior, en ambos paneles |
| Ctrl+Z / Ctrl+Y | Deshacer / rehacer en el editor enfocado, incluidas las copias |

Los botones **Deshacer/Rehacer** permiten revertir copias y, cuando no hay copias pendientes en el historial, las ediciones del último panel enfocado. Los atajos Ctrl+Z/Ctrl+Y actúan directamente sobre el editor enfocado.

La navegación es circular y usa las líneas originales de cada documento. Los filtros afectan el resaltado y la navegación; no ocultan ni sustituyen el texto editable.

## Copiar y guardar

Las flechas centrales copian el bloque completo en la dirección indicada; las de la barra actúan sobre el bloque seleccionado. Cada copia es una operación reversible y recalcula el diff en segundo plano. Se validan la identidad y revisión de ambos documentos antes de modificar texto. Una flecha antigua o un bloque parcialmente oculto por filtros no puede aplicarse.

Por defecto se conserva el estilo de saltos de línea del destino. Desactiva **Conservar EOL destino** para copiar los terminadores de origen; **Comparar EOL** siempre los copia. El símbolo ¶ junto al número de línea señala cambios de terminador o del salto final.

**Guardar izq./der. como…** guarda el documento real, sin filas de alineación ni filtros. Conserva codificación, BOM y saltos de línea; el texto pegado se guarda como UTF-8 sin BOM. La codificación se valida antes de abrir el destino. Cancelar o fallar no marca el documento como guardado.

## Exportar

- **Informe HTML:** comparación completa, números originales, reglas aplicadas, movimientos y revisión. El contenido se escapa para no ejecutar texto de los archivos como HTML.
- **Parche:** unified diff del contenido completo, independiente de filtros y de la opción de ignorar espacios. Conserva si existe un salto final. Utiliza un único bloque con todo el contexto y normaliza el estilo de finales de línea; no es una comparación binaria.

Las exportaciones solicitan un archivo de destino y nunca escriben sobre los archivos comparados por iniciativa propia.

## Rendimiento

- Comparación, carga/decodificación, proyección de filtros, búsqueda y preparación de informes en segundo plano.
- Publicación de una vista completa por actualización, sin notificaciones por cada línea.
- Búsqueda con espera breve y cancelación de solicitudes anteriores para evitar resultados obsoletos.
- Índices de dibujo y búsqueda por fila, pinceles reutilizados y renderizado de las líneas visibles de AvaloniaEdit.
- Anclas monótonas en O(n log n) y Myers bidireccional con memoria lineal para el guion de cambios.
- Emparejamiento ponderado y monótono de líneas; usa una banda en bloques grandes para limitar el coste de presentación sin alterar los rangos exactos de copia.
- Refinamiento exacto por grafemas Unicode, también en líneas largas y divisiones/uniones de líneas; sin recorte arbitrario del tramo central.
- Documentos independientes de la vista, capturas inmutables y publicación validada por versión. El recálculo es completo y cancelable; todavía no es incremental.

Arquitectura, referencias y límites: [motor implementado](docs/ENGINE_IMPLEMENTATION.es.md).

## Compilar y probar

Requiere el SDK de .NET 10.

```powershell
dotnet run --project DiffComparer.UI
dotnet test DiffComparer.slnx -c Release
```

Las pruebas incluyen motor, filtros, revisión, codificaciones, exportaciones y ventanas con Avalonia Headless. No necesitan abrir ventanas del escritorio. La prueba `PerformanceSmokeTests` mide una comparación de 20.000 líneas; sus tiempos dependen del equipo.

Opcionalmente, define `DIFFCOMPARER_TEST_ARTIFACTS` con una carpeta de salida para guardar una captura, mediciones y ejemplos de parches al ejecutar las pruebas.

## Alcance y límites

El análisis es textual: no interpreta semánticamente código, JSON ni XML. La detección conservadora de movimientos no intenta adivinar bloques repetidos o movidos con ediciones. Los textos modificados en el editor no se guardan automáticamente y las revisiones no persisten entre sesiones.

Quedan para una etapa posterior: comparación de carpetas, merge de tres versiones con ancestro común, recálculo incremental, sesiones persistentes y arrastrar archivos. Las entradas con muy pocas coincidencias pueden seguir siendo costosas: Myers no garantiza latencia constante.

# Interfaz de comparación

El diseño adapta los principios de Material Design 3 a los controles nativos de Avalonia. Conserva Fluent como base técnica, sin instalar un paquete de controles Material ni cambiar el motor de comparación.

## Sistema visual

- Los roles `Surface`, `Container`, `ContainerHigh`, `Primary`, `PrimaryContainer` y sus colores de texto tienen valores independientes para claro y oscuro. La selección Sistema sigue la apariencia del equipo. Referencia: [roles de color de Material Design 3](https://m3.material.io/styles/color/the-color-system).
- Tarjetas de 16 px, botones redondeados, espaciado regular y jerarquía de texto separan acciones, contenido y estado. El texto comparado ocupa el área flexible principal.
- Las diferencias tienen fondos suaves verdes, rojos y amarillos. Los fragmentos cambian a un tono más intenso. Los indicadores `+`, `−` y `~` permiten distinguir los tipos sin depender solo del color.
- El foco de teclado tiene un contorno visible. Los estados hover y pressed cambian el fondo con una transición de 120 ms; los controles deshabilitados conservan etiquetas legibles.
- A menos de 1100 px de ancho o 740 px de alto se reduce el ruido del encabezado y se abrevian las acciones que ya tienen contexto en el panel. El mínimo admitido es 850 × 600. Se puede arrastrar la separación para repartir el ancho.
- Las opciones se abren sobre el área de edición, con desplazamiento propio, sin reducir el tamaño de los documentos. Listo o Esc las cierran.

## Aplicación de las diez heurísticas

La siguiente tabla documenta decisiones de diseño, no una certificación ni una evaluación con usuarios. Referencia: [las diez heurísticas de Nielsen Norman Group](https://www.nngroup.com/articles/ten-usability-heuristics/).

| Heurística | Aplicación |
| --- | --- |
| Estado visible | Mensaje y progreso al actualizar, abrir, guardar y exportar; confirmación al copiar o recuperar. |
| Lenguaje familiar | Tooltips en español sencillo, títulos Original y Versión a comparar; explicación de los controles técnicos. |
| Control y libertad | Limpiar cada panel, intercambiar, restablecer, recuperar textos y opciones, deshacer y rehacer; salida con Listo o Esc. |
| Consistencia | Los mismos roles, colores, formas y estados en controles, fondos, fragmentos y corchetes. |
| Prevención de errores | Se mantiene la validación existente antes de copiar. Restablecer no vuelve a ejecutarse sobre un espacio ya vacío con opciones iniciales. |
| Reconocimiento | Acciones junto al texto que afectan, etiquetas y tooltips, corchetes de sección e indicadores de revisión y saltos. |
| Flexibilidad | Atajos existentes, búsqueda en ambos paneles, copia de texto completo y ajuste del ancho de los editores. |
| Minimalismo | Opciones secundarias guardadas en un panel; encabezado compacto y una acción principal de copiar resultado. |
| Recuperación de errores | Aviso comprensible si falla el portapapeles; recuperación de los textos y opciones tras limpiar o restablecer. |
| Ayuda | Guía breve integrada, explicación de colores y atajos, ayudas al pasar el puntero. |

## Acciones y límites

- Copiar resultado copia el texto completo del panel derecho, aunque haya filtros activos. No crea una tercera versión ni escribe un archivo.
- Limpiar e intercambiar trabajan sobre las mismas instancias de documento y utilizan su edición existente. No cambian los archivos de origen.
- Recuperar textos guarda una sola instantánea anterior de ambos textos, las referencias a archivos y las opciones. No es un historial ilimitado ni conserva las marcas de revisión de una sesión restablecida.
- Restablecer conserva la apariencia elegida. Las opciones de comparación vuelven a sus valores iniciales.
- Los tooltips no interceptan clics sobre los controles que explican.

## Verificación

Las pruebas de interfaz recorren controles con clics y teclado, fuerzan el renderizado y examinan capturas. Comprueban claro, oscuro y sistema; tamaños normal y compacto; tooltips; documentos editables; copia al portapapeles mediante un servicio simulado; limpieza, intercambio y recuperación. Verifican contraste de al menos 7:1 en el texto normal del editor y 4,5:1 sobre fondos de diferencias en los ejemplos probados. Esto no sustituye una auditoría completa de accesibilidad.

El algoritmo, los resultados canónicos, la comparación automática, la validación de copias, los filtros y los formatos de exportación conservan su comportamiento.

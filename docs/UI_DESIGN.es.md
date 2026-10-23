# Interfaz de comparación

El diseño adapta los principios de Material Design 3 a los controles nativos de Avalonia. Conserva Fluent como base técnica, sin instalar un paquete de controles Material ni cambiar el motor de comparación.

## Sistema visual

- La paleta de interfaz usa los colores solicitados por el usuario:

  | Rol | Claro | Oscuro |
  | --- | --- | --- |
  | Primario | `#3B82F6` | `#003994` |
  | Secundario | `#8B5CF6` | `#4F1F9E` |
  | Fondo | `#FFFFFF` | `#111827` |
  | Texto | `#1F2937` | `#F9FAFB` |
  | Éxito | `#10B981` | `#00401A` |
  | Error | `#EF4444` | `#830000` |

  Las superficies, bordes y estados de foco usan tonos derivados para mantener la separación y legibilidad. Los fondos, fragmentos, indicadores y corchetes rojos, verdes y amarillos de comparación conservan sus valores anteriores en `DiffPalette` y en los recursos `AddedInk`, `DeletedInk` y `ModifiedInk`. La apariencia Sistema sigue la preferencia del equipo.

- La barra superior agrupa Archivo, Edición, Vista, Herramientas y Ayuda. La franja rápida conserva navegación, revisión y búsqueda. Abrir, guardar, exportar, limpiar y recuperar quedan en desplegables; las cabeceras de documentos muestran solo sus nombres. El merge usa el mismo estilo y mantiene a mano navegación y guardado, con los checks junto al texto.
- Los roles `Surface`, `Container`, `ContainerHigh`, `Primary`, `PrimaryContainer` y sus colores de texto tienen valores independientes para claro y oscuro. La selección Sistema sigue la apariencia del equipo. Referencia: [roles de color de Material Design 3](https://m3.material.io/styles/color/the-color-system).
- Tarjetas de 16 px, botones redondeados, espaciado regular y jerarquía de texto separan acciones, contenido y estado. El texto comparado ocupa el área flexible principal.
- Las diferencias tienen fondos suaves verdes, rojos y amarillos. Los fragmentos cambian a un tono más intenso. Los indicadores `+`, `−` y `~` permiten distinguir los tipos sin depender solo del color.
- Los botones tienen una sola superficie visual y estados normal, hover, pulsado, foco y deshabilitado definidos en `Styles/ControlStates.axaml`. No se acumulan las capas de estado de Fluent ni hay transiciones de brocha: el estado cambia inmediatamente, sin destellos blancos retardados. Los botones activos tienen borde visible; los deshabilitados usan fondo, borde y texto neutros específicos, sin reducir la opacidad de todo el control ni responder al hover. El foco de teclado tiene un contorno de 2 px.
- El buscador conserva su superficie de la aplicación al escribir, pasar el puntero o recibir foco. Se definen los colores sobre la parte real `PART_BorderElement` de TextBox, conservando su edición, selección y comportamiento de accesibilidad nativos. También se cubren las superficies de los selectores y sus desplegables.
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

## Estados interactivos: investigación y validación

La separación entre superficie neutra y acciones sigue los [roles de color de Material Design 3](https://m3.material.io/m3/pages/color-roles?s=m3). La paleta solicitada se conserva como base; sus tonos derivados permiten mostrar controles y foco con contraste suficiente. El predominio de superficies neutras y uso moderado de acentos toma la proporción 60–30–10 como orientación visual, no como requisito normativo ni como medición exacta de cada pantalla.

[Apple HIG: Color](https://developer.apple.com/design/human-interface-guidelines/color) recomienda dar significado consistente a los colores y revisar las variantes claras y oscuras. [Carbon: Color](https://www.carbondesignsystem.com/building-blocks/foundations/color/overview) distingue estados interactivos y utiliza una familia neutra para los controles deshabilitados, que no deben responder al hover ni recibir foco.

Se comprueba contraste de texto activo de al menos 4,5:1 y contraste de borde/foco de al menos 3:1 en los controles probados. [WCAG 2.2](https://www.w3.org/TR/WCAG22/) distingue texto, indicadores no textuales y controles inactivos; estos últimos están exentos del requisito de contraste, aunque sus etiquetas siguen siendo legibles. Esto verifica estados concretos y no constituye una certificación completa de conformidad de la aplicación.

El análisis técnico usa el código de [Button de Avalonia 11.3.12](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Avalonia.Themes.Fluent/Controls/Button.xaml) y [TextBox de la misma versión](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Avalonia.Themes.Fluent/Controls/TextBox.xaml). Fluent aplica sus estados sobre partes internas de plantilla; cambiar solo Background en el control no cubría el foco del buscador. La nueva plantilla de botón evita la segunda superficie y sus animaciones; los campos mantienen su comportamiento nativo con estilos explícitos en sus partes.

`ControlStateTests` mueve el puntero y pulsa botones de verdad en ambos temas, repite el hover dos veces y comprueba muestras inmediatas y retardadas, propiedades y píxeles. Incluye botones normales, principales, secundarios, destructivos y de menú, además de deshabilitado y foco de teclado. El buscador real se prueba escribiendo con foco y buscando un texto sin coincidencias, para verificar la superficie del campo y el estado de sus botones. Los colores de las diferencias de texto permanecen sin modificar.

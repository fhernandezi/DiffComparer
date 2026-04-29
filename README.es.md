# DiffComparer

Una herramienta de comparación de texto enfocada en la **legibilidad humana**, no solo en la mínima cantidad de cambios.

A diferencia de los diff tradicionales, este proyecto prioriza una alineación estable y clara, haciendo que las diferencias sean más fáciles de entender de un vistazo.

---

## ✨ Características

- 🔍 Motor de comparación basado en líneas  
- 🧠 Alineación basada en anclas (reduce ruido en el diff)  
- ⚡ Uso del algoritmo de Myers como respaldo para comparaciones precisas  
- ✏️ Resaltado de diferencias a nivel de palabra  
- 📂 Apertura y comparación de archivos reales  
- 🖥️ Interfaz limpia tipo lado a lado (Avalonia)  

---

## 🎯 Filosofía

La mayoría de herramientas diff optimizan para:

- Minimizar el número de cambios

Este proyecto optimiza para:

- Maximizar la legibilidad para humanos

👉 Esto significa:

- Menos “saltos” en las líneas  
- Alineación más estable  
- Cambios visuales más claros  

---

## 🧪 Ejemplo

En lugar de desordenar todo al insertar una línea:
A
B
C

vs

A
X
B
C

Este motor mantiene la alineación estable:

A
-X
B
C


---

## 🏗️ Cómo funciona (simplificado)

1. Detecta líneas ancla (coincidencias exactas o similares)  
2. Divide el texto en segmentos estables  
3. Aplica el algoritmo de Myers dentro de cada segmento  
4. Realiza comparación a nivel de palabra en líneas modificadas  

---

## 🚀 Primeros pasos

### Requisitos

- .NET (última versión recomendada)  
- Avalonia UI  

---

## 📸 Interfaz

Abrir archivo izquierdo/derecho
Comparación instantánea
Diferencias visuales con colores:
🟢 Agregado
🔴 Eliminado
🟡 Modificado

---

## 🧠 Roadmap

Navegación entre cambios
Modo “mostrar solo diferencias”
Soporte drag & drop
Optimización de rendimiento
Detección de bloques movidos
Herramienta de merge (futuro)

---

## ⚠️ Limitaciones conocidas

Bloques grandes movidos se interpretan como eliminación + inserción
Rendimiento no optimizado para archivos muy grandes
No incluye funcionalidad de merge (aún)

---

## 💡 ¿Por qué este proyecto?

Este proyecto nace como un experimento para explorar:

Algoritmos de diff (Myers, LCS)
Estrategias heurísticas de alineación
Diseño de UI/UX en herramientas para desarrolladores

---

## 🤝 Contribuciones

Se agradece cualquier feedback o idea.

Si encuentras un caso donde el diff sea confuso o incorrecto, abre un issue.

---

## 📜 Licencia

MIT


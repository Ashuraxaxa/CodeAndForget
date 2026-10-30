# Manual de marca de Celdo

**Versión:** 1 · 2026-10-30 · D-103. Los archivos del logo están en [marca/](marca/).

---

## 1. La idea

Una **celda** es la unidad mínima de una tabla: el lugar donde vive un dato. Celdo toma esa celda y la vuelve
trabajable: se ve, se busca, se corrige y queda registrado quién la cambió.

- **Nombre:** Celdo, de *celda* más *do* (hacer). Se pronuncia igual en español y en inglés: *CEL-do*.
- **Lema:** *Tu base de datos, lista para trabajar.* En inglés: *Your database, ready to work.*
- **Promesa en una frase:** Celdo convierte tu base de datos en una aplicación lista para usar, en minutos, sin
  programar y dentro de tu red.

### Personalidad

| Celdo es | Celdo no es |
|---|---|
| **Claro:** dice qué hace en palabras simples. | Críptico ni lleno de siglas. |
| **Confiable:** cuida los datos de producción y lo demuestra (permisos, auditoría). | Temerario ni "muévete rápido y rompe cosas". |
| **Práctico:** resuelve el problema de hoy, el ticket de las 9 de la mañana. | Futurista sin sustancia. |
| **Cercano:** habla como un colega de TI que sabe. | Corporativo ni distante. |

---

## 2. Logo

| Pieza | Archivo | Uso |
|---|---|---|
| Logo completo (fondo claro) | `marca/celdo-logo.svg` | Landing, documentos, presentaciones |
| Logo completo (fondo oscuro) | `marca/celdo-logo-oscuro.svg` | Fondos oscuros, modo noche |
| Símbolo | `marca/celdo-simbolo.svg` | Favicon, ícono de la app, avatar de redes, espacios chicos |

**El símbolo** es una tabla de 2 × 2 sobre un cuadrado azul. Tres celdas en blanco translúcido y **una celda
activa en amarillo marcador**: el dato que estás editando. Esa celda amarilla es el elemento más reconocible de la
marca.

**La palabra** "celdo" va siempre en minúsculas, en Bricolage Grotesque Bold, con un espaciado un poco más cerrado
que el normal (−1,5 en 44 px).

> El texto del logo en SVG usa la fuente instalada. Para piezas impresas o para el registro de la marca, se debe
> pasar a curvas (contornos) en un editor vectorial.

### Reglas

- **Espacio libre:** alrededor del logo, al menos el ancho de una celda del símbolo (¼ de su lado).
- **Tamaño mínimo:** 96 px de ancho el logo completo; 16 px el símbolo (funciona como favicon).
- **No:** cambiar los colores de las celdas, mover la celda amarilla, girar el símbolo, agregar sombras o degradados,
  escribir "Celdo" con mayúscula dentro del logo, ni poner el logo sobre fotos sin un fondo sólido.

---

## 3. Colores

La paleta sigue la de la aplicación, para que el landing y el producto se vean como una sola cosa.

| Nombre | Hex | Uso |
|---|---|---|
| **Azul Celdo** | `#2563EB` | Color principal: botones, enlaces, el cuadrado del símbolo. |
| **Marcador** | `#FFD23F` | Acento: la celda activa, resaltados y lo que debe llamar la atención. **Nunca como color de texto sobre blanco.** |
| **Tinta** | `#0F172A` | Texto principal en fondo claro; fondo de bloques oscuros. |
| **Noche** | `#0B1120` | Fondo en modo noche. |
| **Pizarra** | `#64748B` | Texto secundario, notas. |
| **Niebla** | `#EEF2F9` | Fondos suaves en modo claro, bandas del landing. |

**Proporción aproximada:** 70 % neutros (blanco, Niebla, Tinta), 25 % Azul Celdo y 5 % Marcador. El amarillo funciona
porque es escaso.

**Contraste:**
- Tinta sobre blanco, blanco sobre Azul Celdo y Tinta sobre Marcador pasan WCAG AA.
- Pizarra sobre blanco solo pasa en textos de 14 px o más.

---

## 4. Tipografía

| Rol | Fuente | Dónde |
|---|---|---|
| Titulares de marketing | **Bricolage Grotesque** (600–800) | Landing, presentaciones, redes. Con moderación: titulares, nunca párrafos. |
| Texto y producto | **Inter** (400–600) | Párrafos del landing y toda la aplicación. |
| Código y datos | **JetBrains Mono** (400–500) | Comandos (`docker run …`), nombres de tablas y columnas. |

Las tres son de Google Fonts, con licencia libre (OFL).

**Escala del landing:** 56 / 40 / 28 / 20 / 17 / 14 px. Titulares con interlineado 1,05 y espaciado −2 %; texto
con interlineado 1,6.

---

## 5. Cómo habla Celdo

- **Tú, no usted**, en Latinoamérica. Directo y cordial.
- **Habla del problema del usuario, no de la tecnología:** "corrige un dato sin pedirle a TI" en vez de "CRUD
  dinámico sobre metadata del catálogo".
- **Verbos concretos:** conecta, busca, corrige, exporta, delega.
- **Cifras reales cuando las hay:** "4 motores", "hasta 25 usuarios", "auditoría de 1 año".
- **Sin promesas vacías:** nada de "revolucionario", "potenciado por IA" ni "la mejor solución del mercado".

| Mejor | Evitar |
|---|---|
| Conecta tu base y empieza a trabajar en minutos. | Una solución innovadora que transforma tu gestión de datos. |
| Cada cambio queda registrado: quién, cuándo y qué valor tenía antes. | Trazabilidad empresarial de última generación. |
| Tus datos no salen de tu red. | Seguridad de nivel bancario. |
| La IA propone; tú decides. | Automatización inteligente con IA. |

### Nombres de los planes

**Gratis**, **Pro** y **Empresa**. En inglés: *Free*, *Pro* y *Business*.

---

## 6. Elementos gráficos

- **La celda activa:** un rectángulo con borde de 2 px en Azul Celdo y relleno Marcador. Sirve para resaltar una
  cifra o una palabra clave en un titular.
- **Cuadrícula:** líneas finas (1 px, Niebla en claro, `#1E293B` en oscuro) como fondo de bandas. Evoca una hoja de
  datos sin recargar.
- **Íconos:** de [Lucide](https://lucide.dev) (los mismos de la aplicación), con trazo de 2 px y esquinas
  redondeadas.
- **Capturas del producto:** siempre reales, en modo claro y en modo noche, con datos de ejemplo creíbles (nunca
  datos de clientes).
- **Bordes:** radio de 10 px en tarjetas y 6 px en botones, como en la aplicación.

---

## 7. Aplicaciones

| Pieza | Indicaciones |
|---|---|
| Favicon e ícono de la app | El símbolo sobre fondo transparente. |
| Avatar de redes | El símbolo centrado, con 15 % de margen. |
| Portada de LinkedIn o YouTube | Fondo Tinta con cuadrícula, logo para fondo oscuro a la izquierda, lema en Bricolage a la derecha. |
| Presentaciones | Fondo blanco; títulos en Tinta; un solo resaltado Marcador por diapositiva. |
| Correo de la empresa | Firma con el logo completo de 120 px y el lema. |

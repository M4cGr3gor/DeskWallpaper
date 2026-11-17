# DeskWallpaper

Gestor de fondos de escritorio para Windows 11 con **varios monitores**. Sirve para elegir, encuadrar y aplicar una imagen o un vídeo por zona: por ejemplo, una panorámica repartida entre dos monitores colocados uno junto al otro y otra imagen distinta en un tercero. Además lleva una biblioteca con tus fondos, busca otros nuevos en internet y puede mejorar imágenes con IA que funciona en tu propio equipo.

Pensado para usarlo con calma de vez en cuando: lo abres, cambias los fondos y lo cierras. Windows conserva los fondos por sí solo, así que no hace falta que el programa quede abierto (salvo el pequeño ayudante de los fondos animados).

## Funciones

**Lienzo y zonas**
- Detecta los monitores, su posición, su resolución, su escala y su tamaño físico (por EDID).
- Une monitores contiguos en una zona para repartir una panorámica entre ellos o sepáralos para poner una imagen en cada uno.
- Encuadre con arrastre y zoom. El recorte se calcula a escala física, así que una imagen que cruza dos monitores de distinto tamaño o densidad se ve continua.
- Compensación del marco entre monitores, con un patrón para ajustarlo a ojo.
- «Probar 15 s»: pone los fondos, minimiza todo y pregunta si mantenerlos; si no contestas, vuelve a los anteriores.
- Un monitor que se deja vacío en el lienzo queda en negro al aplicar.
- Historial de combinaciones aplicadas y perfiles según los monitores conectados.

**Biblioteca**
- Vigila las carpetas que elijas (con o sin subcarpetas) y se actualiza sola al añadir o quitar archivos; también hay un botón para volver a revisarlas.
- Secciones por forma (panorámicas, un monitor, otras), favoritas, etiquetas, búsqueda por nombre y filtro por color.
- Visor a pantalla completa con flechas e información.
- Nunca modifica, mueve ni borra tus originales.

**Búsqueda online**
- Escribe qué buscas una vez y busca en varias fuentes a la vez, ya filtrado por la resolución de tus zonas.
- Fuentes con API (sus resultados se juntan en una cuadrícula) y fuentes web (se abren en un navegador integrado).
- Las fuentes se configuran sin recompilar: se pueden editar, añadir y quitar desde Ajustes. Vienen de serie Wallhaven y Google Imágenes para imágenes, y Pexels y Pixabay para vídeos (sus API piden una clave personal gratuita).

**IA local** (ONNX Runtime, sin enviar nada a internet)
- Quitar marcas pintando encima (LaMa).
- Ampliar imágenes pequeñas (Real-ESRGAN, con la tarjeta gráfica mediante DirectML si se puede).
- Quitar ruido y artefactos JPEG (SCUNet).
- Los modelos se descargan la primera vez que se usan y el resultado se guarda siempre en un archivo nuevo.

**Fondos animados**
- Vídeo detrás de los iconos del escritorio, repartido entre varios monitores igual que una imagen, y mezclable con imágenes en el mismo escritorio.
- Pestaña y biblioteca propias para los vídeos (MP4, WebM, MKV, MOV, M4V), con duración y visor que los reproduce.
- Se pausa por monitor cuando una ventana maximizada lo tapa y del todo al bloquear la sesión.
- Arranca con Windows al iniciar sesión e incluye un icono junto al reloj para pausar, abrir el programa o quitarlo.
- Opcional (activado por defecto): usa una copia del vídeo del tamaño de tus monitores para gastar menos memoria.

## Requisitos

- Windows 11 (probado en 24H2) de 64 bits.
- Para compilar:
  - [SDK de .NET 10](https://dotnet.microsoft.com/download).
  - Visual Studio 2022 o posterior (o sus Build Tools) con el componente **Desarrollo para el escritorio con C++**, para el ayudante de fondos animados.

## Compilar y probar

```bat
build.cmd test      rem compila todo y pasa las pruebas
publicar.cmd        rem genera la versión para usar en Distribucion\DeskWallpaper
```

La versión publicada es autocontenida: no hace falta instalar .NET ni el Windows App SDK en el equipo donde se use. Basta con copiar la carpeta `Distribucion\DeskWallpaper` donde quieras y ejecutar `DeskWallpaper.exe`; el `LEEME.txt` que se genera al lado lo explica.

Para probar sin tocar tus datos, la app acepta otra carpeta de datos:

```bat
DeskWallpaper.exe --data=C:\ruta\a\una\carpeta\de\prueba
```

## Dónde guarda las cosas

Todo va en `%LOCALAPPDATA%\DeskWallpaper`: la configuración, la biblioteca (SQLite), el historial, las miniaturas, los fondos generados, los modelos de IA descargados y `error.log`. Las cachés tienen siempre un tope de tamaño. Ningún error cierra la aplicación: los inesperados se apuntan en `error.log` y se avisa en la barra de estado.

## Estructura

| Carpeta | Qué contiene |
|---|---|
| `src/DeskWallpaper.Nucleo` | Toda la lógica, sin interfaz: monitores y zonas, geometría y recortes, aplicar y probar fondos, historial, biblioteca, configuración, búsqueda online, vídeos e IA. |
| `src/DeskWallpaper.Windows` | Acceso a Windows: detección de monitores y EDID, `IDesktopWallpaper` y lo que habla con el ayudante de fondos animados. |
| `src/DeskWallpaper.App` | La aplicación WinUI 3. |
| `src/DeskWallpaper.Animado` | El ayudante de fondos animados en C++ (Win32, Media Foundation y Direct3D 11). Se compila con su `compilar.cmd`, al que llaman `build.cmd` y `publicar.cmd`. |
| `src/DeskWallpaper.Cli` | `dwcli`, una herramienta de línea de órdenes para el desarrollo (monitores, guardar y restaurar fondos, aplicar…). |
| `tests/DeskWallpaper.Pruebas` | Pruebas xUnit del núcleo. No tocan el escritorio real ni la red. |
| `tools` | Utilidades de desarrollo: capturas de la interfaz y generación del icono. |

## Tecnología

- C# con .NET 10 y WinUI 3 (Windows App SDK), sin empaquetar y autocontenida.
- [SkiaSharp](https://github.com/mono/SkiaSharp) para las imágenes y [Microsoft.Data.Sqlite](https://learn.microsoft.com/dotnet/standard/data/sqlite/) para la biblioteca.
- ONNX Runtime con DirectML (incluido en el Windows App SDK) para la IA.
- C++20 con Media Foundation y Direct3D 11 para los fondos animados.

## Modelos de IA

No van incluidos en el repositorio: el programa los descarga de Hugging Face la primera vez que se usan y comprueba su huella SHA-256. Cada uno tiene su propia licencia:

| Modelo | Para qué | Origen | Licencia |
|---|---|---|---|
| LaMa | Quitar marcas | [Carve/LaMa-ONNX](https://huggingface.co/Carve/LaMa-ONNX) | Apache-2.0 (entrenado con Places2, CC-BY 4.0) |
| Real-ESRGAN ×4 | Ampliar | [SceneWorks/real-esrgan-onnx](https://huggingface.co/SceneWorks/real-esrgan-onnx) | BSD-3-Clause |
| SCUNet | Quitar ruido | [Heliosoph/scunet-onnx](https://huggingface.co/Heliosoph/scunet-onnx) | Apache-2.0 |

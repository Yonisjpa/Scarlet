# Scarlet · alpha 0.3 (app de Windows)

Launcher a pantalla completa estilo consola para la ROG Xbox Ally X. La interfaz es la misma de la demo (`web/index.html`); la app de Windows (C# + WebView2) le da acceso real a Steam, al botón Xbox, al sistema y a los mandos. Proyecto personal.

## 1. Compilar (una sola vez)

1. Instala el **SDK de .NET 8**: abre PowerShell y escribe `winget install Microsoft.DotNet.SDK.8` (o descárgalo de dotnet.microsoft.com).
2. Doble clic en **`build.cmd`**. La primera vez descarga paquetes y tarda unos minutos.
3. Al terminar se abre la carpeta `dist\Scarlet` con **`Scarlet.exe`**. Si compilaste en tu PC, copia esa carpeta completa a la Ally (por ejemplo a `C:\Juegos\Scarlet`).

También puedes abrir `XboxScarlet.csproj` en Visual Studio 2022 y presionar F5.

> Si la compilación falla, copia el error y mándamelo: este código no se pudo compilar en el entorno donde se escribió.

## 2. Primera vez que la abres

Aparece **Cuentas y arranque**:

- **SteamID64:** se detecta solo.
- **API key de Steam:** sácala en <https://steamcommunity.com/dev/apikey> (en "dominio" pon cualquier cosa, p. ej. `localhost`). Sin ella la app funciona solo con lo que está instalado y sin amigos ni logros.
- En Steam › tu perfil › Editar perfil › Privacidad, pon **Perfil** y **Detalles de juego** en **Público**.
- **API key de SteamGridDB** (opcional, portadas cuadradas bonitas): <https://www.steamgriddb.com/profile/preferences/api>. Sin ella se usa el arte oficial de Steam que ya está en tu equipo.

Las claves se guardan cifradas con tu usuario de Windows en `%APPDATA%\Scarlet\config.json`.

## 3. Preparar la consola

En la misma ventana (y después en Ajustes › Cuentas y arranque):

- ☑ **Abrir Scarlet al iniciar sesión.** Usa la clave normal de inicio de Windows (`HKCU\...\Run`).
- ☑ **Quitarle el botón Xbox a la Game Bar.** Cambia dos interruptores de Windows (`HKCU\Software\Microsoft\GameBar`): `UseNexusForGameBarEnabled = 0` para el escritorio y `LegacyGamepadXboxGuideButtonEnabled = 1` para el modo Xbox. Guarda los valores que tenías y reinicia la Game Bar para que tome el cambio. Si aun así se llega a abrir con el botón Xbox, Scarlet la cierra al instante.

Y a mano, una sola vez:

- **Steam** › Configuración › Control › desactiva **"El botón Guía activa Steam"**. Si no, Big Picture se roba el botón Xbox.
- **Steam** › Configuración › Interfaz › desactiva **"Iniciar Steam en modo Big Picture"**. Scarlet abre Big Picture solo para la tienda y lo cierra antes de lanzar un juego, para que no se te ponga enfrente.

Todo se deshace quitando las palomitas. No se modifica nada más de Windows.

### Que reemplace de verdad a la app de Xbox

Con solo "abrir al iniciar sesión", Scarlet es una app más: Windows sigue cargando la app de Xbox (modo pantalla completa) o el escritorio detrás. Para que la consola **arranque directo en Scarlet**, tiene que ser la **app de inicio del modo Xbox**, y Windows solo deja elegir apps registradas para eso. La forma sencilla y reversible:

1. Instala **AnyFSE** desde Microsoft Store (gratis, código abierto).
2. Abre la configuración de AnyFSE, en **Choose home app** elige **Custom / Browse** y busca `Scarlet.exe`. En la detección pon el proceso `Scarlet`.
3. Windows › Configuración › Juegos › **Experiencia de pantalla completa** › **App de inicio**: elige **AnyFSE** y activa que entre al modo pantalla completa al iniciar.
4. Reinicia. La Ally arranca en modo Xbox, pero lo que aparece es Scarlet; la app de Xbox ya no se abre y no hay escritorio detrás.

Con esto ya no necesitas "Abrir Scarlet al iniciar sesión" (si lo dejas no pasa nada: la app no se abre dos veces). Para deshacerlo, vuelve a elegir la app de Xbox como app de inicio.

Si abres `Scarlet.exe` cuando ya está corriendo (o AnyFSE / la Game Bar piden "Inicio"), no se abre otra copia: la que ya está se pone al frente.

## 4. Controles

| Botón | Qué hace |
|---|---|
| A / B | Aceptar / atrás |
| X | Opciones del juego · sobre el juego abierto: cerrarlo |
| Y | Ordenar (biblioteca) · tipo (álbum) |
| Menú ☰ | Ajustes · opciones del juego abierto · buscar (biblioteca) |
| LB / RB | Saltar en la fila / por páginas |
| LT / RT | Cambiar colección (tus colecciones de Steam) |
| **Xbox (1 toque)** | Menú rápido (brillo, volumen, energía, Wi-Fi, modo avión), encima de lo que esté abierto, también de un juego |
| **Xbox ×2** | Inicio; otra vez ×2 regresa al juego (el juego sigue corriendo) |
| **Mantener Xbox** | Ventanas activas del modo Xbox (el selector de Windows) |
| **View + Menú juntos** | Respaldo del botón Xbox: tocar = Inicio / regresar al juego · mantener = menú rápido |
| Botón Armoury Crate | Abre su panel normal |

Capturas: usa la Game Bar (Win + Alt + ImprPant) o Steam (F12). Aparecen al instante en el Álbum con su notificación.

## 5. Qué hace de verdad la app

- **Biblioteca:** juegos instalados (de todas tus bibliotecas de Steam), los que tienes en tu cuenta, tus colecciones, horas y última vez jugado. También funciona sin internet con lo instalado.
- **Portadas:** X › Portada muestra todas las de SteamGridDB para ese juego (Y cambia entre cuadradas y todas, incluidas animadas) y eliges la tuya. También puedes volver al arte oficial de Steam.
- **Avatar:** tu avatar de Steam, el animado si tienes uno equipado y el marco. Se guarda en disco para verlo sin internet y se actualiza si lo cambias.
- **Lanzar juegos:** pantalla negra con los logos hasta que la ventana final del juego lleva 2 s estable en pantalla completa. Si Steam muestra un diálogo, se deja ver. Si en 60 s no detecta nada, se quita sola.
- **Notificaciones encima del juego:** amigos que se conectan (revisa cada 30 s), logros (cada minuto mientras juegas), capturas, batería y mandos.
- **Menú rápido:** brillo y volumen reales. Los modos de energía son los de Windows (Mayor eficiencia / Equilibrado / Máximo rendimiento), no los TDP de Armoury Crate. Wi-Fi y modo avión vía Windows.
- **Mandos:** detecta Xbox / DualSense / otros, batería, vibrar para identificar, emparejar y olvidar por Bluetooth.
- **Álbum:** Game Bar (`Videos\Captures`) + capturas de Steam. Borrar las manda a la papelera.
- **Noticias:** de tus juegos recientes (Steam News). Al abrir una noticia, se abre en el navegador de Steam.
- **Xbox Store / Game Pass:** los juegos instalados desde la tienda de Xbox aparecen en la biblioteca (colección "Xbox") con la portada oficial de la tienda, y se abren con la misma pantalla de carga.
- **Sonidos propios:** pon archivos en la carpeta `sounds` junto a `Scarlet.exe` (move, select, back…; ver `sounds\LEEME.txt`) y reemplazan a los sintetizados.
- **Cuentas y arranque:** Ajustes › Cuentas y arranque, dentro de Scarlet y con el control (las claves se escriben con el teclado en pantalla o se pegan).
- **Apps:** YouTube (versión TV) y Edge. Puedes agregar más en `%APPDATA%\Scarlet\apps.json`.

## 6. Límites conocidos

- **Juegos con pantalla completa exclusiva:** las notificaciones no se ven encima (casi todos los juegos actuales usan ventana sin bordes).
- **Anticheat:** algunos juegos no dejan leer su ventana; ahí la pantalla de carga se quita por tiempo.
- **"Unirse a la partida":** solo funciona si el juego del amigo expone un lobby o servidor en Steam.
- **Mapeo de botones de Armoury Crate:** se ve, pero se cambia en Armoury Crate; ASUS no da una forma pública de cambiarlo desde otra app.
- **El botón Xbox** se lee con una función de XInput no documentada (`XInputGetStateEx`). Funciona en Windows 10/11, pero hay que probarlo en tu Ally.

## 7. Si algo falla

- Registro: `log.txt` junto a `Scarlet.exe` (y otra copia en `%LOCALAPPDATA%\Scarlet\log.txt`). Mándamelo si algo raro pasa: ahí se anota cada vez que la app ve el botón Xbox, si la Game Bar se lo intentó quedar y qué avatar se descargó.
- Para inspeccionar la interfaz: en `%APPDATA%\Scarlet\config.json` pon `"DevTools": true` y reinicia. F12 abre las herramientas de desarrollo.
- Salir: Ajustes › Salir al escritorio (o Alt + F4).

> **Cambio de nombre (alpha 0.3):** el programa ahora es `Scarlet.exe`. Tu configuración y portadas se pasan solas a las carpetas nuevas. En AnyFSE vuelve a elegir *Browse* → `Scarlet.exe`.

## 8. Desinstalar

1. Ajustes › Cuentas y arranque: quita las tres palomitas y guarda.
2. Borra la carpeta de la app, `%APPDATA%\Scarlet` y `%LOCALAPPDATA%\Scarlet`.

## Estructura

```
XboxScarlet.csproj   proyecto (.NET 8, WinForms + WebView2)
build.cmd            compila a dist\Scarlet
apps.json            apps de ejemplo (YouTube, Edge)
web\index.html       la interfaz
sounds\              tus sonidos (opcional)
src\MainForm.cs      ventana, puente con la interfaz, flujo de juegos
src\SteamService.cs  Steam local + Steam Web API
src\XboxService.cs   juegos de la Xbox Store y sus portadas oficiales
src\QuickOverlay.cs  menú rápido encima de los juegos
src\GameTracker.cs   lanzar, detectar "juego listo", volver, cerrar
src\XInputService.cs botón Xbox (doble / mantener) y vibración
src\SystemService.cs brillo, volumen, energía, radios, almacenamiento
src\PadService.cs    mandos y Bluetooth
src\AlbumService.cs  capturas
src\ArtCache.cs      portadas y avatar en caché
src\ToastForm.cs     notificaciones encima del juego
src\SetupForm.cs     cuentas y arranque
```

## Estructura del repo

- `src/` — código de la app (.NET 8, WinForms + WebView2).
- `web/index.html` — interfaz ya armada, la que usa la app.
- `ui/` — fuente de la interfaz: `src.html` + `assets/`. Después de editarla, `python ui/build.py` genera `ui/index.html`; cópialo a `web/index.html`.
- `sounds/` — sonidos de la interfaz.
- `build.cmd` — compila en Windows y deja la app en `dist\Scarlet`.

Tus claves, configuración, caché y logs viven en `%APPDATA%\Scarlet` y `%LOCALAPPDATA%\Scarlet`, nunca en este repo.

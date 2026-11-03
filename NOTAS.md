Novedades de la beta 1.4

Barra de abajo:
- Ahora: Steam, Armoury Crate, Álbum, Amigos, Controles, Logros, Ajustes y Apagar. Se fueron Chat, Noticias y Tienda.
- Steam abre Steam Big Picture (tu biblioteca, la tienda y los amigos); al lanzar un juego se cierra solo, como antes la tienda.
- Armoury Crate abre su panel.
- Los íconos de Steam y Armoury Crate son tus imágenes, limpias y del tamaño y color de la barra.
- Al chat con un amigo se sigue llegando desde su perfil («Enviar mensaje»).

Pantalla táctil:
- Un toque elige un juego y el segundo lo abre (en el Inicio y en la Biblioteca). Con A se abre directo, como siempre.
- La fila del Inicio se desliza con el dedo, con inercia; las listas (Biblioteca, Álbum, Ajustes…) también.
- Deslizar desde la orilla izquierda hacia la derecha regresa (una flechita te dice cuándo ya va a regresar).
- Dejar el dedo sobre un juego abre sus opciones; en el Álbum empieza a elegir capturas para mandarlas al celular.
- Las ayudas de abajo («B Atrás», «A Aceptar», X, Y, ☰…), las del menú rápido y las pestañas de la Biblioteca se tocan.

Por dentro:
- Se quitó el código de Noticias (en la interfaz y en la app) y estilos que ya no se usaban.
- Las ayudas de abajo ya no se vuelven a dibujar si no cambiaron.
- 8 pruebas nuevas con un dedo simulado (tocar, deslizar, orilla, toque largo) y la barra nueva en las de siempre.

Novedades de la beta 1.3

Lo que pediste:
- Los mosaicos de juegos y apps dicen «En uso» en vez de «Jugando» (las apps de música siguen con «Sonando / En pausa»).
- El menú rápido (el nuevo y el clásico) ya no tiene «Apagar…» ni «Ajustes»: siguen en los íconos de abajo del Inicio.
- El mosaico «Amigos» del Inicio abre la lista nueva (Perfil › Lista de amigos). Al marcar a alguien con ★ en su perfil, la lista se actualiza al regresar.

Más ligero mientras juegas (con lo que salió en tu log de la 1.2):
- El menú encima del juego ahora sí se congela al cerrarlo. En la 1.2 Windows lo rechazaba cada vez y el menú se quedaba despierto todo el tiempo.
- La interfaz escondida usa la menor memoria posible mientras juegas.
- Los números de Diagnóstico › Rendimiento ya no cuentan el instante en que abres o cierras el menú (de ahí salían los picos de 5–7 %).
- La pantalla de arranque se quedaba animando invisible para siempre, y el «Powered by Dolby Atmos» de la pantalla de carga y del menú cerrado también: ya no.

Arreglos:
- Si vuelves a abrir un juego que todavía está abriendo (por ejemplo, esperando la nube), ya no dice «se reanuda» antes de tiempo: sigue esperando.
- Álbum: una carpeta sin permiso ya no hace que se pierdan las capturas que venían después; muestra hasta 2,000 (antes 600); también .webp, .gif, .bmp, .mkv y .mov. Si hay archivos que no se pueden mostrar (como las capturas HDR .jxr), el log dice cuántos. Las miniaturas cargan conforme bajas.
- El log ya no repite la misma app varias veces al abrir el menú.

Por dentro (no cambia lo que ves):
- La interfaz pasó de un archivo de 5,800 líneas a 43 módulos por tema, y la app de Windows de un archivo de 2,900 líneas a 18 partes por tema, en carpetas.
- Todas las pruebas (las de la interfaz, las de lo que corre dentro de Steam y las de descargas) viven en el repositorio y GitHub las corre en cada cambio: si alguna falla, no se publica la versión.
- README nuevo con capturas, instrucciones y cómo está hecho.

Novedades de la beta 1.2

Internet sin salir de Scarlet (Ajustes › Red y VPN › Internet):
- Tus redes registradas y las que hay alrededor, con la señal en tres barras y candado si piden contraseña. Y vuelve a buscar.
- Conectarse con A: si pide contraseña sale un teclado con página de símbolos (#+=), botón Mostrar y la contraseña en puntos. Se ve el camino Consola → Dispositivo de red → Internet mientras conecta.
- Prueba de conexión (¿hay internet? ¿llega a Steam?), Estado de la conexión (red, seguridad, banda, canal, señal, IP, puerta de enlace, DNS, MAC) y Olvidar esta red.
- En Windows 11 24H2, Windows solo deja ver las redes de alrededor si las apps de escritorio tienen permiso de ubicación; si falta, Scarlet lo explica y abre ese ajuste.
- El menú rápido no cambia: Wi-Fi sigue prendiendo y apagando como antes.

Perfil nuevo (en dos columnas, como Ajustes):
- Perfil: tu avatar, lo que juegas ahora, tu código de amigo de Steam e historial de juego con horas.
- Lista de amigos en tarjetas (★ primero); Y la actualiza. En el perfil de un amigo: ★ Mejores amigos, Opciones, última conexión y lo que ha jugado en las últimas 2 semanas (si su perfil lo deja ver).
- Añadir un amigo: buscar por código de amigo y mandarle la solicitud con Steam. Las solicitudes recibidas y enviadas todavía se ven en Steam (Scarlet no las puede leer).

Rendimiento mientras juegas (el menú se ve y funciona igual):
- Cerrado, el menú encima del juego se congela (no corre nada) y se despierta al abrirlo.
- Con el juego al frente, la interfaz de Scarlet se esconde del todo (no dibuja ni anima) y sus procesos pasan a modo eficiencia de Windows con prioridad baja; al abrir el menú o volver a Scarlet regresan a lo normal al instante.
- Steam se lee cada 5 s en vez de cada segundo y el mando activo 4 veces por segundo en vez de 15.
- El menú rápido ya no se rehace si nada cambió, la música no se repinta con cada avance de la canción y los avisos encima del juego solo redibujan su línea.
- Diagnóstico › Rendimiento: cuánto procesador y memoria usó Scarlet en tu último juego, con el menú cerrado y abierto (también queda en el log).

Arreglos:
- Nube de Steam: la barra naranja ahora va en el juego que de verdad sube su partida (antes salía en el juego que intentabas abrir, con «Jugando»). Ese juego muestra «Esperando la nube de Steam…» en su pantalla de carga.
- «Jugando» ya no aparece mientras un juego todavía está abriendo.
- Ya no se enseñan las ventanas escondidas de Steam en cada arranque (Steam «pregunta» en todos los juegos; ahora solo cuenta si de verdad pregunta algo).
- La carátula de la música ya no se vuelve a buscar cada segundo con el menú abierto.
- Un juego externo nuevo ya no hereda la portada ni las horas de uno que quitaste.
- Si el botón Xbox abre Big Picture, Scarlet intenta apagar ese ajuste de Steam y, si no puede, te dice dónde: Steam › Ajustes › Mando › «El botón Guía enfoca Steam».

Novedades de la beta 1.1

Juegos externos (Ajustes › Juegos externos):
- Agrega cualquier juego o programa de tu PC: un .exe, un acceso directo (.lnk) o un .url (emuladores, otras tiendas). Se elige con un explorador de archivos hecho para el mando.
- Se abre como cualquier juego: pantalla de carga, «Jugando», menú rápido encima y horas jugadas (las cuenta Scarlet).
- En sus opciones: cambiar nombre (el teclado ya tiene mayúsculas), ícono y portada (SteamGridDB, el ícono del programa o una imagen tuya), parámetros de arranque, cambiar el archivo y quitarlo de Scarlet (no se borra nada de tu PC).

Nube de Steam:
- Cuando Steam sube o baja la partida de un juego, se ve en su mosaico como una descarga con la barra NARANJA (con su porcentaje si Steam lo da) y al terminar avisa «Partida guardada en la nube».
- Mientras se sincroniza, Scarlet espera a que el juego abra (antes se rendía al minuto).
- Si Steam te pregunta algo al abrir un juego (por ejemplo, qué partida conservar), ahora se ve en vez de quedar escondido.

Menú rápido:
- Fondo al 50 % para ver el juego detrás (también con los colores de la carátula). Textos, íconos y barras siguen sólidos. Se puede apagar en Ajustes › Sistema › Fondo transparente.

Álbum:
- Busca siempre en Videos › Capturas (y en las capturas de Steam de todas tus cuentas), se vuelve a leer cada vez que lo abres y con RB lo actualizas.

Arreglos:
- La carátula de la música ya no se queda pidiéndose sin parar (pasaba con canciones del mismo álbum) y siempre es la de la canción que suena.
- Apple Music ya no se silencia al volver al menú ni se da por cerrada cuando esconde su ventana; al reanudarla se vuelve a mostrar.
- Modo avión: ahora dice lo que dice Windows (antes salía activo con solo tener el Bluetooth apagado).
- «El juego no responde»: ya no avisa si el juego sigue trabajando (Stellar Blade daba avisos falsos).
- El botón Xbox con Steam o el escritorio al frente (Big Picture que se abre solo) abre el menú de Scarlet y cierra Big Picture.
- Después de hibernar, el juego se reanuda en vez de abrirse otra vez.
- Ya no se toman ventanas de otros programas como si fueran el juego (SteamGridDB for Xbox, Command Center, SCSKiller).
- El log ya no se llena con errores de red ni líneas repetidas.

Novedades de la beta 1.0

Menú rápido (botón Xbox), ahora como el de la Switch 2:
- Una sola lista de arriba a abajo: la canción (si suena algo), Hibernar, Brillo automático (solo si la consola tiene sensor de luz), brillo, volumen, volumen de cada app, Modo avión, Wi-Fi (con el nombre de tu red), VPN, Apagar… y Ajustes.
- ◀▶ mueven las barras; el número sale en una burbujita solo en la barra que eliges. En la canción, ◀▶ eligen anterior / pausa / siguiente.
- Arriba: hora, Wi-Fi, batería y «Powered by Dolby Atmos» (cuando está en uso). Abajo: el mando que estás usando, B Atrás y A Aceptar.
- Sigue tiñéndose con los colores de la carátula. Lo que habla con Windows (brillo, volumen, Wi-Fi, avión, VPN, música, encima del juego, táctil) es lo mismo de antes.
- ¿Algo raro? Ajustes › Sistema › Menú rápido clásico regresa el de antes.

Ajustes, como los de la Switch 2:
- Categorías a la izquierda (Usuario y cuentas, Mandos y vibración, Audio, Pantalla, Temas, Notificaciones, Energía, Red y VPN, Gestión de datos, Sistema) y sus opciones a la derecha, con interruptores y la ayuda debajo de la opción elegida.
- Temas: Claro, Oscuro o Automático (el mismo que Windows).

Notificaciones:
- Salen de una en una, con una línea abajo que se va acabando.
- Las que tienen algo que hacer lo muestran con el botón Xbox: un toque mientras se ve (en el menú) abre el perfil del amigo, los logros, el juego descargado o la captura.
- Capturas con su miniatura de verdad; los videos con ▶ y su duración, y «Guardando video…» mientras la Game Bar lo termina.
- Encima del juego se ven igual (sin la acción).
- Lo que no se mostró mientras jugabas llega en un solo aviso, «Mientras jugabas», al volver al menú.
- Ajustes › Notificaciones: descargas, logros, batería, amigos (Todos / Mejores amigos / Nadie), qué se ve encima del juego (Todos / Solo importantes / Ninguno) y el sonido.
- Mejores amigos: en Amigos, Y les pone ★ y quedan primero.

Además:
- «Powered by Dolby Atmos» en dorado y blanco, con un brillo suave (menú rápido y pantalla de carga). Se puede volver al blanco en Ajustes › Audio.
- El ícono de abajo a la izquierda cambia al mando que estás usando (la Ally, uno de Xbox o un DualSense) y los cuadritos marcan su número de jugador.
- Tocar la batería muestra el porcentaje y cuánto le queda.
- Opciones de energía: Hibernar, Reiniciar o Apagar; si no eliges nada en 10 s, se hiberna sola.
- Opciones de descarga: «Cancelar todas (N)» cuando hay varias.

Novedades de la alpha 0.4.29

Descargas, ahora leídas como lo hace Steam (de su propio código):
- El avance es el de ESTA consola. Con otra PC conectada (tu laptop pasándote un juego por la red de la casa), Scarlet mostraba a ratos el avance de la laptop: por eso marcaba 100 %, "92.70 GB de 78.80 GB" y "1 h 2 min" fijo. Ahora: el mismo porcentaje, GB y tiempo que Steam.
- Nunca más "X de Y" con X mayor que Y.
- Lo que Steam hace después de instalar (sombreadores, workshop) ya no aparece como otra descarga ni da otro aviso: el juego se puede jugar.
- Si justo después de instalar Steam baja algo más del mismo juego (por ejemplo, su DLC), cuenta como la misma descarga y avisa una sola vez, con "Descarga completada".
- Si terminan varias a la vez, un solo aviso ("3 juegos actualizados").
- Si Steam detiene una descarga (sin espacio, error de disco…), te avisa una vez, el menú dice por qué y trae "Reintentar".
- Sin internet a media descarga: "Esperando conexión…"; sigue sola al volver.
- Pausar, reanudar, cancelar e instalar ahora le dicen a Steam de qué dispositivo se trata (como sus propios botones). Sin eso, algunas órdenes no hacían nada; por eso antes Cancelar instalación no borraba.

Actualizaciones:
- Actualización pendiente que Steam no ha puesto a bajar: no sale como descarga; al abrir el juego, Scarlet te avisa y te da "Actualizar ahora" o "Jugar así".
- Si Steam exige actualizar al abrir el juego, Scarlet deja de esperarlo, te muestra la descarga ("Primero se actualiza") y lo abre solo al terminar.
- Cancelar una actualización ("Cancelar actualización") solo la quita de las descargas: el juego NO se desinstala.
- Una actualización en curso se ve en el juego con su franja, y al terminar avisa "Actualización completada".

Desinstalar:
- Ya no sale la ventana de Steam: confirmas en Scarlet y listo. El juego dice "desinstalando…" y el aviso "Desinstalado" sale cuando de verdad se borró.
- Si Steam no hace caso, Scarlet lo borra con Steam cerrado unos segundos (te avisa). Si tienes un juego abierto, espera a que lo cierres.

Por dentro:
- Cada compilación prueba el lector de descargas con 10 escenarios de Steam (dos dispositivos, sombreadores, cola, pendientes, errores, subida, reservar espacio, formato viejo). Si algo falla, no se publica.
- El log anota cada cambio de paso de una descarga y, mientras baja, cada minuto lo que manda Steam.

Novedades de la alpha 0.4.28

- La descarga ya no marca 100 % desde el principio. Steam manda el avance en 7 pasos fijos y uno de ellos es "reservar espacio en disco", que termina casi al instante; Scarlet lo estaba tomando como la descarga. Ahora lee cada paso por su lugar, igual que la pantalla de descargas de Steam.
- El menú dice en qué va Steam: "Reservando espacio en disco…", "Descargando datos…", "Transfiriendo desde YONIS…", "Instalando archivos…" o "Verificando archivos…", con los GB de ese paso.
- Cada minuto queda en el log lo que Steam manda de la descarga (para afinar con datos reales).

Novedades de la alpha 0.4.27

- El avance de la descarga ya no se queda en 0 %: Scarlet ahora lee el porcentaje y el tiempo restante que Steam calcula (lo mismo que muestra Steam), y los bytes de la fase que de verdad se está moviendo. Antes solo miraba la primera fase, que se queda en 0 cuando Steam copia el juego desde otra PC de la casa.
- Si Steam copia el juego desde otra PC de tu red (por ejemplo, tu laptop), el menú dice "Transfiriendo desde YONIS…".
- Cancelar instalación, arreglado:
  - La descarga se detiene y se quita de Steam en unos segundos; la franja dice "Cancelando…" mientras tanto y ya no se puede pedir dos veces.
  - Después se espera a que Steam borre lo descargado. Si su desinstalador no hace nada (en la versión de escritorio a veces lo ignora), Scarlet lo borra: cierra Steam unos segundos, quita los archivos a medias y lo vuelve a abrir. Te avisa con "Borrando lo descargado".
  - Solo se borra a mano una instalación que nunca terminó. A un juego ya instalado (cuando se cancela una actualización) no se le borra nada.
  - Si tienes un juego abierto, el borrado espera a que lo cierres.
  - Si vuelves a darle Instalar antes de que termine el borrado, no se borra nada.
- Una descarga cancelada ya no reaparece en la franja por los archivos que quedaban de ella.

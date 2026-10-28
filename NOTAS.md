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

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

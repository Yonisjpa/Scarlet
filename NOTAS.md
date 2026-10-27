Novedades de la alpha 0.4.17 · estabilidad con los juegos

- Scarlet ya no puede hacer esperar al juego: los vigilantes de ventanas (los que esconden Steam y notan cuándo se cierra el juego) viven en su propio hilo y solo apuntan; el de "ventana cerrada" ahora solo mira el proceso del juego, no todo Windows. Antes, si Scarlet estaba ocupado, el juego se podía quedar esperándolo (congelones de varios segundos en Stellar Blade).
- Scarlet ya no espera a otros programas desde su pantalla: los títulos de ventanas se leen sin preguntarle al programa, la ventana bajo el dedo se busca sin mandarle mensajes al juego, y esconder o mostrar ventanas de otros programas no espera respuesta. Nunca comparte la entrada con una ventana que no responde.
- Audio en un hilo dedicado, con una sola conexión que se rehace sola: se acaba el error que impedía usar el audio de Windows (barras, volumen y silencio del juego fallaban a ratos). Nada del audio traba la pantalla.
- Juego que no responde: ya no se le quita la pantalla. Aparece un aviso chiquito encima; si quieres cerrarlo, presionas el botón Xbox y ahí se pregunta. El log anota la memoria libre y la que usa el juego cuando se congela.
- El log ya no se llena con el mismo error: se anota una vez por minuto con cuántas veces se repitió, y al llegar a 2 MB se guarda como log-anterior.txt en vez de borrarse.
- La versión que muestra la app (Acerca de, log) ya es la correcta (se había quedado en 0.4.8).

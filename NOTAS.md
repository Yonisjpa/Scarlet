Novedades de la alpha 0.4.23

- Steam se reconecta solo: si Steam se abrió sin internet (o con la red bloqueándolo) y después llega la conexión, se reinicia desde cero para detectarla.
  - Al prender la VPN desde Scarlet, Steam se reinicia enseguida.
  - Si prendes la VPN desde su propia app (o cambias de red), Scarlet nota que regresó la conexión tras un rato sin ella y reinicia Steam.
  - Con un juego abierto no se reinicia (lo cerraría): se hace al salir del juego.
  - Avisos: "Reconectando Steam" y "Steam en línea".
- Instalar con Scarlet, corregido de raíz: Steam nombra la lista de juegos de su asistente distinto a como decía la documentación, y por eso Scarlet no reconocía el asistente y terminaba abriendo la ventana de Steam. Ya lo reconoce, y si Steam recién abierto ignora la orden, se la repite.
- Instalar: si no hay internet o la red bloquea Steam, te lo dice (y sugiere la VPN) en lugar de intentar.
- Descargas en vivo: se arregló un error que se repetía cada segundo en el log (Steam también nombra distinto los datos de las descargas).
- Cloudflare WARP se detecta también con su nombre nuevo (Cloudflare One Client).

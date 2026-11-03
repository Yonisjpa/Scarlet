"""Arma la interfaz de Scarlet en un solo archivo (web/index.html).

La interfaz está repartida por áreas para que sea fácil de mantener:
  pagina.html   el esqueleto de la página (con @@CSS@@ y @@JS@@)
  css/*.css     estilos, en orden (los de abajo pueden ajustar a los de arriba)
  js/*.js       código, en orden de ejecución (todo comparte un solo <script>)
  assets/*.png  imágenes propias que se incrustan (__PAD__, __XBTN__, …)

Uso: python ui/build.py [salida]   (sin salida: ui/index.html)
"""
import base64, pathlib, sys

root = pathlib.Path(__file__).parent
ASSETS = [("__PAD__", "pad"), ("__ROG__", "rog"), ("__SLASH__", "slash"), ("__XBOX__", "xbox"), ("__XPAD__", "xboxpad"),
          ("__PSPAD__", "pspad"), ("__DOLBY__", "dolby"), ("__XBTN__", "xboxbtn"),
          ("__STEAMIC__", "steam"), ("__ARMOURYIC__", "armoury")]


def join(folder, ext):
    parts = sorted((root / folder).glob("*" + ext))
    if not parts:
        sys.exit(f"no hay archivos {ext} en ui/{folder}")
    return "".join(p.read_text(encoding="utf-8") for p in parts)


def build():
    page = (root / "pagina.html").read_text(encoding="utf-8")
    html = page.replace("@@CSS@@", join("css", ".css"), 1).replace("@@JS@@", join("js", ".js"), 1)
    for token, name in ASSETS:
        data = base64.b64encode((root / "assets" / f"{name}.png").read_bytes()).decode()
        html = html.replace(token, f"data:image/png;base64,{data}")
    return html


if __name__ == "__main__":
    out = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else root / "index.html"
    html = build()
    out.write_text(html, encoding="utf-8")
    print(out.name, len(html) // 1024, "KB")

import base64, pathlib
root = pathlib.Path(__file__).parent
html = (root / "src.html").read_text(encoding="utf-8")
for token, name in [("__PAD__", "pad"), ("__ROG__", "rog"), ("__SLASH__", "slash"), ("__XBOX__", "xbox"), ("__XPAD__", "xboxpad"), ("__PSPAD__", "pspad")]:
    data = base64.b64encode((root / "assets" / f"{name}.png").read_bytes()).decode()
    html = html.replace(token, f"data:image/png;base64,{data}")
(root / "index.html").write_text(html, encoding="utf-8")
print("index.html", len(html) // 1024, "KB")

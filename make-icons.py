"""
Generates Fermata icon assets:
  - Assets/Icons/AppIcon-{size}.png  (multiple sizes for .icns)
  - Assets/Icons/TrayIcon.png        (44px @2x template for menu bar)
  - Assets/AppIcon.icns
"""
import math, os, subprocess, shutil, tempfile
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "fermata-ui", "Assets", "Icons")
os.makedirs(OUT, exist_ok=True)

BG = (244, 239, 228, 255)   # #F4EFE4  warm cream
FG = ( 42,  36,  32, 255)   # #2A2420  near-black


def draw_fermata(size, bg=BG, fg=FG):
    """
    Draws the fermata symbol: a thick dome arch (top half of a ring)
    with a filled circle dot underneath, centred on a square canvas.
    Renders at 4× for anti-aliasing then scales down.
    """
    S = size * 4   # supersampled
    img  = Image.new("RGBA", (S, S), bg)
    draw = ImageDraw.Draw(img)

    cx, cy = S // 2, S // 2

    # ── Arch ─────────────────────────────────────────────────────────────────
    # The arch occupies roughly the top 58% of the canvas width as its diameter.
    outer_r = int(S * 0.385)
    inner_r = int(S * 0.285)

    # Draw a full filled circle in FG (outer), then punch out inner with BG.
    # Then erase the bottom half to leave only the dome.
    arch_cx = cx
    arch_cy = int(S * 0.50)   # centre of the ring circles

    # Outer filled circle
    draw.ellipse(
        [arch_cx - outer_r, arch_cy - outer_r,
         arch_cx + outer_r, arch_cy + outer_r],
        fill=fg)
    # Punch out inner circle → ring
    draw.ellipse(
        [arch_cx - inner_r, arch_cy - inner_r,
         arch_cx + inner_r, arch_cy + inner_r],
        fill=bg)
    # Erase bottom half (below arch centre) → top dome only
    draw.rectangle([0, arch_cy, S, S], fill=bg)

    # ── Dot ──────────────────────────────────────────────────────────────────
    dot_r  = int(S * 0.068)
    dot_cy = arch_cy + int(S * 0.055)   # just below the arch centre line
    draw.ellipse(
        [arch_cx - dot_r, dot_cy - dot_r,
         arch_cx + dot_r, dot_cy + dot_r],
        fill=fg)

    # Scale back down with LANCZOS for smooth edges
    return img.resize((size, size), Image.LANCZOS)


def draw_tray(size=44):
    """Black-on-transparent template icon for the macOS menu bar."""
    S = size * 4
    img  = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    fg   = (0, 0, 0, 255)
    bg_t = (0, 0, 0, 0)

    cx   = S // 2
    cy   = S // 2

    outer_r = int(S * 0.40)
    inner_r = int(S * 0.28)
    arch_cy = int(S * 0.50)

    draw.ellipse([cx - outer_r, arch_cy - outer_r,
                  cx + outer_r, arch_cy + outer_r], fill=fg)
    draw.ellipse([cx - inner_r, arch_cy - inner_r,
                  cx + inner_r, arch_cy + inner_r], fill=bg_t)
    draw.rectangle([0, arch_cy, S, S], fill=bg_t)

    dot_r  = int(S * 0.075)
    dot_cy = arch_cy + int(S * 0.06)
    draw.ellipse([cx - dot_r, dot_cy - dot_r,
                  cx + dot_r, dot_cy + dot_r], fill=fg)

    return img.resize((size, size), Image.LANCZOS)


# ── Render all sizes ──────────────────────────────────────────────────────────
ICON_SIZES = [16, 32, 64, 128, 256, 512, 1024]

print("Generating app icon sizes...")
for s in ICON_SIZES:
    path = os.path.join(OUT, f"AppIcon-{s}.png")
    draw_fermata(s).save(path)
    print(f"  {path}")

tray_path = os.path.join(OUT, "TrayIcon.png")
draw_tray(44).save(tray_path)
print(f"  {tray_path}")

# ── Assemble .icns via iconutil ───────────────────────────────────────────────
iconset = tempfile.mkdtemp(suffix=".iconset")
mapping = {
    16:   "icon_16x16.png",
    32:   "icon_16x16@2x.png",
    32:   "icon_32x32.png",      # duplicate key — 32 wins; that's fine
    64:   "icon_32x32@2x.png",
    128:  "icon_128x128.png",
    256:  "icon_128x128@2x.png",
    256:  "icon_256x256.png",
    512:  "icon_256x256@2x.png",
    512:  "icon_512x512.png",
    1024: "icon_512x512@2x.png",
}
# Use explicit pairs to avoid duplicate-key issues
pairs = [
    (16,   "icon_16x16.png"),
    (32,   "icon_16x16@2x.png"),
    (32,   "icon_32x32.png"),
    (64,   "icon_32x32@2x.png"),
    (128,  "icon_128x128.png"),
    (256,  "icon_128x128@2x.png"),
    (256,  "icon_256x256.png"),
    (512,  "icon_256x256@2x.png"),
    (512,  "icon_512x512.png"),
    (1024, "icon_512x512@2x.png"),
]
for s, name in pairs:
    src = os.path.join(OUT, f"AppIcon-{s}.png")
    shutil.copy(src, os.path.join(iconset, name))

icns_out = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "fermata-ui", "Assets", "AppIcon.icns")
subprocess.run(["iconutil", "-c", "icns", iconset, "-o", icns_out], check=True)
shutil.rmtree(iconset)
print(f"  {icns_out}")
print("\nDone.")

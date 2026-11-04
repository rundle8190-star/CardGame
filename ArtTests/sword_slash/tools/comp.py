import json, sys
from PIL import Image, ImageDraw, ImageFont, ImageOps

CH = "/mnt/project-files/graphics/bake_test/chars_v2"
FX = "/tmp/claude-0/fx"
FONT = ImageFont.truetype("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", 14)
BG = (52, 56, 66, 255)
GROUND = (36, 38, 46, 255)


def load_anim(char, motion, layer=None):
    base = f"{CH}/{char}/ual2"
    j = json.load(open(f"{base}/{motion}.json"))
    sheet = Image.open(f"{base}/{motion}.png").convert("RGBA")
    lay = Image.open(f"{base}/layers/{layer}/{motion}.png").convert("RGBA") if layer else None
    W, H, cols = j["frameWidth"], j["frameHeight"], j["columns"]
    frames = []
    for i in range(j["frames"]):
        box = ((i % cols) * W, (i // cols) * H, (i % cols + 1) * W, (i // cols + 1) * H)
        f = sheet.crop(box)
        if lay:
            f.alpha_composite(lay.crop(box))
        frames.append(f)
    return dict(frames=frames, px=j["pivotX"], py=H - j["pivotY"], hit=j["hitFrame"], W=W, H=H,
                layer=[lay.crop(((i % cols) * W, (i // cols) * H, (i % cols + 1) * W, (i // cols + 1) * H)) for i in range(j["frames"])] if lay else None)


def load_fx(path, scale):
    j = json.load(open(path + ".json"))
    p = j["meta"]["pixelfx"]
    sheet = Image.open(path + ".png").convert("RGBA")
    fw, fh = p["frameSize"]
    frames = []
    for fr in j["frames"]:
        r = fr["frame"]
        f = sheet.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"]))
        frames.append(f.resize((fw * scale, fh * scale), Image.NEAREST))
    return dict(frames=frames, px=p["pivot"][0] * scale, py=p["pivot"][1] * scale)


def tip_x(anim, i):
    # sword tip = right-most weapon pixel at frame i, relative to pivot
    bb = anim["layer"][i].getbbox()
    return bb[2] - anim["px"] if bb else 40


def cell_frames(cfg, n_total, cw, chh, feet_y, ax):
    atk = cfg["atk"]
    hit = atk["hit"]
    start = hit - cfg.get("lead", 1)
    out = []
    tgt = cfg.get("target")
    tx = ax + cfg.get("tdist", 0)
    for t in range(n_total):
        c = Image.new("RGBA", (cw, chh), BG)
        ImageDraw.Draw(c).rectangle((0, feet_y, cw, chh), fill=GROUND)
        if tgt:
            ti = 0 if t < hit else min(t - hit, len(tgt["frames"]) - 1)
            tf = ImageOps.mirror(tgt["frames"][ti])
            c.alpha_composite(tf, (tx - (tgt["W"] - tgt["px"]), feet_y - tgt["py"]))
        ai = min(t, len(atk["frames"]) - 1)
        c.alpha_composite(atk["frames"][ai], (ax - atk["px"], feet_y - atk["py"]))
        for fx in cfg.get("fx", []):
            k = t - start - fx.get("delay", 0)
            if 0 <= k < len(fx["d"]["frames"]):
                ox = fx.get("x", 0)
                c.alpha_composite(fx["d"]["frames"][k], (ax + ox - fx["d"]["px"], feet_y - fx["d"]["py"]))
        d = ImageDraw.Draw(c)
        d.text((6, 4), cfg["label"], font=FONT, fill=(235, 235, 235, 255))
        out.append(c)
    return out


def grid(cells, cols, cw, chh, n, path, scale=1):
    rows = (len(cells) + cols - 1) // cols
    frames = []
    for t in range(n):
        g = Image.new("RGBA", (cols * cw, rows * chh), (20, 20, 24, 255))
        for i, cf in enumerate(cells):
            g.paste(cf[t], ((i % cols) * cw, (i // cols) * chh))
        if scale != 1:
            g = g.resize((g.width * scale, g.height * scale), Image.NEAREST)
        frames.append(g.convert("RGB"))
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=42, loop=0)
    return frames

# Banner source

- `banner.html` lays out all three images: `?layout=banner` (1600 x 400, the README banner), `?layout=social` (1280 x 640, the social preview for GitHub and itch) and `?layout=discord` (1200 x 800, 3:2 for Discord).
- `bunny-pose.png` is the character cut out of a Camera Sweep render (`cam10-render.png`). The spiral route, its markers and the camera are drawn in SVG by the script in `banner.html`: one and a half turns rising from her legs to her shoulders, the back half drawn behind her and the front half over her. The camera sits on the upper front pass, over her chest. The window is the Camera Routes theme in HTML.
- `bunny.png` is the Studio Browser cut-out, kept as an alternative (change the `img` in `banner.html`).
- `shot.py` renders all three at 2x into `docs/images` (needs Python with Playwright and Chrome).

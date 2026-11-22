# Banner source

- `banner.html` lays out both images: `?layout=banner` (1600 x 400, the README banner) and `?layout=social` (1280 x 640, the social preview for GitHub and itch).
- `bunny.png` is the same character cut-out as Studio Browser's banner. The spiral route, its markers and the camera are drawn in SVG by the script in `banner.html` (one turn rising from her feet to her shoulders: the back half is drawn behind her, the front half over her). The window is the Camera Routes theme in HTML.
- `shot.py` renders both at 2x into `docs/images` (needs Python with Playwright and Chrome).

import pathlib
from playwright.sync_api import sync_playwright

# Renders banner.html at 2x into docs/images: banner.png (1600 x 400 layout) and social-preview.png (1280 x 640 layout).
here = pathlib.Path(__file__).parent.resolve()
sizes = {'banner': (1600, 400), 'social': (1280, 640)}
with sync_playwright() as p:
    chrome = pathlib.Path('/usr/bin/google-chrome')
    b = p.chromium.launch(executable_path=str(chrome)) if chrome.exists() else p.chromium.launch()
    for name, (w, h) in sizes.items():
        pg = b.new_page(viewport={'width': w, 'height': h}, device_scale_factor=2)
        pg.goto(f'file://{here}/banner.html?layout={name}')
        pg.wait_for_timeout(500)
        out = here.parent / ('banner.png' if name == 'banner' else 'social-preview.png')
        pg.locator('.stage').screenshot(path=str(out))
    b.close()

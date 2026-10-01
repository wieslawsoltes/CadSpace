"""Native dimensions through actual ribbon, Properties, command, file import/export and recovery APIs."""
import asyncio, json, os
from pathlib import Path
from playwright.async_api import async_playwright
from ui_helpers import bounds, click, command

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width': 1600, 'height': 1000}, device_scale_factor=1)
        await page.add_init_script('delete window.showOpenFilePicker; delete window.showSaveFilePicker;')
        events = []
        page.on('console', lambda e: events.append({'type': e.type, 'text': e.text}))
        page.on('pageerror', lambda e: events.append({'type': 'pageerror', 'text': str(e)}))
        async def fill(name, value):
            await click(page, events, 'dimension.' + name)
            await page.keyboard.press('Control+a'); await page.keyboard.insert_text(value)
        async def checkpoint(predicate, after=0):
            for _ in range(150):
                records = await page.evaluate('''async () => {
                    const latest = new Map();
                    for (const key of JSON.parse(await CadSpaceRecoveryStorage.list())) {
                        const raw = await CadSpaceRecoveryStorage.read(key); if (!raw) continue;
                        const i = raw.indexOf('\\n'), h = JSON.parse(raw.slice(0,i)), project = JSON.parse(raw.slice(i+1));
                        const old = latest.get(h.id);
                        if (!old || old.generation < h.generation) latest.set(h.id, {generation:h.generation,project});
                    }
                    return Array.from(latest.values());
                }''')
                match = next((r for r in records if r['generation'] > after and predicate(r['project']['drawing'])), None)
                if match: return match
                await page.wait_for_timeout(200)
            raise AssertionError('No newer checkpoint contains the expected native dimension edit')
        async def export(binary=False, review=False):
            await click(page, events, 'tab.Output')
            async with page.expect_download(timeout=30000) as pending:
                await click(page, events, 'command.EXPORT_BINARY' if binary else 'command.EXPORT')
                if review: await click(page, events, 'export.dialog.PrimaryButton')
            download = await pending.value
            path = output / ('dimensions-ui-binary.dxf' if binary else 'dimensions-ui.dxf')
            await download.save_as(path); return path
        async def open_file(path, name):
            async with page.expect_file_chooser(timeout=20000) as chooser:
                await click(page, events, 'quick.OPEN')
            await (await chooser.value).set_files({'name':name,'mimeType':'application/dxf','buffer':path.read_bytes()})
            await page.wait_for_function('(name) => document.title.includes(name)', arg=name, timeout=30000)
            await click(page, events, 'file.report.CloseButton')
        try:
            await page.goto(os.environ.get('CADSPACE_URL', 'http://127.0.0.1:8177/CadSpace/'), wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')", timeout=90000); await page.wait_for_timeout(2800)
            await click(page, events, 'quick.NEW'); await click(page, events, 'tab.Annotate'); await click(page, events, 'command.DIMLINEAR')
            await command(page, '0,0', '100,0', '50,20', 'ZOOM', 'SELECTALL')
            original = await checkpoint(lambda d: len(d['entities']) == 1 and d['entities'][0].get('dimensionType') == 0)
            await click(page, events, 'properties.dimension')
            await fill('text', 'Width <>'); await fill('height', '12'); await fill('precision', '3')
            await page.screenshot(path=str(output/'80-dimension-properties.png'), full_page=True)
            await click(page, events, 'dimension.dialog.PrimaryButton')
            applied = await checkpoint(lambda d: len(d['entities']) == 1 and d['entities'][0].get('textOverride') == 'Width <>' and d['entities'][0]['dimensionFormat']['textHeight'] == 12, original['generation'])
            assert applied['project']['version'] == 3
            assert applied['project']['drawing']['entities'][0]['id'] == original['project']['drawing']['entities'][0]['id']
            await command(page, 'DIMEDIT'); await fill('text', 'Must not persist'); await click(page, events, 'dimension.dialog.CloseButton')
            await command(page, 'POINT', '200,200')
            cancelled = await checkpoint(lambda d: len(d['entities']) == 2 and d['entities'][0].get('textOverride') == 'Width <>', applied['generation'])
            await command(page, 'UNDO', 'UNDO')
            await checkpoint(lambda d: len(d['entities']) == 1 and d['entities'][0].get('textOverride') == '', cancelled['generation'])
            # Supported native subtypes, using only actual command input.
            await command(page, 'DIMALIGNED', '0,100', '100,150', '50,180',
                          'DIMROTATED', '30', '200,0', '300,100', '250,120',
                          'DIMANGULAR', '400,0', '450,0', '400,50', '430,30',
                          'DIMANGULAR2', '500,0', '550,0', '500,0', '500,50', '530,30',
                          'DIMORDINATE', 'X', '600,0', '640,40', '640,80',
                          'CIRCLE', '800,0', '20',
                          'DIMRADIUS', '820,0', '850,30',
                          'DIMDIAMETER', '820,0', '850,-30', 'ZOOM')
            created = await checkpoint(lambda d: sum(e['type'] == 'DIMENSION' for e in d['entities']) == 8)
            dimensions = [e for e in created['project']['drawing']['entities'] if e['type'] == 'DIMENSION']
            assert {e['dimensionType'] for e in dimensions} == set(range(7))
            await page.screenshot(path=str(output/'81-native-dimension-tools.png'), full_page=True)
            ascii_path = await export()
            lines = ascii_path.read_text().splitlines()
            pairs = [(int(lines[i]), lines[i+1]) for i in range(0, len(lines), 2)]
            assert pairs.count((0,'DIMENSION')) == 8 and pairs.count((0,'DIMSTYLE')) == 1
            assert (0,'SOLID') in pairs, 'Native arrowheads must be exported, not display-only triangle meshes'
            await open_file(ascii_path, 'dimensions-imported.dxf')
            await command(page, 'DIMEDIT', '50,20')
            await fill('height', '4'); await fill('text', 'Revised <>'); await click(page, events, 'dimension.dialog.PrimaryButton')
            imported = await checkpoint(lambda d: d['name'] == 'dimensions-imported.dxf' and any(e.get('textOverride') == 'Revised <>' for e in d['entities']))
            binary_path = await export(True, True)
            assert binary_path.read_bytes().startswith(b'AutoCAD Binary DXF\r\n\x1a\0')
            assert b'Revised <>' in binary_path.read_bytes()
            await open_file(binary_path, 'dimensions-binary.dxf')
            await command(page, 'POINT', '1000,1000')
            reopened = await checkpoint(lambda d: d['name'] == 'dimensions-binary.dxf' and sum(e['type'] == 'DIMENSION' for e in d['entities']) == 8)
            revised = [e for e in reopened['project']['drawing']['entities'] if e.get('textOverride') == 'Revised <>']
            assert len(revised) == 1 and revised[0]['dimensionFormat']['textHeight'] == 4
            await command(page, '3DORBIT'); await page.wait_for_timeout(500)
            await page.screenshot(path=str(output/'82-dimensions-3d.png'), full_page=True)
            assert not [e for e in events if e['type'] == 'pageerror' or '3D renderer error:' in e.get('text','')], events[-30:]
            (output/'dimension-checkpoint.json').write_text(json.dumps(reopened, indent=2))
            print('PASS native dimension controls, formatting Apply/Cancel/Undo, seven subtypes, actual ASCII/binary exports/reimports, native v3 checkpoints and 3D display')
        finally:
            await page.screenshot(path=str(output/'dimensions-last-state.png'), full_page=True)
            (output/'dimensions-console.json').write_text(json.dumps(events, indent=2)); await browser.close()
asyncio.run(main())

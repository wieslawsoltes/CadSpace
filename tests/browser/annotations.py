"""Real file chooser, annotation Apply/Cancel, Properties and double-click entry points; persisted drawing evidence."""
import asyncio
import json
import os
import re
from pathlib import Path
from playwright.async_api import async_playwright
from ui_helpers import bounds, click, command

# Independently authored DXF fixture: no CadSpace encoder is used to feed the import UI.
ATTRIBUTE_FILE = '0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n5\nB1\n2\nPART\n10\n0\n20\n0\n30\n0\n0\nLINE\n5\nB2\n8\n0\n10\n0\n20\n0\n30\n0\n11\n10\n21\n0\n31\n0\n0\nENDBLK\n5\nB3\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nINSERT\n5\nAB\n8\n0\n66\n1\n2\nPART\n10\n50\n20\n50\n30\n0\n0\nATTRIB\n5\nAC\n8\n0\n10\n50\n20\n50\n30\n0\n40\n2\n1\nPart A\n2\nLABEL\n70\n0\n0\nATTRIB\n5\nAD\n8\n0\n10\n50\n20\n47\n30\n0\n40\n2\n1\nSecret A\n2\nSERIAL\n70\n1\n0\nSEQEND\n5\nAE\n0\nENDSEC\n0\nEOF\n'

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width':1600,'height':1000}, device_scale_factor=1)
        # Exercise the browser's standard input/file-chooser fallback, without a privileged native FS dialog.
        await page.add_init_script('delete window.showOpenFilePicker; delete window.showSaveFilePicker;')
        events=[]
        page.on('console',lambda e: events.append({'type':e.type,'text':e.text}))
        page.on('pageerror',lambda e: events.append({'type':'pageerror','text':str(e)}))
        async def shot(name): await page.screenshot(path=str(output/name),full_page=True)
        async def fill(value):
            await click(page,events,'annotation.value');await page.keyboard.press('Control+a');await page.keyboard.insert_text(value)
        async def current(predicate, after=0):
            for _ in range(120):
                records=await page.evaluate('''async () => {
                    const latest=new Map();
                    for(const key of JSON.parse(await CadSpaceRecoveryStorage.list())){
                        const raw=await CadSpaceRecoveryStorage.read(key);if(!raw)continue;
                        const i=raw.indexOf('\\n');const h=JSON.parse(raw.slice(0,i));const project=JSON.parse(raw.slice(i+1));
                        const old=latest.get(h.id);if(!old || h.generation>old.generation)latest.set(h.id,{generation:h.generation,project});
                    }
                    return Array.from(latest.values());
                }''')
                hit=next((r for r in records if r['generation']>after and predicate(r['project']['drawing'])),None)
                if hit:return hit
                await page.wait_for_timeout(200)
            raise AssertionError('No newer checkpoint with the expected annotation data')
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await page.wait_for_timeout(2800)
            await click(page,events,'quick.NEW')
            await command(page,'TEXT','0,0','Before','ZOOM','QSELECT','TEXT,*,Replace,All')
            initial=await current(lambda d: len(d['entities'])==1 and d['entities'][0].get('text')=='Before')
            await click(page,events,'properties.annotation');await fill('Must not persist')
            await click(page,events,'annotation.dialog.CloseButton')
            # Force a later checkpoint to prove Cancel did not alter the text, rather than accepting an old slot.
            await command(page,'POINT','100,100')
            cancelled=await current(lambda d: len(d['entities'])==2 and d['entities'][0].get('text')=='Before',initial['generation'])
            await command(page,'QSELECT','TEXT,*,Replace,All');await click(page,events,'tab.Annotate');await click(page,events,'command.DDEDIT')
            await fill('After Ω');await shot('73-staged-text-editor.png');await click(page,events,'annotation.dialog.PrimaryButton')
            applied=await current(lambda d: len(d['entities'])==2 and d['entities'][0].get('text')=='After Ω',cancelled['generation'])
            assert applied['project']['drawing']['entities'][0]['id']==initial['project']['drawing']['entities'][0]['id']
            await command(page,'UNDO')
            await current(lambda d: len(d['entities'])==2 and d['entities'][0].get('text')=='Before',applied['generation'])
            # Use the real camera diagnostics for pointer coordinates, not an app-specific edit hook.
            await command(page,'QSELECT','POINT,*,Replace,All','ERASE','QSELECT','POINT,*,Replace,All','ZOOM')
            controls=await bounds(page,events);s=next(e['text'] for e in reversed(events) if 'CADSPACE_UI_STATE:' in e.get('text',''))
            cx=float(re.search(r'cx=([^;]+)',s).group(1));cy=float(re.search(r'cy=([^;]+)',s).group(1));ppu=float(re.search(r'ppu=([^;]+)',s).group(1))
            x,y,w,h=controls['viewport.surface']
            await page.mouse.dblclick(x+w/2+(8-cx)*ppu,y+h/2-(5-cy)*ppu)
            await page.wait_for_timeout(350);assert 'annotation.value' in await bounds(page,events),'Double-click text must open its editor'
            await click(page,events,'annotation.dialog.CloseButton')
            # Import a file through the actual OPEN control and standard browser chooser.
            async with page.expect_file_chooser(timeout=20000) as chooser:
                await click(page,events,'quick.OPEN')
            await (await chooser.value).set_files({'name':'attributes-ui.dxf','mimeType':'application/dxf','buffer':ATTRIBUTE_FILE.encode()})
            await page.wait_for_function("document.title.includes('attributes-ui.dxf')",timeout=30000);await page.wait_for_timeout(600)
            await command(page,'QSELECT','INSERT,*,Replace,All','EATTEDIT')
            await fill('Part Ω / browser');await shot('74-native-attribute-editor.png');await click(page,events,'annotation.dialog.PrimaryButton')
            attr=await current(lambda d: d['name']=='attributes-ui.dxf' and 'Part Ω / browser' in d['entities'][0].get('source',''))
            assert '0\nATTRIB\n' in attr['project']['drawing']['entities'][0]['source']
            assert 'Secret A' in attr['project']['drawing']['entities'][0]['source']
            assert 'Part A' in attr['project']['dxfOriginal'],'Original source must stay intact for Undo and provenance'
            await command(page,'ZOOM');await shot('75-edited-attribute-drawing.png')
            (output/'annotation-checkpoint.json').write_text(json.dumps(attr,indent=2))
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')],events[-30:]
            print('PASS text Apply/Cancel/Undo with newer checkpoints; Properties/ribbon/double-click; actual DXF file import and native block attribute edit')
        finally:
            await shot('annotations-last-state.png');(output/'annotations-console.json').write_text(json.dumps(events,indent=2));await browser.close()
asyncio.run(main())

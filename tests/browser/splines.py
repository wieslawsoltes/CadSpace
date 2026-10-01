"""Real spline controls, native recovery, and DXF exports: no drawing-mutation test hooks."""
import asyncio, json, os
from pathlib import Path
from playwright.async_api import async_playwright
from ui_helpers import bounds, click, command

RAW = '0\nSECTION\n2\nENTITIES\n0\nSPLINE\n5\nAB\n102\n{CS_SPLINE\n1\nKEEP_SPLINE_APPDATA\n40\n999\n102\n}\n100\nAcDbEntity\n8\n0\n100\nAcDbSpline\n70\n12\n71\n2\n72\n6\n73\n3\n74\n0\n40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n41\n1\n41\n0.7071067811865476\n41\n1\n10\n10\n20\n0\n30\n0\n10\n10\n20\n10\n30\n0\n10\n0\n20\n10\n30\n0\n1001\nCS_SPLINE\n1000\nKEEP_SPLINE_XDATA\n0\nENDSEC\n0\nEOF\n'

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width':1600,'height':1000}, device_scale_factor=1)
        await page.add_init_script('delete window.showOpenFilePicker; delete window.showSaveFilePicker;')
        events=[]
        page.on('console',lambda e:events.append({'type':e.type,'text':e.text}))
        page.on('pageerror',lambda e:events.append({'type':'pageerror','text':str(e)}))
        async def fill(name,value):
            await click(page,events,'spline.'+name);await page.keyboard.press('Control+a');await page.keyboard.insert_text(value)
        async def checkpoint(predicate,after=0):
            for _ in range(150):
                records=await page.evaluate('''async () => {
                    const latest=new Map();
                    for(const key of JSON.parse(await CadSpaceRecoveryStorage.list())) {
                        const raw=await CadSpaceRecoveryStorage.read(key);if(!raw)continue;
                        const i=raw.indexOf('\\n'), h=JSON.parse(raw.slice(0,i)), project=JSON.parse(raw.slice(i+1));
                        const old=latest.get(h.id);if(!old || old.generation<h.generation)latest.set(h.id,{generation:h.generation,project});
                    }
                    return Array.from(latest.values());
                }''')
                found=next((r for r in records if r['generation']>after and predicate(r['project']['drawing'])),None)
                if found:return found
                await page.wait_for_timeout(200)
            raise AssertionError('No newer native checkpoint contains the expected spline edit')
        async def export(binary=False):
            await click(page,events,'tab.Output')
            async with page.expect_download(timeout=30000) as pending:
                await click(page,events,'command.EXPORT_BINARY' if binary else 'command.EXPORT')
                await click(page,events,'export.dialog.PrimaryButton')
            path=output/('spline-ui-binary.dxf' if binary else 'spline-ui.dxf')
            await (await pending.value).save_as(path);return path
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await page.wait_for_timeout(2800)
            await click(page,events,'quick.NEW')
            await command(page,'SPLINE','0,0','40,70','80,-20','100,20','','ZOOM','SELECTALL')
            original=await checkpoint(lambda d:len(d['entities'])==1 and d['entities'][0]['type']=='SPLINE' and len(d['entities'][0]['controls'])==4)
            await click(page,events,'tab.Spline');await click(page,events,'command.SPLINEDIT')
            await fill('knot','0.5');await click(page,events,'spline.insert')
            refined=await checkpoint(lambda d:len(d['entities'])==1 and len(d['entities'][0].get('controls',[]))==5,original['generation'])
            await fill('index','2');await click(page,events,'spline.read');await fill('point','45,75,0');await fill('weight','2')
            await page.screenshot(path=str(output/'83-spline-editor.png'),full_page=True)
            await click(page,events,'spline.apply')
            changed=await checkpoint(lambda d:len(d['entities'])==1 and d['entities'][0].get('controls',[[]]*2)[1]==[45,75,0] and d['entities'][0].get('weights',[0]*2)[1]==2,refined['generation'])
            assert changed['project']['drawing']['entities'][0]['id']==original['project']['drawing']['entities'][0]['id']
            await click(page,events,'spline.dialog.CloseButton');await command(page,'UNDO','UNDO')
            undone=await checkpoint(lambda d:len(d['entities'])==1 and len(d['entities'][0].get('controls',[]))==4,changed['generation'])
            assert undone['project']['drawing']['entities'][0]==original['project']['drawing']['entities'][0]
            async with page.expect_file_chooser(timeout=20000) as chooser:
                await click(page,events,'quick.OPEN')
            await (await chooser.value).set_files({'name':'spline-source.dxf','mimeType':'application/dxf','buffer':RAW.encode()})
            await page.wait_for_function("document.title.includes('spline-source.dxf')",timeout=30000);await page.wait_for_timeout(400)
            await command(page,'SELECTALL');await click(page,events,'properties.spline');await fill('knot','0.5');await click(page,events,'spline.insert')
            await fill('index','2');await click(page,events,'spline.read');await fill('point','12,6,0');await fill('weight','2');await click(page,events,'spline.apply')
            await click(page,events,'spline.dialog.CloseButton')
            ascii_path=await export();content=ascii_path.read_text()
            assert 'KEEP_SPLINE_APPDATA' in content and 'KEEP_SPLINE_XDATA' in content
            lines=content.splitlines();pairs=[(int(lines[i]),lines[i+1]) for i in range(0,len(lines),2)]
            assert pairs.count((0,'SPLINE'))==1 and (73,'4') in pairs and (72,'7') in pairs
            binary_path=await export(True);data=binary_path.read_bytes()
            assert data.startswith(b'AutoCAD Binary DXF\r\n\x1a\0') and b'KEEP_SPLINE_APPDATA' in data
            async with page.expect_file_chooser(timeout=20000) as chooser:
                await click(page,events,'quick.OPEN')
            await (await chooser.value).set_files({'name':'spline-roundtrip.dxf','mimeType':'application/dxf','buffer':data})
            await page.wait_for_function("document.title.includes('spline-roundtrip.dxf')",timeout=30000);await page.wait_for_timeout(300)
            await command(page,'POINT','100,100')
            final=await checkpoint(lambda d:d['name']=='spline-roundtrip.dxf' and d['entities'][0].get('controls',[[]]*2)[1]==[12,6,0])
            assert final['project']['drawing']['entities'][0]['weights'][1]==2
            await command(page,'QSELECT','POINT,*,Replace,All','ERASE','ZOOM','3DORBIT')
            await page.screenshot(path=str(output/'84-spline-native-roundtrip.png'),full_page=True)
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')],events[-20:]
            (output/'spline-checkpoint.json').write_text(json.dumps(final,indent=2))
            print('PASS contextual/Properties SPLINEDIT, knot/control/weight edits and Undo, source-preserving ASCII/binary downloads/reimport, 3D display')
        finally:
            await page.screenshot(path=str(output/'splines-last-state.png'),full_page=True)
            (output/'splines-console.json').write_text(json.dumps(events,indent=2));await browser.close()
asyncio.run(main())

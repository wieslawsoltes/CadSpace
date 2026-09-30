"""Actual MTEXT creation/editing and source-backed ASCII/binary downloads, without mutation hooks."""
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright
from ui_helpers import bounds, click, command

RAW = '0\nSECTION\n2\nENTITIES\n0\nMTEXT\n5\nAB\n100\nAcDbEntity\n8\n0\n100\nAcDbMText\n10\n10\n20\n20\n30\n0\n40\n3\n41\n80\n71\n1\n1\nBefore\n11\n1\n21\n0\n31\n0\n1001\nCUSTOM\n1000\nKEEP_SOURCE_METADATA\n0\nENDSEC\n0\nEOF\n'

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width':1600,'height':1000}, device_scale_factor=1)
        await page.add_init_script('delete window.showOpenFilePicker; delete window.showSaveFilePicker;')
        events=[]
        page.on('console',lambda e: events.append({'type':e.type,'text':e.text}))
        page.on('pageerror',lambda e: events.append({'type':'pageerror','text':str(e)}))
        async def fill(id, value):
            await click(page,events,id);await page.keyboard.press('Control+a');await page.keyboard.insert_text(value)
        async def checkpoint(predicate, after=0):
            for _ in range(120):
                records = await page.evaluate('''async () => {
                    const latest=new Map();
                    for(const key of JSON.parse(await CadSpaceRecoveryStorage.list())) {
                        const value=await CadSpaceRecoveryStorage.read(key);if(!value)continue;
                        const i=value.indexOf('\\n'), h=JSON.parse(value.slice(0,i)), project=JSON.parse(value.slice(i+1));
                        const old=latest.get(h.id);if(!old || h.generation>old.generation)latest.set(h.id,{generation:h.generation,project});
                    }
                    return Array.from(latest.values());
                }''')
                hit=next((r for r in records if r['generation']>after and predicate(r['project']['drawing'])),None)
                if hit:return hit
                await page.wait_for_timeout(200)
            (output/'mtext-unexpected-checkpoints.json').write_text(json.dumps(records,indent=2))
            raise AssertionError('No newer checkpoint for the expected MTEXT operation')
        async def download(binary=False):
            await click(page,events,'tab.Output')
            async with page.expect_download(timeout=30000) as request:
                await click(page,events,'command.EXPORT_BINARY' if binary else 'command.EXPORT')
                await click(page,events,'export.dialog.PrimaryButton')
            result=await request.value
            path=output/('mtext-ui-binary.dxf' if binary else 'mtext-ui.dxf')
            await result.save_as(path)
            return path
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await page.wait_for_timeout(2800)
            await click(page,events,'quick.NEW');await click(page,events,'tab.Annotate');await click(page,events,'command.MTEXT')
            await command(page,'10,20','First\\PSecond','ZOOM','SELECTALL','DDEDIT')
            await fill('annotation.value','Edited\nMultiline');await fill('annotation.height','18');await fill('annotation.rotation','15')
            await page.screenshot(path=str(output/'77-mtext-formatting.png'),full_page=True)
            await click(page,events,'annotation.dialog.PrimaryButton')
            applied=await checkpoint(lambda d: len(d['entities'])==1 and d['entities'][0].get('text')=='Edited\nMultiline' and d['entities'][0].get('height')==18 and d['entities'][0].get('rotation')==15)
            # Reopening and applying with no edits must retain both lines and the Undo stack.
            await command(page,'DDEDIT');await click(page,events,'annotation.dialog.PrimaryButton')
            await command(page,'ZOOM')
            await page.screenshot(path=str(output/'79-mtext-reopened.png'),full_page=True)
            await command(page,'DDEDIT');await fill('annotation.value','Cancelled');await fill('annotation.height','90')
            await click(page,events,'annotation.dialog.CloseButton');await command(page,'POINT','100,100')
            cancelled=await checkpoint(lambda d: len(d['entities'])==2 and d['entities'][0].get('text')=='Edited\nMultiline' and d['entities'][0].get('height')==18,applied['generation'])
            await command(page,'UNDO','UNDO')
            await checkpoint(lambda d: len(d['entities'])==1 and d['entities'][0].get('text')=='First\\PSecond' and d['entities'][0].get('height')==12,cancelled['generation'])
            async with page.expect_file_chooser(timeout=20000) as chooser:
                await click(page,events,'quick.OPEN')
            await (await chooser.value).set_files({'name':'mtext-source.dxf','mimeType':'application/dxf','buffer':RAW.encode()})
            await page.wait_for_function("document.title.includes('mtext-source.dxf')",timeout=30000)
            await click(page,events,'file.report.CloseButton')
            await command(page,'SELECTALL','DDEDIT')
            value='Source-backed Ω 😀 '+('x'*600)+'\\PEnd'
            await fill('annotation.value',value);await fill('annotation.height','6');await click(page,events,'annotation.dialog.PrimaryButton')
            ascii=await download()
            lines=ascii.read_text().splitlines();pairs=[(int(lines[i]),lines[i+1]) for i in range(0,len(lines),2)]
            start=next(i for i,pair in enumerate(pairs) if pair==(0,'MTEXT'))
            end=next(i for i in range(start+1,len(pairs)) if pairs[i][0]==0)
            record=pairs[start:end]
            assert (41,'80') in record and (1000,'KEEP_SOURCE_METADATA') in record and (40,'6') in record
            chunks=[v for c,v in record if c in (1,3)]
            assert ''.join(chunks)==value and all(len(v)==250 for c,v in record if c==3)
            assert len(next(v for c,v in record if c==1))<250
            binary=await download(True);data=binary.read_bytes()
            assert data.startswith(b'AutoCAD Binary DXF\r\n\x1a\0') and b'KEEP_SOURCE_METADATA' in data
            assert 'Source-backed Ω 😀'.encode() in data
            assert '*' in await page.title(), 'DXF copies must not clear native dirty state'
            await page.screenshot(path=str(output/'78-mtext-export-complete.png'),full_page=True)
            # Native Save uses the same completion protocol, but only native completion clears dirty state.
            async with page.expect_download(timeout=30000) as request:
                await click(page,events,'quick.SAVE')
            saved=await request.value
            native=output/'mtext-ui.cadspace';await saved.save_as(native)
            project=json.loads(native.read_text(encoding='utf-8-sig'))
            root=project['drawing']['entities'][0]
            assert root['type']=='PLACED' and root['matrix'][3]==[10,20,0]
            text=root['geometry'][0]
            assert text['text']==value and text['height']==6
            assert project['dxfOriginal']==RAW, 'Native save must retain the untouched original DXF'
            await page.wait_for_function("!document.title.includes('*')",timeout=10000)
            # Reopen the actual downloaded project through the real OPEN control, not a mutation hook.
            async with page.expect_file_chooser(timeout=20000) as chooser:
                await click(page,events,'quick.OPEN')
            await (await chooser.value).set_files(str(native))
            await page.wait_for_function("document.title.includes('mtext-ui.cadspace')",timeout=30000)
            assert '*' not in await page.title()
            await page.screenshot(path=str(output/'80-native-save-reopened.png'),full_page=True)
            (output/'mtext-checkpoint.json').write_text(json.dumps(applied,indent=2))
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')],events[-20:]
            print('PASS MTEXT ribbon creation; staged content/height/rotation Apply/Cancel and Undo; real file import; Unicode chunked ASCII/binary downloads retain source metadata; native Save download and real reopen')
        finally:
            try:
                saved=await page.evaluate("""async () => Promise.all(JSON.parse(await CadSpaceRecoveryStorage.list()).map(async key=>({key,value:await CadSpaceRecoveryStorage.read(key)})))""")
                (output/'mtext-recovery-slots.json').write_text(json.dumps(saved,indent=2))
            except Exception as error:
                events.append({'type':'diagnostic','text':str(error)})
            await page.screenshot(path=str(output/'mtext-last-state.png'),full_page=True)
            (output/'mtext-console.json').write_text(json.dumps(events,indent=2));await browser.close()
asyncio.run(main())

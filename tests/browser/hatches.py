"""Actual hatch creation, gradient pixels, staged editor and exported files; no mutation hooks."""
import asyncio, json, os, re
from pathlib import Path
from PIL import Image
from playwright.async_api import async_playwright
from ui_helpers import bounds, click, command

async def main():
    output=Path('artifacts/browser-smoke');output.mkdir(parents=True,exist_ok=True)
    async with async_playwright() as p:
        browser=await p.chromium.launch(headless=True,args=['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
        page=await browser.new_page(viewport={'width':1600,'height':1000},device_scale_factor=1)
        await page.add_init_script('delete window.showOpenFilePicker; delete window.showSaveFilePicker;')
        events=[]
        page.on('console',lambda e:events.append({'type':e.type,'text':e.text}))
        page.on('pageerror',lambda e:events.append({'type':'pageerror','text':str(e)}))
        async def field(name,text):
            await click(page,events,'hatch.'+name);await page.keyboard.press('Control+a');await page.keyboard.insert_text(text)
        async def checkpoint(predicate,after=0):
            for _ in range(150):
                values=await page.evaluate('''async () => {
                    const latest=new Map();
                    for(const key of JSON.parse(await CadSpaceRecoveryStorage.list())) {
                        const raw=await CadSpaceRecoveryStorage.read(key);if(!raw)continue;
                        const i=raw.indexOf('\\n'),h=JSON.parse(raw.slice(0,i)),project=JSON.parse(raw.slice(i+1));
                        const old=latest.get(h.id);if(!old || old.generation<h.generation)latest.set(h.id,{generation:h.generation,project});
                    }return Array.from(latest.values());
                }''')
                found=next((v for v in values if v['generation']>after and predicate(v['project']['drawing']['entities'])),None)
                if found:return found
                await page.wait_for_timeout(200)
            raise AssertionError('No newer checkpoint contains the expected hatch')
        def hatch(entities): return next((e for e in entities if e['type']=='HATCH_REGION'),{})
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await page.wait_for_timeout(2800)
            await click(page,events,'quick.NEW')
            await command(page,'RECTANG','0,0','200,140','CIRCLE','100,70','25','SELECTALL','GRADIENT','ZOOM','QSELECT','HATCH,*,Replace,All')
            before=await checkpoint(lambda e:len(e)==3 and hatch(e).get('gradient') is not None)
            await click(page,events,'properties.hatch')
            await field('first','FF0000');await field('second','0000FF');await field('angle','0');await field('shift','0')
            await page.screenshot(path=str(output/'85-hatch-editor.png'),full_page=True)
            await click(page,events,'hatch.dialog.PrimaryButton')
            edited=await checkpoint(lambda e:hatch(e).get('gradient',{}).get('first')==0xffff0000,before['generation'])
            assert edited['project']['drawing']['entities'][2]['loops'][1][0]['bulge']==1
            await command(page,'QSELECT','POINT,*,Replace,All');await page.keyboard.press('F7')
            controls=await bounds(page,events)
            state=next(e['text'] for e in reversed(events) if 'CADSPACE_UI_STATE:' in e.get('text',''))
            cx=float(re.search(r'cx=([^;]+)',state).group(1));cy=float(re.search(r'cy=([^;]+)',state).group(1));ppu=float(re.search(r'ppu=([^;]+)',state).group(1))
            x,y,w,h=controls['viewport.surface']
            await page.mouse.move(300,956);await page.wait_for_timeout(300)
            image_path=output/'86-gradient-drafting.png';await page.screenshot(path=str(image_path),full_page=True)
            image=Image.open(image_path).convert('RGB')
            def pixel(wx,wy):return image.getpixel((round(x+w/2+(wx-cx)*ppu),round(y+h/2-(wy-cy)*ppu)))
            left=pixel(45,70);right=pixel(155,70);hole=pixel(100,70)
            assert left[0]>left[2]+60 and right[2]>right[0]+60,(left,right)
            assert max(hole)<80,('Gradient must leave island unfilled',hole)
            await command(page,'QSELECT','HATCH,*,Replace,All','HATCHEDIT')
            await field('first','00FF00');await click(page,events,'hatch.dialog.CloseButton')
            await command(page,'POINT','250,180')
            cancelled=await checkpoint(lambda e:len(e)==4 and hatch(e)['gradient']['first']==0xffff0000,edited['generation'])
            await command(page,'UNDO','UNDO')
            await checkpoint(lambda e:len(e)==3 and hatch(e)['gradient']['first']==0xff387bc4,cancelled['generation'])
            await command(page,'REDO','QSELECT','HATCH,*,Replace,All','HATCHGENERATEBOUNDARY')
            await checkpoint(lambda e:len(e)==5 and sum(x['type']=='LWPOLYLINE' for x in e)==3)
            await command(page,'QSELECT','POINT,*,Replace,All')
            await click(page,events,'tab.Output')
            for binary in (False,True):
                async with page.expect_download(timeout=30000) as pending:
                    await click(page,events,'command.EXPORT_BINARY' if binary else 'command.EXPORT')
                path=output/('hatch-ui-binary.dxf' if binary else 'hatch-ui.dxf')
                await (await pending.value).save_as(path)
                if binary:assert path.read_bytes().startswith(b'AutoCAD Binary DXF\r\n\x1a\0')
                else:
                    text=path.read_text();assert '450\n1\n' in text and '470\nLINEAR\n' in text and '421\n16711680\n' in text
            await command(page,'3DORBIT')
            await page.screenshot(path=str(output/'87-gradient-model.png'),full_page=True)
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')],events[-20:]
            (output/'hatch-checkpoint.json').write_text(json.dumps(edited,indent=2))
            print('PASS native gradient creation; preserved circular bulges; 2D red/blue pixels and island; staged Apply/Cancel/Undo; boundary extraction; real ASCII/binary downloads; 3D rendering')
        finally:
            await page.screenshot(path=str(output/'hatches-last-state.png'),full_page=True)
            (output/'hatches-console.json').write_text(json.dumps(events,indent=2));await browser.close()
asyncio.run(main())

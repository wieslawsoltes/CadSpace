"""Use actual ribbon/menu/command input and the PEDIT dialog; verify the real native checkpoint."""
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright
from ui_helpers import bounds, click, command

async def main():
    output = Path('artifacts/browser-smoke'); output.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
        page = await browser.new_page(viewport={'width':1600,'height':1000}, device_scale_factor=1)
        events=[]
        page.on('console', lambda e: events.append({'type':e.type,'text':e.text}))
        page.on('pageerror', lambda e: events.append({'type':'pageerror','text':str(e)}))
        async def shot(name):
            await page.mouse.move(300,956)
            await page.screenshot(path=str(output/name), full_page=True)
        async def checkpoint(predicate, after_generation=0):
            for _ in range(100):
                records=await page.evaluate("""async () => {
                    const values=new Map();
                    for(const key of JSON.parse(await CadSpaceRecoveryStorage.list())) {
                        const value=await CadSpaceRecoveryStorage.read(key);
                        if(value) {
                            const split=value.indexOf('\\n');
                            const header=JSON.parse(value.slice(0,split));
                            const drawing=JSON.parse(value.slice(split+1)).drawing;
                            const old=values.get(header.id);
                            if(!old || old.generation<header.generation) values.set(header.id,{drawing,generation:header.generation});
                        }
                    }
                    return Array.from(values.values());
                }""")
                found=next((d for d in records if d['generation']>after_generation and predicate(d['drawing']['entities'])),None)
                if found is not None:
                    return found['drawing'], found['generation']
                await page.wait_for_timeout(200)
            raise AssertionError('The actual recovery checkpoint did not contain the expected edit')
        async def fill(control,value):
            await click(page,events,control);await page.keyboard.press('Control+a');await page.keyboard.type(value)
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await page.wait_for_timeout(3000)
            await click(page,events,'quick.NEW')
            await command(page,'LINE','-10,0','0,0','','ARC','0,0','7.071067811865,2.928932188135','10,10','SELECTALL')
            await click(page,events,'tab.Modify');await click(page,events,'command.JOIN');await command(page,'ZOOM')
            joined, joined_generation=await checkpoint(lambda e: len(e)==1 and e[0]['type']=='LWPOLYLINE' and len(e[0]['vertices'])==3 and abs(e[0]['vertices'][1]['bulge'])>.4)
            await command(page,'PEDIT','EDIT')
            await fill('polylineDialog.index','2');await click(page,events,'polylineDialog.read')
            await shot('70-polyline-vertex-editor.png')
            await click(page,events,'polylineDialog.insert')
            split, split_generation=await checkpoint(lambda e: len(e)==1 and e[0]['type']=='LWPOLYLINE' and len(e[0]['vertices'])==4, joined_generation)
            assert abs(split['entities'][0]['vertices'][1]['bulge']-.19891236738)<1e-6
            await click(page,events,'polyline.dialog.CloseButton')
            await command(page,'UNDO')
            await checkpoint(lambda e: len(e)==1 and len(e[0].get('vertices',[]))==3, split_generation)
            # A separate known document checks the new native geometry and source picking for MATCHPROP.
            await click(page,events,'quick.NEW')
            await command(page,'CELTYPE','DASHED','MENUBAR','1');await click(page,events,'menubar.Draw');await click(page,events,'menu.Draw.POLYGON')
            await command(page,'6','0,0','I','20','DONUT','10','20','60,0','','CELTYPE','CONTINUOUS','LINE','100,0','150,0','','ZOOM','QSELECT','LINE,*,Replace,All','MATCHPROP','20,0')
            drawing, _=await checkpoint(lambda e: len(e)==3 and sum(x['type']=='LWPOLYLINE' for x in e)==2 and e[2].get('linetype')=='DASHED')
            polygon,donut,line=drawing['entities']
            assert polygon['linetype']=='DASHED' and line['linetype']=='DASHED', 'MATCHPROP must change the destination style'
            assert line['a']==[100,0,0] and line['b']==[150,0,0], 'Matching must preserve geometry'
            assert len(polygon['vertices'])==6 and donut['constantWidth']==5 and len(donut['vertices'])==2
            await shot('71-polygon-donut-drafting.png')
            await command(page,'3DORBIT');await shot('72-donut-model-view.png')
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')],events[-20:]
            (output/'editing-checkpoint.json').write_text(json.dumps(drawing,indent=2))
            print('PASS ribbon JOIN retains analytic bulge; PEDIT Edit splits via actual dialog and Undo; classic-menu POLYGON, native DONUT and MATCHPROP; real checkpoint and 3D rendering')
        finally:
            await shot('editing-last-state.png');(output/'editing-console.json').write_text(json.dumps(events,indent=2));await browser.close()
asyncio.run(main())

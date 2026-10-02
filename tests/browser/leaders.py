"""Actual LEADER UI edits, Undo, native saving and independently audited DXF downloads."""
import asyncio, json, os, re, subprocess, sys
from pathlib import Path
import ezdxf
from playwright.async_api import async_playwright
from ui_helpers import bounds, click, command

async def main():
    output=Path('artifacts/browser-smoke');output.mkdir(parents=True,exist_ok=True)
    subprocess.run([sys.executable,'tests/fixtures/leaders.py'],check=True)
    async with async_playwright() as p:
        browser=await p.chromium.launch(headless=True,args=['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
        page=await browser.new_page(viewport={'width':1600,'height':1000},device_scale_factor=1)
        await page.add_init_script('delete window.showOpenFilePicker; delete window.showSaveFilePicker;')
        events=[]
        page.on('console',lambda e:events.append({'type':e.type,'text':e.text}))
        page.on('pageerror',lambda e:events.append({'type':'pageerror','text':str(e)}))
        async def field(name,text):
            await click(page,events,'leader.'+name);await page.keyboard.press('Control+a');await page.keyboard.insert_text(text)
        async def checkpoint(predicate,after=0):
            for _ in range(150):
                records=await page.evaluate('''async()=>{
                    const latest=new Map();
                    for(const key of JSON.parse(await CadSpaceRecoveryStorage.list())){
                        const raw=await CadSpaceRecoveryStorage.read(key);if(!raw)continue;
                        const i=raw.indexOf('\\n'),h=JSON.parse(raw.slice(0,i)),project=JSON.parse(raw.slice(i+1));
                        const old=latest.get(h.id);if(!old||old.generation<h.generation)latest.set(h.id,{generation:h.generation,project});
                    }return Array.from(latest.values());
                }''')
                found=next((r for r in records if r['generation']>after and predicate(r['project']['drawing'])),None)
                if found:return found
                await page.wait_for_timeout(200)
            raise AssertionError('No newer native checkpoint contains the expected leader')
        try:
            await page.goto(os.environ.get('CADSPACE_URL','http://127.0.0.1:8177/CadSpace/'),wait_until='domcontentloaded')
            await page.wait_for_function("document.title.includes('CadSpace')",timeout=90000);await page.wait_for_timeout(2800)
            await click(page,events,'quick.NEW');await click(page,events,'tab.Annotate');await click(page,events,'command.LEADER')
            await command(page,'0,0','70,50','120,50','','ZOOM','SELECTALL')
            original=await checkpoint(lambda d:len(d['entities'])==1 and d['entities'][0]['type']=='LEADER')
            await click(page,events,'tab.Leader');await click(page,events,'command.LEADEREDIT')
            await field('vertex','5,5,0');await click(page,events,'leader.insert');await field('vertex','40,40,0');await field('size','12')
            await page.screenshot(path=str(output/'88-leader-editor.png'),full_page=True)
            await click(page,events,'leader.dialog.PrimaryButton')
            applied=await checkpoint(lambda d:len(d['entities'])==1 and d['entities'][0].get('arrowSize')==12 and len(d['entities'][0]['vertices'])==4,original['generation'])
            assert applied['project']['version']==5 and applied['project']['drawing']['entities'][0]['vertices'][1]==[40,40,0]
            await click(page,events,'properties.leader');await field('size','99');await click(page,events,'leader.dialog.CloseButton')
            await command(page,'POINT','150,100')
            cancelled=await checkpoint(lambda d:len(d['entities'])==2 and d['entities'][0].get('arrowSize')==12,applied['generation'])
            await command(page,'UNDO','UNDO')
            undone=await checkpoint(lambda d:len(d['entities'])==1 and len(d['entities'][0]['vertices'])==3,cancelled['generation'])
            assert undone['project']['drawing']['entities'][0]==original['project']['drawing']['entities'][0]
            # Double-click a path midpoint, not a vertex/grip.
            controls=await bounds(page,events)
            state=next(e['text'] for e in reversed(events) if 'CADSPACE_UI_STATE:' in e.get('text',''))
            cx=float(re.search(r'cx=([^;]+)',state).group(1));cy=float(re.search(r'cy=([^;]+)',state).group(1));ppu=float(re.search(r'ppu=([^;]+)',state).group(1))
            x,y,w,h=controls['viewport.surface']
            await page.mouse.dblclick(x+w/2+(35-cx)*ppu,y+h/2-(25-cy)*ppu)
            await click(page,events,'leader.dialog.CloseButton')
            async with page.expect_file_chooser(timeout=20000) as chooser:await click(page,events,'quick.OPEN')
            await (await chooser.value).set_files(str(Path('tests/fixtures/independent-leaders.dxf').resolve()))
            await page.wait_for_function("document.title.includes('independent-leaders.dxf')",timeout=30000)
            await click(page,events,'file.report.CloseButton')
            await command(page,'QSELECT','LEADER,*,Replace,All');await click(page,events,'properties.leader')
            await field('vertex','5,5,0');await click(page,events,'leader.dialog.PrimaryButton')
            # Save through the normal host; verify the new native format, then reopen the downloaded file.
            async with page.expect_download(timeout=30000) as pending:await click(page,events,'quick.SAVE')
            native=output/'leader-ui.cadspace';await (await pending.value).save_as(native)
            project=json.loads(native.read_text());assert project['version']==5
            async with page.expect_file_chooser(timeout=20000) as chooser:await click(page,events,'quick.OPEN')
            await (await chooser.value).set_files(str(native.resolve()))
            await page.wait_for_function("document.title.includes('leader-ui.cadspace')",timeout=30000)
            for binary in (False,True):
                await click(page,events,'tab.Output')
                async with page.expect_download(timeout=30000) as pending:
                    await click(page,events,'command.EXPORT_BINARY' if binary else 'command.EXPORT')
                    await click(page,events,'export.dialog.PrimaryButton')
                target=output/('leader-ui-binary.dxf' if binary else 'leader-ui.dxf');await (await pending.value).save_as(target)
                doc=ezdxf.readfile(target);report=doc.audit();assert not report.errors and not report.fixes,(report.errors,report.fixes)
                leader=doc.modelspace().query('LEADER')[0]
                assert tuple(leader.vertices[0])==(5,5,0) and leader.dxf.dimstyle=='CS_LEADER'
                assert leader.get_app_data('CS_LEADER_DATA')[0].value=='LEADER_APPDATA'
                assert leader.get_xdata('CS_LEADER_DATA')[0].value=='LEADER_XDATA'
                assert doc.entitydb[leader.dxf.annotation_handle].dxftype()=='MTEXT'
            await command(page,'3DORBIT');await page.wait_for_timeout(600)
            await page.screenshot(path=str(output/'89-leader-native-model.png'),full_page=True)
            frames=[e['text'] for e in events if 'CADSPACE_GPU_FRAME:' in e.get('text','')]
            assert any(int(re.search(r'triangles=(\d+)',f).group(1))>=1 for f in frames),frames
            assert not [e for e in events if e['type']=='pageerror' or '3D renderer error:' in e.get('text','')],events[-20:]
            (output/'leader-checkpoint.json').write_text(json.dumps(applied,indent=2))
            print('PASS ribbon LEADER; staged vertex/split/size Apply/Cancel/Undo; Properties and double-click; native Save/reopen; independently audited source-preserving ASCII/binary downloads; GPU arrow geometry')
        finally:
            await page.screenshot(path=str(output/'leaders-last-state.png'),full_page=True)
            (output/'leaders-console.json').write_text(json.dumps(events,indent=2));await browser.close()
asyncio.run(main())

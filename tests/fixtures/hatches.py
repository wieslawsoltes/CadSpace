"""Independent gradient and native attribute export audits. ezdxf is test-only."""
from pathlib import Path
import sys
import ezdxf
ROOT = Path(__file__).resolve().parent
NAMES = ('LINEAR','CYLINDER','INVCYLINDER','SPHERICAL','INVSPHERICAL','HEMISPHERICAL','INVHEMISPHERICAL','CURVED','INVCURVED')

def generate():
    doc = ezdxf.new('R2013')
    doc.appids.new('CS_HATCH')
    for i, name in enumerate(NAMES):
        h = doc.modelspace().add_hatch()
        h.paths.add_polyline_path([(i*30,0),(i*30+20,0),(i*30+20,20),(i*30,20)], is_closed=True)
        h.paths.add_polyline_path([(i*30+7,7),(i*30+13,7),(i*30+13,13),(i*30+7,13)], is_closed=True, flags=0)
        h.set_gradient(color1=(20,60,200), color2=(240,180,40), rotation=30, centered=.25, name=name)
        h.set_xdata('CS_HATCH', [(1000,'kept gradient data')])
    doc.saveas(ROOT/'independent-hatches.dxf')
    assert not doc.audit().errors
    print('Generated nine independent gradient hatches')

def audit(folder):
    folder=Path(folder)
    for name in ('hatches.dxf','hatches-binary.dxf'):
        doc=ezdxf.readfile(folder/name); a=doc.audit()
        assert not a.errors and not a.fixes, (name,a.errors,a.fixes)
        hatches=list(doc.modelspace().query('HATCH'))
        assert len(hatches)==9
        for h, expected in zip(hatches,NAMES):
            g=h.gradient
            assert g is not None and g.name==expected
            assert tuple(g.color1)==(20,60,200) and tuple(g.color2)==(240,180,40)
            assert abs(g.rotation-30)<1e-10 and g.centered==.25
            assert len(h.paths)==2 and not h.dxf.associative
        print('PASS independent export',name,'zero errors, zero repairs')
    for name in ('attributes-native.dxf','attributes-native-binary.dxf'):
        doc=ezdxf.readfile(folder/name); a=doc.audit()
        assert not a.errors and not a.fixes, (name,a.errors,a.fixes)
        insert=doc.modelspace().query('INSERT')[0]
        assert insert.get_attrib('LABEL').dxf.text=='Native Ω'
        assert insert.get_attrib('SERIAL').dxf.text=='Hidden A'
        assert insert.get_attrib('CONST').dxf.text=='Fixed'
        assert list(insert.get_attrib('LABEL').get_app_data('CS_EDIT'))[0].value=='retained attribute application note'
        assert list(insert.get_attrib('LABEL').get_xdata('CS_EDIT'))[0].value=='retained attribute extended note'
        print('PASS independent export',name,'zero errors, zero repairs')

if __name__=='__main__':
    generate() if len(sys.argv)==1 else audit(sys.argv[1])

"""Independent dimension fixture generation and export auditing; no CadSpace code is used here."""
from pathlib import Path
import sys, math
import ezdxf
from ezdxf.math import UCS, Vec3
ROOT = Path(__file__).resolve().parent

def generate():
    for tilted in (False, True):
        doc = ezdxf.new('R2013', setup=True)
        doc.appids.new('CS_DIM')
        m = doc.modelspace()
        style = {'dimtxt': 3, 'dimasz': 2, 'dimgap': 1, 'dimdec': 3, 'dimlfac': 1}
        dims = [
            m.add_linear_dim(base=(30,20), p1=(0,0), p2=(80,10), angle=0, override=style),
            m.add_aligned_dim(p1=(0,0), p2=(60,80), distance=20, override=style),
            m.add_angular_dim_2l(base=(30,30), line1=((0,0),(60,0)), line2=((0,0),(0,60)), override=style),
            m.add_diameter_dim(center=(20,20), radius=10, angle=0, override=style),
            m.add_radius_dim(center=(20,20), radius=10, angle=0, override=style),
            m.add_angular_dim_3p(base=(30,30), center=(0,0), p1=(60,0), p2=(0,60), override=style),
            m.add_ordinate_dim(feature_location=(30,40), offset=(0,20), dtype=1, override=style),
            m.add_ordinate_dim(feature_location=(30,40), offset=(20,0), dtype=0, override=style),
        ]
        ucs = UCS(origin=(10,20,30), ux=(1,0,0), uz=(0,1,0)) if tilted else None
        for i, override in enumerate(dims):
            override.render(ucs=ucs)
            # ezdxf implements add_aligned_dim as a rotated dimension. Exercise the actual type-1 record too.
            if i == 1:
                override.dimension.dxf.dimtype = (override.dimension.dxf.dimtype & ~15) | 1
            # ezdxf 1.4.4 emits angular-2 defpoint5 as WCS; Autodesk specifies group 16 in OCS.
            if tilted and i == 2:
                override.dimension.dxf.defpoint5 = override.dimension.ocs().from_wcs(override.dimension.dxf.defpoint5)
            override.dimension.set_xdata('CS_DIM', [(1000, 'dimension metadata '+str(i))])
        path = ROOT / ('independent-dimensions-tilted.dxf' if tilted else 'independent-dimensions.dxf')
        doc.saveas(path)
        report = doc.audit(); assert not report.errors and not report.fixes, (report.errors, report.fixes)
        print('Generated',path.name, 'with ezdxf', ezdxf.__version__)

def audit(folder):
    for kind in ('new','imported','tilted','nested'):
        for binary in (False,True):
            name='dimensions-'+kind+('-binary' if binary else '')+'.dxf'
            doc=ezdxf.readfile(Path(folder)/name); audit=doc.audit()
            assert not audit.errors and not audit.fixes, (name,audit.errors,audit.fixes)
            dims=list(doc.modelspace().query('DIMENSION'))
            if kind=='nested':
                dims=list(doc.blocks['Dimensions'].query('DIMENSION'))
            assert len(dims)==8,(name,len(dims))
            assert {d.dimtype for d in dims}==set(range(7)),name
            pictures=set()
            for d in dims:
                assert d.dxf.geometry in doc.blocks and d.dxf.dimstyle in doc.dimstyles
                picture=doc.blocks[d.dxf.geometry]; assert len(picture)>0
                assert picture.block.dxf.flags & 1, 'Dimension pictures must be anonymous blocks'
                assert d.dxf.geometry not in pictures; pictures.add(d.dxf.geometry)
                assert d.dxf.owner in doc.entitydb
                assert all(e.dxf.owner==picture.block_record_handle for e in picture)
                assert list(d.virtual_entities()),name
            if kind=='new':
                expected=[80,100,90,20,10,90,30,40]
                for i,d in enumerate(dims):
                    measured=d.get_measurement()
                    if d.dimtype==6: measured=abs(measured.x if d.dxf.dimtype&64 else measured.y)
                    assert abs(measured-expected[i])<1e-6,(name,i,measured)
                assert sum(len(b.query('SOLID')) for b in doc.blocks if b.name in pictures)>=10
                assert len({d.dxf.dimstyle for d in dims})==1, 'Equal formats must share a DIMSTYLE'
            if kind in ('imported','tilted'):
                assert dims[0].dxf.text=='Edited <>'
                assert doc.dimstyles.get(dims[0].dxf.dimstyle).dxf.dimtxt==4
                for i,d in enumerate(dims[1:],1):
                    assert d.get_xdata('CS_DIM')[0].value=='dimension metadata '+str(i)
            if kind=='tilted':
                assert all(Vec3(d.dxf.extrusion).isclose((0,1,0)) for d in dims)
            print('PASS independent dimension audit',name,'zero errors, zero repairs')
if __name__=='__main__':
    generate() if len(sys.argv)==1 else audit(sys.argv[1])

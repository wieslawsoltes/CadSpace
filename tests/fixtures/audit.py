"""Independently audit CadSpace exports after the advanced suite."""
import os
from pathlib import Path
import ezdxf

root = Path(os.environ['CADSPACE_EXCHANGE_OUTPUT'])
for name in ('canonical.dxf', 'canonical-binary.dxf'):
    doc = ezdxf.readfile(root / name)
    result = doc.audit()
    assert not result.errors, [(e.code, e.message) for e in result.errors]
    assert not result.fixes, [(e.code, e.message) for e in result.fixes]
    assert 'Sheet A' in doc.layouts.names(), 'Paper-space layout must survive exchange'
    assert len(doc.layouts.get('Sheet A')) == 1
    # Native attributed INSERT now retains its ATTRIB sequence instead of adding a separate TEXT root.
    assert len(doc.modelspace()) == 15
    inserts = list(doc.modelspace().query('INSERT'))
    assert len(inserts) == 1 and len(inserts[0].attribs) == 1
    assert inserts[0].get_attrib('LABEL').dxf.text == 'Part A'
    assert len(doc.modelspace()) + sum(len(i.attribs) for i in inserts) == 16
    assert list(doc.modelspace().query('SPLINE')), 'Rational spline lost'
    assert list(doc.modelspace().query('HATCH')), 'Native hatch lost'
    assert list(doc.modelspace().query('MESH')), 'Native indexed mesh lost'
    print(f'PASS ezdxf {ezdxf.__version__}: {name}, zero audit errors or repairs')

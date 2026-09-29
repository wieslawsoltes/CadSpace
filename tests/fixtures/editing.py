"""Independent fixture generation and audit for editable source records and analytic drafting tools."""
from pathlib import Path
import sys
import ezdxf

root = Path(__file__).resolve().parent

def generate():
    doc = ezdxf.new('R2013')
    doc.appids.new('CS_EDIT')
    line = doc.modelspace().add_line((1, 2, 3), (10, 20, 30))
    line.set_app_data('CS_EDIT', [(1, 'retained application note'), (40, 123.5)])
    line.set_xdata('CS_EDIT', [(1000, 'retained extended note'), (1010, (9, 8, 7)), (1070, 12)])
    poly = doc.modelspace().add_lwpolyline([(0, 0, 2, 4, .25), (100, 0, 0, 0, 0)], format='xyseb')
    # Native vertex identifiers are added through the export/import tag stream independently of CadSpace.
    block = doc.blocks.new('PART')
    block.add_line((0, 0), (10, 0))
    block.add_attdef('LABEL', (0, 0), height=2)
    insert = doc.modelspace().add_blockref('PART', (50, 50))
    insert.add_attrib('LABEL', 'Part A', (50, 50), {'height': 2})
    path = root / 'independent-editing.dxf'
    doc.saveas(path)
    # Keep syntax independent and preserve the library-produced ownership/table structure.
    text = path.read_text()
    from ezdxf.lldxf.tagwriter import TagCollector
    collector = TagCollector(dxfversion=doc.dxfversion); poly.export_dxf(collector)
    tags = list(collector.tags)
    from io import StringIO
    from ezdxf.lldxf.tagwriter import TagWriter
    def encode(tags):
        stream = StringIO(); writer = TagWriter(stream)
        for tag in tags: writer.write_tag(tag)
        return stream.getvalue()
    old = encode(tags)
    from ezdxf.lldxf.types import DXFTag
    # Identifiers follow their own vertex point, not the preceding vertex.
    new = []
    for tag in tags:
        new.append(tag)
        if tag.code == 20:
            new.append(DXFTag(91, 301 + sum(t.code == 91 for t in new)))
    assert old in text
    path.write_text(text.replace(old, encode(new), 1))
    assert not ezdxf.readfile(path).audit().has_errors
    assert '301' in path.read_text() and '302' in path.read_text()
    print('Generated independent editing fixture with ezdxf', ezdxf.__version__)

def audit(folder):
    folder = Path(folder)
    for name in ('edited.dxf', 'edited-binary.dxf', 'tools.dxf', 'tools-binary.dxf'):
        doc = ezdxf.readfile(folder / name); report = doc.audit()
        assert not report.errors and not report.fixes, (name, report.errors, report.fixes)
        model = doc.modelspace()
        if name.startswith('edited'):
            line = model.query('LINE')[0]
            assert tuple(line.dxf.end) == (60, 70, 80)
            assert list(line.get_app_data('CS_EDIT'))[0].value == 'retained application note'
            assert list(line.get_xdata('CS_EDIT'))[0].value == 'retained extended note'
            assert model.query('LWPOLYLINE')[0].dxf.const_width == 8
            assert model.query('INSERT')[0].get_attrib('LABEL').dxf.text == 'Part A'
        else:
            assert len(model.query('LWPOLYLINE')) == 3
            joined, polygon, donut = list(model)
            assert abs(joined.get_points('b')[1][0] - .41421356237309503) < 1e-12
            assert polygon.closed and len(polygon) == 6
            assert donut.closed and len(donut) == 2 and donut.dxf.const_width == 5
        print('PASS independent editing audit', name, 'zero errors, zero repairs')

if __name__ == '__main__':
    generate() if len(sys.argv) == 1 else audit(sys.argv[1])

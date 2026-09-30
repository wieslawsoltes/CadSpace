"""Independent MTEXT fixtures and semantic audits; ezdxf is test-only."""
from pathlib import Path
import sys
import ezdxf

ROOT = Path(__file__).resolve().parent
VALUE = 'Unicode Ω 😀 ' + 'x' * 700 + '\\PEnd'

def generate():
    doc = ezdxf.new('R2013')
    doc.appids.new('CS_MTEXT')
    text = doc.modelspace().add_mtext('Before', dxfattribs={
        'insert': (10, 20, 30), 'char_height': 3, 'width': 80,
        'attachment_point': 5, 'extrusion': (0, 1, 0), 'text_direction': (1, 0, 0)})
    text.set_bg_color((20, 30, 40))
    text.set_app_data('CS_MTEXT', [(1, 'keep application text'), (3, 'keep application chunks')])
    text.set_xdata('CS_MTEXT', [(1000, 'keep extended text')])
    doc.saveas(ROOT / 'independent-mtext.dxf')
    assert not doc.audit().errors
    print('Generated independent MTEXT fixture with ezdxf', ezdxf.__version__)

def audit(folder):
    folder = Path(folder)
    for kind in ('edited', 'placed', 'created'):
        for binary in (False, True):
            name = 'mtext-' + kind + ('-binary' if binary else '') + '.dxf'
            doc = ezdxf.readfile(folder / name)
            report = doc.audit()
            assert not report.errors and not report.fixes, (name, report.errors, report.fixes)
            text = doc.modelspace().query('MTEXT')[0]
            assert text.text == VALUE, (name, len(text.text))
            if kind == 'edited':
                assert text.dxf.width == 80 and text.dxf.attachment_point == 5
                assert text.dxf.bg_fill_true_color == 0x141e28
                assert text.dxf.char_height == 5 and tuple(text.dxf.insert) == (10, 20, 30)
                assert tuple(text.dxf.text_direction) == (1, 0, 0) and tuple(text.dxf.extrusion) == (0, 1, 0)
                assert text.get_app_data('CS_MTEXT')[0].value == 'keep application text'
                assert text.get_app_data('CS_MTEXT')[1].value == 'keep application chunks'
                assert text.get_xdata('CS_MTEXT')[0].value == 'keep extended text'
            elif kind == 'placed':
                assert text.dxf.char_height == 6 and tuple(text.dxf.insert) == (10, 20, 30)
                assert tuple(text.dxf.text_direction) == (1, 0, 0) and tuple(text.dxf.extrusion) == (0, 1, 0)
            else:
                assert text.dxf.char_height == 12
            from ezdxf.lldxf.tagwriter import TagCollector
            tags = TagCollector.dxftags(text, dxfversion=doc.dxfversion)
            from ezdxf.lldxf.extendedtags import ExtendedTags
            content = next(s for s in ExtendedTags(tags).subclasses if s and s[0].value == 'AcDbMText')
            chunks = [t.value for t in content if t.code in (1, 3)]
            assert max(map(len, chunks)) <= 250 and ''.join(chunks) == VALUE
            print('PASS independent MTEXT audit', name, 'zero errors, zero repairs')

if __name__ == '__main__':
    generate() if len(sys.argv) == 1 else audit(sys.argv[1])

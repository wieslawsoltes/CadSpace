"""Independent rational spline fixture and actual-curve verification for edited DXF output."""
from pathlib import Path
from math import sqrt
import sys
import ezdxf

ROOT = Path(__file__).resolve().parent

def generate():
    doc = ezdxf.new('R2013')
    doc.appids.new('CS_SPLINE')
    spline = doc.modelspace().add_rational_spline(
        [(10, 0, 0), (10, 10, 0), (0, 10, 0)], [1, sqrt(.5), 1], degree=2,
        knots=[0, 0, 0, 1, 1, 1], dxfattribs={'extrusion': (0, 0, 1), 'knot_tolerance': 1e-8, 'control_point_tolerance': 1e-9})
    spline.dxf.flags = 12
    spline.set_app_data('CS_SPLINE', [(1, 'keep spline application data'), (40, 999), (10, (99, 88, 77))])
    spline.set_xdata('CS_SPLINE', [(1000, 'keep spline extended data'), (1070, 7)])
    doc.saveas(ROOT / 'independent-spline.dxf')
    report = doc.audit()
    assert not report.errors and not report.fixes
    print('Generated independent spline fixture with ezdxf', ezdxf.__version__)

def audit(folder):
    original = ezdxf.readfile(ROOT / 'independent-spline.dxf').modelspace().query('SPLINE')[0]
    before = original.construction_tool()
    for kind in ('refined', 'edited'):
        for binary in (False, True):
            path = Path(folder) / ('spline-' + kind + ('-binary' if binary else '') + '.dxf')
            doc = ezdxf.readfile(path)
            report = doc.audit()
            assert not report.errors and not report.fixes, (path, report.errors, report.fixes)
            spline = doc.modelspace().query('SPLINE')[0]
            assert len(spline.control_points) == 4 and len(spline.knots) == 7
            assert spline.dxf.degree == 2 and spline.dxf.flags == 12
            assert tuple(spline.dxf.extrusion) == (0, 0, 1)
            assert spline.dxf.knot_tolerance == 1e-8 and spline.dxf.control_point_tolerance == 1e-9
            assert spline.get_app_data('CS_SPLINE')[0].value == 'keep spline application data'
            assert spline.get_xdata('CS_SPLINE')[0].value == 'keep spline extended data'
            if kind == 'refined':
                after = spline.construction_tool()
                for i in range(501):
                    assert before.point(i / 500).distance(after.point(i / 500)) < 1e-10
            else:
                assert tuple(spline.control_points[1]) == (12, 6, 0) and spline.weights[1] == 2
                assert spline.dxf.color == 4
            print('PASS independent spline audit', path.name, 'zero errors, zero repairs')

if __name__ == '__main__':
    generate() if len(sys.argv) == 1 else audit(sys.argv[1])

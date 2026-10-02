"""Independent LEADER fixtures and native semantic audits. ezdxf is test-only."""
from pathlib import Path
import sys
import ezdxf

ROOT = Path(__file__).resolve().parent

def generate():
    doc = ezdxf.new('R2013')
    doc.appids.new('CS_LEADER_DATA')
    doc.dimstyles.new('CS_LEADER', dxfattribs={'dimasz':4, 'dimscale':2, 'dimgap':1.5, 'dimtad':1})
    # Reserve this name to exercise collision-free canonical style generation.
    doc.dimstyles.new('CadSpaceLeader', dxfattribs={'dimasz':91})
    text = doc.modelspace().add_mtext('Leader annotation Ω', dxfattribs={'insert':(40,10,0), 'char_height':3, 'width':20})
    leader = doc.modelspace().add_leader([(0,0,0),(20,10,0),(40,10,0)], dimstyle='CS_LEADER',
        override={'dimasz':3, 'dimgap':.5}, dxfattribs={'annotation_handle':text.dxf.handle,
            'annotation_type':0, 'has_hookline':1, 'hookline_direction':1, 'text_width':20, 'text_height':3})
    leader.set_app_data('CS_LEADER_DATA', [(1,'LEADER_APPDATA'),(10,(999,998,997))])
    leader.set_xdata('CS_LEADER_DATA', [(1000,'LEADER_XDATA'),(1010,(77,88,99))])
    doc.saveas(ROOT/'independent-leaders.dxf')
    report=doc.audit()
    assert not report.errors and not report.fixes, (report.errors, report.fixes)
    print('Generated independent attributed LEADER with DIMSTYLE overrides')

def audit(folder):
    folder=Path(folder)
    for name in ('leaders-edited','leaders-created','leaders-detached'):
        for binary in (False,True):
            path=folder/(name+('-binary' if binary else '')+'.dxf')
            doc=ezdxf.readfile(path);report=doc.audit()
            assert not report.errors and not report.fixes,(path,report.errors,report.fixes)
            leaders=list(doc.modelspace().query('LEADER'))
            if name=='leaders-edited':
                assert len(leaders)==1
                leader=leaders[0]
                assert [tuple(p) for p in leader.vertices]==[(0,0,0),(25,15,0),(40,10,0)]
                assert leader.dxf.has_arrowhead==0 and leader.dxf.dimstyle=='CS_LEADER'
                assert leader.dxf.color==4
                assert leader.get_app_data('CS_LEADER_DATA')[0].value=='LEADER_APPDATA'
                assert leader.get_xdata('CS_LEADER_DATA')[0].value=='LEADER_XDATA'
                target=doc.entitydb[leader.dxf.annotation_handle]
                assert target.dxftype()=='MTEXT' and target.text=='Leader annotation Ω'
                assert leader.override().get('dimasz')==3 and leader.override().get('dimscale')==2
            elif name=='leaders-created':
                assert len(leaders)==2
                assert all(l.dxf.dimstyle.startswith('CadSpaceLeader') for l in leaders)
                assert all(l.dxf.text_height==1 and l.dxf.text_width==1 for l in leaders[:1])
                assert leaders[1].dxf.text_height==2 and leaders[1].dxf.text_width==2
                assert leaders[0].override().get('dimasz')==5
                assert [tuple(p) for p in leaders[1].vertices]==[(10,20,30),(30,20,30),(30,20,50)]
                assert leaders[1].override().get('dimasz')==10
                paper=list(doc.layout('Layout1').query('LEADER'));assert len(paper)==1
                assert len(doc.blocks['LEADERS'].query('LEADER'))==1
                # All canonical leaders share one style, but retain their individual overrides.
                assert len({e.dxf.dimstyle for e in doc.entitydb.values() if e.dxftype()=='LEADER'})==1
            else:
                assert len(leaders)==1
                leader=leaders[0]
                assert leader.dxf.annotation_handle=='0' and leader.dxf.annotation_type==3
                assert leader.override().get('dimasz')==6
                assert leader.dxf.dimstyle!='CadSpaceLeader', 'Existing source style must not be overwritten'
                assert doc.dimstyles.get('CadSpaceLeader').dxf.dimasz==91
            print('PASS independent LEADER audit',path.name,'zero errors, zero repairs')

if __name__=='__main__':
    generate() if len(sys.argv)==1 else audit(sys.argv[1])

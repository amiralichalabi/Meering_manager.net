"""Deterministic sample data for the sync-file tests and golden vectors (fixed uuids and times, no randomness)."""
from datetime import datetime

from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker

from backend import models as m
from backend.db import Base

T0 = datetime(2026, 11, 1, 10, 0, 0)


def new_session(path):
    engine = create_engine(f'sqlite:///{path}')
    Base.metadata.create_all(engine)
    return sessionmaker(bind=engine, autoflush=False, expire_on_commit=False)()


def populate(s, tag='', pad=0):
    """A small but complete data set. `tag` makes every uuid distinct per device; `pad` burns integer ids so two
    databases never share ids by luck."""
    u = lambda name: f'{tag}{name}'
    for i in range(pad):
        s.add(m.Person(name=f'pad{i}', uuid=u(f'pad-person-{i}'), created_at=T0, updated_at=T0))
        s.add(m.Project(name=f'pad{i}', uuid=u(f'pad-project-{i}'), created_at=T0, updated_at=T0))
    s.flush()
    cat = m.PersonCategory(name='کارمند', uuid=u('cat'), updated_at=T0)
    s.add(cat); s.flush()
    ali = m.Person(name='علی', role='مدیر', category_id=cat.id, category='کارمند', uuid=u('ali'), created_at=T0, updated_at=T0)
    sara = m.Person(name='سارا', uuid=u('sara'), created_at=T0, updated_at=T0)
    nima = m.Person(name='نیما', is_active=0, uuid=u('nima'), created_at=T0, updated_at=T0)
    s.add_all([ali, sara, nima]); s.flush()
    team = m.Team(name='تیم ۱', manager_id=ali.id, uuid=u('team'), updated_at=T0); team.members = [sara, nima]
    s.add_all([team, m.Location(name='اتاق ۱', uuid=u('loc'), updated_at=T0)])
    proj = m.Project(name='پروژه', manager_id=ali.id, progress=40, uuid=u('proj'), created_at=T0, updated_at=T0); proj.members = [sara]
    s.add(proj); s.flush()
    meet = m.Meeting(title='جلسه ۱', meeting_date='1405/08/20', time='10:30', summary='خلاصه', project_id=proj.id,
                     uuid=u('meet1'), created_at=T0, updated_at=T0)
    meet.attendees = [ali, sara]
    s.add(meet); s.flush()
    other = m.Meeting(title='جلسه ۲', meeting_date='1405/08/21', uuid=u('meet2'), created_at=T0, updated_at=T0)
    s.add(other); s.flush()
    parent = m.Action(subject='اقدام والد', meeting_id=meet.id, project_id=proj.id, owner_id=ali.id, assignee_id=sara.id,
                      due='1405/09/01', progress=50, status='در حال انجام', uuid=u('act-parent'), created_at=T0, updated_at=T0)
    parent.collaborators = [nima]
    s.add(parent); s.flush()
    child = m.Action(subject='زیر وظیفه', parent_id=parent.id, uuid=u('act-child'), created_at=T0, updated_at=T0)
    s.add(child); s.flush()
    other.linked_actions = [parent]
    s.add_all([
        m.PreviousItem(meeting_id=other.id, activity='قبلی', owner_id=ali.id, owner_name_snapshot='علی', progress=30, uuid=u('prev'), updated_at=T0),
        m.ProgressHistory(action_id=parent.id, update_date='1405/08/20', old_progress=0, new_progress=50, note='n', uuid=u('hist'), created_at=T0, updated_at=T0),
        m.ActionNote(action_id=parent.id, body='یادداشت', uuid=u('note'), created_at=T0, updated_at=T0),
        m.ChecklistItem(action_id=parent.id, title='مورد', is_done=True, sort_order=2, uuid=u('check'), created_at=T0, updated_at=T0),
        m.Comment(action_id=parent.id, body='نظر', uuid=u('comment-a'), created_at=T0, updated_at=T0),
        m.Comment(meeting_id=meet.id, body='نظر جلسه', uuid=u('comment-m'), created_at=T0, updated_at=T0),
    ])
    s.commit()
    return dict(ali=ali, parent=parent, meet=meet)

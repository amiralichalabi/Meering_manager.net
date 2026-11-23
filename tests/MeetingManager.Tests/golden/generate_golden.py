"""Generate the golden test vectors that pin down the Jalali calendar and action-status rules.

The Python backend is the reference implementation. Other implementations (the Android app's Kotlin
domain layer, the JavaScript in the browser UI) load the generated ``*.json`` files and must reproduce
every expected value exactly.  Run ``python tests/golden/generate_golden.py`` after changing the rules.
"""
import datetime
import json
import os
import random
import sys
import tempfile
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
# Importing backend.main opens a database: keep that away from the real data/ folder.
os.environ.setdefault('MEETING_MANAGER_DATA_DIR', tempfile.mkdtemp(prefix='mm_golden_'))
sys.path.insert(0, str(ROOT))
from backend import jalali  # noqa: E402
from backend.status import effective_status  # noqa: E402

OUT = Path(__file__).resolve().parent


def jalali_vectors():
    pairs = set()
    for jy in range(1380, 1451):                       # calendar edges of every year
        for jm, jd in ((1, 1), (1, 2), (6, 31), (7, 1), (12, 29), (12, 30)):
            if jalali.is_valid(jy, jm, jd):
                pairs.add((jy, jm, jd))
        pairs.add((jy, 12, jalali.days_in_month(jy, 12)))
    # Every year of the supported range 1178-1633: the algorithm has special cases near its table breaks.
    for jy in range(1178, 1634):
        for jm, jd in ((1, 1), (12, 29), (12, 30)):
            if jalali.is_valid(jy, jm, jd):
                pairs.add((jy, jm, jd))
    rnd = random.Random(1405)
    for _ in range(300):                                # deterministic spread over 1300-1500
        jy = rnd.randint(1300, 1500)
        jm = rnd.randint(1, 12)
        pairs.add((jy, jm, rnd.randint(1, jalali.days_in_month(jy, jm))))
    conversions = []
    for jy, jm, jd in sorted(pairs):
        g = jalali.to_gregorian(jy, jm, jd)
        assert jalali.to_jalali(g) == (jy, jm, jd)
        conversions.append({'jalali': jalali.format_date(jy, jm, jd), 'gregorian': g.isoformat(),
                            'ordinal': g.toordinal()})
    normalize = []
    inputs = ['1405/07/10', '۱۴۰۵/۰۷/۱۰', '١٤٠٥/٠٧/١٠', '1405-7-1', '۱۴۰۵.۷.۱', '1405\\07\\10', '1405،07،10',
              ' 1405/07/10 ', '', '   ', '1403/12/30', '1404/12/30', '1405/13/01', '1405/00/10', '1405/07/31',
              '1405/06/31', '1405/7', 'abc', '1405/07/10/1', '14o5/07/10', '1405/07/ ۱۰']
    for text in inputs:
        try:
            normalize.append({'input': text, 'output': jalali.normalize(text), 'error': None})
        except ValueError as e:
            normalize.append({'input': text, 'output': None, 'error': str(e)})
    return {
        'conversions': conversions,
        'normalize': normalize,
        'leap_years': [y for y in range(1178, 1634) if jalali.is_leap(y)],
        'days_in_month': [{'year': y, 'month': m, 'days': jalali.days_in_month(y, m)}
                          for y in (1403, 1404, 1405) for m in range(1, 13)],
        'today': [{'gregorian': d, 'jalali': jalali.today(datetime.date.fromisoformat(d))}
                  for d in ('2024-03-20', '2024-03-21', '2025-03-20', '2025-03-21', '2026-10-03', '2026-11-14')],
    }


def status_vectors():
    cases = []
    for status in ('جدید', 'در حال انجام', 'انجام شده', 'عقب‌افتاده'):
        for due in ('', '1405/07/09', '1405/07/10', '1405/07/11'):
            a = SimpleNamespace(status=status, due=due)
            cases.append({'status': status, 'due': due, 'today': '1405/07/10',
                          'expected': effective_status(a, today='1405/07/10')})
    return {'cases': cases}


def _pydantic_messages(exc):
    # Same mapping as main.validation_error(): readable Persian sentence(s) for the UI.
    from backend.main import FIELD_LABELS
    msgs = []
    for e in exc.errors():
        msg = str(e.get('msg', ''))
        if msg.startswith('Value error, '):
            msgs.append(msg[len('Value error, '):])
            continue
        field = next((str(x) for x in reversed(e.get('loc', ())) if isinstance(x, str) and x in FIELD_LABELS), None)
        msgs.append(f'مقدار «{FIELD_LABELS[field]}» معتبر نیست یا خالی است.' if field
                    else 'اطلاعات واردشده معتبر نیست.')
    return ' '.join(dict.fromkeys(msgs)) or 'اطلاعات واردشده معتبر نیست.'


def action_vectors():
    from pydantic import ValidationError
    from backend.main import status_for
    from backend.schemas import ActionIn, ProgressIn, NoteIn, ChecklistIn

    status_cases = [{'progress': p, 'status': s, 'expected': status_for(p, s)}
                    for p in (0, 1, 50, 99, 100) for s in ('جدید', 'در حال انجام', 'انجام شده', 'عقب‌افتاده', '')]

    def run(model, cases, fields):
        out = []
        for c in cases:
            try:
                m = model(**c)
                out.append({'input': c, 'ok': True, 'output': {k: getattr(m, k) for k in fields}, 'error': None})
            except ValidationError as e:
                out.append({'input': c, 'ok': False, 'output': None, 'error': _pydantic_messages(e)})
        return out

    action_cases = [
        {'subject': 'گزارش'},
        {'subject': 'گزارش', 'start_date': '۱۴۰۵/۰۷/۰۱', 'due': '1405-7-10', 'progress': 40, 'priority': 'زیاد',
         'status': 'در حال انجام'},
        {'subject': 'x', 'start_date': '1405/07/10', 'due': '1405/07/09'},
        {'subject': 'x', 'start_date': '1405/07/10', 'due': '1405/07/10'},
        {'subject': 'x', 'due': '1404/12/30'},
        {'subject': 'x', 'due': 'abc'},
        {'subject': ''},
        {'subject': '   '},
        {'subject': 'x', 'progress': 101},
        {'subject': 'x', 'progress': -1},
        {'subject': 'x', 'progress': 100, 'status': 'جدید'},
        {'subject': 'x', 'start_date': '', 'due': '  '},
        # several problems at once: messages come in field order, identical messages appear once
        {'subject': '', 'progress': 101},
        {'subject': 'x', 'start_date': 'abc', 'due': '1404/12/30'},
        {'subject': 'x', 'start_date': 'abc', 'due': 'abc'},
        {'subject': '', 'due': 'abc', 'progress': -1},
        {'subject': 'x', 'start_date': '1405/07/10', 'due': '1405/07/09', 'progress': 5},
        # the start/due comparison is skipped while any single field is invalid
        {'subject': '', 'start_date': '1405/07/10', 'due': '1405/07/09'},
        {'subject': 'x', 'start_date': '1405/07/10', 'due': '1405/07/09', 'progress': 101},
    ]
    progress_cases = [
        {'new_progress': 50, 'update_date': '1405/07/10'},
        {'new_progress': 50, 'update_date': '۱۴۰۵-۷-۱۰', 'note': 'یادداشت'},
        {'new_progress': 0, 'update_date': ''},
        {'new_progress': 101, 'update_date': '1405/07/10'},
        {'new_progress': -5, 'update_date': '1405/07/10'},
        {'new_progress': 10, 'update_date': '1405/13/10'},
        {'new_progress': 200, 'update_date': ''},
    ]
    text_cases = [{'body': 'متن'}, {'body': ''}, {'body': 'a' * 5000}, {'body': 'a' * 5001},
                  {'body': '😀' * 5000}, {'body': '😀' * 5001}]  # emoji: one character, two UTF-16 units
    check_cases = [{'title': 'مورد'}, {'title': ''}, {'title': 'a' * 500}, {'title': 'a' * 501},
                   {'title': 'مورد', 'is_done': True}]
    return {
        'status_for': status_cases,
        'action_in': run(ActionIn, action_cases, ['subject', 'start_date', 'due', 'status', 'priority', 'progress']),
        'progress_in': run(ProgressIn, progress_cases, ['new_progress', 'update_date', 'note']),
        'note_in': run(NoteIn, text_cases, ['body']),
        'checklist_in': run(ChecklistIn, check_cases, ['title', 'is_done']),
        'progress_flow': progress_flow(),
    }


def progress_flow():
    """Record what the real HTTP API does for a sequence of progress updates on one action."""
    from fastapi.testclient import TestClient
    from backend.main import app, SessionLocal, hash_pw
    from backend.models import User

    s = SessionLocal()
    s.add(User(username='golden', full_name='golden', password_hash=hash_pw('Golden@123'), role='admin', is_active=True))
    s.commit(); s.close()
    c = TestClient(app)
    h = {'X-Session-Token': c.post('/api/auth/login', json={'username': 'golden', 'password': 'Golden@123'}).json()['token']}

    def flow(start_status, start_progress, steps):
        a = c.post('/api/actions', headers=h, json={'subject': 'flow', 'status': start_status, 'progress': start_progress})
        aid = a.json()['id']
        recorded = []
        for step in steps:
            r = c.post(f'/api/actions/{aid}/progress', headers=h, json=step)
            if r.status_code == 200:
                last = r.json()['history'][-1]
                recorded.append({'input': step, 'ok': True, 'progress': r.json()['progress'], 'status': r.json()['status'],
                                 'history': {'date': last['date'], 'from': last['from'], 'to': last['to'], 'note': last['note']}})
            else:
                recorded.append({'input': step, 'ok': False, 'error': r.json()['detail']})
        return {'start_status': start_status, 'start_progress': start_progress, 'steps': recorded}

    d = '1405/07/10'
    return [
        flow('جدید', 0, [
            {'new_progress': 0, 'update_date': d},                        # nothing changes, no note -> refused
            {'new_progress': 0, 'update_date': d, 'note': '  شروع شد '},  # same value with a note is accepted
            {'new_progress': 30, 'update_date': '۱۴۰۵-۷-۱۱'},             # first progress: new -> in progress
            {'new_progress': 30, 'update_date': d, 'note': '   '},        # blank note counts as no note
            {'new_progress': 100, 'update_date': d, 'note': 'تمام'},      # 100% -> done
            {'new_progress': 40, 'update_date': d},                       # lowering a done action reopens it (in progress)
            {'new_progress': 101, 'update_date': d},                      # rejected by the input rules
            {'new_progress': 100, 'update_date': d},                      # done again
            {'new_progress': 0, 'update_date': d},                        # lowered to zero -> new again
        ]),
        flow('عقب‌افتاده', 20, [
            {'new_progress': 60, 'update_date': d},                       # a late action keeps its status
            {'new_progress': 0, 'update_date': d},
        ]),
        flow('در حال انجام', 50, [{'new_progress': 50, 'update_date': d, 'note': 'x'}]),
    ]


def _validation_cases(model, cases, fields):
    from pydantic import ValidationError
    out = []
    for c in cases:
        try:
            m = model(**c)
            out.append({'input': c, 'ok': True, 'output': {k: getattr(m, k) for k in fields}, 'error': None})
        except ValidationError as e:
            out.append({'input': c, 'ok': False, 'output': None, 'error': _pydantic_messages(e)})
    return out


def meeting_project_vectors():
    from backend.schemas import MeetingIn, ProjectIn
    act = {'subject': 'اقدام'}
    meetings = [
        {'title': 'جلسه', 'meeting_date': '1405/07/10'},
        {'title': 'جلسه', 'meeting_date': '۱۴۰۵-۷-۱۰', 'time': '۱۰:۳۰', 'place': 'اتاق ۱'},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'time': '10:30'},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'time': '  '},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'time': '24:00'},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'time': '9:30'},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'time': '10:60'},
        {'title': '', 'meeting_date': '1405/07/10'},
        {'title': 'a' * 300, 'meeting_date': '1405/07/10'},
        {'title': 'a' * 301, 'meeting_date': '1405/07/10'},
        {'title': 'جلسه', 'meeting_date': ''},
        {'title': 'جلسه', 'meeting_date': '   '},
        {'title': 'جلسه', 'meeting_date': '1404/12/30'},
        {'title': '', 'meeting_date': '', 'time': 'x'},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'actions': [act]},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'actions': [{'subject': ''}]},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'actions': [{'subject': 'x', 'progress': 101}, {'subject': '', 'due': 'abc'}]},
        {'title': 'جلسه', 'meeting_date': '1405/07/10',
         'actions': [{'subject': 'x', 'start_date': '1405/07/10', 'due': '1405/07/09'}]},
        {'title': '', 'meeting_date': '1405/07/10',
         'actions': [{'subject': 'x', 'start_date': '1405/07/10', 'due': '1405/07/09'}]},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'previous_items': [{'progress': 50}]},
        {'title': 'جلسه', 'meeting_date': '1405/07/10', 'previous_items': [{'progress': 101}, {'progress': -1}],
         'actions': [{'subject': ''}]},
        {'title': '', 'meeting_date': '', 'time': 'x', 'previous_items': [{'progress': 200}], 'actions': [{'subject': ''}]},
    ]
    projects = [
        {'name': 'پروژه'},
        {'name': 'پروژه', 'start_date': '۱۴۰۵/۰۱/۰۱', 'end_date': '1405-12-29', 'progress': 40},
        {'name': 'پروژه', 'start_date': '1405/07/10', 'end_date': '1405/07/09'},
        {'name': 'پروژه', 'start_date': '1405/07/10', 'end_date': '1405/07/10'},
        {'name': ''},
        {'name': 'a' * 250},
        {'name': 'a' * 251},
        {'name': 'پروژه', 'progress': 101},
        {'name': 'پروژه', 'end_date': '1404/12/30'},
        {'name': '', 'start_date': 'abc', 'progress': -1},
        {'name': 'پروژه', 'start_date': '1405/07/10', 'end_date': '1405/07/09', 'progress': 101},
    ]
    return {
        'meeting_in': _validation_cases(MeetingIn, meetings, ['title', 'meeting_date', 'time']),
        'project_in': _validation_cases(ProjectIn, projects, ['name', 'start_date', 'end_date', 'progress']),
    }


def permission_vectors():
    """Role permissions (as the server evaluates them) and which actions each kind of user may see."""
    from types import SimpleNamespace
    from fastapi.testclient import TestClient
    from backend.main import app, SessionLocal, hash_pw, has_perm, PERMS, DEFAULT_ROLE
    from backend.models import User, Person, Action, Project, Meeting

    s = SessionLocal()
    roles = ['admin', 'manager', 'user', 'guest']
    has = [{'role': r, 'code': code, 'allowed': bool(has_perm(s, SimpleNamespace(role=r), code))}
           for r in roles for code in sorted(PERMS)]
    for model in (Action, Project, Meeting, User, Person):          # start from a clean slate
        if model is User:
            s.query(User).filter(User.username.like('pv_%')).delete(synchronize_session=False)
        else:
            s.query(model).delete(synchronize_session=False)
    s.commit()
    p1, p2 = Person(name='الف'), Person(name='ب')
    s.add_all([p1, p2]); s.flush()
    users = {}
    for key, role, person in (('admin', 'admin', None), ('manager', 'manager', None), ('user_p1', 'user', p1.id),
                              ('user_p2', 'user', p2.id), ('user_nobody', 'user', None), ('guest_p1', 'guest', p1.id)):
        s.add(User(username=f'pv_{key}', full_name=key, password_hash=hash_pw('Pv@123456'), role=role,
                   is_active=True, person_id=person))
        users[key] = {'role': role, 'person_id': person}
    s.commit()
    c = TestClient(app)
    h = {'X-Session-Token': c.post('/api/auth/login', json={'username': 'pv_admin', 'password': 'Pv@123456'}).json()['token']}
    shapes = [  # name, owner, assignee, collaborators
        ('a_owner_p1', p1.id, None, []), ('a_assignee_p1', None, p1.id, []), ('a_collab_p1', None, None, [p1.id]),
        ('a_owner_p2', p2.id, None, []), ('a_nobody', None, None, []), ('a_both', p2.id, p1.id, [p2.id]),
    ]
    actions = []
    for name, owner, assignee, collab in shapes:
        r = c.post('/api/actions', headers=h, json={'subject': name, 'owner_id': owner, 'assignee_id': assignee, 'collaborator_ids': collab})
        actions.append({'name': name, 'owner_id': owner, 'assignee_id': assignee, 'collaborator_ids': collab, 'id': r.json()['id']})
    visible = {}
    for key, u in users.items():
        tok = c.post('/api/auth/login', json={'username': f'pv_{key}', 'password': 'Pv@123456'}).json()['token']
        got = c.get('/api/actions', headers={'X-Session-Token': tok}).json()
        visible[key] = sorted(a['subject'] for a in got)
    s.close()
    return {'perms': [{'code': k, 'label': v[0], 'group': v[1]} for k, v in PERMS.items()],
            'default_roles': {r: sorted(v) for r, v in DEFAULT_ROLE.items()},
            'has_perm': has, 'users': users, 'actions': actions, 'visible': visible}


def dashboard_vectors():
    """Dashboard numbers for a fixed 'today', recorded from the real /api/dashboard endpoint."""
    from fastapi.testclient import TestClient
    from backend import jalali as jalali_mod
    from backend.main import app, SessionLocal, hash_pw
    from backend.models import User, Action, Project, Meeting

    today = '1405/07/10'
    s = SessionLocal()
    for model in (Action, Project, Meeting):
        s.query(model).delete(synchronize_session=False)
    if not s.query(User).filter_by(username='dash').first():
        s.add(User(username='dash', full_name='dash', password_hash=hash_pw('Dash@123456'), role='admin', is_active=True))
    s.commit(); s.close()
    c = TestClient(app)
    h = {'X-Session-Token': c.post('/api/auth/login', json={'username': 'dash', 'password': 'Dash@123456'}).json()['token']}
    projects = []
    for name, prog in (('بدون اقدام', 35), ('میانگین ۰٫۵', 0), ('میانگین ۱٫۵', 0), ('میانگین ۲٫۵', 0), ('خالی ۲', 80)):
        projects.append(c.post('/api/projects', headers=h, json={'name': name, 'progress': prog}).json()['id'])
    pid = {i: projects[i] for i in range(len(projects))}
    spec = [  # project index, progress, status, due
        (1, 0, 'جدید', ''), (1, 1, 'در حال انجام', '1405/07/09'),
        (2, 1, 'در حال انجام', '1405/08/01'), (2, 2, 'عقب‌افتاده', '1405/01/01'),
        (3, 2, 'در حال انجام', '1405/07/10'), (3, 3, 'جدید', '1405/07/11'),
        (0, 100, 'جدید', '1400/01/01'), (None, 37, 'در حال انجام', '1405/06/30'),
    ]
    for proj, progress, status, due in spec:
        c.post('/api/actions', headers=h, json={'subject': 'd', 'progress': progress, 'status': status, 'due': due,
                                                'project_id': pid[proj] if proj is not None else None})
    saved = jalali_mod.today
    jalali_mod.today = lambda *a, **k: today
    try:
        d = c.get('/api/dashboard', headers=h).json()
    finally:
        jalali_mod.today = saved
    acts = c.get('/api/actions', headers=h).json()
    return {
        'today': today,
        'actions': sorted(({'id': a['id'], 'project_id': a['project_id'], 'progress': a['progress'], 'status': a['status'],
                            'due': a['due']} for a in acts), key=lambda a: a['id']),
        'projects': [{'id': pid[i], 'progress': p} for i, p in ((0, 35), (1, 0), (2, 0), (3, 0), (4, 80))],
        'expected': {'actions': d['actions'], 'avg_progress': d['avg_progress'], 'overdue': d['overdue'],
                     'by_status': d['by_status'],
                     'project_stats': [{'id': x['id'], 'progress': x['progress'], 'actions': x['actions'], 'done': x['done']}
                                       for x in d['project_stats']]},
    }


def _ns_action(d):
    """Build an ORM-like action object from plain fixture data (see report_vectors)."""
    from types import SimpleNamespace as NS
    named = lambda n: NS(name=n) if n is not None else None
    return NS(id=d['id'], subject=d.get('subject'), decision=d.get('decision'),
              project=NS(name=d['project']) if d.get('project') is not None else None,
              meeting=NS(title=d['meeting']) if d.get('meeting') is not None else None,
              owner_person=named(d.get('owner')), assignee_person=named(d.get('assignee')),
              collaborators=[NS(name=n) for n in d.get('collaborators', [])],
              due=d.get('due'), priority=d.get('priority'), progress=d.get('progress', 0), status=d.get('status', 'جدید'))


def _ns_meeting(d):
    from types import SimpleNamespace as NS
    return NS(title=d['title'], meeting_date=d['meeting_date'], time=d.get('time'), place=d.get('place'),
              project=NS(name=d['project']) if d.get('project') is not None else None,
              attendees=[NS(name=n) for n in d.get('attendees', [])],
              summary=d.get('summary'), description=d.get('description'),
              actions=[_ns_action(a) for a in d.get('actions', [])],
              linked_actions=[_ns_action(a) for a in d.get('linked_actions', [])])


def report_vectors():
    """Report content (backend/report_model.py) for hand-made fixtures that cover the edge cases."""
    from backend.report_model import actions_report_model, meetings_report_model
    today = '1405/07/10'

    def A(i, **kw):
        return {'id': i, 'subject': f'اقدام {i}', **kw}

    full = A(1, decision='تصمیم', project='پروژه', meeting='جلسه', owner='علی', assignee='سارا',
             collaborators=['نیما', 'مینا'], due='1405/07/09', priority='زیاد', progress=40, status='در حال انجام')
    actions_cases = [
        {'name': 'empty', 'user': '', 'actions': []},
        {'name': 'one full action (overdue -> late)', 'user': 'مدیر', 'actions': [full]},
        {'name': 'missing everything', 'user': 'x', 'actions': [{'id': 2, 'subject': 'فقط موضوع', 'decision': None, 'due': None,
                                                                'priority': None, 'progress': 0, 'status': ''}]},
        # progress 1 + 2 = 3 -> 1.5 -> rounds to 2 (half to even); 2 + 3 = 5 -> 2.5 -> rounds to 2
        {'name': 'average 1.5', 'user': '', 'actions': [A(3, progress=1, due='1405/12/01'), A(4, progress=2, due='1405/12/01')]},
        {'name': 'average 2.5', 'user': '', 'actions': [A(5, progress=2, due='1405/12/01'), A(6, progress=3, due='1405/12/01')]},
        {'name': 'average 0.5 and 3.5', 'user': '', 'actions': [A(7, progress=0), A(8, progress=1)]},
        {'name': 'status counting', 'user': 'ک', 'actions': [
            A(9, status='جدید', due='1405/07/09'),                      # overdue -> late
            A(10, status='جدید', due='1405/07/10'),                     # due today is not late
            A(11, status='در حال انجام', due='1405/07/11', progress=10),
            A(12, status='انجام شده', due='1400/01/01', progress=100),  # finished is never late
            A(13, status='عقب‌افتاده', progress=20),                    # already late, no due date
            A(14, status='جدید', due='')]},
    ]
    meeting_actions_ = [A(20, decision='د', owner='علی', due='1405/07/09', progress=50, status='در حال انجام'),
                        A(21, progress=100, status='انجام شده')]
    meetings_cases = [
        {'name': 'empty', 'meetings': []},
        {'name': 'full meeting', 'meetings': [{
            'title': 'جلسه', 'meeting_date': '1405/07/05', 'time': '10:30', 'place': 'اتاق ۱', 'project': 'پروژه',
            'attendees': ['علی', 'سارا'], 'summary': '  خلاصه  ', 'description': 'توضیح',
            'actions': meeting_actions_, 'linked_actions': [A(22, project='پ', progress=5)]}]},
        {'name': 'linked overlaps own actions (listed once)', 'meetings': [{
            'title': 'جلسه', 'meeting_date': '1405/07/05', 'actions': meeting_actions_,
            'linked_actions': [meeting_actions_[1], A(23), meeting_actions_[0]]}]},
        {'name': 'summary falls back to description', 'meetings': [
            {'title': 'الف', 'meeting_date': '1405/01/01', 'summary': '', 'description': '  فقط توضیح  '},
            {'title': 'ب', 'meeting_date': '1405/01/02', 'summary': None, 'description': None},
            {'title': 'ج', 'meeting_date': '1405/01/03', 'summary': '   ', 'description': 'توضیح'},
            {'title': 'د', 'meeting_date': '1405/01/04', 'summary': 'خلاصه', 'description': 'توضیح'}]},
        {'name': 'several meetings keep their order', 'meetings': [
            {'title': 'سوم', 'meeting_date': '1405/03/01'}, {'title': 'اول', 'meeting_date': '1405/01/01'},
            {'title': 'دوم', 'meeting_date': '1405/02/01', 'time': None, 'place': None}]},
    ]
    return {
        'today': today,
        'actions': [{'input': c, 'expected': actions_report_model([_ns_action(a) for a in c['actions']], c['user'], today)}
                    for c in actions_cases],
        'meetings': [{'input': c, 'expected': meetings_report_model([_ns_meeting(m) for m in c['meetings']], today)}
                     for c in meetings_cases],
    }


def sync_vectors():
    """Sync-file merges: a local snapshot, a remote snapshot, and what `import_snapshot` makes of them."""
    import copy
    from backend.sync_file import export_snapshot, import_snapshot
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from sync_fixtures import populate, new_session

    def snap(s):
        d = export_snapshot(s, now_ms=0)
        d.pop('device')
        import time
        now = int(time.time() * 1000)
        for rs in d['tables'].values():
            for r in rs:   # tombstones made by a cascade carry the wall clock; pin them so the file is reproducible
                if r.get('deleted_at') is not None and abs(r['deleted_at'] - now) < 3_600_000:
                    r['updated_at'] = r['deleted_at'] = 1
        return d

    def rows(d, table):
        return {r['uuid']: r for r in d['tables'][table]}

    scenarios = []

    def scenario(name, setup_local, make_remote):
        with tempfile.TemporaryDirectory() as tmp:
            local = new_session(Path(tmp) / 'local.db')
            setup_local(local)
            local_snap = snap(local)
            remote = make_remote()
            before = copy.deepcopy(remote)
            stats = import_snapshot(local, copy.deepcopy(remote))
            assert remote == before
            result = snap(local)
            local.close()
        scenarios.append({'name': name, 'local': local_snap, 'remote': remote, 'stats': stats, 'result': result})

    def donor(tag=''):
        with tempfile.TemporaryDirectory() as tmp:
            s = new_session(Path(tmp) / 'donor.db')
            populate(s, tag=tag)
            d = snap(s)
            s.close()
            return d

    scenario('into an empty database', lambda s: None, donor)
    scenario('same file again changes nothing', lambda s: populate(s), donor)
    scenario('other device with its own uuids and ids', lambda s: populate(s, tag='b-', pad=3), donor)

    def mixed():
        d = donor()
        a = rows(d, 'actions')['act-parent']
        a['subject'] = 'ویرایش جدیدتر'; a['progress'] = 80; a['updated_at'] += 5000          # newer: applied
        p = rows(d, 'people')['ali']
        p['name'] = 'ویرایش قدیمی‌تر'; p['updated_at'] -= 1000                                # older: ignored
        sa = rows(d, 'people')['sara']
        sa['deleted_at'] = sa['updated_at'] - 1; sa['updated_at'] -= 1                       # older tombstone: ignored
        mt = rows(d, 'meetings')['meet2']
        mt['updated_at'] += 7000; mt['deleted_at'] = mt['updated_at']                        # newer tombstone: deleted
        d['tables']['action_notes'].append({'uuid': 'note-new', 'updated_at': 9000, 'deleted_at': None, 'body': 'یادداشت تازه',
                                            'created_at': 9000, 'action_uuid': 'act-child', 'author_name': ''})
        d['tables']['checklist_items'].append({'uuid': 'check-gone', 'updated_at': 9000, 'deleted_at': 9000, 'title': 'x',
                                               'is_done': False, 'sort_order': 0, 'created_at': 1, 'action_uuid': 'act-child'})
        c = rows(d, 'checklist_items')['check']
        c['is_done'] = False; c['sort_order'] = 5; c['updated_at'] += 1                      # newer: applied
        pr = rows(d, 'projects')['proj']
        pr['member_uuids'] = ['ali', 'nima']; pr['updated_at'] += 1                          # a changed set of links
        return d
    scenario('mixed: newer wins, older ignored, tombstones', lambda s: populate(s), mixed)

    def odd():
        d = donor()
        d['tables']['action_notes'][0]['action_uuid'] = 'no-such-action'                     # orphan
        d['tables']['comments'][0].update(action_uuid=None, meeting_uuid=None)               # comment without a parent
        d['tables']['people'].append({'uuid': 'never-seen', 'updated_at': 5, 'deleted_at': 5, 'name': 'x', 'department': '',
                                      'role': '', 'category': '', 'is_active': True, 'created_at': 1, 'category_uuid': None})
        d['tables']['actions'].append({'uuid': 'act-late-child', 'updated_at': 50, 'deleted_at': None, 'start_date': '', 'subject': 'فرزند اول',
                                       'decision': '', 'due': '', 'status': 'جدید', 'priority': 'متوسط', 'progress': 0, 'created_at': 1,
                                       'meeting_uuid': None, 'project_uuid': None, 'parent_uuid': 'act-parent', 'owner_uuid': 'ghost',
                                       'assignee_uuid': None, 'collaborator_uuids': ['sara', 'ghost'],
                                       'sprint': '', 'carried_over_from': '', 'ado_id': '', 'done_date': '', 'work_item_type': '',
                                       'parent_ref': '', 'ado_priority': None, 'ado_notes': ''})
        return d
    scenario('orphans, unknown tombstone, child listed before parent, unknown links', lambda s: None, odd)
    return {'scenarios': scenarios}


def tasks_vectors():
    """ADO task sheet: xlsx files built with openpyxl (fixed timestamps, so the bytes are reproducible) and what the
    Python parser reads from them. The Kotlin parser must read the same rows and the same warnings."""
    import base64, datetime, zipfile
    from io import BytesIO
    from openpyxl import Workbook
    sys.path.insert(0, str(ROOT))
    from backend import tasks_excel
    H = tasks_excel.HEADERS

    def pack(wb):
        wb.properties.created = wb.properties.modified = datetime.datetime(2026, 1, 1)
        raw = BytesIO(); wb.save(raw)
        out = BytesIO()
        with zipfile.ZipFile(raw) as zi, zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as zo:
            for info in zi.infolist():
                zi_ = zipfile.ZipInfo(info.filename, (2026, 1, 1, 0, 0, 0)); zi_.compress_type = zipfile.ZIP_DEFLATED
                body = zi.read(info.filename)
                if info.filename == 'docProps/core.xml':      # openpyxl stamps "now" on save
                    import re
                    body = re.sub(rb'(<dcterms:(?:created|modified)[^>]*>)[^<]*', rb'\g<1>2026-01-01T00:00:00Z', body)
                zo.writestr(zi_, body)
        return out.getvalue()

    def book(rows, header=H, sheets=('Items',)):
        wb = Workbook(); wb.remove(wb.active)
        for name in sheets:
            ws = wb.create_sheet(name)
            if name in ('Items', sheets[-1]) :
                ws.append(header)
                for r in rows:
                    ws.append(r)
        return wb
    E = [None] * 7
    rows = [
        ['Alpha', 'کار اول', 'Sara Test', 'New', 'Sprint 1', None, None, None, None, None, None, 'یادداشت & <خاص>'],
        ['Alpha', 'کار دوم', 'Sara Test', 'in progress', 'Sprint 1', 'Sprint 0', 1234, None, 'Feature', 'Epic X', 2, None],
        ['Beta', 'کار سوم', 'Omid Test', 'Done', 'Sprint 1', None, '77', datetime.datetime(2026, 11, 1), None, None, 4.0, None],
        [None] * 12,
        ['Beta', None, 'Omid Test', 'New', 'Sprint 1'] + E,
        ['Beta', 'کار پنجم', 'Omid Test', 'Weird', 'Sprint 1'] + E,
        [None, 'کار ششم', None, None, None, None, None, '1405/08/10', 'Task', None, 9, None],
        ['Beta', 'کار هفتم', 'Omid Test', 'New', 'Sprint 1', None, None, None, None, None, 'abc', None],
        ['Beta', 'کار هشتم', 'Omid Test', 'Done', 'Sprint 1', None, None, None, None, None, '3', None],
    ]
    cases = [('full', pack(book(rows))), ('items-second-sheet', pack(book(rows[:3], sheets=('Guide', 'Items')))),
             ('shuffled-and-missing-columns', pack(book([[r[1], r[0], r[3]] for r in rows[:3]], header=['Description *', 'Project *', 'Status *']))),
             ('headers-without-stars', pack(book(rows[:3], header=[h.replace(' *', '').lower() for h in H])))]
    out = []
    for name, data in cases:
        parsed, problems = tasks_excel.parse_tasks(data)
        out.append({'name': name, 'xlsx_base64': base64.b64encode(data).decode(), 'rows': parsed, 'problems': problems})
    return {'cases': out}


def dump(name, data):
    (OUT / name).write_text(json.dumps(data, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')


def build():
    return {'jalali.json': jalali_vectors(), 'status.json': status_vectors(), 'actions.json': action_vectors(), 'meetings_projects.json': meeting_project_vectors(),
            'permissions.json': permission_vectors(), 'dashboard.json': dashboard_vectors(),
            'reports.json': report_vectors(), 'sync.json': sync_vectors(), 'tasks.json': tasks_vectors()}


if __name__ == '__main__':
    if '--check' in sys.argv:           # compare with the committed files instead of rewriting them
        stale = [n for n, d in build().items()
                 if (OUT / n).read_text(encoding='utf-8') != json.dumps(d, ensure_ascii=False, indent=1) + '\n']
        print('stale:', ', '.join(stale) if stale else 'none')
        sys.exit(1 if stale else 0)
    for name, data in build().items():
        dump(name, data)
        print('wrote', OUT / name)

"""Read-only BT013 adapter; reconcile before handing campaigns to the engine."""
import csv
import hashlib
import re
from collections import Counter, defaultdict
from datetime import datetime, date, time, timedelta
from decimal import Decimal as D
from pathlib import Path
from zoneinfo import ZoneInfo
from .modelo import Campaign, Sample, session_day


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def canonical(value):
    value = D(value)
    rounded = value.quantize(D('.01'))
    if abs(rounded-value) > D('.000001'):
        raise ValueError('Source monetary value not cent-exact: '+str(value))
    return rounded


def timestamp(value):
    # Reject ambiguous/nonexistent NY wall times, rather than guess a DST fold.
    t = datetime.fromisoformat(value[:26])
    ny = ZoneInfo('America/New_York')
    a, b = t.replace(tzinfo=ny, fold=0), t.replace(tzinfo=ny, fold=1)
    if a.utcoffset() != b.utcoffset():
        raise ValueError('Ambiguous/nonexistent source timestamp: '+value)
    return t


def calendar(path):
    """Use the archived BT calendar, whose exception times are Chicago wall time."""
    source = Path(path).read_text(encoding='utf-8-sig')
    exceptions = {date.fromisoformat(d):(int(h),reason) for d,h,reason in
                  re.findall(r'"(\d{4}-\d{2}-\d{2})", new ExceptionDay\((\d+), "([^"]+)"\)',source)}
    if len(exceptions) < 40:
        raise ValueError('Unrecognised calendar snapshot')
    def close(day):
        if not 2024 <= day.year <= 2026:
            raise ValueError('Calendar outside coverage')
        hhmm, reason = exceptions.get(day,(1600,'REGULAR'))
        if day.weekday() >= 5 or hhmm == 0:
            return None, 'CLOSED_WEEKEND' if day.weekday() >= 5 else reason
        t = datetime.combine(day,time(hhmm//100,hhmm%100), ZoneInfo('America/Chicago'))
        return t.astimezone(ZoneInfo('America/New_York')).replace(tzinfo=None), reason
    return close


def load(root, profile):
    root = Path(root)
    logs = list((root/'Registro').glob('*_events.csv'))
    if len(logs)!=1:
        raise ValueError('Exactly one archived run required')
    log = logs[0]
    groups, evidence, kinds = defaultdict(list), defaultdict(list), Counter()
    config = None
    first = last = None
    with log.open(encoding='utf-8-sig',newline='') as f:
        for line,r in enumerate(csv.DictReader(f),2):
            kinds[r['event']]+=1
            if r['event']=='CONFIG':
                if config: raise ValueError('Multiple runs/configs')
                config = r; continue
            t = timestamp(r['time_ny'])
            first = min(first,t) if first else t
            last = max(last,t) if last else t
            evidence[session_day(t,profile.rollover_hour)].append((t,r['event'],r['reason']))
            if r['event'] in ('FILL_ENTRY','FILL_EXIT','MARK','POSITION_RESULT','ENTRY_RESULT','CLOSE_REQUEST'):
                groups[r['position']].append((t,line,r))
    if config is None or config['instrument'] != 'MNQ 12-26':
        raise ValueError('Unsupported/missing source config')
    source_config = dict(s.split('=',1) for s in config['detail'].split(';'))
    source_fee = D(source_config['fee_roundtrip'].replace(',','.'))
    campaigns, issues = [], []
    entry_count, total_native, total_gross, total_fees = 0, D(0), D(0), D(0)
    leg_records = []
    for identity,rows in groups.items():
        entries = [(t,n,r) for t,n,r in rows if r['event']=='FILL_ENTRY']
        finals = [(t,n,r) for t,n,r in rows if r['event']=='POSITION_RESULT']
        legs = [(t,n,r) for t,n,r in rows if r['event']=='ENTRY_RESULT']
        if not entries: raise ValueError('Campaign without entry: '+identity)
        if len(finals)!=1:
            raise ValueError('Campaign not uniquely finalised: '+identity)
        et,_,end = finals[0]
        bought = int(end['bought'])
        detail = lambda r: dict(s.split('=',1) for s in r['detail'].split(';'))
        if sum(int(detail(r)['quantity']) for _,_,r in entries)!=bought:
            raise ValueError('Entry quantities do not reconcile')
        if int(end['sold'])!=bought or int(end['quantity'])!=0:
            raise ValueError('Incomplete position ledger')
        if sum(int(detail(r)['quantity']) for _,_,r in rows if r['event']=='FILL_EXIT')!=bought:
            raise ValueError('Exit quantities do not reconcile')
        gross=canonical(end['realised_gross']); fee=bought*source_fee
        if canonical(end['fee_reserved'])!=fee or canonical(end['net_liquidation'])!=gross-fee:
            raise ValueError('Campaign commission/net mismatch')
        if sum((canonical(detail(r)['gross']) for _,_,r in legs),D(0)) != gross:
            raise ValueError('FIFO legs do not reconcile')
        if sum((canonical(detail(r)['fee']) for _,_,r in legs),D(0)) != fee:
            raise ValueError('Leg fees mismatch')
        for _,_,r in legs:
            if canonical(detail(r)['net']) != canonical(detail(r)['gross'])-canonical(detail(r)['fee']):
                raise ValueError('Leg net mismatch')
            leg_records.append((identity,r['reason'],canonical(detail(r)['gross']),canonical(detail(r)['fee'])))
        samples = [Sample(t,canonical(D(r['net_liquidation'])+D(r['fee_reserved'])),int(r['bought']),r['event'],n)
                   for t,n,r in rows if r['event'] in ('MARK','FILL_ENTRY','FILL_EXIT','POSITION_RESULT','CLOSE_REQUEST')]
        start=entries[0][0]
        c=Campaign(identity,start,et,gross,bought,samples,max(int(r['quantity']) for _,_,r in rows),len(legs))
        if session_day(start,profile.rollover_hour)!=session_day(et,profile.rollover_hour):
            c.quality='CRUCE_JORNADA';issues.append(dict(campaign=identity,issue=c.quality))
        previous=start
        for s in samples:
            gap=(s.time-previous).total_seconds()
            if gap<0 or gap>profile.max_sample_gap_seconds:
                issues.append(dict(campaign=identity,issue='HUECO_MUESTRAS',seconds=gap,at=str(s.time)))
            previous=s.time
        campaigns.append(c)
        entry_count+=len(entries);total_native+=gross-fee;total_gross+=gross;total_fees+=fee
    campaigns.sort(key=lambda c:c.start)
    if any(a.end>b.start for a,b in zip(campaigns,campaigns[1:])):
        raise ValueError('Overlapping campaigns')
    with (root/'Originales'/'Trades.csv').open(encoding='utf-8-sig',newline='') as f:
        trades=list(csv.DictReader(f,delimiter=';'))
    money=lambda s:D(s.replace('$','').replace(' ','').replace('.','').replace(',','.'))
    exported_gross=sum((money(r['Ganancias'])+money(r['Comisión']) for r in trades),D(0))
    contracts=sum(int(r['Cant.']) for r in trades)
    if exported_gross!=total_gross or contracts!=sum(c.contracts for c in campaigns) or len(trades)!=entry_count:
        raise ValueError('Analyzer and native ledger do not reconcile')
    resolve=calendar(root/'Codigo'/'CalendarioSesionesNY.cs')
    start_day=session_day(campaigns[0].start,profile.rollover_hour)
    if first > datetime.combine(start_day-timedelta(days=1),time(profile.rollover_hour)):
        start_day+=timedelta(days=1)
    end_day=session_day(last,profile.rollover_hour)
    sessions, coverage, unknown = [], [], []
    day=start_day
    while day<=end_day:
        close,reason=resolve(day)
        complete=close is not None and close<=last
        events=evidence.get(day,[])
        coverage.append(dict(day=str(day),calendar_close=str(close),reason=reason,
                             complete_boundary=complete,event_count=len(events),
                             first_event=str(events[0][0]) if events else '',
                             last_event=str(events[-1][0]) if events else ''))
        if complete:
            sessions.append(day)
            if not events:unknown.append(day)
        elif close is None and any(session_day(c.start,profile.rollover_hour)==day for c in campaigns):
            raise ValueError('Campaign on calendar-closed day: '+str(day))
        day+=timedelta(days=1)
    selected=[c for c in campaigns if session_day(c.start,profile.rollover_hour) in sessions]
    quality=dict(source_config=source_config,run=config['run'],version=config['version'],
                 log_first=str(first),log_last=str(last),effective_start=str(sessions[0]),
                 effective_end=str(sessions[-1]),campaigns=len(campaigns),entries=entry_count,
                 contracts=contracts,gross=total_gross,native_fees=total_fees,native_net=total_native,
                 analyzer_net=sum((money(r['Ganancias']) for r in trades),D(0)),
                 analyzer_fees=sum((money(r['Comisión']) for r in trades),D(0)),
                 selected_campaigns=len(selected),excluded_campaigns=[c.id for c in campaigns if c not in selected],
                 over20=sum(c.max_contracts>20 for c in campaigns),max_contracts=max(c.max_contracts for c in campaigns),
                 issues=issues,unknown_days=[str(x) for x in unknown],event_counts=dict(kinds),
                 coverage_basis='Archived calendar + later events; not a complete market-bar audit',
                 files=[dict(path=str(p.relative_to(root)),sha256=sha(p),bytes=p.stat().st_size)
                        for p in [log,*(root/'Originales').glob('*.csv'),root/'Codigo'/'CalendarioSesionesNY.cs']])
    return selected,sessions,unknown,quality,coverage

"""Decimal state machine. No imports from the evaluations simulator."""
from dataclasses import dataclass, field, fields
from datetime import datetime, timedelta
from decimal import Decimal, ROUND_DOWN
from collections import Counter
from statistics import median

D = Decimal
ZERO = D('0')
WARNING = ('Estimación exploratoria sin aplicar el escalado de contratos de LucidFlex; '
           'no acredita cumplimiento completo de la empresa.')


@dataclass(frozen=True)
class Profile:
    version: str = 'v1'
    initial: Decimal = D('50000')
    drawdown: Decimal = D('2000')
    cap: Decimal = D('50100')
    minimum_campaigns: int = 2
    daily_stop: Decimal = D('150')
    valid_day: Decimal = D('150')
    required_days: int = 5
    request_min: Decimal = D('500')
    request_max: Decimal = D('2000')
    request_fraction: Decimal = D('.5')
    trader_fraction: Decimal = D('.9')
    commission: Decimal = D('1.82')
    sensitivity: Decimal = D('1.90')
    ignore_scaling: bool = True
    rollover_hour: int = 18
    max_sample_gap_seconds: int = 90

    @classmethod
    def load(cls, obj):
        defaults = cls()
        return cls(**{k: D(str(v)) if isinstance(getattr(defaults, k), D) else v
                      for k, v in obj.items()})

    def __post_init__(self):
        for f in fields(self):
            v = getattr(self, f.name)
            if isinstance(v, D) and (not v.is_finite() or v < 0):
                raise ValueError('Invalid decimal: '+f.name)
        if not self.ignore_scaling:
            raise ValueError('Scaling not implemented; explicit research exception required')
        if min(self.initial, self.drawdown, self.request_min) <= 0:
            raise ValueError('Positive balances and limits required')
        if self.cap < self.initial-self.drawdown or self.request_max < self.request_min:
            raise ValueError('Inconsistent limits')
        if not 0 < self.request_fraction <= 1 or not 0 < self.trader_fraction <= 1:
            raise ValueError('Invalid payout fraction')
        if self.minimum_campaigns < 1 or self.required_days < 1 or not 0 <= self.rollover_hour <= 23:
            raise ValueError('Invalid session/count configuration')
        if self.max_sample_gap_seconds < 60:
            raise ValueError('Minute data requires gap allowance >=60s')


def session_day(t, rollover=18):
    # Source is already NY wall time; never convert it a second time.
    return (t + timedelta(days=int(t.hour >= rollover))).date()


@dataclass(frozen=True)
class Sample:
    time: datetime
    gross_equity: Decimal
    bought: int
    event: str = 'MARK'
    row: int = 0


@dataclass
class Campaign:
    id: str
    start: datetime
    end: datetime
    gross: Decimal
    contracts: int
    samples: list[Sample] = field(default_factory=list)
    max_contracts: int = 1
    legs: int = 1
    quality: str = ''

    def net(self, fee):
        return self.gross - self.contracts*fee


def simulate(campaigns, sessions, profile, fee, mode='intradia', unknown_days=()):
    """Stops the entire segment at INDETERMINADA. Never invents a restart."""
    if mode not in ('cierres', 'intradia'):
        raise ValueError('Unknown evidence mode')
    by_day = {}
    for c in sorted(campaigns, key=lambda c: c.start):
        by_day.setdefault(session_day(c.start, profile.rollover_hour), []).append(c)
    accounts, daily, audit = [], [], []
    a = None
    def finish(status, reason, end):
        a.update(status=status, reason=reason, end=str(end),
                 calendar_days=(end-a.pop('_start')).days+1)
        accounts.append(dict(a))
    def final_result():
        seen = {r['campaign'] for r in audit}
        for c in campaigns:
            if c.id not in seen:
                audit.append(dict(account=None, day=str(session_day(c.start,profile.rollover_hour)),
                                  campaign=c.id, start=str(c.start), end=str(c.end),
                                  gross=c.gross, contracts=c.contracts, commission=c.contracts*fee,
                                  source_net=c.net(fee), decision='NO_SIMULADA',
                                  reason='SECUENCIA_DETENIDA_INDETERMINADA'))
        return result(accounts, daily, audit, mode, fee)
    for day in sessions:
        items = by_day.get(day, [])
        if a is None and not items:
            continue
        if a is None:
            a = dict(id=len(accounts)+1, start=str(day), _start=day, balance=profile.initial,
                     H=profile.initial, L=profile.initial-profile.drawdown, valid_days=0,
                     traded_days=0, sessions=0, request=ZERO, cash_estimated=ZERO,
                     post_balance=None, residual_margin=None, status='ACTIVA')
        a['sessions'] += 1
        if day in unknown_days:
            finish('INDETERMINADA', 'JORNADA_SIN_EVIDENCIA', day)
            break
        opening, threshold = a['balance'], a['L']
        count, pnl, stopped, terminal = 0, ZERO, False, False
        traded_today = False
        for c in items:
            row = dict(account=a['id'], day=str(day), campaign=c.id, start=str(c.start),
                       end=str(c.end), contracts=c.contracts, max_contracts=c.max_contracts,
                       legs=c.legs, gross=c.gross, commission=c.contracts*fee,
                       source_net=c.net(fee), balance_before=a['balance'], L=threshold)
            audit.append(row)
            if terminal or stopped:
                row.update(decision='OMITIDA', reason='CUENTA_TERMINADA' if terminal else 'PARADA_DIARIA',
                           daily_count=count, daily_pnl=pnl)
                continue
            if not traded_today:
                a['traded_days'] += 1
                traded_today = True
            if c.quality or session_day(c.end, profile.rollover_hour) != day:
                row.update(decision='INDETERMINADA', reason=c.quality or 'CRUCE_JORNADA')
                finish('INDETERMINADA', row['reason'], day)
                daily.append(dict(account=a['id'],day=str(day),pnl=pnl,closed_campaigns=count,
                                  balance_close=a['balance'],L_intraday=threshold,status='INDETERMINADA'))
                return final_result()
            minimum, breach = a['balance'], None
            if mode == 'intradia':
                if not c.samples:
                    finish('INDETERMINADA', 'SIN_MUESTRAS', day)
                    row.update(decision='INDETERMINADA', reason='SIN_MUESTRAS')
                    daily.append(dict(account=a['id'],day=str(day),pnl=pnl,closed_campaigns=count,
                                      balance_close=a['balance'],L_intraday=threshold,status='INDETERMINADA'))
                    return final_result()
                previous = c.start
                for s in c.samples:
                    gap = (s.time-previous).total_seconds()
                    if gap < 0 or gap > profile.max_sample_gap_seconds:
                        row.update(decision='INDETERMINADA', reason='HUECO_MUESTRAS',
                                   gap_seconds=gap, event_time=str(s.time))
                        finish('INDETERMINADA', 'HUECO_MUESTRAS', day)
                        daily.append(dict(account=a['id'],day=str(day),pnl=pnl,closed_campaigns=count,
                                          balance_close=a['balance'],L_intraday=threshold,status='INDETERMINADA'))
                        return final_result()
                    eq = a['balance']+s.gross_equity-s.bought*fee
                    minimum = min(minimum, eq)
                    if eq <= threshold:
                        breach = s
                        break
                    previous = s.time
            if breach is not None:
                # This is observed equity, not an invented liquidation fill.
                a['observed_equity_at_loss'] = a['balance']+breach.gross_equity-breach.bought*fee
                a['loss_time'] = str(breach.time)
                row.update(decision='PERDIDA', reason='EQUITY_OBSERVADA_TOCA_L',
                           event_time=str(breach.time), event_row=breach.row,
                           observed_equity=a['observed_equity_at_loss'], minimum_observed=minimum,
                           daily_count=count, daily_pnl=pnl)
                finish('PERDIDA', 'EQUITY_OBSERVADA_TOCA_L', day)
                terminal = True
                continue
            minimum = min(minimum, a['balance']+c.net(fee))
            a['balance'] += c.net(fee)
            count += 1
            pnl += c.net(fee)
            row.update(decision='ACEPTADA', reason='CIERRE_COMPLETO', minimum_observed=minimum,
                       daily_count=count, daily_pnl=pnl, balance_after=a['balance'])
            if a['balance'] <= threshold:
                row.update(decision='PERDIDA', reason='CIERRE_TOCA_L')
                finish('PERDIDA', 'CIERRE_TOCA_L', day)
                terminal = True
            elif count >= profile.minimum_campaigns and pnl >= profile.daily_stop:
                stopped = True
        d = dict(account=a['id'], day=str(day), balance_open=opening, balance_close=a['balance'],
                 L_intraday=threshold, closed_campaigns=count, pnl=pnl, stopped=stopped,
                 valid=False, minimum_policy_unmet=count < profile.minimum_campaigns,
                 available_campaigns=len(items))
        if not terminal:
            d['valid'] = pnl >= profile.valid_day
            a['valid_days'] += int(d['valid'])
            a['H'] = max(a['H'], a['balance'])
            a['L'] = max(a['L'], min(profile.cap, a['H']-profile.drawdown))
            profit = a['balance']-profile.initial
            request = min(profit*profile.request_fraction, profile.request_max).quantize(D('.01'), rounding=ROUND_DOWN)
            if a['balance'] <= a['L']:
                finish('PERDIDA', 'UMBRAL_EOD', day)
                terminal = True
            elif a['valid_days'] >= profile.required_days and profit > 0 and request >= profile.request_min:
                if a['balance']-request > profile.cap:
                    a.update(request=request, cash_estimated=(request*profile.trader_fraction).quantize(D('.01'),rounding=ROUND_DOWN),
                             post_balance=a['balance']-request, residual_margin=a['balance']-request-profile.cap,
                             L=profile.cap)
                    finish('PRIMERA_RETIRADA_ELEGIBLE', 'REQUISITOS_EOD', day)
                    terminal = True
                else:
                    d['payout_blocked'] = 'SALDO_POSTERIOR_EN_O_BAJO_UMBRAL'
        d.update(H=a['H'], L_next=a['L'], valid_days=a['valid_days'])
        daily.append(d)
        if terminal:
            a = None
    if a is not None and a.get('status') == 'ACTIVA':
        finish('PENDIENTE_FIN_DATOS', 'FIN_COBERTURA_COMPLETA', sessions[-1])
    return final_result()


def result(accounts, daily, audit, mode, fee):
    states = Counter(a['status'] for a in accounts)
    n = len(accounts)
    eligible = states['PRIMERA_RETIRADA_ELEGIBLE']
    resolved = eligible+states['PERDIDA']
    streaks, streak = [], 0
    for a in accounts:
        if a['status'] == 'PERDIDA':
            streak += 1
        elif streak:
            streaks.append(streak); streak = 0
    if streak:
        streaks.append(streak)
    annual = {}
    for a in accounts:
        y = a['end'][:4]
        bucket = annual.setdefault(y, dict(accounts_finished=0, eligible=0, lost=0, request=ZERO, cash_estimated=ZERO))
        bucket['accounts_finished'] += 1
        bucket['eligible'] += int(a['status']=='PRIMERA_RETIRADA_ELEGIBLE')
        bucket['lost'] += int(a['status']=='PERDIDA')
        bucket['request'] += a['request']; bucket['cash_estimated'] += a['cash_estimated']
    durations = {}
    for status in states:
        values = [a['calendar_days'] for a in accounts if a['status']==status]
        durations[status] = dict(count=len(values),minimum=min(values),median=median(values),
                                 maximum=max(values),histogram=dict(Counter(values)))
    return dict(summary=dict(mode=mode, commission=fee, warning=WARNING, started=n, states=dict(states),
                 resolved=resolved, eligible_over_started=D(eligible)/n if n else None,
                 eligible_over_resolved=D(eligible)/resolved if resolved else None,
                 gross_requests=sum((a['request'] for a in accounts),ZERO),
                 cash_estimated=sum((a['cash_estimated'] for a in accounts),ZERO),
                 fees='NO CALCULADOS', actual_payments='NO VERIFICADOS', loss_streaks=streaks,
                 duration_days=[a['calendar_days'] for a in accounts],duration_by_state=durations, by_end_year=annual),
                accounts=accounts, daily=daily, campaigns=audit)

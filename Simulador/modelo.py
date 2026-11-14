"""Motor determinista, importes Decimal; aprobaciones siempre condicionales.

No reconstruye equity intradía. Un breach aportado como evidencia tiene
prioridad, pero la ausencia de evidencia no acredita supervivencia flotante.
"""
from dataclasses import dataclass
from datetime import date
from decimal import Decimal
from statistics import median

ZERO = Decimal("0")


def money(value):
    result = Decimal(str(value))
    if not result.is_finite() or result != result.quantize(Decimal("0.01")):
        raise ValueError(f"Importe no finito o con fracciones de centavo: {value}")
    return result


def usd(value):
    return format(value, ".2f") if value is not None else None


@dataclass(frozen=True)
class Profile:
    version: str = "lucidflex50k-investigacion-2026-11-13-v1"
    rules_as_of: str = "2026-11-13"
    initial_balance: Decimal = Decimal("50000")
    profit_target: Decimal = Decimal("3000")
    eod_drawdown: Decimal = Decimal("2000")
    threshold_cap: Decimal = Decimal("50100")
    consistency: Decimal = Decimal("0.50")
    daily_loss_limit: None = None
    max_contracts: int = 40
    attempt_fee: Decimal = Decimal("110")
    roundtrip_commission: Decimal = Decimal("1.82")
    sensitivity_commission: Decimal = Decimal("1.90")
    restart_after_pass: bool = True
    restart_after_fail: bool = True
    source_timezone: str = "America/New_York"
    session_rollover_hour: int = 18
    additional_slippage: Decimal = ZERO
    consistency_review_band: Decimal = Decimal("0.01")

    @classmethod
    def from_dict(cls, values):
        decimal_fields = {"initial_balance", "profit_target", "eod_drawdown",
                          "threshold_cap", "consistency", "attempt_fee",
                          "roundtrip_commission", "sensitivity_commission",
                          "additional_slippage", "consistency_review_band"}
        return cls(**{k: Decimal(str(v)) if k in decimal_fields else v
                      for k, v in values.items()})

    def __post_init__(self):
        for name in ("initial_balance", "profit_target", "eod_drawdown", "threshold_cap",
                     "attempt_fee", "roundtrip_commission", "sensitivity_commission"):
            money(getattr(self, name))
        if min(self.initial_balance, self.profit_target, self.eod_drawdown) <= 0:
            raise ValueError("Saldo, objetivo y drawdown deben ser positivos")
        if min(self.attempt_fee, self.roundtrip_commission, self.sensitivity_commission) < 0:
            raise ValueError("Costes negativos")
        if not ZERO < self.consistency <= 1 or self.consistency_review_band < 0:
            raise ValueError("Consistencia inválida")
        if self.threshold_cap < self.initial_balance - self.eod_drawdown:
            raise ValueError("Tope inferior al umbral inicial")
        if self.daily_loss_limit is not None or self.additional_slippage != 0:
            raise ValueError("DLL y slippage adicional no implementados en v0.1")
        if type(self.max_contracts) is not int or self.max_contracts < 1:
            raise ValueError("Límite de contratos inválido")
        if type(self.restart_after_pass) is not bool or type(self.restart_after_fail) is not bool:
            raise ValueError("Reinicios deben ser booleanos")
        if not 0 <= self.session_rollover_hour <= 23:
            raise ValueError("Corte de jornada inválido")


@dataclass(frozen=True)
class Campaign:
    id: str
    quantity: int
    gross: Decimal
    commission: Decimal
    source_ids: str = ""

    @property
    def net(self):
        return self.gross - self.commission

    def __post_init__(self):
        if type(self.quantity) is not int or self.quantity < 1:
            raise ValueError("Cantidad inválida")
        money(self.gross)
        if money(self.commission) < 0:
            raise ValueError("Comisión negativa")


@dataclass(frozen=True)
class Session:
    day: date
    campaign: Campaign | None = None
    quality: str = "known"  # known / unknown; unknown stops the entire segment
    reason: str = ""
    operable: bool = True
    observed_pnl_min: Decimal | None = None  # ONLY an externally proven breach bound


@dataclass
class Attempt:
    id: int
    start: date
    balance: Decimal
    high: Decimal
    threshold: Decimal
    fee: Decimal
    end: date | None = None
    status: str = "active"
    reason: str = ""
    best_day: Decimal = ZERO
    traded_sessions: int = 0
    operable_sessions: int = 0
    gross: Decimal = ZERO
    commission: Decimal = ZERO
    near_consistency_events: int = 0


def attempt_record(a, profile):
    profit = a.balance - profile.initial_balance
    return dict(id=a.id, start=a.start.isoformat(), end=a.end.isoformat(),
                status=a.status, reason=a.reason, fee_usd=usd(a.fee),
                balance_usd=usd(a.balance), profit_usd=usd(profit),
                high_eod_usd=usd(a.high), threshold_usd=usd(a.threshold),
                remaining_margin_usd=usd(a.balance-a.threshold),
                gross_processed_usd=usd(a.gross), commission_usd=usd(a.commission),
                best_day_usd=usd(a.best_day),
                consistency=str(a.best_day/profit) if profit > 0 else None,
                traded_sessions=a.traded_sessions, operable_sessions=a.operable_sessions,
                calendar_days=(a.end-a.start).days+1,
                censored=a.status in ("pending", "indeterminate"),
                crosses_year=a.start.year != a.end.year,
                near_consistency_events=a.near_consistency_events)


def simulate(sessions, profile):
    """One non-overlapping sequence; only buy when an actual campaign is available.

    Known flat SKIPs keep the account; no manufactured zero-price operations.
    Unknown exposure halts, with no arbitrary next restart date. Approval is
    checked once flat and daily PNL is final, before the next EOD threshold.
    """
    sessions = list(sessions)
    if not sessions or any(b.day <= a.day for a, b in zip(sessions, sessions[1:])):
        raise ValueError("Sesiones vacías, desordenadas o repetidas")
    if any((b.day-a.day).days != 1 for a, b in zip(sessions, sessions[1:])):
        raise ValueError("Fechas ausentes: deben clasificarse, no saltarse")
    attempts, audit, active = [], [], None
    stopped_at = None
    allow_new = True
    for session in sessions:
        if session.quality not in ("known", "unknown"):
            raise ValueError("Calidad desconocida")
        if session.quality == "unknown":
            stopped_at = session.day.isoformat()
            if active:
                active.status, active.reason, active.end = "indeterminate", session.reason, session.day
            audit.append(dict(date=stopped_at, attempt_id=active.id if active else None,
                              decision="indeterminate_segment_end", quality="unknown",
                              reason=session.reason))
            break
        c = session.campaign
        if active is None and c is not None and allow_new:
            active = Attempt(len(attempts)+1, session.day, profile.initial_balance,
                             profile.initial_balance, profile.initial_balance-profile.eod_drawdown,
                             profile.attempt_fee)
            attempts.append(active)
        if active is None:
            audit.append(dict(date=session.day.isoformat(), attempt_id=None,
                              decision="no_purchase_no_campaign" if c is None else "restart_disabled",
                              quality=session.quality, reason=session.reason))
            continue
        active.end = session.day
        active.operable_sessions += int(session.operable)
        before, old_high, old_limit = active.balance, active.high, active.threshold
        decision = "flat_no_trade"
        breach = None
        net = ZERO
        if c:
            active.traded_sessions += 1
            if c.quantity > profile.max_contracts:
                decision, active.status, active.reason = "size_breach", "failed", "contracts_above_limit"
            else:
                net = c.net
                if session.observed_pnl_min is not None and before+session.observed_pnl_min <= old_limit:
                    breach = before + session.observed_pnl_min
                    active.balance = breach
                    decision, active.status, active.reason = "proven_breach", "failed", "observed_equity_at_or_below_threshold"
                else:
                    active.gross += c.gross
                    active.commission += c.commission
                    active.balance += net
                    active.best_day = max(active.best_day, net)
                    profit = active.balance-profile.initial_balance
                    if active.balance <= old_limit:
                        decision, active.status, active.reason = "closed_breach", "failed", "closed_balance_at_or_below_threshold"
                    elif profit >= profile.profit_target:
                        # Exact Decimal multiplication; no commercial tolerance.
                        if active.best_day <= profit*profile.consistency:
                            decision, active.status, active.reason = "conditional_pass", "passed_conditional", "target_and_strict_consistency_at_flat_close"
                        else:
                            decision = "target_consistency_not_met"
                            if active.best_day/profit-profile.consistency <= profile.consistency_review_band:
                                active.near_consistency_events += 1
                    else:
                        decision = "continue"
        if active.status == "active":
            active.high = max(active.high, active.balance)
            active.threshold = max(old_limit, min(profile.threshold_cap, active.high-profile.eod_drawdown))
        audit.append(dict(date=session.day.isoformat(), attempt_id=active.id,
                          campaign_id=c.id if c else None, source_ids=c.source_ids if c else None,
                          quantity=c.quantity if c else 0, quality=session.quality,
                          reason=session.reason, gross_usd=usd(c.gross if c else ZERO),
                          commission_usd=usd(c.commission if c else ZERO), net_usd=usd(net),
                          balance_before_usd=usd(before), balance_after_usd=usd(active.balance),
                          high_before_usd=usd(old_high), high_eod_usd=usd(active.high),
                          limit_during_session_usd=usd(old_limit), next_limit_usd=usd(active.threshold),
                          best_day_usd=usd(active.best_day), decision=decision,
                          status=active.status, conditional=True, breach_equity_usd=usd(breach)))
        if active.status in ("failed", "passed_conditional"):
            allow_new = profile.restart_after_pass if active.status == "passed_conditional" else profile.restart_after_fail
            active = None
    if active and active.status == "active":
        active.status, active.reason = "pending", "right_censored_at_last_complete_session"
    records = [attempt_record(a, profile) for a in attempts]
    result = dict(attempts=records, daily=audit, stopped_at=stopped_at,
                  summary=summarize(records, profile))
    return result


def summarize(records, profile):
    counts = {s: sum(r['status'] == s for r in records)
              for s in ("passed_conditional", "failed", "pending", "indeterminate")}
    n, a, failed = len(records), counts['passed_conditional'], counts['failed']
    if n != sum(counts.values()):
        raise AssertionError("N != A+S+P+I")
    fees = sum((money(r['fee_usd']) for r in records), ZERO)
    cycles, streak, max_streak, prior_id, unapproved_fees = [], 0, 0, 0, ZERO
    for r in records:
        unapproved_fees += money(r['fee_usd'])
        if r['status'] == 'failed':
            streak += 1
            max_streak = max(max_streak, streak)
        elif r['status'] == 'passed_conditional':
            cycles.append(dict(approved_attempt_id=r['id'], date=r['end'],
                               attempts_since_previous_approval=r['id']-prior_id,
                               fees_since_previous_approval_usd=usd(unapproved_fees)))
            prior_id, unapproved_fees, streak = r['id'], ZERO, 0
    def durations(rows):
        return {field: dict(median=median([r[field] for r in rows]) if rows else None,
                            maximum=max([r[field] for r in rows], default=None),
                            distribution={str(x): sum(r[field] == x for r in rows)
                                          for x in sorted({r[field] for r in rows})})
                for field in ('traded_sessions', 'calendar_days', 'operable_sessions')}
    years = {}
    for r in records:
        buy, end = r['start'][:4], r['end'][:4]
        for year in (buy, end):
            years.setdefault(year, dict(purchased=0, fees_usd="0.00", passed_conditional=0,
                                         failed=0, pending=0, indeterminate=0))
        years[buy]['purchased'] += 1
        years[buy]['fees_usd'] = usd(money(years[buy]['fees_usd'])+money(r['fee_usd']))
        years[end][r['status']] += 1
    return dict(level="estimación con operaciones cerradas", purchased=n, **counts,
                fees_usd=usd(fees), cost_per_conditional_pass_usd=usd(fees/a) if a else None,
                cost_per_pass_status="available" if a else "sin aprobaciones",
                approval_over_purchased=f"{a}/{n}", approval_over_decided=f"{a}/{a+failed}",
                attempts_per_conditional_pass=str(Decimal(n)/a) if a else None,
                durations_by_status={s: durations([r for r in records if r['status'] == s]) for s in counts},
                completed_durations=durations([r for r in records if not r['censored']]),
                max_consecutive_failures=max_streak, approvals=cycles,
                final_fees_without_new_approval_usd=usd(unapproved_fees), by_year=years,
                conditional_approvals_only=True, commercial_tolerance_implemented=False)

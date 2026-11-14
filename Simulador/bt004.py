"""Lector estricto de BT004: originales + registro, nunca Excel como segunda copia.

Puede leer los originales desde el ZIP archivado. Siempre valida sus SHA-256
contra manifest.json; informa también de originales locales que ya no coinciden.
"""
import csv
import hashlib
import io
import json
import re
import zipfile
from collections import defaultdict
from datetime import date, datetime, timedelta
from decimal import Decimal
from pathlib import Path
from .modelo import Campaign, Session, ZERO, money, usd


def sha256(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def native_money(value):
    return money(value.replace('$', '').replace(' ', '').replace('.', '').replace(',', '.'))


def campaign_from_parts(identity, parts, fee, native_net=False):
    """Synthetic/general helper: group closed parts, charge actual quantity once.

    Each part: quantity, amount, native_commission. For native net, restore gross
    first. Applying the chosen commission replaces the native cost, never adds it.
    """
    qty, gross = 0, ZERO
    for part in parts:
        if type(part['quantity']) is not int or part['quantity'] < 1:
            raise ValueError('Cantidad de parcial inválida')
        qty += part['quantity']
        gross += money(part['amount']) + (money(part.get('native_commission', 0)) if native_net else ZERO)
    return Campaign(identity, qty, gross, money(fee)*qty)


def session_label(wall_time, rollover_hour=18):
    """NY wall timestamp already converted by NT. Evening belongs to next date.

    Intraday morning campaigns in BT004 coincide with calendar date. No fixed
    UTC offset and no second conversion of NY timestamps.
    """
    return wall_time.date() + timedelta(days=int(wall_time.hour >= rollover_hour))


def load_bt004(root, profile, completed_through, originals_zip=None):
    root = Path(root)
    manifest = json.loads((root/'manifest.json').read_text(encoding='utf-8-sig'))
    hashes = {r['file'].replace('\\', '/'): r['sha256'] for r in manifest}
    sources, issues = [], []
    z = zipfile.ZipFile(originals_zip) if originals_zip else None
    try:
        def read_original(name):
            key = 'Originales/'+name
            local = root/key
            if z:
                matches = [n for n in z.namelist() if n.endswith('/'+key)]
                if len(matches) != 1:
                    raise ValueError(f'Original ambiguo o ausente en ZIP: {key}')
                raw = z.read(matches[0])
                origin = str(originals_zip)+'::'+matches[0]
            else:
                raw, origin = local.read_bytes(), str(local)
            actual = hashlib.sha256(raw).hexdigest()
            if hashes.get(key) != actual:
                raise ValueError(f'SHA-256 no coincide con manifiesto: {key}')
            sources.append(dict(file=origin, sha256=actual, bytes=len(raw)))
            if z and local.exists() and sha256(local) != actual:
                issues.append(dict(type='local_original_replaced', file=key,
                                   reason='Se usa copia ZIP verificada; original local no coincide',
                                   local_sha256=sha256(local), verified_sha256=actual))
            return list(csv.DictReader(io.StringIO(raw.decode('utf-8-sig')), delimiter=';'))

        # All original hashes are checked, not only the table used for calculation.
        originals = {n: read_original(n) for n in
                     ('Trades.csv', 'Resumen.csv', 'Analisis.csv', 'Ordenes.csv', 'ejecuciones.csv')}
    finally:
        if z:
            z.close()
    trades = originals['Trades.csv']
    if len(trades) != 469 or any(t['Cuenta'] != 'Backtest' for t in trades):
        raise ValueError('Esta primera versión requiere BT004 histórico de 469 filas')
    log_keys = [k for k in hashes if re.fullmatch(r'Evidencias/[0-9a-f]{32}\.csv', k)]
    if len(log_keys) != 1:
        raise ValueError('Registro propio ambiguo o ausente')
    log = root/log_keys[0]
    log_hash = sha256(log)
    if log_hash != hashes[log_keys[0]]:
        raise ValueError('Registro propio no coincide con SHA-256 archivado')
    sources.append(dict(file=str(log), sha256=log_hash, bytes=log.stat().st_size))
    logged, skipped, submitted, ends, metadata, warning_dates = [], {}, {}, [], {}, {}
    with log.open(encoding='utf-8-sig', newline='') as source:
        for r in csv.DictReader(source):
            for field in ('mas_percent', 'mas_sessions', 'mas_type', 'release', 'calendar',
                          'zone', 'instrument', 'assembly_mvid', 'roundtrip_fee'):
                if r.get(field):
                    metadata.setdefault(field, set()).add(r[field])
            if r['event'] == 'TRADE':
                logged.append(r)
            elif r['event'] == 'SKIP':
                if int(r['open']) != 0:
                    warning_dates[r['session']] = 'SKIP con exposición no nula'
                skipped[r['session']] = r
            elif r['event'] == 'SUBMIT':
                if r['session'] in submitted:
                    raise ValueError('Más de una entrada solicitada por sesión')
                submitted[r['session']] = r
            elif r['event'] in ('WARNING', 'ERROR'):
                warning_dates[r['session']] = r['event']+':'+r['reason']
            elif r['event'] == 'END':
                ends.append(r)
    if metadata['mas_percent'] != {'40'} or metadata['zone'] != {'Eastern Standard Time'}:
        raise ValueError('BT o zona no acreditan MAS 40 / Nueva York')
    if len(logged) != len(trades) or len(ends) != 1 or int(ends[0]['open']) != 0:
        raise ValueError('Registro incompleto o posición final abierta')
    by_day, control_gross, control_native_fee, control_qty = {}, ZERO, ZERO, 0
    for t, r in zip(trades, logged):
        entry = datetime.strptime(t['Tiempo de entrada'], '%d/%m/%Y %H:%M:%S')
        exit_time = datetime.strptime(t['Tiempo de salida'], '%d/%m/%Y %H:%M:%S')
        # Guard the source labels without silently converting already-NY times.
        day = session_label(entry, profile.session_rollover_hour)
        if (session_label(exit_time, profile.session_rollover_hour) != day or
                entry.date() != exit_time.date() or day.isoformat() != r['session']):
            raise ValueError('Campaña cruza jornada; requiere revisión, no corrección automática')
        if entry > exit_time or exit_time.strftime('%Y-%m-%d %H:%M:%S') != r['event_time_ny'][:19]:
            raise ValueError('Cronología de fills no conciliada')
        qty = int(t['Cant.'])
        gross = native_money(t['Ganancias']) + native_money(t['Comisión'])
        if (qty != int(r['filled']) or qty != int(r['exited']) or int(r['open']) != 0 or
                gross != money(r['gross_realised']) or
                native_money(t['Precio de entrada']) != money(r['entry_average']) or
                native_money(t['Precio de salida']) != money(r['exit_average'])):
            raise ValueError('Cantidades/precios/resultado no conciliados con TRADE')
        if day in by_day or day.isoformat() not in submitted:
            raise ValueError('Más de una campaña por sesión o solicitud ausente')
        if money(r['commission_paid']) != qty*money(r['roundtrip_fee']):
            raise ValueError('Comisión propia no concilia con contratos ejecutados')
        if gross-money(r['commission_paid']) != money(r['net_liquidation']):
            raise ValueError('Neto propio no conciliado')
        by_day[day] = Campaign(r['run']+':'+r['session'], qty, gross,
                               qty*profile.roundtrip_commission, t['Número de trade'])
        control_gross += gross
        control_native_fee += native_money(t['Comisión'])
        control_qty += qty
    # Use the archived classification of every date without an entry.
    quality_key = 'Evidencias/Fechas_sin_entrada.csv'
    quality_path = root/quality_key
    if sha256(quality_path) != hashes[quality_key]:
        raise ValueError('Clasificación de días no coincide con manifiesto')
    with quality_path.open(encoding='utf-8-sig', newline='') as source:
        no_entry = {r['date']: r for r in csv.DictReader(source)}
    sources.append(dict(file=str(quality_path), sha256=hashes[quality_key]))
    start = date(2025, 1, 6)
    last_event = date.fromisoformat(ends[0]['event_time_ny'][:10])
    if completed_through < start or completed_through >= last_event:
        raise ValueError('Corte requiere última sesión completa ANTERIOR al día final truncado')
    sessions = []
    day = start
    while day <= completed_through:
        key = day.isoformat()
        c = by_day.get(day)
        if key in warning_dates:
            s = Session(day, c, 'unknown', warning_dates[key])
        elif c:
            s = Session(day, c, reason='closed_trade_reconciled')
        elif key in skipped:
            s = Session(day, reason='SKIP flat: '+skipped[key]['reason'])
            issues.append(dict(date=key, type='known_flat_skip', reason=s.reason))
        elif key in no_entry and no_entry[key]['type'] == 'Calendario':
            s = Session(day, reason=no_entry[key]['reason'], operable=False)
        else:
            s = Session(day, quality='unknown', reason='Sin operación ni SKIP plano ni calendario acreditado')
            issues.append(dict(date=key, type='unknown_day', reason=s.reason))
        sessions.append(s)
        day += timedelta(days=1)
    excluded = [d.isoformat() for d in by_day if d > completed_through]
    issues.append(dict(type='right_censoring', date=last_event.isoformat(),
                       reason='Sesión final truncada excluida, incluida su operación cerrada',
                       excluded_trade_dates=excluded))
    quality = dict(source_bt='BT004', source_strategy_commit='e56baf17e484c8ae429c9156f41e007d1c2a1af5',
                   start=start.isoformat(), completed_through=completed_through.isoformat(),
                   original_end=last_event.isoformat(), source_trades=len(trades),
                   included_campaigns=sum(s.campaign is not None for s in sessions),
                   source_gross_usd=usd(control_gross), source_native_fees_usd=usd(control_native_fee),
                   source_quantity=control_qty, sources=sources, issues=issues,
                   metadata={k: sorted(v) for k, v in metadata.items()},
                   session_convention='America/New_York; rollover 18:00 NY; campañas 09:36–cierre misma fecha',
                   hidden_floating_breach_assumed_absent=True,
                   equivalence_to_actual_firm_rules_certified=False)
    return sessions, quality

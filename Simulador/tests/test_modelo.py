"""Ejemplos manuales de §9 y regresiones, todos con datos sintéticos."""
import unittest
from dataclasses import replace
from datetime import date, datetime, timedelta
from decimal import Decimal as D
from Simulador.modelo import Campaign, Profile, Session, money, simulate
from Simulador.bt004 import campaign_from_parts, session_label


def days(*pnls):
    return [Session(date(2025, 1, 6)+timedelta(days=i),
                    Campaign(str(i), 1, money(p), D('0'))) for i, p in enumerate(pnls)]


class ManualSpecification(unittest.TestCase):
    def setUp(self):
        self.profile = Profile()

    def run_days(self, *values):
        return simulate(days(*values), self.profile)

    def test_01_two_equal_days_pass_conditionally(self):
        r = self.run_days(1500, 1500)
        a = r['attempts'][0]
        self.assertEqual(a['status'], 'passed_conditional')
        self.assertEqual(a['traded_sessions'], 2)
        self.assertEqual(a['balance_usd'], '53000.00')
        self.assertEqual(a['consistency'], '0.5')

    def test_02_loss_then_win(self):
        a = self.run_days(-100, 1500)['attempts'][0]
        self.assertEqual(a['balance_usd'], '51400.00')
        self.assertEqual(a['threshold_usd'], '49400.00')
        self.assertEqual(a['remaining_margin_usd'], '2000.00')
        self.assertEqual(a['status'], 'pending')

    def test_03_win_then_loss(self):
        a = self.run_days(1500, -100)['attempts'][0]
        self.assertEqual(a['balance_usd'], '51400.00')
        self.assertEqual(a['threshold_usd'], '49500.00')
        self.assertEqual(a['remaining_margin_usd'], '1900.00')

    def test_04_touch_then_recovery_cannot_rescue(self):
        s = days(-100, 3500)
        s[1] = replace(s[1], observed_pnl_min=D('-1900'))
        r = simulate(s, self.profile)
        self.assertEqual(r['attempts'][0]['status'], 'failed')
        self.assertEqual(r['attempts'][0]['balance_usd'], '48000.00')
        self.assertEqual(r['daily'][1]['decision'], 'proven_breach')
        self.assertEqual(r['summary']['purchased'], 1)

    def test_05_no_commercial_tolerance_inferred(self):
        a = self.run_days(1501, 1500)['attempts'][0]
        self.assertEqual(a['status'], 'pending')
        self.assertEqual(a['profit_usd'], '3001.00')
        self.assertEqual(a['near_consistency_events'], 1)
        self.assertGreater(D(a['consistency']), D('.50'))

    def test_06_cap_and_no_lowering(self):
        r = self.run_days(2100, -100, 100)
        self.assertEqual([x['next_limit_usd'] for x in r['daily']], ['50100.00']*3)
        self.assertEqual(r['daily'][0]['limit_during_session_usd'], '48000.00')

    def test_07_partials_one_campaign_actual_quantity_cost(self):
        c = campaign_from_parts('one_order', [dict(quantity=2, amount='100'),
                                 dict(quantity=3, amount='200')], D('1.82'))
        self.assertEqual(c.quantity, 5)
        self.assertEqual(c.net, D('290.90'))
        r = simulate([Session(date(2025, 1, 6), c)], self.profile)
        self.assertEqual(r['summary']['purchased'], 1)
        self.assertEqual(r['attempts'][0]['traded_sessions'], 1)

    def test_08_failure_at_end_no_phantom_purchase(self):
        r = self.run_days(-2000)
        self.assertEqual(r['summary']['failed'], 1)
        self.assertEqual(r['summary']['purchased'], 1)
        self.assertEqual(r['summary']['fees_usd'], '110.00')

    def test_09_open_attempt_is_pending_fee_counted(self):
        r = self.run_days(100)
        self.assertEqual(r['summary']['pending'], 1)
        self.assertEqual(r['summary']['fees_usd'], '110.00')
        self.assertIsNone(r['summary']['cost_per_conditional_pass_usd'])
        self.assertEqual(r['summary']['cost_per_pass_status'], 'sin aprobaciones')

    def test_10_known_flat_skip_preserves_account(self):
        s = days(1500, 0, 1500)
        s[1] = Session(s[1].day, reason='SKIP flat: missing regular data')
        r = simulate(s, self.profile)
        self.assertEqual(r['summary']['purchased'], 1)
        self.assertEqual(r['attempts'][0]['traded_sessions'], 2)
        self.assertEqual(r['attempts'][0]['calendar_days'], 3)
        self.assertEqual(r['summary']['passed_conditional'], 1)

    def test_11_unknown_exposure_halts_segment(self):
        s = days(1500, 1000, 1500, 1500)
        s[1] = replace(s[1], quality='unknown', reason='Gap with exposure')
        r = simulate(s, self.profile)
        self.assertEqual(r['summary']['indeterminate'], 1)
        self.assertEqual(r['summary']['purchased'], 1)
        self.assertEqual(len(r['daily']), 2)
        self.assertEqual(r['stopped_at'], s[1].day.isoformat())

    def test_12_native_net_commission_not_twice(self):
        part = dict(quantity=10, amount='1481.80', native_commission='18.20')
        c = campaign_from_parts('native', [part], D('1.82'), native_net=True)
        self.assertEqual(c.gross, D('1500'))
        self.assertEqual(c.net, D('1481.80'))
        alt = campaign_from_parts('native', [part], D('1.90'), native_net=True)
        self.assertEqual(alt.net, D('1481.00'))


class Regression(unittest.TestCase):
    def test_restart_after_approval_next_day(self):
        r = simulate(days(1500, 1500, 1500, 1500), Profile())
        self.assertEqual([a['status'] for a in r['attempts']], ['passed_conditional']*2)
        self.assertEqual(r['summary']['fees_usd'], '220.00')
        self.assertEqual(r['attempts'][1]['start'], '2025-01-08')

    def test_restart_after_failure_next_actual_entry(self):
        s = days(-2000, 0, 100)
        s[1] = Session(s[1].day, operable=False, reason='market_closed')
        r = simulate(s, Profile())
        self.assertEqual(r['attempts'][1]['start'], '2025-01-08')
        self.assertEqual(r['summary']['purchased'], 2)
        self.assertEqual(r['attempts'][1]['balance_usd'], '50100.00')

    def test_above_threshold_one_cent_survives(self):
        self.assertEqual(simulate(days('-1999.99'), Profile())['summary']['pending'], 1)

    def test_below_threshold_fails(self):
        self.assertEqual(simulate(days('-2000.01'), Profile())['summary']['failed'], 1)

    def test_threshold_during_loss_is_previous_eod(self):
        r = simulate(days(1500, -2000), Profile())
        self.assertEqual(r['daily'][1]['limit_during_session_usd'], '49500.00')
        self.assertEqual(r['summary']['failed'], 1)

    def test_size_not_resized(self):
        c = Campaign('oversize', 41, D('3000'), D('0'))
        r = simulate([Session(date(2025, 1, 6), c)], Profile())
        self.assertEqual(r['summary']['failed'], 1)
        self.assertEqual(c.quantity, 41)

    def test_fees_external_to_balance(self):
        r = simulate(days(1500, 1500), replace(Profile(), attempt_fee=D('999')))
        self.assertEqual(r['attempts'][0]['balance_usd'], '53000.00')
        self.assertEqual(r['summary']['fees_usd'], '999.00')

    def test_restart_switch_no_new_account(self):
        r = simulate(days(1500, 1500, 100), replace(Profile(), restart_after_pass=False))
        self.assertEqual(r['summary']['purchased'], 1)

    def test_unknown_before_purchase_no_fee(self):
        s = [Session(date(2025, 1, 6), quality='unknown')]
        self.assertEqual(simulate(s, Profile())['summary']['fees_usd'], '0.00')

    def test_missing_date_not_silently_skipped(self):
        s = days(100, 100, 100)
        with self.assertRaises(ValueError):
            simulate([s[0], s[2]], Profile())

    def test_duplicate_day_rejected(self):
        with self.assertRaises(ValueError):
            simulate(days(100)*2, Profile())

    def test_nan_and_fractional_cent_rejected(self):
        for bad in ('NaN', 'Infinity', '1.001'):
            with self.assertRaises(ValueError):
                money(bad)

    def test_unsupported_dll_rejected(self):
        with self.assertRaises(ValueError):
            replace(Profile(), daily_loss_limit=1000)

    def test_annual_buys_and_endings(self):
        s = [Session(date(2025, 12, 31), Campaign('1', 1, D('1500'), D('0'))),
             Session(date(2026, 1, 1), Campaign('2', 1, D('1500'), D('0')))]
        r = simulate(s, Profile())
        self.assertEqual(r['summary']['by_year']['2025']['purchased'], 1)
        self.assertEqual(r['summary']['by_year']['2026']['passed_conditional'], 1)
        self.assertTrue(r['attempts'][0]['crosses_year'])

    def test_new_york_session_labels_across_dst(self):
        for text in ('2026-03-06T09:36', '2026-03-09T09:36',
                     '2026-10-30T09:36', '2026-11-02T09:36'):
            dt = datetime.fromisoformat(text)
            self.assertEqual(session_label(dt), dt.date())
            self.assertEqual(session_label(dt.replace(hour=18)), dt.date()+timedelta(days=1))

    def test_identity_counts_and_cost_cycles(self):
        r = simulate(days(-2000, 1500, 1500, 100), Profile())
        s = r['summary']
        self.assertEqual(s['purchased'], sum(s[k] for k in ['passed_conditional','failed','pending','indeterminate']))
        self.assertEqual(s['approvals'][0]['fees_since_previous_approval_usd'], '220.00')
        self.assertEqual(s['final_fees_without_new_approval_usd'], '110.00')
        self.assertEqual(s['cost_per_conditional_pass_usd'], '330.00')


if __name__ == '__main__':
    unittest.main()

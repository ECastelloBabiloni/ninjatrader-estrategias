"""v0.2: conservative study criteria, NOT Lucid's optional DLL."""
import unittest
from dataclasses import replace
from datetime import date, timedelta
from decimal import Decimal as D
from Simulador.modelo import Campaign, Profile, Session, simulate
from Simulador.bt004 import campaign_from_parts


def run_values(*pnls, profile=None):
    sessions = [Session(date(2025, 1, 6)+timedelta(days=i),
                        Campaign(str(i), 1, D(str(pnl)), D('0')))
                for i, pnl in enumerate(pnls)]
    return simulate(sessions, profile or Profile())


class StudyV02(unittest.TestCase):
    def test_loss_1969_99_survives_with_30_01_margin(self):
        r = run_values('-1969.99')
        self.assertEqual(r['summary']['pending'], 1)
        self.assertEqual(r['attempts'][0]['remaining_margin_usd'], '30.01')

    def test_loss_1970_exact_fails_both_rules_once(self):
        r = run_values('-1970.00')
        self.assertEqual(r['summary']['failed'], 1)
        self.assertEqual(r['summary']['purchased'], 1)
        self.assertEqual(r['summary']['fees_usd'], '110.00')
        self.assertEqual(r['attempts'][0]['suspension_causes'],
                         'study_campaign_net_loss_limit|study_account_buffer')

    def test_loss_1998_fails(self):
        a = run_values('-1998')['attempts'][0]
        self.assertEqual(a['status'], 'failed')
        self.assertEqual(a['remaining_margin_usd'], '2.00')

    def test_loss_2050_not_exempt_by_interval(self):
        a = run_values('-2050')['attempts'][0]
        self.assertEqual(a['status'], 'failed')
        self.assertEqual(a['reason'], 'official_modeled_threshold_contact')
        self.assertIn('study_campaign_net_loss_limit', a['suspension_causes'])

    def test_loss_limit_even_with_large_remaining_margin(self):
        r = run_values('2500', '-1980')
        a = r['attempts'][0]
        self.assertEqual(a['status'], 'failed')
        self.assertEqual(a['remaining_margin_usd'], '420.00')
        self.assertEqual(a['suspension_causes'], 'study_campaign_net_loss_limit')

    def test_accumulated_smaller_losses_cross_buffer(self):
        a = run_values('-1000', '-980')['attempts'][0]
        self.assertEqual(a['status'], 'failed')
        self.assertEqual(a['suspension_causes'], 'study_account_buffer')
        self.assertEqual(a['remaining_margin_usd'], '20.00')

    def test_accumulated_contact_official_threshold(self):
        a = run_values('-1000', '-1000')['attempts'][0]
        self.assertEqual(a['suspension_causes'], 'official_modeled_threshold_contact')

    def test_margin_30_01_30_00_29_99(self):
        for loss, status in [('-969.99','pending'),('-970.00','failed'),('-970.01','failed')]:
            with self.subTest(loss=loss):
                a = run_values('-1000', loss)['attempts'][0]
                self.assertEqual(a['status'], status)
                if status == 'failed':
                    self.assertEqual(a['suspension_causes'], 'study_account_buffer')

    def test_january_6_7_8_restart_acceptance(self):
        r = run_values('1502.76', '-1998.56', '-1997.24')
        self.assertEqual([a['status'] for a in r['attempts']], ['failed', 'failed'])
        self.assertEqual(r['attempts'][0]['end'], '2025-01-07')
        self.assertEqual(r['attempts'][1]['start'], '2025-01-08')
        self.assertEqual(r['attempts'][1]['balance_usd'], '48002.76')
        self.assertEqual(r['daily'][2]['balance_before_usd'], '50000.00')
        self.assertEqual(r['daily'][2]['limit_during_session_usd'], '48000.00')
        self.assertEqual(r['summary']['fees_usd'], '220.00')
        self.assertIn('study_account_buffer', r['attempts'][0]['suspension_causes'])

    def test_partials_complete_campaign_one_fee(self):
        parts = [dict(quantity=1, amount='-983.18'), dict(quantity=1, amount='-983.18')]
        c = campaign_from_parts('entry1', parts, D('1.82'))
        self.assertEqual(c.net, D('-1970.00'))
        r = simulate([Session(date(2025, 1, 6), c)], Profile())
        self.assertEqual(r['summary']['purchased'], 1)
        self.assertEqual(r['summary']['failed'], 1)
        self.assertEqual(r['attempts'][0]['commission_usd'], '3.64')

    def test_native_fee_replaced_once_both_scenarios(self):
        parts = [dict(quantity=2, amount='-1970.00', native_commission='3.64')]
        for fee, expected in [('1.82','-1970.00'),('1.90','-1970.16')]:
            c = campaign_from_parts('native', parts, D(fee), native_net=True)
            self.assertEqual(c.net, D(expected))
            r = simulate([Session(date(2025, 1, 6), c)], Profile())
            self.assertEqual(r['summary']['failed'], 1)

    def test_observed_buffer_precedes_possible_approval(self):
        s = Session(date(2025, 1, 6), Campaign('recover', 1, D('3500'), D('0')),
                    observed_pnl_min=D('-1970'))
        r = simulate([s], Profile())
        self.assertEqual(r['summary']['failed'], 1)
        self.assertEqual(r['summary']['passed_conditional'], 0)
        self.assertEqual(r['attempts'][0]['reason'], 'study_account_buffer')

    def test_official_breach_cannot_be_disabled_with_buffer(self):
        a = run_values('-2050', profile=replace(Profile(),loss_buffer_usd=D('0'),
                                              campaign_loss_limit_usd=None))['attempts'][0]
        self.assertEqual(a['reason'], 'official_modeled_threshold_contact')

    def test_invalid_settings(self):
        for kwargs in [dict(loss_buffer_usd=D('-1')),dict(campaign_loss_limit_usd=D('0'))]:
            with self.assertRaises(ValueError):
                replace(Profile(), **kwargs)


if __name__ == '__main__':
    unittest.main()

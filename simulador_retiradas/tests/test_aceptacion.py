import unittest
from datetime import datetime, date, timedelta
from decimal import Decimal as D
from dataclasses import replace
from simulador_retiradas.modelo import Profile, Campaign, Sample, simulate, session_day
from simulador_retiradas.fuente import timestamp, canonical


class Acceptance(unittest.TestCase):
    def run_days(self, days, mode='cierres', fee=D(0), customize=None):
        cs=[];sessions=[]
        for i,profits in enumerate(days):
            day=date(2026,1,5)+timedelta(days=i);sessions.append(day)
            for j,p in enumerate(profits):
                t=datetime.combine(day,datetime.min.time())+timedelta(hours=9,minutes=j*2)
                cs.append(Campaign(str(len(cs)),t,t+timedelta(minutes=1),D(str(p)),1))
        if customize: customize(cs)
        return simulate(cs,sessions,Profile(gap_policy='stop'),fee,mode)

    def test_stop_two_600(self):
        r=self.run_days([[300,300,999]])
        self.assertEqual(r['daily'][0]['pnl'],600)
        self.assertEqual(r['campaigns'][-1]['decision'],'OMITIDA')

    def test_three_needed(self):
        r=self.run_days([[300,-200,300]])
        self.assertEqual(r['daily'][0]['closed_campaigns'],3)
        self.assertEqual(r['daily'][0]['pnl'],400)

    def test_four_needed(self):
        r=self.run_days([[300,-500,300,300]])
        self.assertEqual(r['daily'][0]['closed_campaigns'],4)
        self.assertEqual(r['daily'][0]['pnl'],400)

    def test_first_600_not_stop(self):
        r=self.run_days([[600,1]])
        self.assertEqual(r['daily'][0]['pnl'],601)

    def test_14999_continue(self):
        r=self.run_days([[100,'49.99',1]])
        self.assertEqual(r['daily'][0]['closed_campaigns'],3)

    def test_150_stop(self):
        r=self.run_days([[100,50,999]])
        self.assertEqual(r['daily'][0]['closed_campaigns'],2)

    def test_recovery_one_campaign(self):
        def change(cs):cs[0].legs=2;cs[0].contracts=40;cs[0].max_contracts=40
        r=self.run_days([[50]],customize=change)
        self.assertEqual(r['daily'][0]['closed_campaigns'],1)
        self.assertEqual(r['daily'][0]['pnl'],50)
        self.assertIn('sin aplicar el escalado',r['summary']['warning'])

    def test_one_signal_still_valid(self):
        r=self.run_days([[300]])
        self.assertTrue(r['daily'][0]['valid'])
        self.assertTrue(r['daily'][0]['minimum_policy_unmet'])

    def test_accumulated_loss_and_no_same_day_restart(self):
        r=self.run_days([[-1000,-1000,9999],[300]])
        self.assertEqual(r['accounts'][0]['status'],'PERDIDA')
        self.assertEqual(r['campaigns'][2]['decision'],'OMITIDA')
        self.assertEqual(r['accounts'][1]['start'],'2026-01-06')

    def test_float_breach_not_rescued(self):
        def change(cs):
            c=cs[0];c.samples=[Sample(c.start,D(-2000),1),Sample(c.end,D(300),1)]
        r=self.run_days([[300]],mode='intradia',customize=change)
        self.assertEqual(r['accounts'][0]['status'],'PERDIDA')
        self.assertEqual(r['accounts'][0]['observed_equity_at_loss'],48000)
        self.assertEqual(r['accounts'][0]['balance'],50000)

    def test_five_150_insufficient_request(self):
        r=self.run_days([[150]]*5)
        self.assertEqual(r['accounts'][0]['status'],'PENDIENTE_FIN_DATOS')
        self.assertEqual(r['accounts'][0]['valid_days'],5)

    def test_five_200(self):
        a=self.run_days([[200]]*5)['accounts'][0]
        self.assertEqual((a['request'],a['cash_estimated'],a['post_balance'],a['L'],a['residual_margin']),
                         (500,450,50500,50100,400))

    def test_five_600(self):
        a=self.run_days([[600]]*5)['accounts'][0]
        self.assertEqual((a['request'],a['cash_estimated'],a['post_balance'],a['residual_margin']),
                         (1500,1350,51500,1400))

    def test_negative_day_retains_valid_days(self):
        a=self.run_days([[300],[300],[-100],[300],[300],[300]])['accounts'][0]
        self.assertEqual(a['valid_days'],5)
        self.assertEqual(a['balance'],51400)

    def test_midnight_same_session_and_dst(self):
        for t in ('2026-03-08T23:00:00','2026-03-09T01:00:00'):
            self.assertEqual(session_day(timestamp(t)),date(2026,3,9))
        self.assertEqual(timestamp('2026-11-01T18:00:00').hour,18)
        with self.assertRaises(ValueError):timestamp('2026-11-01T01:30:00')
        with self.assertRaises(ValueError):timestamp('2026-03-08T02:30:00')

    def test_no_double_commission(self):
        r=self.run_days([['301.82']],fee=D('1.82'))
        self.assertEqual(r['daily'][0]['pnl'],300)
        self.assertEqual(canonical('300.000000000001'),300)
        with self.assertRaises(ValueError):canonical('149.999')

    def test_gap_indeterminate_no_restart(self):
        def change(cs):
            cs[0].samples=[Sample(cs[0].start,D(0),1),Sample(cs[0].end+timedelta(minutes=3),D(300),1)]
        r=self.run_days([[300],[300]],mode='intradia',customize=change)
        self.assertEqual(r['accounts'][0]['status'],'INDETERMINADA')
        self.assertEqual(len(r['accounts']),1)

    def test_monotonic_L_and_cap(self):
        r=self.run_days([[300],[-100],[200],[-200],[300],[300]])
        levels=[d['L_next'] for d in r['daily']]
        self.assertEqual(levels,sorted(levels))
        self.assertTrue(all(x<=50100 for x in levels))

    def test_request_cap_and_floor(self):
        a=self.run_days([[1000]]*5)['accounts'][0]
        self.assertEqual(a['request'],2000)
        a=self.run_days([['200.01']]*5)['accounts'][0]
        self.assertEqual(a['request'],D('500.02'))

    def test_breach_before_payout(self):
        r=self.run_days([[600]]*4+[[-2300,10000]])
        self.assertEqual(r['accounts'][0]['status'],'PERDIDA')
        self.assertEqual(r['accounts'][0]['request'],0)
        self.assertEqual(sum(r['summary']['states'].values()),r['summary']['started'])

    def test_contact_no_30_buffer(self):
        r=self.run_days([[-1970]])
        self.assertEqual(r['accounts'][0]['status'],'PENDIENTE_FIN_DATOS')
        r=self.run_days([[-2000]])
        self.assertEqual(r['accounts'][0]['status'],'PERDIDA')

    def test_unknown_day_stops(self):
        t=datetime(2026,1,5,9)
        c=Campaign('a',t,t+timedelta(minutes=1),D(300),1)
        r=simulate([c],[t.date(),date(2026,1,6)],Profile(),D(0),'cierres',[date(2026,1,6)])
        self.assertEqual(r['accounts'][0]['status'],'INDETERMINADA')

    def test_scaling_is_explicit(self):
        with self.assertRaises(ValueError):replace(Profile(),ignore_scaling=False)


if __name__=='__main__':unittest.main()


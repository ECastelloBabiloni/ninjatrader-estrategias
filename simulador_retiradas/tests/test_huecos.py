import unittest
from dataclasses import replace
from datetime import datetime, timedelta
from decimal import Decimal as D
from simulador_retiradas.modelo import Campaign, Sample, Profile, simulate

class Gaps(unittest.TestCase):
    def campaign(self, day=5, minute=0, gap=False, loss=False):
        t=datetime(2026,1,day,9,minute)
        end=t+timedelta(minutes=4 if gap else 1)
        samples=[Sample(t,D(-2000 if loss else 0),1),Sample(end,D(300),1)]
        return Campaign(f'{day}-{minute}',t,end,D(300),1,samples)

    def run_case(self, cs, policy='loss_and_restart'):
        days=sorted({c.start.date() for c in cs})
        return simulate(cs,days,Profile(gap_policy=policy),D(0),'intradia')

    def test_gap_loses_once_skips_day_and_resets_next_day(self):
        cs=[self.campaign(),self.campaign(minute=2,gap=True),
            self.campaign(minute=10),self.campaign(day=6)]
        r=self.run_case(cs)
        a,b=r['accounts']
        self.assertEqual(a['status'],'PERDIDA')
        self.assertEqual(a['reason'],'HUECO_ASUMIDO_PERDIDA')
        self.assertEqual(a['balance'],50300)
        self.assertNotIn('observed_equity_at_loss',a)
        self.assertEqual(r['campaigns'][2]['decision'],'OMITIDA')
        n=r['campaigns'][3]
        self.assertEqual((n['balance_before'],n['L'],n['daily_count']),(50000,48000,1))
        self.assertEqual(b['valid_days'],1)
        self.assertEqual(r['summary']['lost_assumed_due_to_gap'],1)
        self.assertEqual(r['summary']['lost_observed'],0)
        self.assertEqual(sum(r['summary']['states'].values()),r['summary']['started'])

    def test_omitted_gap_does_not_lose_account(self):
        r=self.run_case([self.campaign(),self.campaign(minute=2),self.campaign(minute=4,gap=True)])
        self.assertEqual(r['summary']['lost_assumed_due_to_gap'],0)
        self.assertEqual(r['accounts'][0]['balance'],50600)

    def test_observed_loss_before_gap_takes_priority(self):
        r=self.run_case([self.campaign(gap=True,loss=True)])
        self.assertEqual(r['accounts'][0]['reason'],'EQUITY_OBSERVADA_TOCA_L')
        self.assertEqual(r['summary']['lost_observed'],1)
        self.assertEqual(r['summary']['lost_assumed_due_to_gap'],0)

    def test_legacy_stop_is_available(self):
        r=self.run_case([self.campaign(gap=True),self.campaign(day=6)],'stop')
        self.assertEqual(len(r['accounts']),1)
        self.assertEqual(r['accounts'][0]['status'],'INDETERMINADA')

    def test_final_day_gap_does_not_create_phantom_account(self):
        r=self.run_case([self.campaign(gap=True)])
        self.assertEqual(len(r['accounts']),1)
        self.assertEqual(r['accounts'][0]['status'],'PERDIDA')

    def test_invalid_policy_rejected(self):
        with self.assertRaises(ValueError):Profile(gap_policy='invent')

    def test_negative_time_remains_indeterminate(self):
        c=self.campaign();c.samples.reverse()
        r=self.run_case([c])
        self.assertEqual(r['accounts'][0]['status'],'INDETERMINADA')

if __name__=='__main__': unittest.main()

"""Synthetic fixtures only; no backtest or market data in Git."""
import csv
import tempfile
import unittest
from pathlib import Path
from simulador_retiradas.fuente import load, calendar
from simulador_retiradas.modelo import Profile
from datetime import date
from decimal import Decimal


class Reader(unittest.TestCase):
    def fixture(self, root, corrupt=False):
        for folder in ('Registro','Originales','Codigo'):(root/folder).mkdir()
        # Reuse public calendar source, never source market records.
        repo=Path(__file__).resolve().parents[2]
        (root/'Codigo'/'CalendarioSesionesNY.cs').write_bytes((repo/'AddOns'/'CalendarioSesionesNY.cs').read_bytes())
        keys=['run','version','time_ny','event','reason','position','instrument','quantity','bought','sold',
              'realised_gross','net_liquidation','fee_reserved','detail']
        rows=[]
        def add(t,event,qty=0,bought=0,sold=0,gross='0',net='0',fee='0',reason='',detail=''):
            rows.append(dict(zip(keys,['synthetic','test',t,event,reason,'campaign','MNQ 12-26',qty,bought,sold,gross,net,fee,detail])))
        add('0001-01-01T00:00:00','CONFIG',detail='fee_roundtrip=1,82')
        add('2026-01-04T18:00:00','BLOCK')
        add('2026-01-05T09:00:00','FILL_ENTRY',25,25,0,net='-45.5',fee='45.5',reason='RN_E1',detail='quantity=25')
        add('2026-01-05T09:01:00','FILL_ENTRY',40,40,0,net='-72.8',fee='72.8',reason='RN_E2',detail='quantity=15')
        add('2026-01-05T09:02:00','FILL_EXIT',0,40,40,gross='122.8',net='50',fee='72.8',detail='quantity=40')
        add('2026-01-05T09:02:00','POSITION_RESULT',0,40,40,gross='122.8',net='49' if corrupt else '50',fee='72.8')
        add('2026-01-05T09:02:00','ENTRY_RESULT',detail='gross=60;fee=45.5;net=14.5',reason='RN_E1')
        add('2026-01-05T09:02:00','ENTRY_RESULT',detail='gross=62.8;fee=27.3;net=35.5',reason='RN_E2')
        add('2026-01-06T07:00:00','END')
        with (root/'Registro'/'test_events.csv').open('w',newline='',encoding='utf-8') as f:
            w=csv.DictWriter(f,fieldnames=keys);w.writeheader();w.writerows(rows)
        (root/'Originales'/'Trades.csv').write_text('Cant.;Ganancias;Comisión\n25;12,50;47,50\n15;34,30;28,50\n',encoding='utf-8')

    def test_joint_recovery_and_fees(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);self.fixture(root)
            cs,ss,unknown,q,cov=load(root,Profile())
            self.assertEqual(len(cs),1)
            self.assertEqual(cs[0].contracts,40)
            self.assertEqual(cs[0].legs,2)
            self.assertEqual(cs[0].net(Profile().commission),50)
            self.assertEqual(q['analyzer_net'],Decimal('46.80'))
            self.assertEqual(ss,[date(2026,1,5)])
            self.assertFalse(unknown)

    def test_corrupt_net_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);self.fixture(root,True)
            with self.assertRaisesRegex(ValueError,'commission/net'):load(root,Profile())

    def test_calendar_holiday_and_dst(self):
        resolve=calendar(Path(__file__).resolve().parents[2]/'AddOns'/'CalendarioSesionesNY.cs')
        self.assertEqual(resolve(date(2026,6,19))[0].hour,13)
        self.assertEqual(resolve(date(2026,3,9))[0].hour,17)
        self.assertEqual(resolve(date(2026,11,2))[0].hour,17)
        self.assertIsNone(resolve(date(2026,12,25))[0])

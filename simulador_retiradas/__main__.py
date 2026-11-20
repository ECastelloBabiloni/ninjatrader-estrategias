"""python -m simulador_retiradas --bt-root ... --out ... (never trades)."""
import argparse
import csv
import json
import subprocess
import platform
from dataclasses import asdict
from pathlib import Path
from . import __version__
from .fuente import load, sha
from .modelo import Profile, simulate, WARNING


def write_csv(path, rows):
    keys=list(dict.fromkeys(k for row in rows for k in row))
    with path.open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=keys)
        writer.writeheader();writer.writerows(rows)


def main():
    p=argparse.ArgumentParser(description=WARNING)
    p.add_argument('--bt-root',type=Path,required=True)
    p.add_argument('--profile',type=Path,default=Path(__file__).with_name('perfil.json'))
    p.add_argument('--out',type=Path,required=True)
    args=p.parse_args()
    repo=Path(__file__).resolve().parent.parent
    output=args.out.resolve()
    if output==repo or repo in output.parents or output==args.bt_root.resolve() or args.bt_root.resolve() in output.parents:
        p.error('Outputs must be outside repository and source BT')
    if output.exists() and any(output.iterdir()):
        p.error('Output not empty: refusing to overwrite a prior report')
    profile=Profile.load(json.loads(args.profile.read_text(encoding='utf-8-sig')))
    campaigns,sessions,unknown,quality,coverage=load(args.bt_root,profile)
    output.mkdir(parents=True,exist_ok=True)
    def save(name,obj):
        (output/name).write_text(json.dumps(obj,indent=2,ensure_ascii=False,default=str),encoding='utf-8')
    save('calidad_fuente.json',quality);save('perfil.json',asdict(profile))
    write_csv(output/'cobertura.csv',coverage);write_csv(output/'incidencias.csv',quality['issues'])
    runs={}
    for mode in ('cierres','intradia'):
        for label,fee in (('principal',profile.commission),('sensibilidad',profile.sensitivity)):
            key=mode+'_'+label
            r=simulate(campaigns,sessions,profile,fee,mode,unknown)
            runs[key]=r['summary']
            save(key+'.json',r)
            for table in ('accounts','daily','campaigns'):
                write_csv(output/(key+'_'+table+'.csv'),r[table])
            write_csv(output/(key+'_retiradas.csv'),[a for a in r['accounts'] if a['request']>0])
    save('resumen.json',runs)
    commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()
    dirty=subprocess.check_output(['git','status','--porcelain'],cwd=repo,text=True).strip()
    manifest=dict(version=__version__,commit=commit,working_tree_dirty=bool(dirty),
                  python=platform.python_version(),command='python -m simulador_retiradas',
                  profile_sha256=sha(args.profile),source_files=quality['files'],
                  code=[dict(path=str(f.relative_to(repo)),sha256=sha(f))
                        for f in sorted((repo/'simulador_retiradas').rglob('*'))
                        if f.is_file() and '__pycache__' not in str(f)],
                  outputs=[dict(path=f.name,sha256=sha(f)) for f in sorted(output.iterdir()) if f.is_file()])
    save('manifest.json',manifest)
    report=[WARNING,'','Retiradas elegibles estimadas; no pagos reales. Fees y coste de obtener aprobadas: NO CALCULADOS.',
            'Cobertura: '+quality['effective_start']+' a '+quality['effective_end'],
            'Cierre completo inferido del calendario archivado y continuidad posterior del registro; no auditoría de todas las barras.',
            'Última jornada truncada excluida. MARK: cierres de minuto; fills adicionales. Riesgo entre muestras desconocido.',
            'No se importan señales no ejecutadas; el filtro de campañas no reconstruye estados contrafactuales de la estrategia.',
            'INDETERMINADA detiene la secuencia; no se inventa reinicio. Escalado ignorado por autorización expresa.',
            'Comisión 1,82 principal y 1,90 sensibilidad, no tarifas actuales de Lucid. Sin slippage adicional.',
            'Si se pierde intradía, balance es el realizado anterior y observed_equity_at_loss es la muestra de pérdida; no se inventa fill de liquidación.',
            'Archivo de cuentas: balance es anterior a solicitud, post_balance refleja débito bruto; cash_estimated es 90%.',
            'Versión '+__version__+'; commit '+commit+'; dirty='+str(bool(dirty)),'']
    for key,s in runs.items():
        report += [key,json.dumps(s,ensure_ascii=False,default=str),'']
    (output/'Informe.txt').write_text('\n'.join(report),encoding='utf-8')
    manifest['outputs']=[dict(path=f.name,sha256=sha(f)) for f in sorted(output.iterdir())
                         if f.is_file() and f.name!='manifest.json']
    save('manifest.json',manifest)
    print(json.dumps(dict(quality={k:quality[k] for k in ('effective_start','effective_end','selected_campaigns','unknown_days','issues')},
                          runs={k:{x:v[x] for x in ('started','states','gross_requests','cash_estimated')} for k,v in runs.items()}),default=str))


if __name__=='__main__':main()

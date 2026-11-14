"""CLI de investigación; no toca archivos fuente ni conexiones de trading."""
import argparse
import csv
import json
import subprocess
from dataclasses import asdict, replace
from datetime import date
from pathlib import Path
from . import __version__
from .bt004 import load_bt004, sha256
from .modelo import Profile, simulate


def write_csv(path, rows):
    rows = list(rows)
    keys = list(dict.fromkeys(k for r in rows for k in r))
    with path.open('w', encoding='utf-8-sig', newline='') as target:
        writer = csv.DictWriter(target, fieldnames=keys)
        writer.writeheader()
        writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser(description='Estimación con operaciones cerradas, aprobaciones condicionales')
    parser.add_argument('--bt-root', required=True, type=Path)
    parser.add_argument('--originals-zip', type=Path)
    parser.add_argument('--profile', type=Path, default=Path(__file__).with_name('perfil_lucidflex_50k_v2.json'))
    parser.add_argument('--completed-through', required=True, type=date.fromisoformat)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    output = args.out.resolve()
    if output == repo or repo in output.parents:
        parser.error('Resultados privados deben guardarse fuera del repositorio')
    if output.exists() and any(output.iterdir()):
        parser.error('Directorio de salida no vacío: no sobrescribir otra simulación')
    profile = Profile.from_dict(json.loads(args.profile.read_text(encoding='utf-8-sig')))
    sessions, quality = load_bt004(args.bt_root, profile, args.completed_through, args.originals_zip)
    # Parse and reconcile once; sensitivity changes costs, never fills or quantity.
    secondary_sessions = [replace(s, campaign=replace(s.campaign,
                          commission=s.campaign.quantity*profile.sensitivity_commission))
                          if s.campaign else s for s in sessions]
    result = simulate(sessions, profile)
    sensitivity_profile = replace(profile, roundtrip_commission=profile.sensitivity_commission)
    sensitivity = simulate(secondary_sessions, sensitivity_profile)
    output.mkdir(parents=True, exist_ok=True)
    def save(name, value):
        (output/name).write_text(json.dumps(value, indent=2, ensure_ascii=False, default=str), encoding='utf-8')
    for prefix, run in (('principal', result), ('sensibilidad', sensitivity)):
        save(prefix+'.json', run)
        write_csv(output/(prefix+'_intentos.csv'), run['attempts'])
        write_csv(output/(prefix+'_diario.csv'), run['daily'])
        write_csv(output/(prefix+'_aprobaciones.csv'), run['summary']['approvals'])
    save('calidad.json', quality)
    write_csv(output/'incidencias.csv', quality['issues'])
    try:
        commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip()
        dirty = bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=repo, text=True).strip())
    except (OSError, subprocess.CalledProcessError):
        commit, dirty = 'unknown', None
    code_hashes = {str(p.relative_to(repo)): sha256(p) for p in Path(__file__).parent.rglob('*')
                   if p.suffix in ('.py', '.json', '.md') and '__pycache__' not in p.parts}
    save('manifiesto.json', dict(simulator_version=__version__, code_commit=commit,
                                worktree_dirty=dirty, code_sha256=code_hashes,
                                profile=asdict(profile), quality=quality,
                                files={p.name: sha256(p) for p in output.iterdir() if p.is_file()}))
    columns = ('purchased', 'passed_conditional', 'failed', 'pending', 'indeterminate',
               'fees_usd', 'cost_per_conditional_pass_usd')
    print(json.dumps({name: {k: run['summary'][k] for k in columns}
                      for name, run in [('principal', result), ('sensibilidad', sensitivity)]},
                     indent=2, ensure_ascii=False))


if __name__ == '__main__':
    main()

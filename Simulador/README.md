# Simulador de evaluaciones v0.1

**Estimación con operaciones cerradas. Todas las aprobaciones son condicionales:**
no acredita que se respetara el límite de pérdida flotante durante cada operación.
No ejecuta NinjaTrader, trading, compras de cuentas ni retiradas. Python 3.11+
con biblioteca estándar; no requiere instalar paquetes. Sin interfaz ni otros perfiles.

## Reglas fijadas y alcance

Implementa la [especificación v0.1](https://app.notion.com/p/41748909eb548198988adbb61b4f6303)
y los supuestos autorizados por el usuario el 14/11/2026. El borrador anterior no
autorizaba programar; la petición posterior sí autoriza esta implementación.

Perfil de investigación fijado a 13/11/2026, contrastado el 14/11/2026:

| Parámetro | Valor inicial |
| --- | --- |
| Saldo inicial / objetivo neto | 50.000 / 3.000 USD |
| Drawdown EOD / umbral inicial | 2.000 / 48.000 USD |
| Tope del umbral | 50.100 USD |
| Consistencia | Estricta: mejor día / beneficio total ≤ 0,50 |
| DLL | Sin DLL opcional; habilitarlo se rechaza por no implementado |
| Máximo | 40 MNQ; lector inicial exclusivo de BT004 MAS 40 % |
| Fee por compra | 110 USD ilustrativos, no precio oficial verificado |
| Comisión ida/vuelta | 1,82 USD por contrato; sensibilidad alternativa 1,90 |
| Tras aprobar o suspender | Nuevo intento en la siguiente sesión con entrada disponible |
| Slippage adicional | Cero, conservando fills originales |

Los valores y los reinicios se configuran en `perfil_lucidflex_50k.json`. Copiar el
perfil fuera del repo para escenarios privados; no modificarlo para reproducir esta entrega.
`consistency_review_band=0.01` marca únicamente excesos de hasta un punto porcentual
para revisión: **no cambia ninguna decisión**, no es tolerancia comercial.
No se implementa ni se deduce la fórmula de tolerancia del ejemplo comercial.

Fuentes oficiales: [evaluación](https://support.lucidtrading.com/en/articles/12945790-lucidflex-evaluation-account),
[drawdown](https://support.lucidtrading.com/en/articles/12945815-lucidflex-drawdown),
[consistencia](https://support.lucidtrading.com/en/articles/12945805-lucidflex-consistency-percentage),
[configuraciones](https://support.lucidtrading.com/en/articles/16226050-lucidflex-customization).
Se reproduce un perfil fijado, sin afirmar que existiera en todo 2025–2026 ni
certificar fidelidad contractual. La fee no se presenta como tarifa de Lucid.

## Modelo y cronología

- Durante una sesión el umbral L permanece fijo. Al EOD, H = max(H, B),
  L = max(L anterior, min(50.100, H − 2.000)). H comienza en 50.000.
- Saldo realizado B = saldo inicial + bruto − comisión elegida una sola vez.
  Cuando el resultado nativo ya es neto, se restaura bruto sumando su comisión
  nativa y se reemplaza por la comisión elegida. Nunca se suman 1,82 y 1,90.
- Si un cierre neto ≤ L, suspende; contacto exacto también incumple. El saldo
  registrado es el resultado original, incluso si queda bajo L. No se inventa
  una liquidación al precio de L ni se altera el stop.
- Con posición plana y resultado diario final, aprobar si beneficio ≥ 3.000
  y mejor día ≤ beneficio × 0,50. Decimal exacto, sin redondear la ratio para decidir.
  El código también permite aportar una cota de incumplimiento acreditado para
  pruebas: tiene prioridad sobre una recuperación. BT004 no utiliza ese campo;
  v0.1 no es un reproductor de equity intradía ni de muestras MAE/MFE.
- Una cuenta activa. Fee externa al saldo; nunca se convierte saldo simulado
  en efectivo para financiar fees. No se fija un presupuesto máximo para comprar
  intentos ni un límite artificial de duración. Aprobadas salen del modelo.
- SKIP acreditado plano = cero PNL y misma cuenta. Sin entrada tras terminar,
  no se compra otra cuenta. Fines de semana cuentan para días naturales inclusivos,
  no para sesiones operadas. Primer intento en la primera entrada observada.
- Incertidumbre de exposición/PNL: intento indeterminado y fin de segmento;
  no se inventa fecha de reinicio ni se continúa la cadena. Al fin de la última
  sesión completa, cuenta activa = pendiente/censurada, fee incluida.

## Fuentes y seguridad de datos

`bt004.py` lee CSV originales, registro propio y clasificación de fechas sin
entrada; los reconcilia por cantidades, precios, timestamps y bruto/neto.
La hoja Excel no se importa como otra copia de operaciones. Comprueba 469
operaciones de la fuente y MAS 40; excluye el día truncado del corte final.
Los SHA-256 de **todos** los CSV originales, registro y calendario de ausencias
deben coincidir con el manifiesto de BT004, o se aborta.

Si los originales locales ya no coinciden, `--originals-zip` permite leer la
copia archivada y verificarla contra el manifiesto. Se informa de la discrepancia,
sin sobrescribir, reparar ni borrar los originales. Un CSV de MR no puede
pasar como BT004. No se certifica integridad criptográfica de un tercero:
se verifica consistencia con el manifiesto local conservado.

Timestamps de BT004 ya están en Nueva York: no se convierten dos veces ni
se usa un offset fijo. Jornada de investigación con rollover 18:00 NY; las
campañas mañana/tarde de BT004 terminan el mismo día y su sesión etiquetada se
contrasta con el registro. No se equipara una operación nocturna a la fecha
natural; un cruce inesperado se rechaza para revisión. No se simula la fase
administrativa de aprobación; la comprobación plana diaria es una convención.

## Ejecución

Desde la raíz del repositorio, **primero las pruebas**:

```powershell
python -m unittest discover -s Simulador/tests -v
python -m Simulador --bt-root "RUTA_PRIVADA_BT004" --originals-zip "RUTA_PRIVADA_ZIP_BT004" --completed-through 2026-11-11 --out "RUTA_PRIVADA_SALIDA_NUEVA"
```

Omitir `--originals-zip` si los originales coinciden con el manifiesto.
`--profile` permite indicar otra copia JSON de los supuestos. La salida debe estar
fuera del repo y vacía: no se sobrescribe una simulación anterior.

Salidas por escenario: `principal` y `sensibilidad`, cada uno con JSON completo,
CSV de intentos, auditoría diaria y costes entre aprobaciones. También se generan
calidad, incidencias y manifiesto con hashes, perfil, commit y estado del árbol.
Los importes CSV/JSON son USD decimales; estados públicos:
`passed_conditional`, `failed`, `pending`, `indeterminate`.

El resumen incluye N=A+S+P+I, A/N, A/(A+S), fees/N y coste por aprobada
cuando existe alguna, duraciones por estado, distribuciones, costes entre
aprobaciones, racha de suspensos y asignación anual de compras y terminaciones.
`high_eod` y `threshold` de una cuenta terminada son los últimos valores vigentes:
no se actualiza el umbral para una cuenta que ya sale del modelo.
Auditoría de sesión incluye el umbral anterior y el siguiente explícitamente.

## Pruebas y límites

28 pruebas sintéticas incluyen los 12 casos manuales de §9 antes de BT004,
centavo sobre/bajo L, fechas repetidas/ausentes, corte de jornada, reinicios,
denominadores, costes, censura, desconocidos y cambio de año. No contienen datos privados.
La prueba del toque flotante demuestra prioridad si se acredita ese toque;
no demuestra que BT004 esté libre de toques ocultos.

No cambiar contratos, stops, objetivos, precios, horarios ni secuencia de fills.
No optimizar parámetros ni tratar los intentos como muestras independientes.
La sensibilidad puede alterar fechas de suspensión y reinicios de toda la cadena;
más aprobadas en un escenario no implica mejor expectativa ni cobros.
Los resultados reales, exportaciones, informes y respaldos se guardan fuera del
Git público. Los informes de esta ejecución se enlazan en la especificación de Notion.

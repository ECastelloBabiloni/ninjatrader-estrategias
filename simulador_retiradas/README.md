# Simulador independiente de primeras retiradas — v0.2

Implementa la [especificación fijada el 20/11/2026](https://app.notion.com/p/41e48909eb5481779246e9a00c3bdde4).
No importa ni modifica `Simulador/` (evaluaciones), ni NinjaScript. No conecta cuentas.

**Estimación exploratoria sin aplicar el escalado de contratos de LucidFlex;
no acredita cumplimiento completo de la empresa.**

Python 3.11+ con biblioteca estándar y una base IANA disponible para `zoneinfo`.
En Windows puede requerir el paquete público `tzdata`. CLI reproducible desde la raíz:

```powershell
python -m unittest discover -s simulador_retiradas/tests -v
python -m simulador_retiradas --bt-root "RUTA_PRIVADA_BT013" --out "RUTA_PRIVADA_NUEVA_RET001"
```

Perfil versionado: `perfil.json`; se puede pasar `--profile` con otra copia.
La salida debe estar fuera del repositorio y del BT, y vacía. Nunca sobrescribe
un informe. No se publica ningún dato de mercado, exportación ni resultado privado.

## Convenciones

- Una campaña completa por ID de posición incluye RN_E1 y RN_E2. Reconcilia cantidades,
  compras/ventas, resultados FIFO de patas y bruto/comisión/neto de cada campaña;
  contrasta bruto, cantidades y entradas agregadas con Trades.csv. No suma las patas
  de nuevo al resultado de la campaña. No usa el resumen como entrada de simulación.
- Jornada NY con corte a las 18:00, sin conversión doble. `zoneinfo` valida DST;
  marcas ambiguas/inexistentes se rechazan. Calendario: snapshot C# archivado del BT,
  excepciones Chicago convertidas por IANA a NY. No se usa un offset fijo.
- Al menos dos campañas cerradas y neto diario >=150 detienen nuevas campañas.
  Una sola campaña >=150 sí cuenta como día válido. Cinco días válidos, solicitud
  mínima 500, máxima min(50% beneficio,2000), cobro estimado 90%. Solicitud y cobro
  se truncan a céntimos. La solicitud bruta se resta del saldo; umbral pasa a 50100.
- Trailing EOD 2000, cap 50100; contacto intradía pierde. No se heredan colchón 30,
  límite por campaña 1970, consistencia ni fees del simulador de evaluaciones.
- Nuevas cuentas solo en la siguiente jornada con entrada. Desde v0.2, el perfil
  `gap_policy=loss_and_restart` descarta por incertidumbre una cuenta expuesta a un
  hueco y omite el resto de la jornada. La siguiente jornada operable comienza con
  saldo, H/L y contadores nuevos. `stop` conserva el comportamiento histórico.
  Otras indeterminaciones (p. ej. una jornada sin evidencia) siguen deteniendo la secuencia.
- Se conserva la campaña aceptada y todos sus fills. No se simulan señales no ejecutadas.
  El filtro no reproduce el estado contrafactual de cooldown/señales entre jornadas.

## Evidencia, costes y límites

Se generan cuatro resultados separados: cierres/intradía por 1,82/1,90 USD de comisión
ida/vuelta. El modo intradía usa MARK, FILL_ENTRY, FILL_EXIT, POSITION_RESULT y
CLOSE_REQUEST. Este último registra el precio actualizado antes de solicitar el cierre;
evita interpretar como ausencia de muestra la rama que retorna antes de emitir MARK.
No se usa ORDER como precio nuevo (puede contener una marca anterior).

Huecos entre estas muestras superiores a 90 segundos (parámetro técnico configurable,
no regla de Lucid) descartan como perdida por incertidumbre una cuenta con campaña
aceptada, salvo pérdida ya observada antes del hueco. Se identifica el motivo
`HUECO_ASUMIDO_PERDIDA` y se separan `lost_assumed_due_to_gap` y `lost_observed`.
Se conserva el saldo realizado previo, sin inventar una pérdida monetaria ni un fill.
Un hueco en una campaña omitida no afecta a la cuenta. Desorden de muestras no se
trata como pérdida: permanece indeterminado. La reserva de comisión completa continúa
siendo una convención del modelo. No se interpola el recorrido ausente.
Incluso con muestras continuas de un minuto pueden existir cruces entre muestras:
ninguna retirada se presenta como pago ni como cumplimiento flotante acreditado.
Una muestra que cruza L sí demuestra pérdida bajo el modelo. No se inventa un fill
de liquidación: se conservan saldo realizado previo y equity observada por separado.

Importes Decimal; solo se normalizan residuos de serialización binaria menores que
0,000001 USD a su céntimo exacto; otros valores subcéntimo se rechazan. 149,99 no
se convierte en 150. El bruto de muestras se restaura con neto+comisión reservada
y se aplica una única vez la comisión elegida sobre contratos comprados hasta entonces.
Comisiones de estudio, no afirmación de tarifa actual de Lucid; slippage adicional cero.

La cobertura completa se infiere del calendario y registros posteriores al cierre,
no de la última operación. La jornada final truncada se excluye. Un día abierto sin
eventos se marca desconocido. El registro no prueba todas las barras ni todas las señales;
sus huecos flat y los datos de mercado no se reconstruyen. Cierres abreviados se respetan.

Los JSON y CSV contienen cuentas, diarios, decisiones por campaña (incluidas omitidas
y no simuladas tras indeterminación), cronología de retiradas, distribución de duración,
rachas, resultados por año de terminación y manifiesto SHA-256. `balance` es previo a
solicitud; `post_balance` debita la solicitud bruta; `cash_estimated` es el efectivo modelado.
No anualizar años incompletos. Fees y coste de aprobadas: **NO CALCULADOS**.

Fuentes oficiales contrastadas para el perfil, sin presumir que existía en todo el
histórico: [retiradas](https://support.lucidtrading.com/en/articles/12945796-lucidflex-payouts)
y [drawdown](https://support.lucidtrading.com/en/articles/12945815-lucidflex-drawdown).
No se modelan evaluaciones, segundos retiros, DLL opcional, Live ni tiempos de pago.


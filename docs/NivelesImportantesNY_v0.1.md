# NivelesImportantesNY 0.1.0

Entrega de desarrollo del 17/11/2026. [Especificación de origen](https://app.notion.com/p/41a48909eb548100a678e3413d6787ac).

Indicador independiente para MNQ/NQ. Fuente completo: [NivelesImportantesNY.cs](../Indicators/NivelesImportantesNY.cs), que contiene el motor y el adaptador gráfico. Reutiliza únicamente [CalendarioSesionesNY.cs](../AddOns/CalendarioSesionesNY.cs). No modifica MAS, Experimento A ni sus parámetros. Sin dependencia de RiTo y sin órdenes, osciladores, señales, gestión monetaria o agrupaciones por prioridad.

## Instalación y configuración

1. Descargar `Indicators/NivelesImportantesNY.cs` y copiarlo a `Documents/NinjaTrader 8/bin/Custom/Indicators`.
2. Debe existir una sola copia de `AddOns/CalendarioSesionesNY.cs` en `Custom/AddOns`. Si ya está instalado el calendario del proyecto, conservarlo: no se ha cambiado.
3. No copiar las pruebas, todo el repositorio ni el archivo de referencia `Indicators/RiTo` dentro de Custom. No añadir manualmente código de fábrica generado por NinjaTrader.
4. En NinjaScript Editor, pulsar F5. NinjaTrader debe generar sus accesores habituales. La entrega no incluye DLL compilada.
5. Configurar la **zona de la aplicación en Nueva York**, con horario estacional. Gráfico MNQ o NQ, **1 minuto**, precio **Último/Last**, `Calculate.OnBarClose`, plantilla ETH que incluya la noche, Tick Replay innecesario. El indicador avisa y no calcula si la zona, instrumento, resolución, fuente Last o modo de cálculo no son los admitidos.
6. Cargar historia suficiente para cinco sesiones RTH anteriores más la noche actual. El panel informa `RTH_HISTORY_n/5`, número de periodos completos y calentamiento insuficiente. Cinco días naturales pueden no bastar.
7. Añadir `NivelesImportantesNY`. No requiere activar una estrategia.

No se copia automáticamente a la instalación de NinjaTrader en esta entrega. La compilación externa descrita abajo no sustituye F5.

## Familias y horarios NY

| Familia / códigos | Periodo y publicación | Color inicial |
| --- | --- | --- |
| PREV_RTH_HIGH / PREV_RTH_LOW | RTH terminado, normalmente 09:30–16:00, limitado por calendario y cierre efectivo de plantilla | Azul |
| OVERNIGHT_HIGH / OVERNIGHT_LOW | Apertura anterior de futuros (18:00 por defecto) hasta 09:30, o cierre oficial anterior si lo hay; fecha del día de negociación siguiente | Violeta |
| FIRST_HOUR_HIGH / FIRST_HOUR_LOW | 09:30–10:30; en formación antes de las 10:30 | Naranja |
| CURRENT_RTH_HIGH / CURRENT_RTH_LOW | Extremos desde 09:30 del RTH actual; no se redefine como rango nocturno | Verde azulado |
| RTH_OPEN | Open de la barra que comienza exactamente a las 09:30 | Verde |
| PREV_RTH_CLOSE | Close de la barra que termina exactamente al cierre efectivo del RTH; no es settlement | Gris |

El cierre RTH es el menor entre 16:00 NY, el final del calendario y el final de la sesión informado por `SessionIterator`. Se incluyen RTH abreviados en la retención; no se aplica el filtro de sesiones regulares del MAS. Días sin RTH no crean pares históricos ni consumen su retención. Puede haber metadatos UNAVAILABLE sin precio para explicar que una familia actual no existe ese día.

Se reutiliza el calendario finito `CME-ETH-5119-2024-2026`, sin modificarlo. Fechas fuera de **2024–2026** dan `CALENDAR_OUT_OF_COVERAGE` y no exponen niveles utilizables. Sus excepciones y eventuales limitaciones son las del calendario existente; no se promete cobertura de nuevos festivos ni de aperturas extraordinarias. Una plantilla con interrupciones adicionales o una apertura distinta de la configurada producirá cobertura incompleta conservadora, sin rellenarla.

## Tiempo y cobertura

Cada evento procesa una **barra cerrada**. La barra etiquetada 09:30 representa 09:29–09:30 y no entra en el RTH iniciado a las 09:30. El primer dato RTH se procesa a las 09:31; la apertura queda disponible desde esa observación a las 09:31. Esta latencia deliberada de un minuto evita simular conocimiento intrabar. Primera hora completa: 60 barras etiquetadas 09:31…10:30. RTH normal: 390 barras, 09:31…16:00. No usa reloj local, offsets horarios fijos ni reconvierte timestamps que ya están en NY.

Cobertura completa significa una barra válida por cada minuto del intervalo esperado, sin huecos, incluyendo ambos extremos. Una barra ausente no se rellena ni se confunde con un cierre oficial. No certifica calidad de ticks ni que el proveedor no haya construido barras sintéticas. Una carga que comienza tarde nunca sustituye el open por el primer precio posterior. El cierre requiere su minuto exacto; si se observa ese minuto pero faltan otros, se conserva su precio con estado INCOMPLETE y `SourceComplete=false`.

Estados:

- `FORMING`: intervalo abierto y prefijo observado continuo.
- `FIXED`: intervalo terminado y cobertura acreditada; para RTH_OPEN basta su minuto inicial exacto.
- `INCOMPLETE`: hay precios observados, pero faltan minutos, cola o parte de la primera hora por cierre anticipado.
- `UNAVAILABLE`: no hay precio del periodo, falta el minuto específico de apertura/cierre o no existe RTH.

`Reason`, `ObservedMinutes`, `ExpectedMinutes`, `SourceComplete` y `PrefixComplete` permiten distinguir los casos. `PrefixComplete` se refiere al prefijo conocido y no equivale a periodo completo. Primera hora recortada a menos de 60 minutos nunca se marca completa. El cierre/fijación solo ocurre cuando llega un evento con timestamp suficiente; si acaba el conjunto de datos antes, no se adelanta con el reloj del PC. En tiempo real el evento de cierre de barra puede llegar con el siguiente tick: no se promete puntualidad de reloj en un mercado sin eventos.

## Decisiones provisionales y retención

Son decisiones de implementación configurables, no nuevos acuerdos de estrategia:

- `SesionesRthAnteriores=5`, rango 1–50: últimos periodos RTH esperados terminados desde la primera fecha cargada. Una sesión normal sin datos ocupa su lugar como UNAVAILABLE o INCOMPLETE; no se reemplaza por una más antigua. Se expulsa el par más antiguo al publicar el siguiente RTH terminado. Fines de semana y días sin RTH no cuentan.
- `RetencionOtrosPeriodos=1`, rango 1–20: nocturno, primera hora, apertura y RTH actual conservan el último día de negociación creado, o los N últimos. Se renuevan al observar la nueva jornada, normalmente desde la noche anterior; no esperan a la siguiente apertura RTH. El cierre anterior conserva los N últimos RTH terminados. Un periodo esperado ausente también ocupa su lugar.
- `HoraAperturaFuturosNY=18`, rango 17–23, hora entera. Delimita el comienzo del nocturno y el cambio de fecha de negociación. No representa un calendario completo de aperturas excepcionales.
- `MostrarUltimoRthFueraSesion=false`: oculta CURRENT_RTH después de su cierre; PREV_RTH conserva su par. La API mantiene CURRENT_RTH mientras su periodo siga retenido.
- `MostrarIncompletos=false`: oculta precios incompletos, sin dejar de calcularlos ni retirarlos de la API. UNAVAILABLE nunca se dibuja.

Las consultas históricas solo abarcan **IDs aún retenidos** y sus revisiones. No son un archivo perpetuo del gráfico entero. Al caducar un periodo se eliminan sus revisiones y dibujos. Para análisis de toda una carga, el consumidor debe capturar las instantáneas conforme avanza. No se exporta CSV en v0.1.

## Dibujo

Seis interruptores de familia, seis colores, grosor, etiquetas y tamaño configurables. HIGH continuo y LOW discontinuo cuando están fijos; FORMING punteado; INCOMPLETE raya-punto. Intensidad menor con la antigüedad. Cada etiqueta incluye código, fecha, precio y estado; se ordenan por precio/ID y se desplazan verticalmente en píxeles dentro de grupos de precios próximos (`SeparacionEtiquetasTicks=8`, configurable). Este desplazamiento no cambia precios ni agrupa prioridades. Su legibilidad depende del zoom y queda pendiente de inspección visual real.

Los segmentos empiezan en la disponibilidad de cada revisión, nunca al comienzo retrospectivo de su periodo. `MostrarHistorialDinamico=true` dibuja el recorrido escalonado de los valores conocidos; desactivarlo deja solo el valor vigente desde su última observación. `MaxSegmentosDibujo=12000` limita los segmentos históricos, no las versiones disponibles en la API. El precio puede no cambiar aunque cambien la cobertura o los metadatos de un minuto y por ello se genere otra revisión. Los dibujos usan prefijo de instancia para no borrar los de otro indicador; ese prefijo no forma parte del ID estable del nivel.

## Interfaz de datos

Namespace de datos: `NinjaTrader.NinjaScript.NivelesNYv1`. `NivelNY` es una instantánea inmutable; las colecciones devueltas son de solo lectura.

| Miembro del indicador | Uso |
| --- | --- |
| `Niveles` | Última instantánea conocida; actualiza el indicador antes de consultar |
| `ObtenerNiveles(DateTime asOfNY)` | Última revisión de cada ID retenido cuya disponibilidad sea ≤ fecha solicitada |
| `ObtenerNivel(string levelId, DateTime asOfNY)` | Consulta puntual; null si no existía entonces o ya caducó |
| `EsOperativo`, `EstadoCobertura`, `VersionIndicador` | Configuración/cobertura/versión. Operativo no implica todas las familias completas |

Cada nivel expone `LevelId`, `Family`, `FamilyCode`, `Side`, `Name`, `Price`, `PreviousPrice`, `Instrument`, `SeriesIdentity`, `CalendarVersion`, `IndicatorVersion`, `SourceStartNY`, `SourceEndNY`, `TradingDate`, `AvailableFromNY`, `ObservedAtNY`, `Status`, `SourceComplete`, `PrefixComplete`, `Reason`, `AgeSessions`, `ObservedMinutes`, `ExpectedMinutes`, `Revision` y `Priority`.

ID: `instrumento/contrato|código|inicio yyyyMMddTHHmm|fin yyyyMMddTHHmm|lado`. Apariencia, número de instancia y retención no alteran el ID. Familias enum agrupan las parejas; `FamilyCode` distingue HIGH/LOW. `PreviousPrice` es el precio de la revisión inmediatamente anterior, que puede ser igual si solo cambió la cobertura. Se pueden consultar revisiones anteriores por timestamp para calcular cruces sin usar el extremo final.

Edad: 0 para periodo actual; 1 para el último PREV_RTH/PREV_RTH_CLOSE recién terminado, incrementa con posteriores RTH terminados, aunque alguno tenga datos ausentes; no con días naturales. `Priority=0` significa sin asignar, no una jerarquía.

Ejemplo para un futuro consumidor, con `niveles` como referencia a una instancia de `NivelesImportantesNY` ya creada y vinculada por NinjaTrader a su serie de un minuto:

```csharp
// using System.Linq;
// using NinjaTrader.NinjaScript.NivelesNYv1;
if (niveles.EsOperativo)
{
    var conocidos = niveles.ObtenerNiveles(Time[0]); // timestamps ya NY
    var fijos = conocidos.Where(n => n.Status == NivelEstado.FIXED
        && n.SourceComplete && !double.IsNaN(n.Price)).ToList();
    // No inspeccionar DrawObjects. Decidir el uso de FORMING/INCOMPLETE explícitamente.
}
```

No se entrega una estrategia ni un ejemplo que envíe órdenes. Los accesores/fábricas generados por F5 quedan pendientes de verificar en plataforma. Para pruebas fuera de NT, `MotorNivelesNY.ProcessMinute(closeNY, open, high, low, close, templateEndNY)` comparte exactamente el mismo cálculo. La definición `NIVELES_CORE_ONLY` excluye solo el adaptador NT al compilar las pruebas.

`SeriesIdentity` registra resolución, Last, plantilla, zona y MergePolicy del instrumento. No modifica el merge. Si el instrumento hereda la configuración global, ese valor heredado no acredita por sí solo cuál era el valor global ni identifica el contrato subyacente de cada barra fusionada. No hay exportaciones en esta entrega; una exportación futura deberá registrar también política global efectiva, vencimientos y rollover. `Instrument.FullName` identifica la serie solicitada, no cada contrato de origen de un continuo. Recargar datos o cambiar merge puede cambiar precios bajo los mismos IDs de periodo: conservar siempre la procedencia/configuración junto al ID, sin mezclar instantáneas de series distintas.

## Verificación realizada y pendiente

Desde la raíz, en PowerShell:

```powershell
& .\Validacion\Probar-Niveles.ps1
& .\Validacion\Comprobar-API.ps1
& .\Validacion\Probar.ps1
```

Estado real de esta entrega:

- **76 comprobaciones sintéticas superadas** sobre el motor real: seis familias, límites 09:30/09:31/10:30/16:00, noche del domingo, medianoche, inmutabilidad, consulta sin conocimiento futuro, cierres exactos, huecos de cabecera/interior/cola, sesiones abreviadas 19/06, 03/07 y 07/09/2026, días sin RTH, plantilla recortada, retención 5/2, fin de semana, contratos/IDs, repetición determinista, tick configurable, duplicados y entradas inválidas, fechas antes/después de DST y calendario fuera de cobertura.
- **Compilación externa correcta** de los fuentes C# del repositorio con las DLL y las clases base de NinjaTrader instaladas. Emite advertencias CS0436 por los tipos que existen también en Custom.dll; el compilador usa las definiciones fuente. No es F5 ni ejecución del motor NinjaTrader.
- Se intentó la regresión existente `Probar.ps1`: compiló su ejecutable pero **Windows Application Control bloqueó Validation.exe al iniciarlo**. No se declara superada esa regresión en esta entrega. Sus fuentes y los de estrategias/MAS no se modifican.
- **Pendiente en NinjaTrader:** F5 y generación de accesores; cargar MNQ/NQ Last de un minuto con ETH/NY; contrastar precios con barras reales; revisión visual de etiquetas, colores, conservación/caducidad y rendimiento; recarga idéntica con misma fuente/merge; transición histórico–Playback; plantilla efectiva y casos de sesión abreviada/datos ausentes. No se ha instalado ni ejecutado el nuevo indicador en plataforma.

Referencias API consultadas: [BarsPeriod y Last](https://docs.ninjatrader.com/ninjascript/barsperiod), [construcción y marcas de barras](https://support.ninjatrader.com/s/article/How-Bars-Are-Built-NinjaTrader-Desktop?language=en_US), [ActualSessionBegin y zona configurada](https://static.ninjatrader.com/support/helpGuides/nt8/actualsessionbegin.htm). Las pruebas de compatibilidad usan las bibliotecas locales; las pruebas de precios usan exclusivamente datos sintéticos públicos.

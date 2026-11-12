ACTUALIZACIÓN 12/11/2026: consultar docs/Entrega_2026-11-12.md. Ahora también se requieren los dos auxiliares de AddOns; Experimento A usa selección por calendario y margen de cierre de 2 minutos. F5 y BT003 pendientes. El texto siguiente conserva la guía histórica del 11/11.

# Experimento A — versión de un minuto, 11 de noviembre de 2026

Esta versión sustituye el requisito de gráfico de un tick. Usa gráfico principal de **1 minuto**, cálculo **OnBarClose** y una serie interna también de un minuto para reutilizar el MAS. No añade ni requiere histórico de ticks. Tick Replay desactivado.

## Lógica

Cuando cierra la barra 09:34–09:35 NY, su etiqueta es 09:35. Si coincide con HoraEntrada y no se ha solicitado una entrada ese día, calcular contratos y enviar compra a mercado. Histórico Standard modela la ejecución en la siguiente apertura disponible; Playback/Sim101 usa el precio disponible después de la solicitud. No se garantiza precio exacto de apertura.

La comparación usa la barra cerrada, nunca una etiqueta futura en formación. Si falta esa barra, no se entra tarde. Los inputs de entrada/cierre deben estar alineados a minuto, con segundos cero. La zona de NinjaTrader debe ser Nueva York con horario estacional; no se usa el reloj del PC.

Se conserva una solicitud por fecha NY sin reentrada. Al siguiente cierre, HoraEntrada+1 minuto, se solicita cancelar remanentes; las ejecuciones en tiempo real también comprueban el límite. Si faltan eventos no se promete cancelación puntual. Los fills parciales mantienen protección y distancias; no se amplía el stop para compensar cantidad ausente.

En el cierre de barra etiquetado 15:00 se cancela remanente y se solicita liquidar la posición para la siguiente apertura/evento disponible. Las marcas de fill del motor histórico se guardan sin reinterpretarlas como un reloj real: pueden ser etiquetas de barra. Las decisiones horarias históricas usan los cierres de barras.

## Configuración

1. Mantener estrategia desactivada durante actualización. Instalar solo MAS_SesionNY.cs en Custom/Indicators y AperturaNY_MAS_ExperimentoA.cs más ExperimentoAMath.cs en Custom/Strategies. No copiar pruebas ni respaldos dentro de Custom.
2. Cerrar pestañas de código antiguas sin sobrescribir las copias actualizadas. Abrir NinjaScript Editor y pulsar F5.
3. Quitar la instancia anterior y añadir una nueva. Gráfico o Analyzer: MNQ del contrato apropiado, **1 Minuto**, Last, Tick Replay desactivado. El código fuerza OnBarClose incluso ante plantillas antiguas OnEachTick.
4. Zona NinjaTrader Eastern Time/New York con horario estacional. Plantilla Trading Hours con cobertura 09:30–16:00 NY. Cargar cinco sesiones completas anteriores al primer día operativo, preferiblemente varias semanas de margen.
5. Inputs: entrada 09:35:00; cierre 15:00:00; MAS 5; Simple; 40 %; contratos 1–40; pérdida presupuestada 2000; objetivo neto 1500; comisión ida/vuelta 1,82 USD/contrato. Resolución Standard y slippage 0.
6. Para conciliar, Include commission desactivado en Analyzer: su resultado será bruto, mientras el CSV calcula el neto. Si se activa plantilla equivalente, no volver a restar comisiones al neto CSV.
7. Solo histórico, Playback101 o Sim101. Mantener WaitUntilFlat. La regla de una entrada es por instancia/fecha, no un coordinador entre varias instancias o reinicios sin histórico suficiente.

## Cambios y compatibilidad MAS

Se conserva el dimensionamiento y la gestión managed de órdenes. El MAS mantiene fórmulas, selección de sesiones y comportamiento independiente por defecto. Adaptación mínima al hospedarlo OnBarClose: procesar el minuto cerrado [0] también en tiempo real, en lugar de [1], que correspondía al primer tick de una barra nueva. La estrategia configura explícitamente el indicador en OnBarClose. Una sola región nativa de integración, con enum cualificado.

V2.1 no está entre los fuentes recibidos y no se ha modificado. Los tres fuentes anteriores se respaldan en Respaldo_antes_1min. No hay repositorio Git ni instrucciones AGENTS aplicables encontradas en la revisión inicial. No se crea otro indicador MAS.

## CSV, excursiones y barras ambiguas

CSV por instancia en el directorio de datos NinjaTrader/ExperimentoA; Output muestra la ruta efectiva. Se mantienen SUBMIT, ORDER, FILL, TRADE, NO_FILL, SKIP, MARK y END con cantidades, MAS, distancias, costes, bruto/neto y motivo.

MARK y MAE/MFE usan cierres de minuto y fills: **son muestras, no excursiones intrabar exactas ni una prueba de ausencia de breaches flotantes**. El campo excursion_resolution lo identifica. Equity relativa desde cero; no saldo de cuenta. La valoración neta reserva comisión completa de ida/vuelta y commission_paid modela lados ejecutados.

BAR_RANGE guarda high/low del minuto completo cuando puede asociarse a la campaña. No implica que todo el rango haya ocurrido con posición abierta. POTENTIAL_SL_TP_SAME_BAR_SEQUENCE_UNKNOWN avisa de posible toque de ambos brackets; con parciales a distintos precios es conservador y puede referirse a fills distintos. Un BAR_RANGE puede aparecer después de TRADE por el orden de callbacks: revisar todos los eventos de sesión.

El resultado Standard de una barra ambigua no demuestra cuál ocurrió primero en el mercado. El CSV conserva el resultado simulado y la advertencia; no inventa una secuencia favorable. Resolverla requiere datos más detallados posteriormente. Exportar-Resumen.ps1 incluye BAR_RANGE en Trayectoria.csv. Mantener el original completo. END con INCOMPLETE_OPEN_POSITION no cuenta como operación cerrada.

El MAS conserva su límite de calendario: no detecta un día bursátil entero ausente ni resuelve oficialmente festivos/medias sesiones. Revisar cobertura y SKIP antes de interpretar resultados.

## Pruebas y pendientes

Auxiliar C# real: **5.719 comprobaciones** pasadas sobre 135 configuraciones monetarias. Se prueban ejemplo, presupuestos, cantidades/empates, primavera/otoño, rechazo de señal 09:34/09:36, inclusión 09:35, horario cambiado y detector de doble toque.

Ejemplo: MAS 250, referencia 100, 10 MNQ, comisión 18,20, SL 99 y TP 76; pérdida prevista 1.998,20 y ganancia prevista 1.501,80 USD. TickSize y PointValue proceden del instrumento. El stop no garantiza ese importe en mercado.

Compilación externa contra DLL NinjaTrader instaladas con los fuentes y región de fábrica entregados, sin puente que la suplante: pasa. Advertencias CS0436 por clases base locales frente a Custom.dll. Exportador probado con cinco eventos sintéticos incluyendo BAR_RANGE: una operación, cuatro filas de trayectoria y cuatro eventos. No son operaciones de mercado.

Pendiente dentro de NinjaTrader: F5 y regeneración, MAS hospedado OnBarClose, señal y fill siguiente apertura, ausencia de anticipación/reentrada, cierre 15:00, cancelaciones, parciales a precios distintos, brackets OCO y rechazos, conciliación de CSV e inspección de barras ambiguas. Sin backtest ni rentabilidad obtenidos por el agente. Evaluaciones, fees y retiradas siguen fuera de alcance.

Referencias: [Calculate y hospedaje](https://static.ninjatrader.com/support/helpGuides/nt8/calculate.htm?zoom_highlightsub=calculate), [multiserie](https://docs.ninjatrader.com/ninjascript/multi_time_frame_instruments), [Notion](https://app.notion.com/p/41248909eb5481c2be85f9557424d359).

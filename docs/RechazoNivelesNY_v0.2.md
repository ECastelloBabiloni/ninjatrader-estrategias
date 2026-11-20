# RechazoNivelesNY — 0.2.0-dev

Primera implementación de desarrollo, 20/11/2026. [Especificación: acuerdos consolidados v0.2](https://app.notion.com/p/41a48909eb548100a678e3413d6787ac). No es Experimento A ni sustituye ninguna estrategia existente. No demuestra rentabilidad. Solo histórico, Sim101 y Playback101; el código rechaza otras cuentas al entrar en tiempo real.

## Archivos e instalación

- `Strategies/RechazoNivelesNY.cs`: adaptador NinjaTrader, órdenes y eventos CSV.
- `Strategies/RechazoNivelesCore.cs`: detector, agrupación, MAS por franjas, reloj, pausa y fórmulas monetarias puras.
- `AddOns/RechazoStochFactory.cs`: acceso con nombre único al **Stochastics estándar de NinjaTrader**, sin reimplementar el oscilador ni duplicar sus fábricas generadas.
- Dependencias existentes, conservadas: `Indicators/NivelesImportantesNY.cs`, `AddOns/CalendarioSesionesNY.cs` y `Strategies/ExperimentoAMath.cs`.
- `Validacion/Probar-Rechazo.ps1` y `RechazoValidation.cs`: pruebas sintéticas fuera de plataforma.
- `Herramientas/Resumir-Rechazo.ps1`: resumen regenerable de posiciones cerradas.

Copiar los dos fuentes nuevos de Strategies a `Documents/NinjaTrader 8/bin/Custom/Strategies` y el nuevo factory a `Custom/AddOns`. Las tres dependencias existentes deben estar instaladas una sola vez. No copiar pruebas ni todo el repositorio a Custom; el archivo de referencia RiTo no es dependencia. Pulsar F5 en NinjaScript Editor. No se entrega DLL binaria.

**Configuración de gráfico o Analyzer:** MNQ inicialmente; precio Last/Último; serie principal de **1 minuto**; ETH incluyendo noche; aplicación en Nueva York con DST. Calculate.OnBarClose. Tick Replay innecesario. Las velas de señales se configuran mediante **Minutos de las señales**, inicial 1, rango 1–60, en una serie secundaria. Para señales de cinco minutos mantener la principal en uno y poner ese parámetro en cinco. Se validará primero 1 y después 5/15 minutos en plataforma. No se promete compatibilidad ya probada con todas las combinaciones o con optimización multiserie.

La serie principal de un minuto calcula niveles/MAS, comprueba cierres y recibe todas las órdenes. Se sigue la [recomendación oficial de SetStopLoss para varios Bars del mismo instrumento](https://docs.ninjatrader.com/ninjascript/setstoploss): enviar órdenes al primer contexto. No se carga el indicador gráfico ni se crean DrawObjects: se reutiliza directamente su `MotorNivelesNY`, sin cambiar su código. La interfaz as-of conserva las restricciones de retención de ese motor. El calendario compartido solo cubre 2024–2026; fuera de cobertura no se admiten señales.

Cargar suficientes días para cinco franjas completas de **cada** tipo y los niveles históricos. Se descartan entradas si falta MAS. No se realiza comparación de rentabilidad en esta entrega.

## Reglas implementadas

| Aspecto | Comportamiento inicial |
| --- | --- |
| Niveles | Solo FIXED y SourceComplete, existentes al inicio de la vela de ruptura. CURRENT_RTH queda excluido de señales, aunque termine fijo |
| Ruptura de venta | Cierre anterior ≤ nivel, cierre actual > nivel, K ≥ 80 |
| Ruptura de compra | Cierre anterior ≥ nivel, cierre actual < nivel, K ≤ 20 |
| Oscilador | Stochastics convencional D=7, K=14, Smooth=3. D se registra pero no filtra |
| Confirmación | Vela posterior que cierre estrictamente al otro lado del mismo precio; hasta 10 velas siguientes incluidas; igualdad no confirma |
| Rearme | No reiniciar contador por cierres fuera. Después de confirmar/caducar hace falta nuevo cruce. Reset al quedar flat y en bloqueo/reanudación |
| Solicitud | Mercado en la serie de un minuto tras confirmar, para el siguiente evento/apertura disponible; sin filtro adicional sobre el lado de apertura |
| Pausa | Cinco barras completas de señales después de salir; la barra que contiene la salida no cuenta. Confirmaciones durante pausa se registran y descartan |
| Concurrencia | Una posición conjunta, hasta dos entradas con igual dirección. Reserva antes de enviar para impedir duplicación por callbacks o señales simultáneas |
| Proximidad | Cinco puntos, configurable. Agrupación anclada al nivel confirmado más antiguo; no encadenar A–B–C por proximidad transitiva |
| Desempate | Fecha/hora de origen ascendente y después ID ordinal. No hay jerarquía de familias |
| Bloqueo NY | 15:45–18:15. Cancelar entrada pendiente y solicitar cierre de la posición; renovar señales al reanudar |
| Abreviadas | Anticipar cierre dos minutos respecto al fin del calendario cuando sea anterior a 16:00; margen configurable |

Los secundarios cercanos se conservan con sus IDs, precios y disponibilidad, incluso si no fueron cruzados. El nivel principal de una **señal** debe haber sido cruzado y rechazado: no se atribuye la ruptura a otro nivel antiguo solo porque esté cerca. Es la resolución provisional de la tensión entre «principal más antiguo» y «no atribuir ruptura a un nivel no cruzado». Entre confirmaciones cercanas realmente válidas, manda la más antigua. Las señales restantes se registran y se aplica la única posición permitida, sin una regla adicional para señales opuestas inventada.

Si entre dos eventos se atraviesa una hora límite, se detecta aunque el siguiente evento llegue después de todo el bloqueo: se solicita cerrar y se registra el retraso. No puede garantizarse un fill a las 15:45 si no existe evento/mercado. El reloj es exclusivamente el de los datos NY. Los fills históricos pueden usar las etiquetas de barras del motor NT; contrastar hora efectiva de apertura y timestamps de ejecución en la validación de plataforma.

## MAS y dimensionamiento

Tres ventanas NY independientes: Asia 18:00–03:00, Europa 03:00–09:30 y América 09:30–16:00. No son nombres de sesiones oficiales de bolsas diferentes. Cada media simple usa las últimas N franjas **completas del mismo tipo**, inicialmente cinco; N y porcentaje se configuran por tipo, 15 % inicial.

Una franja exige todos sus minutos contiguos. Se excluyen las oficialmente recortadas y las observadas con huecos/carga parcial, con eventos de diagnóstico. Una franja totalmente ausente tampoco entra en la media; pueden usarse completas más antiguas conforme al criterio de últimas completas. `MAS_LOOKBACK` registra las fechas efectivamente incluidas para que esta búsqueda no quede oculta. El bloqueo 15:45 no trunca el cálculo americano hasta16:00. No se usa el tramo en formación.

El módulo de franjas generaliza la agregación de rangos máximo/mínimo a estas tres ventanas, sin modificar ni instanciar otro MAS_SesionNY. El dimensionamiento **reutiliza ExperimentoAMath.Size**: contratos enteros dentro de límites, minimizando la separación entre SL final y referencia MAS × porcentaje, empates hacia menor cantidad. Se obtiene tick y valor del punto del instrumento. Se ajustan SL/TP por comisión y ticks, sin optimización de parámetros.

Objetivo neto inicial 300 USD; presupuesto conjunto 2.000 USD; comisión ilustrativa 1,82 USD por contrato ida/vuelta; mínimo uno y máximo total40. Slippage configurado cero, sin garantía sobre fills. Para NQ deben configurarse costes/límites propios.

**Margen neto disponible MANUAL** es un límite de estudio, inicial2.000. El presupuesto usado es el mínimo entre ese input y el presupuesto configurado. No lee margen real, trailing, DLL ni reglas de una empresa; no ajusta automáticamente ese input tras ganar/perder. Antes de simulaciones de cuenta se necesitará una fuente explícita de capacidad restante. No presentar este modelo como protección acreditada de una evaluación.

La franja y el MAS se toman al solicitar la primera entrada y permanecen durante la posición conjunta. No se recalculan al pasar de franja. La solicitud y sus fills se registran por separado. Un fill retrasado que cambie de franja requiere revisión en Playback: esta entrega no reinterpreta retrospectivamente el origen usando información posterior.

## Recuperación y protección

Recuperación **desactivada inicialmente**, para probar primero la base y luego activar el mismo módulo sobre datos/costes idénticos. No cambia el indicador.

Umbral30 % del presupuesto, vigilado al **cierre de cada minuto**, con comisiones ida/vuelta reservadas. No se afirma vigilancia intratick. Provisionales configurables: `VigilanciaPersistente=true`, de modo que un rebote no desarma; `ExigirRupturaTrasUmbral=true`, que reinicia intentos al armar y exige ruptura posterior al umbral. Si se desactiva esta última opción, pueden madurar intentos previos en otros niveles. Si se desactiva persistencia, salir de la zona desarma y reinicia.

La adición requiere señal completa en la misma dirección, nivel distinto y más favorable, y precio disponible favorable al enviar. Se excluyen los IDs del nivel inicial y sus secundarios de la misma zona para evitar reutilizarla. Cantidad = min(contratos iniciales realmente ejecutados, máximo total − abiertos). Solo un intento de adición por posición; si se cancela sin fill no se vuelve a intentar. Es una elección conservadora provisional. Objetivo conjunto50 netos se aplica al **primer fill** de recuperación, no al mero envío. Un fill de adición que resulte no favorable solicita cierre conjunto y queda registrado.

Antes de la primera entrada se preparan StopLoss y ProfitTarget por ticks; con cada fill se recalculan precios sobre la cantidad real y media ponderada conjunta. Bracket managed `ByStrategyPosition` con stop-market/target. En cada recalculo se incluye una sola vez la comisión ida/vuelta de todos los contratos ejecutados:

`TP = media + s × (objetivo − realizado_bruto + comisiones) / (valor_punto × abiertos)`

`SL = media − s × (presupuesto + realizado_bruto − comisiones) / (valor_punto × abiertos)`

Compras: ambos precios hacia arriba al tick. Ventas: ambos hacia abajo. Con recuperación el presupuesto no se suma ni se reinicia. Los precios teóricos no son un límite garantizado frente a gaps o slippage.

Parciales: no se presume ejecutada la cantidad solicitada. Hasta que se resuelve la entrada inicial no se solicita adición. Al comenzar una salida, se cancelan entradas restantes y se solicita terminar cualquier remanente; un fill tardío también se cierra. Se deduplican ejecuciones por executionId, reservando la solicitud antes de callbacks. Rechazo/error de orden detiene nuevas entradas y solicita cierre; además se configura StopCancelClose. Stop ausente/cancelado detectado en la comprobación de minuto provoca cierre de emergencia. Precio de stop ya alcanzado, objetivo ya alcanzado o cálculo inviable también solicita cierre, sin omitirlo silenciosamente.

Estas son rutas de manejo implementadas, **no prueba de que los callbacks/OCO nativos ya se hayan verificado**. Cancelación, reemplazo y fills pueden competir: revisar secuencias reales de órdenes y cobertura de cantidad en Playback antes de atribuir protección acreditada. La pausa empieza al quedar completamente flat; no bloquea recuperación. Un error fatal exige reiniciar la estrategia, no autorreanuda.

## Registro

CSV nuevo por ejecución en `Documents/NinjaTrader 8/RechazoNivelesNY/<run>_events.csv`. Escribe eventos en orden, sin reescribir hechos; nombre GUID evita sustituir otra ejecución. Se vacía al terminar posiciones y al cerrar. Configuración, versión/MVID, calendario, plantilla y MergePolicy del instrumento quedan registrados. Si merge hereda ajustes globales, conservar manualmente también la política global efectiva y vencimientos; el código no la cambia ni afirma identificar cada contrato subyacente de un continuo.

Eventos: configuración, franjas completas/incompletas, ruptura, rechazo, caducidad/reset, señales válidas, no ejecutadas y motivo, selección MAS/fechas, solicitudes, estados de órdenes, fills, recalculo SL/TP previo/nuevo, marcas de minuto, umbral, cierres y resultados. La señal registra principal/secundarios, precios/disponibilidad y K de ruptura; columnas K/D reflejan la última observación de señales. ID de señal dentro del run, ID de posición conjunta y executionId/orderId permiten auditar.

`POSITION_RESULT`: realizado bruto conjunto menos la comisión reservada, sin descontarla dos veces. `ENTRY_RESULT`: reparto FIFO de los fills de salida entre E1/E2 para análisis, no asignación contable acreditada por el broker ni salida real independiente. La suma final coincide con el conjunto; el libro conjunto intermedio usa precio medio. Las señales descartadas no tienen P/L ejecutado. Analyzer puede agrupar trades de otra manera y aplicar su propia plantilla de comisiones; no descontar otra vez la comisión del CSV.

Resumen regenerable sin tocar eventos:

```powershell
& .\Herramientas\Resumir-Rechazo.ps1 -EventsPath 'C:\ruta\run_events.csv'
```

El resumen incluye solo posiciones cerradas. El evento END identifica posiciones abiertas/incompletas. Datos, CSV y resultados privados se excluyen de Git.

## Pruebas y estado real

```powershell
& .\Validacion\Probar-Rechazo.ps1
& .\Validacion\Probar.ps1
& .\Validacion\Probar-Niveles.ps1
& .\Validacion\Comprobar-API.ps1
```

Superadas: **424 comprobaciones** nuevas de detector, rearme, ventana10/11, igualdad, K, resets, proximidad no transitiva, tipos de franja, SMA, huecos/cierre oficial, ticks, ambos sentidos, comisiones, capacidad25+15, presupuesto conjunto, realizado parcial, bloqueo/DST, hueco que atraviesa bloqueo y pausa. También las anteriores **5.719** de cálculo/horario, **37** de calendario y **76** de niveles. Compilación externa con DLL instaladas correcta, con advertencias CS0436 por fuentes que también están en Custom.dll. No inicia NinjaTrader.

**Pendiente obligatorio en plataforma:** F5/accesores y dependencias; sincronización de las dos series, Stochastics estándar sobre la serie de señales, ejecución siguiente sin anticipación en límites de franja, señales1/5/15 minutos y cierre15:45, festivos/cierres anticipados, fills parciales/cancelación/rechazo, redimensionamiento OCO tras E2, salida parcial mientras entra E2, protección de fill tardío, transición histórico–Playback, consumo de recursos y contraste con las barras. No se han ejecutado órdenes, Playback ni backtests de esta estrategia en esta entrega. No hay tasa de acierto, comparación base/recuperación ni resultado de fondeo acreditados.

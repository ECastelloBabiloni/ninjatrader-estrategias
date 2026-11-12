# BT003 — pendiente de validación en NinjaTrader

No hay resultados de BT003 en esta entrega. Las pruebas externas no ejecutan el motor de órdenes.

## Primero: pruebas específicas, sin optimizar

1. Compilar con F5 los fuentes actualizados y los dos auxiliares AddOns. Confirmar ausencia de errores. Cerrar y abrir Analyzer para cargar la versión nueva; comprobar el input Margen cierre minutos = 2.
2. Usar MNQ 12-26, Last, minuto 1, OnBarClose, Standard, Tick Replay desactivado, slippage 0; entrada 09:35 NY, cierre 15:00, MAS 5 Simple 40 %, 1–40 contratos, TP 1500, SL 2000, comisión input 1,82. Mantener la plantilla de comisión original y registrar si IncludeCommission está activo: no sumar otra vez el coste al neto del CSV propio.
3. Zona de aplicación Eastern Time con horario estacional. Mantener y fotografiar la plantilla efectiva CME US Index Futures ETH y Merge Policy global MergeBackAdjusted; verificar el override del instrumento y guardar la tabla de rollovers/offsets. No cambiarlos para conseguir un resultado.
4. Ejecutar primero 24/08–16/09/2026: comprobar CSV SUBMIT o SKIP concreto para 8, 9, 10, 11 y 14/09. El día 7 debe figurar como SHORTENED, nunca como datos regulares ausentes. Para el día 8 las cinco fechas esperadas son 31/08 y 01–04/09. Si falta una, bloquear con su fecha.
5. Ejecutar 01/06–23/06 y 15/06–07/07/2026: con posiciones aún abiertas, comprobar CLOSE_REQUEST 12:58 NY los días 19/06 y 03/07, y FILL anterior a las 13:00. En histórico la etiqueta esperada con barras contiguas es 12:59; comprobar también precio/open y órdenes, sin confundir etiqueta con reloj real. Si SL/TP sale antes, el caso no valida por sí solo el cierre horario.
6. Contrastar un día normal (cierre habitual 15:00; siguiente etiqueta histórica 15:01) y un conjunto separado de datos sintéticos con un día regular íntegramente ausente y otro con hueco. No borrar ni alterar el histórico original del usuario para fabricar la prueba.
7. Revisar parciales, cancelación al minuto, rechazo y carreras de salida en Sim/Playback; inspeccionar protección nativa. No usar cuentas reales. Probar recarga y compatibilidad de otros consumidores antes de sustituir su instalación.

## Después: periodo completo

Solo tras superar las pruebas específicas, ejecutar 06/01/2025–12/11/2026, con calentamiento suficiente para las cinco sesiones anteriores. Si Analyzer empieza exactamente el 06/01, conservar los SKIP de preparación como tales y no atribuirlos al calendario. No fabricar datos anteriores ni sustituirlos silenciosamente.

Archivar en una carpeta BT003 distinta de BT002: Trades, Orders, Executions, Summary, Analysis, CSV propio, XML y fuente guardada de Analyzer, captura completa de parámetros, commit y etiqueta, MVID de DLL, calendario/hash, plantilla, zona, Merge Policy global/override, rollovers/offsets, proveedor y cobertura de minutos. Revisar END/INCOMPLETE_OPEN_POSITION y WARNING/SESSION_END_PASSED_WITH_OPEN_POSITION. Comparar fechas omitidas con el calendario y con los datos realmente disponibles. Adjuntar resultados a Notion únicamente después de ejecutarlos.

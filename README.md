# Estrategias e indicadores para NinjaTrader 8

Código NinjaScript de desarrollo para histórico, Playback101 y Sim101.

## Código disponible

| Carpeta | Contenido |
| --- | --- |
| [Strategies](Strategies) | Estrategia AperturaNY_MAS_ExperimentoA y su auxiliar de dimensionamiento ExperimentoAMath |
| [Indicators](Indicators) | Indicador compartido MAS_SesionNY |
| [Validacion](Validacion) | Pruebas de cálculos/horarios y compilación externa con bibliotecas NinjaTrader instaladas |
| [Herramientas](Herramientas) | Exportador de resúmenes de los CSV de la estrategia |
| [docs](docs) | Configuración, instalación, limitaciones y estado de validación |

## Versión vigente

Entrega 12/11/2026: [selección de sesiones regulares y cierre anticipado](docs/Entrega_2026-11-12.md). Código y pruebas externas completados; **F5, pruebas en plataforma y BT003 pendientes**. La nueva guía prevalece sobre las instrucciones históricas del 11/11.

Gráfico de **un minuto**, Calculate.OnBarClose. Al cierre etiquetado 09:35 NY solicita compra a mercado para la siguiente apertura/evento disponible. No exige series de ticks. Una solicitud por fecha, sin reentrada; cierre configurable inicial 15:00 NY.

MAS compartido: cinco sesiones anteriores, media simple, 40 %. Dimensionamiento con TickSize y PointValue reales del instrumento, contratos enteros y ajustes SL/TP por comisiones. La estrategia conserva protección managed por ejecución y está restringida en tiempo real a Sim101/Playback101.

## Instalación

1. Copiar los archivos de `Strategies` a `Documents/NinjaTrader 8/bin/Custom/Strategies`.
2. Copiar `Indicators/MAS_SesionNY.cs` a `Documents/NinjaTrader 8/bin/Custom/Indicators`. Mantener una sola definición.
   Copiar también los dos fuentes de `AddOns` a `Documents/NinjaTrader 8/bin/Custom/AddOns`; son necesarios para el calendario y la fábrica compatible.
3. No copiar las pruebas ni las herramientas dentro de Custom.
4. Compilar con F5 en NinjaScript Editor. Añadir la estrategia a un gráfico de 1 minuto, con Tick Replay desactivado y NinjaTrader configurado en Nueva York con horario estacional.

Ver [guía completa](docs/Configuracion_1min.md) o [versión de texto](docs/Configuracion_1min.txt).

## Validación

Desde la raíz del repositorio, en PowerShell:

```powershell
& .\Validacion\Probar.ps1
& .\Validacion\Comprobar-API.ps1
```

La segunda prueba requiere NinjaTrader instalado; permite especificar `-NtBin` y `-NtCustom`. No inicia la plataforma.

Se han comprobado 5.719 condiciones de cálculo/horario y compilación externa con los fuentes entregados. Esto no sustituye F5, el motor de fills ni la validación en NinjaTrader. El usuario ha aportado un backtest para análisis separado; sus datos no están incluidos en este repositorio.

El registro de excursiones usa cierres de minuto y fills: no reconstruye una trayectoria intrabar exacta. Los posibles toques de SL y TP dentro de una misma vela se marcan para revisión.

## Documentación de referencia

[Experimento A en Notion](https://app.notion.com/p/41248909eb5481c2be85f9557424d359) · [Especificación MAS](https://app.notion.com/p/41148909eb5481559dd9c06c93828cdf)

La estrategia V2.1 no forma parte de los archivos recibidos y no se ha modificado. Los backtests y datos de mercado se conservan localmente fuera del repositorio.

# Instrucciones de trabajo

- Estrategia vigente: AperturaNY_MAS_ExperimentoA, gráfico de un minuto, OnBarClose, compra para la siguiente apertura disponible. No reintroducir una exigencia de ticks sin petición del usuario.
- Reutilizar MAS_SesionNY y conservar V2.1 si se incorpora posteriormente. No duplicar el cálculo MAS.
- Usar timestamps de datos en la zona de Nueva York configurada en NinjaTrader, con horario estacional. No usar el reloj local para señales.
- Mantener una solicitud de entrada por fecha, la ventana fija de un minuto y la protección de parciales.
- No operar ni conectar cuentas reales. Histórico, Playback101 o Sim101 únicamente.
- Distinguir pruebas externas de compilación F5 y backtests en NinjaTrader; no afirmar pruebas no realizadas.
- MAE/MFE de minutos son muestras. Conservar los avisos de posible SL/TP en la misma barra.
- Excluir datos de mercado, backtests, binarios, credenciales y respaldos del repositorio.
- Para cálculos ejecutar Validacion/Probar.ps1 en Windows con .NET Framework. Para API usar Validacion/Comprobar-API.ps1 si NinjaTrader está instalado. Esta última no ejecuta NinjaTrader.

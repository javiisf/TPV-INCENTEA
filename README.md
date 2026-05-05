# ServidorImpresion

Servidor HTTP local para impresoras de tickets en entornos TPV. Recibe un POST con bytes ESC/POS (o ZPL) y los envía a una impresora COM o USB configurada en Windows. Corre en la bandeja del sistema y arranca con Windows.

---

## Requisitos

- Windows 10/11
- .NET 10
- Impresora ESC/POS (COM o USB/spooler)


## Uso

### Imprimir ZPL

```bash
# Sin API key
curl -X POST http://localhost:8080/print/zpl --data-binary @etiqueta.zpl

# Con API key
curl -X POST http://localhost:8080/print/zpl \
  -H "X-Api-Key: TU_CLAVE" \
  --data-binary @etiqueta.zpl
```

La key también se puede pasar como query string (`?key=TU_CLAVE`), útil desde el navegador.

### Scripts de ticket (ESC/POS dinámico)

El servidor compila y ejecuta scripts C# en tiempo de ejecución. Cada script recibe el JSON del ERP y genera los bytes ESC/POS del ticket.

```bash
# Ejecutar script con JSON del ERP
curl -X POST http://localhost:8080/print/pos/venta \
  -H "X-Api-Key: TU_CLAVE" \
  -H "Content-Type: application/json" \
  -d '[{"empresaData": {...}, "ticketData": {...}}]'

# Listar scripts disponibles
curl http://localhost:8080/print/pos?key=TU_CLAVE
```

Los scripts `.cs` viven en la carpeta `scripts/` junto al ejecutable. El servidor los detecta y recompila automáticamente cuando cambian. Los JSONs de prueba están en `scripts/pruebas/`.

**Errores de script:** si falta un campo en el JSON el servidor devuelve `400` con el nombre exacto del campo y la línea del script donde falló, por ejemplo:
```
Campo requerido no encontrado (línea 42): 'cantidadApertura'
```

Scripts incluidos: `venta`, `factura`, `devolucion`, `aperturaCaja`, `cierre`, `albaran`, `pedido`, `presupuesto`.

### Health

```bash
# HTML (navegador, auto-refresco cada segundo)
curl http://localhost:8080/health?key=TU_CLAVE

# JSON
curl -H "Accept: application/json" http://localhost:8080/health?key=TU_CLAVE
```

El dashboard muestra: versión, estado de la impresora, circuit breaker, estadísticas de sesión e históricas, rate limiter y últimos 50 trabajos con su resultado (persisten entre reinicios).

---

## Códigos de respuesta

| Código | Significado |
|-------:|-------------|
| `200` | Trabajo aceptado e impreso |
| `400` | Payload vacío o formato inválido (ejecutable, PDF, ZIP…) |
| `401` | API key ausente o incorrecta |
| `413` | Payload demasiado grande (límite configurable, 512 KB por defecto) |
| `429` | Rate limit excedido |
| `500` | Error de impresión |
| `503` | Cola saturada (más de 50 trabajos en espera) |
| `504` | Timeout (15 s) |

---

## Configuración

Fichero: `%APPDATA%\ServidorImpresion\config_tpv.json`

La API key se guarda cifrada con DPAPI (nunca en texto plano). El resto de campos son legibles directamente.

| Campo | Por defecto | Descripción |
|-------|-------------|-------------|
| `PuertoServidor` | `8080` | Puerto HTTP |
| `UltimoCOM` | `""` | Puerto serie, p. ej. `COM3` |
| `UltimaUSB` | `""` | Nombre de impresora instalada en Windows |
| `BaudRate` | `9600` | Velocidad del puerto serie (300–115200) |
| `MaxTicketBytes` | `524288` | Tamaño máximo de ticket en bytes |
| `NivelLog` | `"Information"` | Nivel de log: `Debug`, `Information`, `Warning`, `Error`. Configurable en caliente sin reiniciar. |

Si editas el fichero a mano, los valores fuera de rango se corrigen automáticamente al arrancar.

---

## Cómo funciona (lo importante)

La app es un `HttpListener` sobre localhost. Cada petición pasa por:

1. **LocalHostFilter** — rechaza cualquier petición que no venga de 127.0.0.1 (protección contra DNS rebinding).
2. **ApiKeyAuthFilter** — si hay API key configurada, la valida con comparación en tiempo constante (evita timing attacks).
3. **RateLimitFilter** — 50 req/s por IP, 200 req/s global.
4. **Handler** — `/print/zpl` va a `PrintEndpointHandler`, `/health` a `HealthEndpointHandler`, `/print/pos/{name}` a `ScriptEndpointHandler`, `/print/pos` (listado) también a `ScriptEndpointHandler`.

Los trabajos de impresión no se procesan en el hilo HTTP. Van a una cola `Channel` con un único consumidor que los serializa. Si la cola llega a 50 trabajos, las nuevas peticiones reciben 503 directamente.

El `PrinterService` reintenta hasta 3 veces con backoff de 500 ms. Si hay 8 fallos consecutivos el circuit breaker se abre 15 segundos para no machacar una impresora con problemas. Mientras está abierto, un loop de fondo sonda la impresora cada 3 segundos y cierra el breaker en cuanto responde.

---

## Transporte COM vs USB

**COM** — conexión persistente. El puerto serie se abre la primera vez y se mantiene abierto entre trabajos. Si falla se intenta reabrir en el siguiente reintento. Configura el baud rate según tu impresora (9600 es el valor habitual en impresoras de tickets, algunas usan 115200).

**USB** — stateless. Cada trabajo va al spooler de Windows vía `winspool.drv` (bytes RAW, sin driver de impresión). Antes de enviar se consulta WMI para detectar si la impresora está offline, sin papel, con puerta abierta o con atasco. El resultado WMI se cachea 4 segundos para evitar latencia (~1-2 s por consulta) en ráfagas de tickets consecutivos.

---

## Builder ESC/POS (opcional)

Si generas tickets desde C#, `EscPosBuilder` evita ensamblar bytes a mano:

```csharp
byte[] ticket = EscPosBuilder
    .Initialize()
    .AlignCenter()
    .BoldOn().TextLine("MI TIENDA").BoldOff()
    .Separator()
    .AlignLeft()
    .LeftRight("Producto A", "12,50 €")
    .LeftRight("Producto B", " 8,00 €")
    .Separator()
    .BoldOn().LeftRight("TOTAL", "20,50 €").BoldOff()
    .Cut()
    .Build();
```

---

## Logs

`%APPDATA%\ServidorImpresion\Logs\impresion-YYYYMMDD.log`

Rotación diaria, retención 30 días. Cada petición lleva un `RequestId` para poder correlacionar logs de una misma llamada.

---

## Estructura del proyecto

```
ServidorImpresion/
├── Configuration/      ConfigData, ConfigStore (DPAPI), WindowsStartupManager
├── Hosting/            AppHost, AppStatusMonitor, LogManager
├── Printing/           PrinterService, CircuitBreaker, transportes COM/USB,
│                       EscPosBuilder, PrintHistoryStore
│   └── Scripts/        ScriptEngine (Roslyn), ITicketScript, Printer helper
├── Server/
│   ├── Filters/        LocalHostFilter, ApiKeyAuthFilter, RateLimitFilter
│   ├── Handlers/       PrintEndpointHandler, HealthEndpointHandler, ScriptEndpointHandler
│   └── Health/         HealthSnapshotBuilder, HealthPageRenderer, DTOs
└── UI/                 MainForm, AppController, TrayService, DeviceDiscoveryService

ServidorImpresion.Tests/
    CircuitBreakerTests, PayloadValidatorTests, PerIpRateLimiterTests,
    PrintJobServiceTests, EscPosBuilderTests, ScriptEngineTests,
    ConfigDataTests, ConfigStoreTests, PrintHistoryStoreTests, AppStatusMonitorTests
```

# ServidorImpresion

Servidor HTTP local para impresoras de tickets en entornos TPV. Corre en la bandeja del sistema, arranca con Windows y expone una API REST que permite imprimir desde cualquier sistema local (ERP, navegador, script…) sin depender de drivers ni configuración especial en el cliente.

---

## Requisitos

- Windows 10/11 (64 bits)
- .NET 10 Desktop Runtime
- Impresora ESC/POS o ZPL conectada por COM o USB

---

## Instalación

Ejecuta el instalador y sigue los pasos. Al finalizar, la app arranca automáticamente y aparece en la bandeja del sistema. También se añade al inicio de Windows.

---

## Primeros pasos

1. **Abre el menú del tray** — clic derecho sobre el icono de la bandeja.
2. **Selecciona la impresora** — elige el puerto COM o la impresora USB instalada en Windows.
3. **Genera la API key** — desde el menú del tray. Una vez generada puedes verla o regenerarla desde el mismo sitio.
4. **Comprueba el estado** — abre `http://localhost:8080/health?key=TU_CLAVE` en el navegador.

---

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

La key también se puede pasar como query string (`?key=TU_CLAVE`).

### Imprimir tickets ESC/POS desde el ERP

El servidor ejecuta scripts C# que reciben el JSON del ERP y generan los bytes ESC/POS. No hace falta tocar la app para cambiar el diseño de un ticket: solo edita el `.cs` correspondiente en la carpeta `scripts/` y el servidor lo recoge en la siguiente petición.

```bash
# Ejecutar script
curl -X POST http://localhost:8080/print/pos/venta \
  -H "X-Api-Key: TU_CLAVE" \
  -H "Content-Type: application/json" \
  -d '[{"empresaData": {...}, "ticketData": {...}}]'

# Listar scripts disponibles
curl http://localhost:8080/print/pos?key=TU_CLAVE
```

Scripts incluidos: `venta`, `factura`, `devolucion`, `aperturaCaja`, `cierre`, `albaran`, `pedido`, `presupuesto`.

Los JSONs de prueba están en `scripts/pruebas/`.

**Si un campo requerido no llega en el JSON**, el servidor devuelve `400` indicando el campo y la línea del script:
```
Campo requerido no encontrado (línea 42): 'cantidadApertura'
```

### Health / estado

```bash
# Dashboard HTML (navegador, auto-refresco)
http://localhost:8080/health?key=TU_CLAVE

# JSON
curl -H "Accept: application/json" http://localhost:8080/health?key=TU_CLAVE
```

Muestra: versión, estado de la impresora, circuit breaker, estadísticas de sesión e históricas, rate limiter y los últimos 50 trabajos con su resultado (persisten entre reinicios).

---

## Seguridad

La API key se guarda cifrada con DPAPI (nunca en texto plano en el disco). Todas las peticiones deben venir de `127.0.0.1`; cualquier acceso externo se rechaza antes de llegar a la validación de clave.

---

## Códigos de respuesta

| Código | Significado |
|-------:|-------------|
| `200` | Trabajo aceptado e impreso |
| `400` | Payload vacío, formato inválido, o campo faltante en el script |
| `401` | API key ausente o incorrecta |
| `413` | Payload demasiado grande (512 KB por defecto) |
| `429` | Rate limit excedido (50 req/s por IP, 200 req/s global) |
| `500` | Error de impresión |
| `503` | Cola saturada (más de 50 trabajos en espera) |
| `504` | Timeout (15 s) |

---

## Configuración

Fichero: `%APPDATA%\ServidorImpresion\config_tpv.json`

| Campo | Por defecto | Descripción |
|-------|-------------|-------------|
| `PuertoServidor` | `8080` | Puerto HTTP |
| `UltimoCOM` | `""` | Puerto serie, p. ej. `COM3` |
| `UltimaUSB` | `""` | Nombre de impresora instalada en Windows |
| `BaudRate` | `9600` | Velocidad del puerto serie (300–115200) |
| `MaxTicketBytes` | `524288` | Tamaño máximo de ticket en bytes |
| `NivelLog` | `"Information"` | Nivel de log: `Debug`, `Information`, `Warning`, `Error`. Aplicable en caliente sin reiniciar. |

Si editas el fichero a mano, los valores fuera de rango se corrigen automáticamente al arrancar.

---

## Logs

`%APPDATA%\ServidorImpresion\Logs\impresion-YYYYMMDD.log`

Rotación diaria, retención 30 días. Cada petición lleva un `RequestId` para poder correlacionar entradas de un mismo trabajo.

---

## Transporte COM vs USB

**COM** — el puerto serie se abre la primera vez y se mantiene abierto entre trabajos. Si falla, se reintenta en el siguiente trabajo. Configura el baud rate según tu impresora (9600 es lo habitual; algunas usan 115200).

**USB** — cada trabajo va al spooler de Windows como datos RAW, sin driver de impresión. Antes de enviar se consulta el estado de la impresora (offline, sin papel, atasco) con una caché de 4 segundos para evitar latencia en ráfagas consecutivas.

En ambos casos el servidor reintenta hasta 3 veces con espera de 500 ms entre intentos. Tras 8 fallos consecutivos, el circuit breaker se abre 15 segundos y sonda la impresora cada 3 segundos hasta que responda.

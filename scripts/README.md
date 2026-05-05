# Guía de scripts de tickets

Los scripts son archivos `.cs` que definen cómo se imprime un tipo de ticket.
Cada archivo vive en esta carpeta y se invoca con `POST /print/pos/{nombre}`.

---

## Estructura mínima

```csharp
using System.Text;
using ServidorImpresion;

public class MiScript : ITicketScript
{
    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc);

        // ... construir el ticket ...

        printer.Cut();
        return printer.Close();
    }
}
```

El servidor llama a `Render()` con los datos del JSON parseados.
`printer.Close()` devuelve los bytes que se mandan a la impresora.

> **Hot-reload:** si modificas un `.cs` el servidor lo recompila automáticamente en la siguiente petición. No hace falta reiniciarlo.

---

## Datos disponibles

Los objetos `empresa` y `ticket` contienen exactamente lo que manda el ERP en el JSON:

```csharp
empresa.nombreEmpresa   empresa.cif
empresa.direccion       empresa.codPostal
empresa.provincia       empresa.textoLegal

ticket.ticket           ticket.total
ticket.pagoEfectivo     ticket.pagoTarjeta
ticket.pagoVale         ticket.pagoGiro
ticket.items            // lista de artículos
```

Cada artículo en `ticket.items`:

```csharp
foreach (var item in ticket.items)
{
    item.cantidad
    item.descripcion
    item.precioUnidad
    item.precioDescuento
    item.dtoPropio       // % de descuento (0 si no hay)
    item.iva             // 4, 10 o 21
}
```

> Los valores numéricos son `decimal`. Para operaciones aritméticas usa cast explícito:
> `decimal total = (decimal)item.cantidad * (decimal)item.precioDescuento;`

---

## Comandos disponibles

### Texto

| Comando | Descripción |
|---|---|
| `printer.Text("hola\n")` | Escribe texto (incluir `\n` para saltar línea) |
| `printer.Feed()` | Salto de línea |
| `printer.Feed(2)` | N saltos de línea |
| `printer.Separator()` | Línea separadora con `=` |
| `printer.Separator('-')` | Línea separadora con el carácter indicado |

### Alineación

| Comando | Descripción |
|---|---|
| `printer.SetJustification(Justify.Left)` | Alinear a la izquierda (por defecto) |
| `printer.SetJustification(Justify.Center)` | Centrar |
| `printer.SetJustification(Justify.Right)` | Alinear a la derecha |

### Tamaño de texto

| Comando | Descripción |
|---|---|
| `printer.SetTextSize(1, 1)` | Tamaño normal |
| `printer.SetTextSize(2, 1)` | Doble ancho |
| `printer.SetTextSize(1, 2)` | Doble alto |
| `printer.SetTextSize(2, 2)` | Doble ancho y alto |

### Estilos

| Comando | Descripción |
|---|---|
| `printer.SetBold(true)` | Negrita activada |
| `printer.SetBold(false)` | Negrita desactivada |
| `printer.SetUnderline(true)` | Subrayado activado |
| `printer.SetUnderline(false)` | Subrayado desactivado |
| `printer.SetFont(0)` | Fuente A (normal, por defecto) |
| `printer.SetFont(1)` | Fuente B (condensada, más caracteres por línea) |

### Código de barras

| Comando | Descripción |
|---|---|
| `printer.Barcode("datos")` | Imprime un código de barras |
| `printer.SetBarcodeHeight(40)` | Altura del código de barras en puntos (por defecto 40) |
| `printer.SetBarcodeWidth(2)` | Ancho de las barras (1-6, por defecto 2) |

### Campos opcionales

Si un campo puede no venir en el JSON, acceder a él directamente con `dynamic` lanza una excepción.
Usa estos helpers para leerlos de forma segura:

```csharp
// Lee un decimal; devuelve def (0 por defecto) si el campo no existe
static decimal Opt(dynamic obj, string key, decimal def = 0)
{
    var d = (IDictionary<string, object?>)obj;
    return d.TryGetValue(key, out var v) && v != null ? (decimal)v : def;
}

// Lee un string; devuelve def ("" por defecto) si el campo no existe
static string OptStr(dynamic obj, string key, string def = "")
{
    var d = (IDictionary<string, object?>)obj;
    return d.TryGetValue(key, out var v) && v != null ? (string)v : def;
}

// Comprueba si un campo existe
static bool Has(dynamic obj, string key)
    => ((IDictionary<string, object?>)obj).ContainsKey(key);
```

```csharp
// Parte un texto largo en líneas que caben en 'ancho' caracteres
static void WordWrap(Printer printer, string texto, int ancho)
{
    var palabras = texto.Trim().Split(' ');
    string linea = "";
    foreach (var palabra in palabras)
    {
        if (linea.Length + 1 + palabra.Length > ancho)
        {
            printer.Text(linea.Trim() + "\n");
            linea = palabra;
        }
        else
        {
            linea += (linea == "" ? "" : " ") + palabra;
        }
    }
    if (linea != "") printer.Text(linea.Trim() + "\n");
}
```

Uso: `WordWrap(printer, (string)empresa.textoLegal, 47);`

Requiere `using System.Collections.Generic;` al inicio del script.

Uso:

```csharp
decimal dto = Opt(item, "dtoPropio");          // 0 si no viene
decimal iva = Opt(item, "iva", 21);            // 21 si no viene
string nif  = OptStr(cliente, "nif");          // "" si no viene

if (Has(ticket, "cliente")) { /* mostrar datos cliente */ }
```

---

### Alineación de columnas

Para alinear texto en columnas se usan los métodos estándar de C#:

| Comando | Descripción |
|---|---|
| `s.PadRight(10)` | Texto alineado a la izquierda en campo de 10 chars |
| `s.PadLeft(10)` | Texto alineado a la derecha en campo de 10 chars |
| `new string('=', 47)` | Repetir un carácter N veces |

### Fin de ticket

| Comando | Descripción |
|---|---|
| `printer.Cut()` | Corta el papel |
| `printer.Pulse()` | Abre el cajón portamonedas |
| `printer.Close()` | **Obligatorio al final** — devuelve los bytes |

---

## Ejemplo completo

```csharp
using System;
using System.Text;
using ServidorImpresion;

public class VentaScript : ITicketScript
{
    public byte[] Render(dynamic empresa, dynamic ticket, Encoding enc)
    {
        var printer = new Printer(enc);

        // Cabecera
        printer.SetJustification(Justify.Center);
        printer.SetTextSize(2, 2);
        printer.Text(empresa.nombreEmpresa + "\n");
        printer.SetTextSize(1, 1);
        printer.Feed();
        printer.Text("CIF: " + empresa.cif + "\n");
        printer.Text(empresa.direccion + "\n");
        printer.Feed(2);

        // Número de ticket
        printer.SetJustification(Justify.Left);
        printer.Text("Ticket: " + ticket.ticket + "\n");
        printer.Feed(2);

        // Artículos
        foreach (var item in ticket.items)
        {
            printer.Text(item.cantidad + "  " + item.descripcion + "\n");
            decimal total = (decimal)item.cantidad * (decimal)item.precioDescuento;
            printer.Text("     " + ((decimal)item.precioUnidad).ToString("N2") + " €   Total: " + total.ToString("N2") + " €\n");
        }

        printer.Separator('=');

        // Total
        printer.SetJustification(Justify.Center);
        printer.SetTextSize(2, 2);
        printer.Text("TOTAL: " + ((decimal)ticket.total).ToString("N2") + " EUR\n");
        printer.SetTextSize(1, 1);
        printer.Feed(2);

        // Pie
        printer.Text("Fecha: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + "\n");
        printer.Feed();
        printer.SetJustification(Justify.Center);
        printer.Text("Gracias por su visita\n");
        printer.Barcode((string)ticket.ticket);
        printer.Feed();
        printer.Cut();
        printer.Pulse();

        return printer.Close();
    }
}
```

---

## Llamada

```
POST http://localhost:{puerto}/print/pos/venta
Content-Type: application/json

[{
  "empresaData": { ... },
  "ticketData":  { ... }
}]
```

Si el script tiene un error, el servidor devuelve `400` con el mensaje del compilador.
Si el archivo no existe, devuelve `404`.

Para listar los scripts disponibles:

```
GET http://localhost:{puerto}/print/pos
→ ["factura","venta"]
```

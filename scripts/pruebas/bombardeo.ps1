$url = "http://localhost:8080/script/factura"

Write-Host "Modo ultra-compatible (Zero .NET Methods)" -ForegroundColor Cyan

for ($i = 1; $i -le 20; $i++) {
    # JSON plano. Evitamos variables complejas dentro.
    $json = @"
[{
  "empresaData": {
    "nombreEmpresa": "Ferretería López",
    "cif": "B12345678",
    "direccion": "Calle Mayor, 12",
    "codPostal": "28001",
    "provincia": "Madrid",
    "textoLegal": "Factura emitida conforme a la normativa vigente. Conserve este documento."
  },
  "ticketData": {
    "ticket": "F-2024-00089",
    "cliente": {
      "razon_social": "Construcciones Ruiz SL",
      "nif": "B11223344",
      "direccion": "Avda. de la Industria, 45",
      "cod_postal": "28100 Madrid",
      "regimen": "NORMAL"
    },
    "items": [
      {
        "cantidad": 3,
        "descripcion": "Cemento Portland 25kg",
        "precioUnidad": 8.90,
        "precioDescuento": 8.01,
        "dtoPropio": 10,
        "iva": 21
      },
      {
        "cantidad": 10,
        "descripcion": "Ladrillo hueco doble",
        "precioUnidad": 0.45,
        "precioDescuento": 0.45,
        "dtoPropio": 0,
        "iva": 21
      }
    ],
    "pagoEfectivo": 30.00,
    "pagoTarjeta": 0,
    "pagoVale": 0,
    "pagoGiro": 0,
    "total": 28.53
  }
}]
"@

    try {
        # Invoke-RestMethod con string directo. 
        # PowerShell se encarga de la conversion a bytes internamente sin llamar a GetBytes().
        Invoke-RestMethod -Uri $url -Method Post -Body $json -ContentType "application/json"
        Write-Host "Ticket #$i enviado OK" -ForegroundColor Green
    } catch {
        Write-Host "Ticket #$i ERROR: $($_.Exception.Message)" -ForegroundColor Red
    }
}
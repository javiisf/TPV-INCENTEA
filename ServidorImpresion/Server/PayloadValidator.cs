using System;

namespace ServidorImpresion
{
    /// <summary>
    /// Valida que un payload sea un comando de impresora legítimo (ESC/POS, ZPL, texto plano).
    /// Rechaza ejecutables y archivos maliciosos.
    /// </summary>
    public static class PayloadValidator
    {
        public static bool IsValidPrinterPayload(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return false;

            // --- Blacklist: rechazar formatos peligrosos por magic bytes ---
            // Se verifica desde el offset 0 y, cuando el primer byte es un prefijo
            // ESC (0x1B) o GS (0x1D), también desde el offset 1. Esto cierra el bypass
            // trivial de anteponer un byte ESC/POS a cualquier binario peligroso.
            if (HasDangerousMagicBytes(bytes, 0)) return false;
            if ((bytes[0] == 0x1B || bytes[0] == 0x1D) && HasDangerousMagicBytes(bytes, 1)) return false;

            // --- Whitelist: aceptar formatos de impresión conocidos ---
            // ESC/POS: secuencia ESC (0x1B)
            if (bytes[0] == 0x1B) return true;
            // GS commands ESC/POS
            if (bytes[0] == 0x1D) return true;
            // ZPL: comienza con ^ o ~
            if (bytes[0] == (byte)'^' || bytes[0] == (byte)'~') return true;

            // --- Validación de texto plano ---
            // El primer byte debe ser un carácter imprimible
            if (bytes[0] < 0x20 || bytes[0] > 0x7E)
                return false;

            int sampleSize = Math.Min(bytes.Length, 1024);
            int step = Math.Max(1, bytes.Length / sampleSize);
            int validCount = 0;
            int checkedCount = 0;

            for (int i = 0; i < bytes.Length && checkedCount < sampleSize; i += step)
            {
                byte b = bytes[i];

                // Bytes nulos son indicadores fiables de contenido binario
                if (b == 0x00)
                    return false;

                if ((b >= 0x20 && b <= 0x7E) || b == 0x0A || b == 0x0D || b == 0x09 || (b >= 0x80 && b <= 0xFF))
                    validCount++;

                checkedCount++;
            }

            // Umbral estricto: 95 % de los bytes muestreados deben ser válidos
            if (checkedCount == 0 || validCount < (int)Math.Ceiling(checkedCount * 0.95))
                return false;

            // Para payloads largos exigir al menos un salto de línea en los primeros 512 bytes:
            // un ticket real siempre tiene varias líneas.
            if (bytes.Length > 128)
            {
                int checkRange = Math.Min(bytes.Length, 512);
                for (int i = 0; i < checkRange; i++)
                {
                    if (bytes[i] == 0x0A || bytes[i] == 0x0D)
                        return true;
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Devuelve <c>true</c> si se detecta un magic number de formato binario peligroso
        /// comenzando en <paramref name="offset"/>. Requiere al menos 2 bytes disponibles;
        /// los patrones de 3-4 bytes usan 0x00 como fallback si no hay suficientes bytes,
        /// lo que nunca coincide con ningún patrón real.
        /// </summary>
        private static bool HasDangerousMagicBytes(byte[] bytes, int offset)
        {
            int remaining = bytes.Length - offset;
            if (remaining < 2) return false;

            byte b0 = bytes[offset];
            byte b1 = bytes[offset + 1];
            byte b2 = remaining >= 3 ? bytes[offset + 2] : (byte)0;
            byte b3 = remaining >= 4 ? bytes[offset + 3] : (byte)0;

            // Windows PE (MZ)
            if (b0 == 0x4D && b1 == 0x5A) return true;
            // ELF
            if (b0 == 0x7F && b1 == 0x45 && b2 == 0x4C && b3 == 0x46) return true;
            // ZIP / JAR / DOCX / XLSX
            if (b0 == 0x50 && b1 == 0x4B) return true;
            // PDF
            if (b0 == 0x25 && b1 == 0x50 && b2 == 0x44 && b3 == 0x46) return true;
            // GZIP
            if (b0 == 0x1F && b1 == 0x8B) return true;
            // RAR
            if (b0 == 0x52 && b1 == 0x61 && b2 == 0x72 && b3 == 0x21) return true;
            // OLE / MS-Office (DOC, XLS, PPT)
            if (b0 == 0xD0 && b1 == 0xCF && b2 == 0x11 && b3 == 0xE0) return true;
            // PNG
            if (b0 == 0x89 && b1 == 0x50 && b2 == 0x4E && b3 == 0x47) return true;
            // GIF87a / GIF89a
            if (b0 == 0x47 && b1 == 0x49 && b2 == 0x46 && b3 == 0x38) return true;
            // 7-Zip
            if (b0 == 0x37 && b1 == 0x7A && b2 == 0xBC && b3 == 0xAF) return true;
            // WebAssembly
            if (b0 == 0x00 && b1 == 0x61 && b2 == 0x73 && b3 == 0x6D) return true;
            // Java class file
            if (b0 == 0xCA && b1 == 0xFE && b2 == 0xBA && b3 == 0xBE) return true;
            // Mach-O little-endian 32-bit
            if (b0 == 0xCE && b1 == 0xFA && b2 == 0xED && b3 == 0xFE) return true;
            // Mach-O little-endian 64-bit
            if (b0 == 0xCF && b1 == 0xFA && b2 == 0xED && b3 == 0xFE) return true;
            // SQLite database
            if (b0 == 0x53 && b1 == 0x51 && b2 == 0x4C && b3 == 0x69) return true;
            // JPEG
            if (b0 == 0xFF && b1 == 0xD8 && b2 == 0xFF) return true;

            return false;
        }
    }
}

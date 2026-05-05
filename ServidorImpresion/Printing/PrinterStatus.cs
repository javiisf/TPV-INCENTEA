using System;

namespace ServidorImpresion
{
    public class PrinterStatus
    {
        public bool CircuitBreakerOpen { get; set; }
        public int ConsecutiveFailures { get; set; }
        public int CooldownRemainingSeconds { get; set; }

        public DateTime? LastErrorUtc { get; set; }
        public string LastErrorMessage { get; set; } = string.Empty;
    }
}

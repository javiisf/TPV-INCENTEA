namespace ServidorImpresion
{
    /// <summary>
    /// Resultado de una operación de impresión.
    /// </summary>
    public class PrintResult
    {
        public bool IsSuccess { get; }
        public string Message { get; }

        private PrintResult(bool isSuccess, string message)
        {
            IsSuccess = isSuccess;
            Message = message;
        }

        public static PrintResult SuccessResult(string message) => new PrintResult(true, message);
        public static PrintResult FailureResult(string message) => new PrintResult(false, message);
    }
}

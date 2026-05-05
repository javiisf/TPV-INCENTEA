using System.Threading.Tasks;

namespace ServidorImpresion
{
    /// <summary>
    /// Interfaz para handlers de peticiones HTTP.
    /// Cada handler decide si puede manejar la petición (CanHandle)
    /// y si puede, la procesa (HandleAsync).
    /// </summary>
    public interface IRequestHandler
    {
        bool CanHandle(RequestContext ctx);
        Task HandleAsync(RequestContext ctx);
    }
}

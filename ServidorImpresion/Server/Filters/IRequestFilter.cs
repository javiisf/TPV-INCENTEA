using System.Threading.Tasks;

namespace ServidorImpresion
{
    /// <summary>
    /// Filtro cross-cutting que se ejecuta antes de los handlers.
    /// Retorna true para continuar el pipeline, false si ya respondió (rate-limit, auth, etc.).
    /// </summary>
    public interface IRequestFilter
    {
        bool AppliesTo(RequestContext ctx);
        Task<bool> ExecuteAsync(RequestContext ctx);
    }
}

using System.Text;

namespace ServidorImpresion
{
    public interface ITicketScript
    {
        byte[] Render(dynamic empresa, dynamic ticket, Encoding encoding);
    }
}

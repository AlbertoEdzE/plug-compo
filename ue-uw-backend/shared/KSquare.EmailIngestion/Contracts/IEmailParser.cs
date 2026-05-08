using KSquare.EmailIngestion.Models;

namespace KSquare.EmailIngestion.Contracts;

public interface IEmailParser
{
    EmailMessage Parse(byte[] rawEmail);
}

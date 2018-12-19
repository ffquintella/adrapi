using System;
namespace adrapi.domain.Exceptions
{
    public class InvalidCredentialsException: Exception
    {
        public InvalidCredentialsException() : base() { }
        public InvalidCredentialsException(string message): base(message) { }

    }
}

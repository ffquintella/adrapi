using System;
namespace adrapi.domain.Exceptions
{
    public class SSLRequiredException: Exception
    {
        public SSLRequiredException() : base() { }
        public SSLRequiredException(string message): base(message) { }

    }
}

using System;
namespace adrapi.domain.Exceptions
{
    public class NullException: Exception
    {
        public NullException() : base() { }
        public NullException(string message): base(message) { }

    }
}

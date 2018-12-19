using System;
namespace adrapi.domain.Exceptions
{
    public class WrongParameterException: Exception
    {
        public WrongParameterException() : base() { }
        public WrongParameterException(string message): base(message) { }

    }
}

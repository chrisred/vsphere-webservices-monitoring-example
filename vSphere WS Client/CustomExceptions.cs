using System;

namespace vSphereWsClient
{
    internal class NotAuthenticatedException : Exception
    {
        public NotAuthenticatedException()
        {
        }

        public NotAuthenticatedException(string message) : base(message)
        {
        }

        public NotAuthenticatedException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    internal class ObjectDeletedException : Exception
    {
        public ObjectDeletedException()
        {
        }

        public ObjectDeletedException(string message) : base(message)
        {
        }

        public ObjectDeletedException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}


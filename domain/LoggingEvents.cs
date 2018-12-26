using System;
namespace adrapi.domain
{
    public static class LoggingEvents
    {
        public const int GenerateItems = 1000;
        public const int ListItems = 1001;
        public const int GetItem = 1002;
        public const int InsertItem = 1003;
        public const int UpdateItem = 1004;
        public const int DeleteItem = 1005;
        public const int ItemExists = 1006;
        public const int PutItem = 1007;

        public const int GetItemNotFound = 4000;
        public const int UpdateItemNotFound = 4001;

        public const int InternalError = 5001;


    }
}

// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

namespace SharpDc.Messages
{
    public struct ErrorMessage : IStringMessage
    {
        public string Error;

        public string Raw => $"$Error {Error}";

        public static ErrorMessage Parse(string command)
        {
            ErrorMessage msg;

            msg.Error = command.Substring(7);

            return msg;
        }
    }
}
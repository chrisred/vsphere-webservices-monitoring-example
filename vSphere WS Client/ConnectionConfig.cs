using System;

namespace vSphereWsClient
{
    /// <summary>
    /// Store the details for a Connection.
    /// </summary>
    public class ConnectionConfig
    {
        public string Username { get; private set; }
        public string Password { get; private set; }
        public string Url { get; private set; }

        public ConnectionConfig(string username, string password, string url)
        {
            Username = username;
            Password = password;
            Url = url;
        }
    }
}

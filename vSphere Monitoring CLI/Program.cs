using System;
using System.Linq;
using vSphereWsClient;
using Vim25Api;

namespace vSphereMonitoringCLI
{
    class Program
    {
        static void Main(string[] args)
        {
            string username = args.SkipWhile(s => s != "--username").Skip(1).FirstOrDefault();
            string password = args.SkipWhile(s => s != "--password").Skip(1).FirstOrDefault();
            string url = args.SkipWhile(s => s != "--url").Skip(1).FirstOrDefault();
            bool vsan = args.Any(s => s == "--vsan");
            bool vsanFullSummary = args.Any(s => s == "--vsanfullsummary");

            // initialize and autenticate a connection to the vSphere Web Service endpoint
            Console.WriteLine($"Creating connection to {url}, please wait...");
            var conn = new Connection(new ConnectionConfig(username, password, url));
            conn.Connect();

            // use the container views created by the Connection object to get an inventory of managed objects
            ObjectContent[] clusters = conn.RetrieveViewProperties(conn.ClusterView, "ClusterComputeResource", new string[] { "name" });
            ObjectContent[] datastores = conn.RetrieveViewProperties(conn.DatastoreView, "Datastore", new string[] { "name" });
            ObjectContent[] hosts = conn.RetrieveViewProperties(conn.HostView, "HostSystem", new string[] { "name" });

            // iterate over the managed objects in the views and pull the metrics
            while (true)
            {
                Console.WriteLine("CLUSTERS");
                foreach (var cluster in clusters)
                {
                    var clusterMetrics = new ClusterMetrics(conn, cluster.obj);
                    Console.WriteLine($"  Name: {clusterMetrics.Name}");
                    Console.WriteLine($"  Alarm Status: {clusterMetrics.AlarmStatus}");

                    if (vsan)
                    {
                        var vsanHealth = new VsanClusterHealth(conn, cluster.obj, vsanFullSummary);
                        Console.WriteLine($"  vSAN Overall Health: {vsanHealth.OverallHealth}");
                    }

                    Console.WriteLine("");
                }

                Console.WriteLine("DATASTORES");
                foreach (var datastore in datastores)
                {
                    var datastoreMetrics = new DatastoreMetrics(conn, datastore.obj);
                    Console.WriteLine($"  Name: {datastoreMetrics.Name}");
                    Console.WriteLine($"  Alarm Status: {datastoreMetrics.AlarmStatus}");
                    Console.WriteLine($"  Free Space %: {datastoreMetrics.FreeSpacePercentage}");
                    Console.WriteLine("");
                }

                Console.WriteLine("HOSTS");
                foreach (var host in hosts)
                {
                    var hostMetrics = new HostMetrics(conn, host.obj);
                    Console.WriteLine($"  Name: {hostMetrics.Name}");
                    Console.WriteLine($"  Alarm Status: {hostMetrics.AlarmStatus}");
                    Console.WriteLine($"  Status: {hostMetrics.ConnectionState}");
                    Console.WriteLine($"  CPU Usage %: {hostMetrics.CpuUsagePercentage}");
                    Console.WriteLine($"  Memory Usage %: {hostMetrics.MemoryUsagePercentage}");
                    Console.WriteLine("");
                }

                Console.WriteLine("Press ESC to exit or any key to refresh.");
                Console.WriteLine("--------------------------------------------------------------------------------");
                Console.WriteLine("");

                ConsoleKey key = Console.ReadKey().Key;
                if (key == ConsoleKey.Escape)
                {
                    conn.Disconnect();
                    break;
                }
            }
        }
    }
}

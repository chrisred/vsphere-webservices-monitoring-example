using System;
using System.Linq;
using vSphereWsClient;
using Vim25Api;

namespace vSphereMonitoringCLI
{
    /// <summary>
    /// Collect the common metrics that should be present on the managed object.
    /// </summary>
    public class Metrics
    {
        public string AlarmStatus { get; private set; }
        public string Name { get; private set; }
        protected Connection Conn { get; }
        public ManagedObjectReference Mor { get; }

        /// <summary>
        /// Initialize a Metric with a reference to the managed object and Connection used to access it.
        /// </summary>
        /// <param name="conn">The Connection used to access the managed object and metrics.</param>
        /// <param name="mor">The managed object reference to read the metrics from.</param>
        public Metrics(Connection conn, ManagedObjectReference mor)
        {
            Conn = conn;
            Mor = mor;
            Poll();
        }

        /// <summary>
        /// Retreieve the name of the managed object.
        /// </summary>
        /// <returns></returns>
        protected string GetName()
        {
            ObjectContent[] content = Conn.RetreieveObjectProperties(Mor, new string[] { "name" });
            return content[0].propSet[0].val.ToString();
        }

        /// <summary>
        /// Retreive the alarm status of the managed object.
        /// </summary>
        /// <returns></returns>
        protected string GetAlarmStatus()
        {
            ObjectContent[] content = Conn.RetreieveObjectProperties(Mor, new string[] { "overallStatus" });
            return content[0].propSet[0].val.ToString();
        }

        /// <summary>
        /// Poll the managed object for the current metrics.
        /// </summary>
        public virtual void Poll()
        {
            Name = GetName();
            AlarmStatus = GetAlarmStatus();
        }
    }

    /// <summary>
    /// Collect metrics from Cluster managed objects.
    /// </summary>
    public class ClusterMetrics : Metrics
    {
        public int NumberOfHosts { get; private set; }

        public ClusterMetrics(Connection conn, ManagedObjectReference mor) : base(conn, mor)
        {
            Poll();
        }

        private int GetNumberOfHosts()
        {
            ObjectContent[] content = Conn.RetreieveObjectProperties(Mor, new string[] { "summary.numHosts" });
            return (int)content[0].propSet[0].val;
        }

        /// <summary>
        /// Poll the Cluster object for the current metrics.
        /// </summary>
        public override void Poll()
        {
            base.Poll();
            NumberOfHosts = GetNumberOfHosts();
        }
    }

    /// <summary>
    /// Collect metrics from Datastore managed objects.
    /// </summary>
    public class DatastoreMetrics : Metrics
    {
        public double FreeSpacePercentage { get; private set; }

        public DatastoreMetrics(Connection conn, ManagedObjectReference mor) : base(conn, mor)
        {
            Poll();
        }

        /// <summary>
        /// Retreieve avaiable free space from the Datastore as a percentage.
        /// </summary>
        /// <returns>A Double rounded to the nearest integer.</returns>
        private double GetFreeSpacePercentage()
        {
            ObjectContent[] content = Conn.RetreieveObjectProperties(Mor, new string[] { "summary" });
            var summary = (DatastoreSummary)content[0].propSet[0].val;
            double usage = summary.capacity - summary.freeSpace;
            return Math.Round(usage / summary.capacity * 100);
        }

        /// <summary>
        /// Poll the Datastore object for the current metrics.
        /// </summary>
        public override void Poll()
        {
            base.Poll();
            FreeSpacePercentage = GetFreeSpacePercentage();
        }
    }

    /// <summary>
    /// Collect metrics from Host managed objects.
    /// </summary>
    public class HostMetrics : Metrics
    {
        public double UptimeMinutes { get; private set; }
        public string ConnectionState { get; private set; }
        public double CpuUsagePercentage { get; private set; }
        public double MemoryUsagePercentage { get; private set; }

        public HostMetrics(Connection conn, ManagedObjectReference mor) : base(conn, mor)
        {
            Poll();
        }

        /// <summary>
        /// Retreieve the host uptime in minutes.
        /// </summary>
        /// <returns></returns>
        private double GetUptimeMinutes()
        {
            ObjectContent[] content = Conn.RetreieveObjectProperties(Mor, new string[] { "summary.quickStats" });
            var quickStats = (HostListSummaryQuickStats)content[0].propSet[0].val;
            return quickStats.uptime / 60;
        }

        /// <summary>
        /// Retreieve the host connection state.
        /// </summary>
        /// <returns></returns>
        private string GetConnectionState()
        {
            ObjectContent[] content = Conn.RetreieveObjectProperties(Mor, new string[] { "runtime" });
            var runtime = (HostRuntimeInfo)content[0].propSet[0].val;
            return runtime.connectionState.ToString();
        }

        /// <summary>
        /// Retreieve the host CPU usage as a percentage.
        /// </summary>
        /// <returns>A Double rounded to the nearest integer.</returns>
        private double GetCpuUsagePercentage()
        {
            PerfEntityMetric metric = Conn.QueryRawPerformaceMetric(Mor, "cpu.usage.average");
            PerfMetricIntSeries series = (PerfMetricIntSeries)metric.value[0];
            return Math.Round(series.value.Average() / 100);
        }

        /// <summary>
        /// Retreieve the host memory usage as a percentage.
        /// </summary>
        /// <returns>A Double rounded to the nearest integer.</returns>
        private double GetMemoryUsagePercentage()
        {
            PerfEntityMetric metric = Conn.QueryRawPerformaceMetric(Mor, "mem.usage.average");
            PerfMetricIntSeries series = (PerfMetricIntSeries)metric.value[0];
            return Math.Round(series.value.Average() / 100);
        }

        public override void Poll()
        {
            base.Poll();
            UptimeMinutes = GetUptimeMinutes();
            ConnectionState = GetConnectionState();
            CpuUsagePercentage = GetCpuUsagePercentage();
            MemoryUsagePercentage = GetMemoryUsagePercentage();
        }
    }

    /// <summary>
    /// Collect metrics from the vSAN Cluster Health managed object.
    /// </summary>
    public class VsanClusterHealth
    {
        public bool UseCache { get; set; }
        public bool UseFullSummary { get; set; }
        public VsanHealthApi.VsanClusterHealthGroup[] HealthGroup { get; private set; }
        public VsanHealthApi.VsanClusterEncryptionHealthSummary EncryptionHealthSummary { get; private set; }
        public VsanHealthApi.VsanClusterFileServiceHealthSummary FileServiceHealthSummary { get; private set; }
        public VsanHealthApi.VsanClusterLimitHealthResult CapacityHealthSummary { get; private set; }
        public VsanHealthApi.VsanClusterNetworkHealthResult NetworkHealthSummary { get; private set; }
        public VsanHealthApi.VsanObjectOverallHealth ObjectHealthSummary { get; private set; }
        public VsanHealthApi.VsanPerfsvcHealthResult PerformanceServiceHealthSummary { get; private set; }
        public string OverallHealth { get; private set; }
        public string OverallHealthDescription { get; private set; }
        public DateTime Timestamp { get; private set; }

        private Connection _conn;
        private VsanHealthApi.ManagedObjectReference _vsanClusterMor;

        public VsanClusterHealth(Connection conn, ManagedObjectReference mor, bool useFullSummary = false, bool useCache = true)
        {
            _conn = conn;
            _vsanClusterMor = new VsanHealthApi.ManagedObjectReference
            {
                type = mor.type,
                Value = mor.Value
            };

            UseCache = useCache;
            UseFullSummary = useFullSummary;

            Poll();
        }

        /// <summary>
        /// Retreieve vSAN cluster health summary information.
        /// </summary>
        private void GetHealthSummary()
        {
            var properties = new string[] {
                "encryptionHealth", "fileServiceHealth", "groups", "limitHealth", "networkHealth", "objectHealth", "overallHealth",
                "overallHealthDescription", "perfsvcHealth", "timestamp"
            };
            var healthSummary = _conn.QueryVsanClusterHealthSummary(_vsanClusterMor, properties, UseCache);

            Timestamp = healthSummary.timestamp;
            HealthGroup = healthSummary.groups;
            OverallHealth = healthSummary.overallHealth;
            OverallHealthDescription = healthSummary.overallHealthDescription;

            if (UseFullSummary)
            {
                EncryptionHealthSummary = healthSummary.encryptionHealth;
                FileServiceHealthSummary = healthSummary.fileServiceHealth;
                CapacityHealthSummary = healthSummary.limitHealth;
                NetworkHealthSummary = healthSummary.networkHealth;
                ObjectHealthSummary = healthSummary.objectHealth;
                PerformanceServiceHealthSummary = healthSummary.perfsvcHealth;
            }
        }

        public void Poll()
        {
            GetHealthSummary();
        }
    }
}

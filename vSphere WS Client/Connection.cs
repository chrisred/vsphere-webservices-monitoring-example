using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Web.Services.Protocols;
using Vim25Api;
using VMware.Binding.WsTrust;

namespace vSphereWsClient
{
    /// <summary>
    ///  Manages connections to a vSphere Web Services API endpoint. This can be a vCenter server or ESXi host.
    /// </summary>
    public class Connection
    {
        public VimPortType Service { get; private set; }
        public VsanHealthApi.VsanhealthService VsanHealthService { get; private set; }
        public ServiceContent Content { get; private set; }
        public ManagedObjectReference ServiceInstance { get; private set; }
        public ManagedObjectReference ClusterView { get; private set; }
        public ManagedObjectReference DatastoreView { get; private set; }
        public ManagedObjectReference HostView { get; private set; }
        public ManagedObjectReference VmView { get; private set; }
        /// <summary>
        /// Indicates whether the connection is pre (Pending) or post (Created) authentication. This would show whether
        /// an autenticated<br/> session had already been created. However it doesn't account for sessions timing out or
        /// being disconnected on the server side.
        /// </summary>
        public ConnectionStates ConnectionState { get; private set; }
        public ConnectionConfig Config { get; private set; }

        private Dictionary<string, PerfCounterInfo> _counters;
        private Uri _vsanUri;
        private string _vsanNamespace;

        public enum ConnectionStates
        {
            Pending = 0,
            Created = 1
        }

        /// <summary>
        /// Initialise a connection to the vSphere WS API endpoint, this is hosted on a vCenter server or ESXi host.
        /// </summary>
        /// <param name="config">A ConnectionConfig object with details of the endpoint to connect to.</param>
        /// <param name="ignoreCert">When set to true will ignore untrusted certificate errors.</param>
        public Connection(ConnectionConfig config, bool ignoreCert = true)
        {
            // this hard codes TLS 1.1 and 1.2 as enabled, TLS 1.2 is not enabled by default until .NET 4.6
            //ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            if (ignoreCert)
            {
                // ignore certificate validity by proving a callback that is alway true
                ServicePointManager.ServerCertificateValidationCallback += new RemoteCertificateValidationCallback(
                    ValidateRemoteCertificate
                );
            }

            // setup connections to the service endpoint, unauthenticated at this stage
            Config = config;
            ServiceInstance = new ManagedObjectReference { type = "ServiceInstance", Value = "ServiceInstance" };
            Service = CreateService(config.Url);
            Content = Service.RetrieveServiceContent(ServiceInstance);
            VsanHealthService = CreateVsanHealthService(config.Url);
            ConnectionState = ConnectionStates.Pending;
        }

        /// <summary>
        /// Authenticate the connection and create views for key inventory types.
        /// </summary>
        public void Connect()
        {
            // authenticate the vCenter/ESX Service endpoint connection
            Service.Login(Content.sessionManager, Config.Username, Config.Password, null);
            HostView = CreateContainerView(Content.rootFolder, "HostSystem");
            DatastoreView = CreateContainerView(Content.rootFolder, "Datastore");
            ClusterView = CreateContainerView(Content.rootFolder, "ClusterComputeResource");
            VmView = CreateContainerView(Content.rootFolder, "VirtualMachine");
            _counters = CreatePerformaceCounters();

            // authenticate the vSAN Health Service endpoint, uses same credentials as the vCenter connection
            ConnectVsanHealth();

            // set connection state to show an authenticated session has been created
            ConnectionState = ConnectionStates.Created;
        }

        /// <summary>
        /// Authenticate the vSAN health service connection. This uses the session created by the vSphere WS API
        /// connection.
        /// </summary>
        private void ConnectVsanHealth()
        {
            CookieContainer cookieContainer = new CookieContainer();
            var cookieManager = ((IContextChannel)Service).GetProperty<IHttpCookieContainerManager>();
            CookieCollection cookie = cookieManager.CookieContainer.GetCookies(_vsanUri);
            cookieContainer.Add(cookie);
            VsanHealthService.ConnectVsanService(_vsanNamespace, _vsanUri.OriginalString, cookieContainer, 30000);
        }

        /// <summary>
        /// Disconnect from the vSphere WS API by closing the session.
        /// </summary>
        public void Disconnect()
        {
            Service.Logout(Content.sessionManager);
            ConnectionState = ConnectionStates.Pending;
        }

        private bool ValidateRemoteCertificate(
            object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors policyErrors)
        {
            return true;
        }

        /// <summary>
        /// Create a reference to the ServiceInstance object, this is the root of the vSphere inventory and can be used
        /// to gain access to the other managed objects.
        /// </summary>
        /// <remarks>https://code.vmware.com/apis/704/vsphere/vim.ServiceInstance.html</remarks>
        /// <param name="url">The vSphere WS API endpoint URL.</param>
        /// <returns>A VimPortType object which represents the ServiceInstance.</returns>
        private VimPortType CreateService(string url)
        {
            // VMware STS requires SOAP version 1.1
            var wcfEncoding = new TextMessageEncodingBindingElement(MessageVersion.Soap11, Encoding.UTF8);
            // Communication with the STS is over https
            var wcfTransport = new HttpsTransportBindingElement
            {
                RequireClientCertificate = false,
                AllowCookies = true,
                MaxReceivedMessageSize = int.MaxValue
            };

            // There is no built-in WCF binding capable of communicating with VMware STS, so we create a custom one. The
            // binding does not provide support for WS-Trust, that is currently implemented as a WCF endpoint behaviour.
            var binding = new CustomBinding(wcfEncoding, wcfTransport);
            var address = new EndpointAddress(url);
            var factory = new ChannelFactory<VimPortType>(binding, address);

            // Attach the behaviour that handles the WS-Trust 1.4 protocol for VMware Vim Service
            factory.Endpoint.Behaviors.Add(new WsTrustBehavior());
            factory.Credentials.SupportInteractive = false;
            VimPortType service = factory.CreateChannel();

            return service;
        }

        /// <summary>
        /// Create a reference to the VsanhealthService object.
        /// </summary>
        /// <param name="url">The vSAN WS API endpoint URL.</param>
        /// <returns></returns>
        private VsanHealthApi.VsanhealthService CreateVsanHealthService(string url)
        {
            Uri configUri = new Uri(url);

            // choose the vSAN endpoint URL based on the current connection type (ESX/vCenter)
            if (Content.about.apiType == "HostAgent")
            {
                _vsanUri = new Uri($"https://{configUri.Host}/vsan");
            }
            else
            {
                _vsanUri = new Uri($"https://{configUri.Host}/vsanHealth");
            }

            // The vSAN namespace can be "urn:vim25" for versions prior to 6.6 and "urn:vsan/6.x" for versions after.
            // https:/host/sdk/vsanServiceVersions.xml will reutrn the supported vSAN versions, however "urn:vsan"
            // appears to select a default version based on the SDK in use, this seems to be a reasonable default.
            _vsanNamespace = "urn:vsan";

            var vsanHealthService = new VsanHealthApi.VsanhealthService();
            return vsanHealthService;
        }

        /// <summary>
        /// Create a ContainerView managed object for this session.
        /// </summary>
        /// <remarks>https://code.vmware.com/apis/704/vsphere/vim.view.ViewManager.html#createContainerView</remarks>
        /// <param name="root"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private ManagedObjectReference CreateContainerView(ManagedObjectReference root, string type)
        {
            var request = new CreateContainerViewRequest
            {
                _this = Content.viewManager,
                container = root,
                type = new string[] { type },
                recursive = true
            };

            return Service.CreateContainerView(request).returnval;
        }

        /// <summary>
        /// Create a dictionary which maps performance counters using a key in the format "group.name.rollup", for
        /// example "cpu.usage.average".
        /// </summary>
        /// <returns></returns>
        private Dictionary<string, PerfCounterInfo> CreatePerformaceCounters()
        {
            var propertySpecs = new PropertySpec[] { new PropertySpec() };
            propertySpecs[0].all = false;
            propertySpecs[0].pathSet = new string[] { "perfCounter" };
            propertySpecs[0].type = "PerformanceManager";

            var propertyFilterSpec = new PropertyFilterSpec();
            propertyFilterSpec.propSet = propertySpecs;
            propertyFilterSpec.objectSet = new ObjectSpec[] { new ObjectSpec() };
            propertyFilterSpec.objectSet[0].obj = Content.perfManager;
            var propertyFilterSpecs = new PropertyFilterSpec[] { propertyFilterSpec };

            // select the "perfCounter" property to get a list of counters and IDs, the ID may change between sessions
            var options = new RetrieveOptions();
            var request = new RetrievePropertiesExRequest(Content.propertyCollector, propertyFilterSpecs, options);
            RetrieveResult results = Service.RetrievePropertiesEx(request).returnval;
            PerfCounterInfo[] counters = (PerfCounterInfo[])results.objects[0].propSet[0].val;

            // add all the counters to a Dictionary where a counter can be accessed as d["cpu.usage.average"]
            var counterMap = new Dictionary<string, PerfCounterInfo>();
            foreach (PerfCounterInfo counter in counters)
            {
                var counterName = $"{counter.groupInfo.key}.{counter.nameInfo.key}.{counter.rollupType}";

                // In vSphere 7.0 there is a duplicate counter (disk.scsiReservationCnflctsPct.average) which doesn't
                // appear in previous versions. This could be a bug as there appears to be no reason to duplicate it,
                // so just skip any duplicate keys/counters here.
                if (!counterMap.ContainsKey(counterName))
                {
                    counterMap.Add(counterName, counter);
                }
            }

            return counterMap;
        }

        /// <summary>
        /// Retrieve the managed objects and properties referenced in a view.
        /// </summary>
        /// <param name="view">The view object to return properties from.</param>
        /// <param name="type">The type of managed object to return.</param>
        /// <param name="properties">The names of the properties to return.</param>
        /// <returns></returns>
        public ObjectContent[] RetrieveViewProperties(ManagedObjectReference view, string type, string[] properties)
        {
            var traversalSpec = new TraversalSpec();
            traversalSpec.name = "Container View";
            traversalSpec.type = "ContainerView";
            traversalSpec.path = "view";
            traversalSpec.skip = false;

            var propertySpecs = new PropertySpec[] { new PropertySpec() };
            propertySpecs[0].type = type;
            propertySpecs[0].all = false;
            propertySpecs[0].pathSet = properties;

            var objectSpecs = new ObjectSpec[] { new ObjectSpec() };
            objectSpecs[0].obj = view;
            objectSpecs[0].skip = true;
            objectSpecs[0].selectSet = new SelectionSpec[] { traversalSpec };

            var propertyFilterSpecs = new PropertyFilterSpec[] { new PropertyFilterSpec() };
            propertyFilterSpecs[0].objectSet = objectSpecs;
            propertyFilterSpecs[0].propSet = propertySpecs;

            var options = new RetrieveOptions();
            var request = new RetrievePropertiesExRequest(Content.propertyCollector, propertyFilterSpecs, options);
            RetrieveResult results = null;

            try
            {
                results = Service.RetrievePropertiesEx(request).returnval;
            }
            catch (FaultException e)
            {
                if (e.Message == "The object has already been deleted or has not been completely created")
                {
                    throw new ObjectDeletedException("Object cannot be found, the session may no longer be active.");
                }
            }

            return results.objects;
        }

        /// <summary>
        /// Retrieve the properties for a managed object.
        /// </summary>
        /// <param name="mor">The managed object to return properties from.</param>
        /// <param name="properties">The names of the properties to return.</param>
        /// <returns></returns>
        public ObjectContent[] RetreieveObjectProperties(ManagedObjectReference mor, string[] properties)
        {
            var propertySpecs = new PropertySpec[] { new PropertySpec() };
            propertySpecs[0].type = mor.type;
            propertySpecs[0].all = false;
            propertySpecs[0].pathSet = properties;

            var objectSpecs = new ObjectSpec[] { new ObjectSpec() };
            objectSpecs[0].obj = mor;
            objectSpecs[0].skip = true;

            var propertyFilterSpecs = new PropertyFilterSpec[] { new PropertyFilterSpec() };
            propertyFilterSpecs[0].objectSet = objectSpecs;
            propertyFilterSpecs[0].propSet = propertySpecs;

            var options = new RetrieveOptions();
            var request = new RetrievePropertiesExRequest(Content.propertyCollector, propertyFilterSpecs, options);
            RetrieveResult results = Service.RetrievePropertiesEx(request).returnval;
            MissingProperty[] missingProperties = results.objects[0].missingSet;

            if (missingProperties is object && missingProperties.Length > 0)
            {
                // check for any missing property fault with type NotAuthenticated
                if (missingProperties.Where(p => p.fault.fault is NotAuthenticated).Count() > 0)
                {
                    throw new NotAuthenticatedException("The current session is no longer authenticated");
                }
            }

            return results.objects;
        }

        /// <summary>
        /// Query a raw (real-time) performance counter for a managed object such as a host or virtual machine.
        /// </summary>
        /// <remarks>https://code.vmware.com/apis/704/vsphere/vim.PerformanceManager.html#queryStats</remarks>
        /// <param name="entity">The managed object to query the counter for.</param>
        /// <param name="counter">The name of the counter to query in the "group.name.rollup" format.</param>
        /// <returns>A PerfEntityMetric object with the last 15 samples at a 20 second interval.</returns>
        public PerfEntityMetric QueryRawPerformaceMetric(ManagedObjectReference entity, string counter)
        {
            var perfMetricId = new PerfMetricId();
            perfMetricId.instance = "";
            perfMetricId.counterId = _counters[counter].key;

            // https://code.vmware.com/apis/704/vsphere/vim.PerformanceManager.QuerySpec.html
            var perfQuerySpec = new PerfQuerySpec();
            perfQuerySpec.intervalId = 20;
            perfQuerySpec.intervalIdSpecified = true;
            perfQuerySpec.maxSample = 15;
            perfQuerySpec.maxSampleSpecified = true;
            perfQuerySpec.metricId = new PerfMetricId[] { perfMetricId };
            perfQuerySpec.entity = entity;

            PerfEntityMetricBase[] queryPerfResponse;

            try
            {
                var queryPerfRequest = new QueryPerfRequest(Content.perfManager, new PerfQuerySpec[] { perfQuerySpec });
                queryPerfResponse = Service.QueryPerf(queryPerfRequest).returnval;
            }
            catch (FaultException e)
            {
                // Identify if the exception is an authentication issue and throw a NotAuthenticated exception in the
                // same way RetrieveObjectProperties does.
                if (e.Message == "The session is not authenticated.")
                {
                    throw new NotAuthenticatedException("The current session is no longer authenticated");
                }

                throw;
            }

            return (PerfEntityMetric)queryPerfResponse[0];
        }

        /// <summary>
        /// Query the vSAN health summary managed object.
        /// </summary>
        /// <param name="vsanClusterMor">The vSAN cluster managed object.</param>
        /// <param name="properties">The names of the properties to return.</param>
        /// <param name="useCache">Choose to use the cached health summary or to run a full query for the latest data.</param>
        /// <returns></returns>
        public VsanHealthApi.VsanClusterHealthSummary QueryVsanClusterHealthSummary(
            VsanHealthApi.ManagedObjectReference vsanClusterMor, string[] properties, bool useCache = true)
        {
            var vsanHealthMor = new VsanHealthApi.ManagedObjectReference
            {
                type = "VsanVcClusterHealthSystem",
                Value = "vsan-cluster-health-system"
            };

            VsanHealthApi.VsanClusterHealthSummary healthSummary;

            try
            {
                bool fetchFromCache = useCache;
                bool fetchFromCacheSpecified = useCache;
                bool includeObjectUuid = false;
                bool includeObjectUuidSpecified = true;
                int vmCreateTimeout = 0;
                bool vmCreateTimeoutSpecified = true;
                string perspective = "defaultView";
                var fields = properties;

                healthSummary = VsanHealthService.VsanQueryVcClusterHealthSummary(
                    vsanHealthMor, vsanClusterMor, vmCreateTimeout, vmCreateTimeoutSpecified, null, includeObjectUuid,
                    includeObjectUuidSpecified, fields, fetchFromCache, fetchFromCacheSpecified, perspective, null, null
                );
            }
            catch (SoapException e)
            {
                if (e.Detail is object && e.Detail.SelectSingleNode("NotAuthenticatedFault") is object)
                {
                    throw new NotAuthenticatedException("The current session is no longer authenticated");
                }

                throw;
            }

            return healthSummary;
        }
    }
}

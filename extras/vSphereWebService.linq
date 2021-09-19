<Query Kind="Program">
  <Reference Relative="..\lib\STSService.dll"></Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.IdentityModel.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Runtime.Serialization.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ServiceModel.dll</Reference>
  <Reference Relative="..\lib\Vim25Service.dll"></Reference>
  <Reference Relative="..\lib\VMware.Binding.WsTrust.dll"></Reference>
  <Reference Relative="..\lib\VsanhealthService.dll"></Reference>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Net.Security</Namespace>
  <Namespace>System.Security.Cryptography.X509Certificates</Namespace>
  <Namespace>System.ServiceModel</Namespace>
  <Namespace>System.ServiceModel.Channels</Namespace>
  <Namespace>Vim25Api</Namespace>
  <Namespace>VMware.Binding.WsTrust</Namespace>
  <Namespace>vmware.sso</Namespace>
</Query>

// Should a FileNotFoundException occur for STSService.dll one of the following options should resolve it.
// 1. Copy STSService.dll to /extras (so it is in the same folder as this .linq file).
// 2. Copy LINQPad.exe to /lib and run LINQPad.exe from the /lib folder.

void Main()
{
    var conn = new Connection(
        new ConnectionConfig("user@vsphere.local", "password", "https://192.168.0.1/sdk")
    );

	ObjectContent[] about = conn.RetreieveObjectProperties(conn.ServiceInstance, new string[] { "content.about.fullName" });
	about.Dump("Some properties can be retrieved without authentication.");

	// create an authenticated connection, this may take a while to execute on the first run
    conn.Connect();

	// retrieve the name of the managed objects in two of the views created during the connection setup
    ObjectContent[] hosts = conn.RetrieveViewProperties(conn.HostView, "HostSystem", new string[] { "name" });
	ObjectContent[] clusters = conn.RetrieveViewProperties(conn.ClusterView, "ClusterComputeResource", new string[] { "name" });
	hosts.Dump("The name of all the HostSystem managed objects (Hosts).");
	clusters.Dump("The name of all the ClusterComputeResource managed objects (Clusters).");

    conn.Disconnect();
}

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

    private void ConnectVsanHealth()
    {
        CookieContainer cookieContainer = new CookieContainer();
        var cookieManager = ((IContextChannel)Service).GetProperty<IHttpCookieContainerManager>();
        CookieCollection cookie = cookieManager.CookieContainer.GetCookies(_vsanUri);
        cookieContainer.Add(cookie);
        VsanHealthService.ConnectVsanService(_vsanNamespace, _vsanUri.OriginalString, cookieContainer, 30000);
    }

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
}

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
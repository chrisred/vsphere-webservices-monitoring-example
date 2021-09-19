<Query Kind="Program">
  <Reference Relative="..\lib\STSService.dll"></Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.IdentityModel.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Runtime.Serialization.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ServiceModel.dll</Reference>
  <Reference Relative="..\lib\Vim25Service.dll"></Reference>
  <Reference Relative="..\lib\VMware.Binding.WsTrust.dll"></Reference>
  <Reference Relative="..\lib\VsanhealthService.dll"></Reference>
  <Reference Relative="..\lib\vSphereWsClient.dll"></Reference>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Net.Security</Namespace>
  <Namespace>System.Security.Cryptography.X509Certificates</Namespace>
  <Namespace>System.ServiceModel</Namespace>
  <Namespace>System.ServiceModel.Channels</Namespace>
  <Namespace>Vim25Api</Namespace>
  <Namespace>VMware.Binding.WsTrust</Namespace>
  <Namespace>vmware.sso</Namespace>
  <Namespace>vSphereWsClient</Namespace>
</Query>

// Should a FileNotFoundException occur for STSService.dll one of the following options should resolve it.
// 1. Copy STSService.dll to /extras (so it is in the same folder as this .linq file).
// 2. Copy LINQPad.exe to /lib and run LINQPad.exe from the /lib folder.

void Main()
{
    var conn = new Connection(
        new ConnectionConfig("user@vsphere.local", "password", "https://192.168.0.1/sdk")
    );

    conn.Connect();
	
	// select one of the HostSystem objects in the environment
	ObjectContent[] hosts = conn.RetrieveViewProperties(conn.HostView, "HostSystem", new string[] { "name" });
	ManagedObjectReference mor = hosts[0].obj;
	
	// TraversalSpec to allow the property collector to traverse to child objects, at a HostSystem object select the "vm" property
	TraversalSpec hostToVms = new TraversalSpec();
    hostToVms.name = "HostToVms";
    hostToVms.type = "HostSystem";
    hostToVms.path = "vm";
    hostToVms.skip = true;
	
	// when a VirtualMachine object is found the name property will be returned
	var vmPropertySpecs = new PropertySpec[] { new PropertySpec() };
    vmPropertySpecs[0].type = "VirtualMachine";
    vmPropertySpecs[0].all = false;
    vmPropertySpecs[0].pathSet = new string[] { "name" };
	
	// the managed object where the search for properties begins
    var vmObjectSpecs = new ObjectSpec[] { new ObjectSpec() };
    vmObjectSpecs[0].obj = mor;
    vmObjectSpecs[0].skip = true;
    vmObjectSpecs[0].selectSet = new SelectionSpec[] { hostToVms };

	// the complete filter that the property collector will use to discover the properties
    var vmPropertyFilterSpecs = new PropertyFilterSpec[] { new PropertyFilterSpec() };
    vmPropertyFilterSpecs[0].objectSet = vmObjectSpecs;
    vmPropertyFilterSpecs[0].propSet = vmPropertySpecs;

	// retrieves the properties specificed by the filter using the PropertyCollector managed object
    var vmOptions = new RetrieveOptions();
    var vmRequest = new RetrievePropertiesExRequest(conn.Content.propertyCollector, vmPropertyFilterSpecs, vmOptions);
    RetrieveResult vmResults = conn.Service.RetrievePropertiesEx(vmRequest).returnval;
	vmResults.Dump("An example of simple usage of a TraversalSpec object to allow the property collector to traverse the child objects of another object. In this example VirtualMachine objects which are childern of a HostSystem have their 'name' property retrieved.");
	
	// similar example to the above except we select a StorageSystem object
    TraversalSpec hostToStorageSystem = new TraversalSpec();
    hostToStorageSystem.name = "HostToStorageSystem";
    hostToStorageSystem.type = "HostSystem";
    hostToStorageSystem.path = "configManager.storageSystem";
    hostToStorageSystem.skip = true;

    var storagePropertySpecs = new PropertySpec[] { new PropertySpec() };
    storagePropertySpecs[0].type = "HostStorageSystem";
    storagePropertySpecs[0].all = false;
    storagePropertySpecs[0].pathSet = new string[] { "storageDeviceInfo.scsiLun" };

    var storageObjectSpecs = new ObjectSpec[] { new ObjectSpec() };
    storageObjectSpecs[0].obj = mor;
    storageObjectSpecs[0].skip = true;
    storageObjectSpecs[0].selectSet = new SelectionSpec[] { hostToStorageSystem };

    var storagePropertyFilterSpecs = new PropertyFilterSpec[] { new PropertyFilterSpec() };
    storagePropertyFilterSpecs[0].objectSet = storageObjectSpecs;
    storagePropertyFilterSpecs[0].propSet = storagePropertySpecs;

    var storageOptions = new RetrieveOptions();
    var storageRequest = new RetrievePropertiesExRequest(conn.Content.propertyCollector, storagePropertyFilterSpecs, storageOptions);
    RetrieveResult storageResults = conn.Service.RetrievePropertiesEx(storageRequest).returnval;
    var disks = (ScsiLun[])storageResults.objects[0].propSet[0].val;
	disks.Dump("Retrieve the disks attatched to a host.", 1);
	
    conn.Disconnect();
}
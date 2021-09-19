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
	
	// begin to define a set of TraversalSpec objects to allow the property collector to walk the inventory tree
	
	// at a Folder object ("rootFolder" in this case) select the "childEntity" property
    var folderToChild = new TraversalSpec();
    folderToChild.name = "FolderToChild";
    folderToChild.type = "Folder";
    folderToChild.path = "childEntity";
    folderToChild.skip = true;
	
	// a SelectionSpec simply references an existing TraversalSpec so it can be reused, this allows nested folders to be traversed recursively
	var recurseFolders = new SelectionSpec(); 
	recurseFolders.name = "FolderToChild";
	
	// at a Datacenter object select the "hostFolder" property
    var datacenterToHostFolder = new TraversalSpec();
    datacenterToHostFolder.name = "DatacenterToHostFolder";
    datacenterToHostFolder.type = "Datacenter";
    datacenterToHostFolder.path = "hostFolder";
    datacenterToHostFolder.skip = true;
	// assigning this SelectionSpec allows the folders under the Datacenter to be traversed recursively
	datacenterToHostFolder.selectSet = new SelectionSpec[] { recurseFolders };
	
	// at a ComputeResource object select the host property (a ComputeResource object cannot contain folders)
    var computeResourceTohost = new TraversalSpec();
    computeResourceTohost.name = "ComputeResourceToHost";
    computeResourceTohost.type = "ComputeResource";
    computeResourceTohost.path = "host";
    computeResourceTohost.skip = true;
	
	// create a SelectionSpec, this uses the TraversalSpec/SelectionSpec to define the full path through inventory tree
	var selectionSpecs = new SelectionSpec[] { recurseFolders, datacenterToHostFolder, computeResourceTohost };
	// assign the SelectionSpec to the TraversalSpec which defines the starting point of the traversal (rootFolder)
	folderToChild.selectSet = selectionSpecs;
	
	// when the HostSystem object is found the name property will be returned
    var propertySpecs = new PropertySpec[] { new PropertySpec() };
    propertySpecs[0].type = "HostSystem";
    propertySpecs[0].pathSet = new string[] { "name", "summary.runtime.powerState" };
	propertySpecs[0].all = false;

	// the managed object where the search for properties begins
    var objectSpecs = new ObjectSpec[] { new ObjectSpec() };
    objectSpecs[0].obj = conn.Content.rootFolder;
	objectSpecs[0].selectSet = new SelectionSpec[] { folderToChild };
    objectSpecs[0].skip = true;

	// the complete filter that the property collector will use to discover the properties
    var propertyFilterSpecs = new PropertyFilterSpec[] { new PropertyFilterSpec() };
    propertyFilterSpecs[0].objectSet = objectSpecs;
    propertyFilterSpecs[0].propSet = propertySpecs;
	
	// retrieves the properties specificed by the filter using the PropertyCollector managed object
    var options = new RetrieveOptions();
    var request = new RetrievePropertiesExRequest(conn.Content.propertyCollector, propertyFilterSpecs, options);
	var results = conn.Service.RetrievePropertiesEx(request).returnval;
	
	results.Dump("An example of traversing the inventory tree using TraversalSpec/SelectionSpec objects to reach the HostSystem 'Managed Object Reference'. To reach a host three objects in the tree must be passed through.");

    conn.Disconnect();
}
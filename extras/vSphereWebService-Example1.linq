<Query Kind="Program">
  <Reference Relative="..\lib\STSService.dll"></Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.IdentityModel.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Runtime.Serialization.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ServiceModel.dll</Reference>
  <Reference Relative="..\lib\Vim25Service.dll"></Reference>
  <Reference Relative="..\lib\VimService.XmlSerializers.dll"></Reference>
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
	
	// basic use of the PropertyCollector, we select the "childEntity" property on the "rootFolder" Folder managed object

	// the properties from the managed object that will be returned to the client if found
    var propertySpecs = new PropertySpec[] { new PropertySpec() };
    propertySpecs[0].type = "Folder";
    propertySpecs[0].all = false;
    propertySpecs[0].pathSet = new string[] { "childEntity" } ;

	// the managed object where the search for properties begins
    var objectSpecs = new ObjectSpec[] { new ObjectSpec() };
    objectSpecs[0].obj = conn.Content.rootFolder;
    objectSpecs[0].skip = true;

	// the complete filter that the property collector will use to discover the properties
    var propertyFilterSpecs = new PropertyFilterSpec[] { new PropertyFilterSpec() };
    propertyFilterSpecs[0].objectSet = objectSpecs;
    propertyFilterSpecs[0].propSet = propertySpecs;
	
	// retrieves the properties specificed by the filter using the PropertyCollector managed object
    var options = new RetrieveOptions();
    var request = new RetrievePropertiesExRequest(conn.Content.propertyCollector, propertyFilterSpecs, options);
	RetrieveResult results = conn.Service.RetrievePropertiesEx(request).returnval;

	results.objects[0].propSet.Dump(
		"An example of a property filter where a single property is selected on an object. In this case the 'childEntity' property of the 'rootFolder' Folder object. A Datacenter 'Managed Object Reference' is returned from the 'childEntity' property."
	);

    conn.Disconnect();
}
[![Docker](https://github.com/ContainerSolutions/dotnet-operator-sdk/workflows/Docker/badge.svg)](https://hub.docker.com/repository/docker/sebagomez/k8s-mssqldb)

## Microsoft SQL Server Database operator

### Definition

This is a controller for a newly defined `CustomResourceDefinition` (CRD) that lets you create or delete (drop) databases from a Microsoft SQL Server `Pod` running in your Kubernetes cluster.

```yaml
apiVersion: apiextensions.k8s.io/v1beta1
kind: CustomResourceDefinition
metadata:
  name: mssqldbs.samples.k8s-cs-controller
spec:P
  group: samples.k8s-cs-controller
  version: v1
  subresources:
    status: {}
  scope: Namespaced
  names:
    plural: mssqldbs
    singular: mssqldb
    kind: MSSQLDB
  validation:
    openAPIV3Schema:
      type: object
      description: "A Microsoft SQLServer Database"
      properties:
        spec:
          type: object
          properties:
            dbname:
              type: string
            configmap:
              type: string
            data:
              credentials: string
          required: ["dbname","configmap", "credentials"]
```

This `CRD` has three properties, `dbname`, `configmap`, and `credentials`. All three of them are strings, but they all have different semantics.  

- `dbname` holds the name of the Database that will be added/delete to the SQL Server instance.
- `configmap` is the name of a [`ConfigMap`](https://kubernetes.io/docs/concepts/configuration/configmap/) with a property called `instance`. That's where the name of the [`Service`](https://kubernetes.io/docs/concepts/services-networking/service/) related to the SQL Server pod is listening.
- `credentials` is also an indirection, but in this case to a [`Secret`](https://kubernetes.io/docs/concepts/configuration/secret/) that holds both the user (`userid`) and password (`password`) to the SQL Server instance.

As you can see, these are mandatory for the controller to successfully communicate to the SQL Server instance.

So, a typical yaml for my new resource, called `MSSQLDB`, will lool like this

```yaml
apiVersion: "samples.k8s-cs-controller/v1"
kind: MSSQLDB
metadata:
  name: db1
spec:
  dbname: MyFirstDB
  configmap: mssql-config
  credentials: mssql-credentials 
```

This yaml will create (or delete) an object of kind `MSSQLDB`, named db1 with the properties mentioned above. In this case, a `ConfigMap` called `mssql-config` and a `Secret` called `mssql-credentials` must exist.

### Implementation

If we first apply the first file ([`CustomResourceDefinition`](./yaml/mssql-crd.yaml)) and we then apply the second one ([db1.yaml](./yaml/db1.yaml)), we'll see that Kubernetes successfully creates the object.

```bash
kubectl apply -f .\db1.yaml  
mssqldb.samples.k8s-cs-controller/db1 created
```

But nothing actually happens other than the API-Server saving the data in the cluster's etcd database. We need to do something that "listens" for our newly created definition and eventually would create or delete databases.

#### Base class

We need to create a class that represents our definition. For that purpose, the SDK provides a class called **`BaseCRD`** which is where your class will inherit from. Also, you must create a spec class that will hold the properties defined in your custom resource. In my case, this is what they look like.

```cs
public class MSSQLDB : BaseCRD
{
	public MSSQLDB() :
		base("samples.k8s-cs-controller", "v1", "mssqldbs", "mssqldb")
	{ }

	public MSSQLDBSpec Spec { get; set; }
}

public class MSSQLDBSpec
{
	public string DBName { get; set; }

	public string ConfigMap { get; set; }

	public string Credentials { get; set; }
}
```

Keep in mind the strings you must pass over the base class' constructor. These are the same values defined in the `CustomeResourceDefinition` file.

Then you need to create the class that will be actually creating or deleting the databases. For this purpose, create a class that implements the **`IOperationHAndler<T>`**, where **`T`** is your implementation of the **`BaseCRD`**,  in my case **`MSSQLDB`**.

```cs
public interface IOperationHandler<T> where T : BaseCRD
{
	Task OnAdded(Kubernetes k8s, T crd);

	Task OnDeleted(Kubernetes k8s, T crd);

	Task OnUpdated(Kubernetes k8s, T crd);

	Task OnBookmarked(Kubernetes k8s, T crd);

	Task OnError(Kubernetes k8s, T crd);

	Task CheckCurrentState(Kubernetes k8s);
}
```
The implementation is pretty straight forward, you need to implement the **`OnAction`** methods. These methods are the ones that will communicate with the SQL Server instance and will create or delete the databases. So whenever somebody uses `kubectl` to create, apply or delete an object, these methods will be called.

But what happens if somebody or something connects to your SQL Server instance and deletes the databases? Here's where the **`CheckCurrentState`** method comes into play. This method, in my case, is checking every 5 seconds if the **`MSSQLDB`** objects created in my cluster are actually created as databases in the SQL Server instance. If they are not, it will try to recreate them.

### Start your engines!

Ok, now it's time to start and try everything.

In my case it's a .NET Core console application where I start the controller. (I've also seen ASP.NET Hosted Services)

```cs
static void Main(string[] args)
{
	try
	{
		string k8sNamespace = "default";
		if (args.Length > 1)
			k8sNamespace = args[0];

		Controller<MSSQLDB>.ConfigLogger();

		Log.Info($"=== {nameof(MSSQLController)} STARTING for namespace {k8sNamespace} ===");

		MSSQLDBOperationHandler handler = new MSSQLDBOperationHandler();
		Controller<MSSQLDB> controller = new Controller<MSSQLDB>(new MSSQLDB(), handler);
		Task reconciliation = controller.SatrtAsync(k8sNamespace);

		Log.Info($"=== {nameof(MSSQLController)} STARTED ===");

		reconciliation.ConfigureAwait(false).GetAwaiter().GetResult();

	}
	catch (Exception ex)
	{
		Log.Fatal(ex);
	}
	finally
	{
		Log.Warn($"=== {nameof(MSSQLController)} TERMINATING ===");
	}
}
```
Here you can see that I first create the handler and pass it over to the controller instance. This **`Controller`** is given by the SDK and it's the one checking on the objects created by the kubernetes-apiserver. I then start the controller, the handler for the current state, and that's it!.

### Take it for a spin

Start your console application and see what happens.

```log
2020-10-04 12:26:22.2833 [INFO] mssql_db.MSSQLController:=== MSSQLController STARTING for namespace default ===
2020-10-04 12:26:23.0727 [INFO] mssql_db.MSSQLController:=== MSSQLController STARTED ===
2020-10-04 12:26:29.5139 [INFO] K8sControllerSDK.Controller`1:Reconciliation Loop for CRD mssqldb will run every 5 seconds.
2020-10-04 12:26:29.5954 [INFO] K8sControllerSDK.Controller`1:mssql_db.MSSQLDB db1 Added on Namespace default
2020-10-04 12:26:29.6158 [INFO] mssql_db.MSSQLDBOperationHandler:DATABASE MyFirstDB will be ADDED
2020-10-04 12:26:30.4513 [INFO] mssql_db.MSSQLDBOperationHandler:DATABASE MyFirstDB successfully created!
2020-10-04 12:26:39.6329 [INFO] K8sControllerSDK.Controller`1:mssql_db.MSSQLDB db1 Deleted on Namespace default
2020-10-04 12:26:39.6339 [INFO] mssql_db.MSSQLDBOperationHandler:DATABASE MyFirstDB will be DELETED!
2020-10-04 12:26:39.7343 [INFO] mssql_db.MSSQLDBOperationHandler:DATABASE MyFirstDB successfully deleted!
2020-10-04 12:26:45.7297 [INFO] K8sControllerSDK.Controller`1:mssql_db.MSSQLDB db1 Added on Namespace default
2020-10-04 12:26:45.7297 [INFO] mssql_db.MSSQLDBOperationHandler:DATABASE MyFirstDB will be ADDED
2020-10-04 12:26:47.0061 [INFO] mssql_db.MSSQLDBOperationHandler:DATABASE MyFirstDB successfully created!
2020-10-04 12:26:59.7036 [WARN] mssql_db.MSSQLDBOperationHandler:Database MyFirstDB was not found!
2020-10-04 12:26:59.7036 [INFO] mssql_db.MSSQLDBOperationHandler:DATABASE MyFirstDB will be ADDED
2020-10-04 12:27:01.3013 [INFO] mssql_db.MSSQLDBOperationHandler:DATABASE MyFirstDB successfully created!
```

Here's the log of the execution. The first thing I did was created the first db (all these yaml files are in the [yaml](./yaml) folder)

`kubectl apply -f .\db1.yaml`

I then deleted the object, and the database was successfully created:

`kubectl delete -f .\db1.yaml`

I then created it again, and connected to the pod running the SQL Server and dropped the MyFirstDB database, thus, you see that `Database MyFirstDB was not found!` message.

Also, in the log shown above, you'll notice some messages seem to have the same info, but they actually come from two sources. One from the controller engine itself (from inside the SDK) and some form my own MSSQLDB implementation.

### Run it in your container

This msqldb controller is also available as a Docker image in my personal Docker Hub repository under [sebagomez/k8s-mssqldb](https://hub.docker.com/repository/docker/sebagomez/k8s-mssqldb). 

In the [yaml](./samples/msssql-db/yaml) folder there are a few files that can be used to try the controller.

File|Description
---|---
[deployment.yaml](./samples/msssql-db/yaml/deployment.yaml)|Sets up a `Pod` with an instance of MS SQL Server, a `Service` to access the `Pod` and the `ConfigMap` and `Secret` needed to access the SQL Server instance.
[mssql-crd.yaml](./samples/msssql-db/yaml/mssql-crd.yaml)|The `CustomResourceDefinition` for this new resource called `MSSQLDB`.
[controller-deployment.yaml](./samples/msssql-db/yaml/controller-deployment.yaml)|Spins up a `Pod` with the controller itself.
[db1.yaml](./samples/msssql-db/yaml/db1.yaml)|A sample MSSQLDB that creates a database called 'MyFirstDB'

Apply those scripts in the order described above. Also, you can play around renaming the SQL Database instance modifiyng `dbname` value of the `db1.yaml` file.

But, is it working?

I've added a little script at [sqlcmd.sh](./samples/msssql-db/yaml/sqlcmd.sh) that will spin a `Pod` with the SqlCmd utility.  
Once you run the script you can connect to your SQL Server instance with running the following command

`sqlcmd -S mssql-service -U sa -P MyNotSoSecuredPa55word!`


Once connected, you can check the existent databases like this

```sql
select name from sys.databases;
go
```

Want to make interesting? Drop the database created by Kubernetes and see what happens

```sql
drop database MyFirstDB;
go
``` 

If you're fast enough, you will see that the database is gone. But after a few seconds (5 max) the database is once again created. This is because the reconciliation loop realized that the actual state of the cluster is not consistent with the desired state, so it will try to change that.

Have fun with it and let me know what you think.

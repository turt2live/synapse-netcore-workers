# synapse-netcore-workers

An experimental project to build Synapse-compatible workers in .NET Core. For questions or development news, visit 
[#synapse-netcore-workers:t2bot.io](https://matrix.to/#/#synapse-netcore-workers:t2bot.io) on Matrix.

----

**⚠️⚠️ Not intended for production usage yet ⚠️⚠️** 

Much of this is largely based upon internal features to Synapse and is generally considered unstable. Do not use this in a
production environment as this may not work perfectly yet. The plan is to make this production ready, although with a caution
that it may not be forwards compatible with future Synapse releases.

It is assumed you are an expert on running Synapse workers before trying to use this project.

----

Reference material:
* [Synapse TCP replication protocol](https://github.com/matrix-org/synapse/blob/master/docs/tcp_replication.rst)

Built with Visual Studio 2017 Community Edition.


## Running a worker

Each worker should have its own `appsettings.json` file to set various values. However, this is checked in to source control
and may not be ideal to use in production environments. Production environments can use either the command line or environment
variables to override the values without changing the `appsettings.json` file.

For example, a `appsettings.json` may look like this:
```json
{
	"ConnectionStrings": {
		"synapse": "..."
	}
}
```

To override the connection string in the example, specify `--ConnectionStrings__synapse="..."` (note the double underscore) or 
`ConnectionStrings__synapse=...` (as an environment variable, with double underscores).


## Docker

All Docker images are intended to be built relative to the root directory (where this README is). From there, you can build
your workers:

```
docker build -t synapseinterop:appservice_sender -f Matrix.SynapseInterop.Worker.AppserviceSender/Dockerfile .
```


## Developer information

#### Migrations

TODO: Actual documentation

```
dotnet ef migrations add CreateAppservices --project .\Matrix.SynapseInterop.Worker.AppserviceSender\Matrix.SynapseInterop.Worker.AppserviceSender.csproj --startup-project .\Matrix.SynapseInterop.Worker.AppserviceSender\Matrix.SynapseInterop.Worker.AppserviceSender.csproj --context Matrix.SynapseInterop.Worker.AppserviceSender.AppserviceDb
```

# synapse-netcore-workers
An experimental project to build Synapse-compatible workers in .NET Core.

**Not intended for production usage yet.**

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

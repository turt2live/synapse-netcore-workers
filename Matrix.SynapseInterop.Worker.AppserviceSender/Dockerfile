FROM microsoft/dotnet:sdk AS build-env
WORKDIR /app

COPY Matrix.SynapseInterop.Worker.AppserviceSender/ Matrix.SynapseInterop.Worker.AppserviceSender/
COPY Matrix.SynapseInterop.Database/ Matrix.SynapseInterop.Database/
COPY Matrix.SynapseInterop.Replication/ Matrix.SynapseInterop.Replication/
RUN dotnet restore Matrix.SynapseInterop.Worker.AppserviceSender/Matrix.SynapseInterop.Worker.AppserviceSender.csproj

RUN dotnet publish Matrix.SynapseInterop.Worker.AppserviceSender -c Release -o out

FROM microsoft/dotnet:runtime
WORKDIR /app
COPY --from=build-env /app/Matrix.SynapseInterop.Worker.AppserviceSender/out .
ENTRYPOINT ["dotnet", "Matrix.SynapseInterop.Worker.AppserviceSender.dll"]

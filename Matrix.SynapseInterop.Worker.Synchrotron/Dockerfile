FROM microsoft/dotnet:sdk AS build-env
WORKDIR /app

ENV WORKER=Matrix.SynapseInterop.Worker.Synchrotron

COPY ${WORKER}/ ${WORKER}/
COPY Matrix.SynapseInterop.Common/ Matrix.SynapseInterop.Common/
COPY Matrix.SynapseInterop.Common.MatrixUtils/ Matrix.SynapseInterop.Common.MatrixUtils/
COPY Matrix.SynapseInterop.Database/ Matrix.SynapseInterop.Database/
COPY Matrix.SynapseInterop.Replication/ Matrix.SynapseInterop.Replication/
RUN dotnet restore ${WORKER}/${WORKER}.csproj

RUN dotnet publish ${WORKER} -c Release -o out

FROM microsoft/dotnet:runtime
WORKDIR /app

ENV WORKER=Matrix.SynapseInterop.Worker.Synchrotron

COPY --from=build-env /app/${WORKER}/out .
ENTRYPOINT ["sh", "-c", "dotnet $WORKER.dll"]


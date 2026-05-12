FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /repo

COPY Directory.Build.props Directory.Packages.props ./
COPY samples/Common.IoTSensorSimulator/ samples/Common.IoTSensorSimulator/

WORKDIR /repo/samples/Common.IoTSensorSimulator
RUN dotnet restore
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0-preview
WORKDIR /app
COPY --from=build /out .

ENTRYPOINT ["dotnet", "Common.IoTSensorSimulator.dll"]


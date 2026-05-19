FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /repo

# Copy solution infrastructure
COPY Directory.Build.props Directory.Packages.props ./

# Copy source projects
COPY src/ProbahoSSE/ src/ProbahoSSE/
COPY src/ProbahoSSE.Backplane/ src/ProbahoSSE.Backplane/
COPY src/ProbahoSSE.RabbitMq/ src/ProbahoSSE.RabbitMq/

# Copy sample API project
COPY samples/Sample.RabbitMq/Api/ samples/Sample.RabbitMq/Api/

WORKDIR /repo/samples/Sample.RabbitMq/Api
RUN dotnet restore
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=build /out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Sample.RabbitMq.Api.dll"]


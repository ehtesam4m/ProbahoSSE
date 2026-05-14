FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /repo

# Copy solution infrastructure
COPY Directory.Build.props Directory.Packages.props ./

# Copy source projects
COPY src/ProbahoSSE/ src/ProbahoSSE/
COPY src/ProbahoSSE.Backplane/ src/ProbahoSSE.Backplane/
COPY src/ProbahoSSE.RedisStream/ src/ProbahoSSE.RedisStream/

# Copy sample API project
COPY samples/Sample.RedisStream/Api/ samples/Sample.RedisStream/Api/

WORKDIR /repo/samples/Sample.RedisStream/Api
RUN dotnet restore
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=build /out .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Sample.RedisStream.Api.dll"]

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY SortingAlgorithms.csproj ./
RUN dotnet restore SortingAlgorithms.csproj

COPY . .
RUN dotnet publish SortingAlgorithms.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
ENTRYPOINT ["sh", "-c", "dotnet SortingAlgorithms.dll --urls http://0.0.0.0:${PORT:-10000}"]
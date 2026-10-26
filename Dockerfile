FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY ["DateroWebB/DateroWebB.csproj", "DateroWebB/"]
RUN dotnet restore "DateroWebB/DateroWebB.csproj"

COPY . .
RUN dotnet publish "DateroWebB/DateroWebB.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime
WORKDIR /app

ENV DEBIAN_FRONTEND=noninteractive
ENV TZ=America/Lima

RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && ln -snf /usr/share/zoneinfo/$TZ /etc/localtime \
    && echo $TZ > /etc/timezone \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV MODO_SERVICIO=true
EXPOSE 5003
USER app

ENTRYPOINT ["dotnet", "DateroWebB.dll"]

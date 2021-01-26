FROM mcr.microsoft.com/dotnet/sdk:5.0 as builder

COPY ./ /src/

WORKDIR /src

RUN dotnet publish --configuration Release -o out/ ./mssql-db.csproj

FROM mcr.microsoft.com/dotnet/runtime:5.0 as runtime

LABEL maintainer="Sebastián Gómez<sebastian.gomez@container-solutions.com>"

ARG BUILD
ENV APPVERSION=$BUILD

WORKDIR /mssql-controller

COPY --from=builder /src/out ./

CMD ["dotnet", "./mssql-db.dll"]

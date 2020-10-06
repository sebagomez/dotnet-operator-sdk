FROM mcr.microsoft.com/dotnet/core/sdk:3.1 as builder

COPY ./ /src/

WORKDIR /src

RUN dotnet publish --configuration Release -o out/ ./mssql-db.csproj

FROM mcr.microsoft.com/dotnet/core/runtime:3.1 as runtime

WORKDIR /mssql-controller

COPY --from=builder /src/out ./

CMD ["dotnet", "./mssql-db.dll"]

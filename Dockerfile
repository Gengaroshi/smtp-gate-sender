FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./SmtpGateSender/SmtpGateSender.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8885
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8885
ENTRYPOINT ["dotnet", "SmtpGateSender.dll"]

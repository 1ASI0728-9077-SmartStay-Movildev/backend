FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim
WORKDIR /src
COPY contracts/ contracts/
COPY src/Services/ src/Services/
COPY tools/SmartStay.Database/ tools/SmartStay.Database/
RUN dotnet build tools/SmartStay.Database/SmartStay.Database.csproj -c Release
ENTRYPOINT ["dotnet", "tools/SmartStay.Database/bin/Release/net9.0/SmartStay.Database.dll"]
CMD ["migrate"]

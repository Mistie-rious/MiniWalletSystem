# Use .NET SDK image for development
FROM mcr.microsoft.com/dotnet/sdk:9.0

# Set working directory
WORKDIR /app

# Copy only project files first (for better caching)
COPY *.csproj ./
COPY *.sln ./

# Restore dependencies first
RUN dotnet restore

# Copy the rest of the application
COPY . .

# Expose port
EXPOSE 5275

# Default command
CMD ["dotnet", "watch", "run", "--urls=http://0.0.0.0:5275"]

# rakuten-drive-desktop-windows

Official Windows desktop client app for Rakuten Drive. WPF C# project.

## Development Requirements

- Windows 10 version 1809 or later.
- .NET 9.0 SDK or later.
- MSBuild 17.0 or later.
- Jetbrains Rider 2025.1 or later (recommended).
- Visual Studio 2022 or later.

## Installation

1. Clone the repository:

   ```bash
   git clone https://github.com/Rakuten-MTSD-PAIS/rakuten-drive-desktop-windows.git
   ```

2. Open `RakutenDrive.sln` in Rider or Visual Studio.

3. Build/Debug the solution in Rider/VS or by command line:

   ```bash
   dotnet build
   ```

## Architecture

### File Structure

```
RakutenDrive/
├── Assets/             # Images, icons, media
├── Resources/          # Localization files and other resources
├── Source/             # All source files
	├── Controllers/      # Controllers, providers
	├── Models/           # Data models
	├── Services/         # Business services and data access
	├── Utils/            # Util classes
	├── View/             # WPF view markup and classes
	└── App.xaml          # Application entry point
	└── Global.cs         # Global properties
```

## Building

### Debug Build

```bash
dotnet build --configuration Debug
```

### Release Build

```bash
dotnet build --configuration Release
```

### Publishing

Create a self-contained executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

## Testing

TODO

## Dependencies

- AWS SDK 3.7.411
- log4net 3.0.3
- Vanara Core 4.0.4
- Vanara PInvoke 4.0.4

## Contributing

1. Fork the repository.
2. Create a feature or fix branch: `git checkout -b feature/your-feature-name`.
3. Make your changes and commit: `git commit -am 'Add some feature'`.
4. Push to the branch to fork: `git push fork feature/your-feature-name`.
5. Submit a pull request.

## Code Style

- Follow standard C# naming conventions.
- Use tabs for indentation.
- Place opening braces on new lines (Allman style).
- Start private fields with underscore.
- Use two empty lines between methods, classes, and different code bodies (fields, methods, etc.)
- Use C# region markers.
- Include XML documentation for public APIs (can use Jetbrains Rider AI for it).
- Eliminate code warnings asap.

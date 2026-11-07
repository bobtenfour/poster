PosterPrintRequest production deployment package

Application name: PosterPrintRequest
Build configuration: Release
Target framework: net10.0
Hosting: framework-dependent, ASP.NET Core in-process (existing web.config)
Build timestamp: 2026-10-30 14:07

Package contents:
- PosterPrintRequest-Production.zip contains the Release publish output at the archive root.
- Application assemblies, dependency assemblies, runtime configuration, and web.config.
- Static assets under wwwroot.
- appsettings.json has an empty PrinterMonitoring:BaseUrl and no SQL connection string.
- appsettings.Development.json is not included.
- The package does not contain source code, Git metadata, test projects, test fixtures, a printer address, a SQL Server address, credentials, or production data.

IIS deployment:
1. Use the existing PosterPrintRequest application's physical directory. Do not change that path.
2. Keep the existing IIS site, application, authentication, and environment configuration.
3. Keep existing persistent uploaded files and the production database. This package does not include them and must not replace them.
4. Copy the ZIP contents into that physical directory, replacing the previously published application files.
5. On the IIS server, set ConnectionStrings__PosterPrintRequest and PrinterMonitoring__BaseUrl for this application only. Do not put those values in source control or in appsettings.json. The SQL value must use Windows Integrated Authentication and database POSTER. Do not put a server name, password, or local development connection string in the package.
6. Restart the PosterPrintRequest application pool after the files are copied.

Rollback:
Restore the previous published application files into the same physical directory, then restart the application pool.

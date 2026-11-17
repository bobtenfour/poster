TDX Validation Tool
====================

This program checks communication with the TDX sandbox. It uses a generated test Poster ID, a test title, and the requester name text "Integration Test". It does not use PosterPrintRequest posters, the database, shared storage, or Entra authentication.

The sandbox base path
----------------------

The placeholder is the constant SandboxHost.BasePath in src/PosterPrintRequest.TdxValidator/SandboxHost.cs. Its source value is <TDX_SANDBOX_API_PATH>.

Before publishing, replace that value locally with the official sandbox base path. Replace everything between the quotes, including the < and > signs; keep the quotes. Example: "<TDX_SANDBOX_API_PATH>" becomes "/EXAMPLE/TDWebApi". Write only the path: no https://, no host (help.uillinois.edu is already fixed), no port, and no trailing slash. Do not include the final /api segment, because the validator appends it. The path must begin with /, and contain no query, fragment, "." or ".." segment, encoded separator, < or >. The validator appends these relative operation paths to it:

- api/auth
- api/{applicationId}/tickets
- api/{applicationId}/tickets/search
- api/{applicationId}/tickets/{ticketId}

Do not commit the replaced value. After publishing, discard the local edit, for example:

git checkout -- src/PosterPrintRequest.TdxValidator/SandboxHost.cs

The host help.uillinois.edu, https, and port 443 stay as they are. The library does not contain the host. This executable checks the host before it calls the library.

Commands
--------

From the repository root, with the placeholder still in place:

dotnet build PosterPrintRequest.sln
dotnet test PosterPrintRequest.sln

Then replace the placeholder locally, publish, and discard the local edit:

dotnet publish src/PosterPrintRequest.TdxValidator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/tdx-validator
git checkout -- src/PosterPrintRequest.TdxValidator/SandboxHost.cs

The published executable contains the real path. artifacts/tdx-validator/ is gitignored; never commit it.

Build and test success are engineering checks, not acceptance.

Settings
--------

Copy tdx-validator.settings.template.json beside the executable and name the copy tdx-validator.settings.json. Replace the placeholder identifiers with the sandbox ticket configuration. Set sourceId and responsibleGroupId to null or to an integer. Add any required custom attributes. Set lookupAttempts and lookupIntervalSeconds to the bounded lookup schedule. The file holds configuration identifiers only. Never put a username, password, or token in it. The filled file is gitignored.

Run
---

On the institutional machine, run PosterPrintRequest.TdxValidator.exe. It opens the standard Windows credential window for the sandbox username and password (masked, never saved; cancelling stops the tool before any TDX call). They are used for that execution only. Credentials and the token stay in memory and are not written to the reports, the console, or any file.

Reports
-------

Reports are written to the reports folder beside the executable. The tool checks that it can create, write, and remove a file there before any TDX call. If it cannot, it stops and does not use another folder. Each execution writes a .json file first and then a .txt summary made only from that same data. The names are tdx-validation-<yyyyMMdd-HHmmss>-<8 hex>. Existing files are not overwritten.
